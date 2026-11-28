using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Gordian.Core.Config;

namespace Gordian.Core.Diagnostics
{
    /// <summary>
    /// The per-character chat log files, <c>chat/&lt;server&gt;/&lt;world&gt;/&lt;character&gt;/yyyy-MM-dd.log</c> under the
    /// logs directory (#565, #639; the folders are <see cref="CharacterFolderKey.RelativePath"/>). One file per local day,
    /// named by the date a line is written, so play past midnight moves to the next day's file. Never rolled by size and
    /// never deleted by <see cref="LogRetention"/>. Used only from the <see cref="GordianLog"/> writer thread, except
    /// <see cref="MoveLegacy"/>.
    /// </summary>
    internal sealed class ChatLogFiles : IDisposable
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private readonly string _logsDirectory;
        private readonly Dictionary<string, (DateTime Date, StreamWriter Writer)> _open = new(StringComparer.Ordinal);

        public ChatLogFiles(string logsDirectory) => _logsDirectory = logsDirectory;

        /// <summary>The file a line for the character <paramref name="folder"/> names, written at <paramref name="timestamp"/>, goes to.</summary>
        public static string PathFor(string logsDirectory, CharacterFolderKey folder, DateTime timestamp) =>
            Path.Combine(logsDirectory, GordianLog.ChatFolder, folder.RelativePath, timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");

        public void WriteLine(CharacterFolderKey folder, DateTime timestamp, string line)
        {
            string key = folder.RelativePath;
            if (!_open.TryGetValue(key, out var entry) || entry.Date != timestamp.Date)
            {
                entry.Writer?.Dispose();
                string path = PathFor(_logsDirectory, folder, timestamp);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                entry = (timestamp.Date, new StreamWriter(stream, Utf8NoBom) { AutoFlush = false });
                _open[key] = entry;
            }
            entry.Writer.WriteLine(line);
        }

        /// <summary>
        /// Moves the chat logs an older build wrote for this character, <c>chat/&lt;server&gt;/&lt;character&gt;/*.log</c>
        /// with no world folder, into the character's folder (#639). The old server folder is matched ignoring case, since
        /// older builds kept a LandSandBoat host's casing and <see cref="Profiles.AccountProfile.ServerFolderName"/>
        /// lower-cases it. A day the new folder already has keeps its file and the old one moves in beside it (see
        /// <see cref="MoveOne"/>). Empty old folders are removed. A file or folder that can't be read or moved stays where it is (logged) and is tried again at
        /// the next login; the move never fails the login. Returns the number of files moved. Runs on the caller's thread,
        /// before the character's first chat line.
        /// </summary>
        public static int MoveLegacy(string logsDirectory, CharacterFolderKey folder)
        {
            string chatRoot = Path.Combine(logsDirectory, GordianLog.ChatFolder);
            if (string.IsNullOrWhiteSpace(folder.Character) || !Directory.Exists(chatRoot)) return 0;

            string server = GordianStorage.SafeFolderName(folder.Server);
            string character = GordianStorage.SafeFolderName(folder.Character);
            string typedName = folder.Character.Trim();
            string target = Path.Combine(chatRoot, folder.RelativePath);
            int moved = 0;
            try
            {
                foreach (string serverDir in Directory.GetDirectories(chatRoot))
                {
                    if (!Path.GetFileName(serverDir).Equals(server, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (string characterDir in Directory.GetDirectories(serverDir))
                    {
                        string name = Path.GetFileName(characterDir);
                        if (!name.Equals(character, StringComparison.OrdinalIgnoreCase) && !name.Equals(typedName, StringComparison.OrdinalIgnoreCase)) continue;
                        // Only the day files directly inside: a world folder of the same name holds character folders, not logs.
                        string[] files = Directory.GetFiles(characterDir, "*.log");
                        if (files.Length == 0) continue;
                        int movedHere = 0;
                        foreach (string file in files)
                        {
                            if (MoveOne(file, target)) movedHere++;
                        }
                        moved += movedHere;
                        if (movedHere > 0) GordianLog.Info(LogCategories.Storage, $"Moved {movedHere} chat log file(s) from '{characterDir}' to '{target}'.");
                        GordianStorage.DeleteIfEmpty(characterDir);
                    }
                    GordianStorage.DeleteIfEmpty(serverDir);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                GordianLog.Warn(LogCategories.Storage, $"Could not look through the old chat logs in '{chatRoot}': {ex.Message}. The rest stay where they are.");
            }
            return moved;
        }

        /// <summary>
        /// Moves one day file into <paramref name="folder"/>. A day the folder already has keeps its file, and the old one
        /// moves in beside it as <c>yyyy-MM-dd_2.log</c> (then <c>_3</c>...), the numbering <see cref="RollingLogFile"/>
        /// uses. Files are never merged, so a day file the writer thread has open is never written to here, and no line is
        /// lost or repeated.
        /// </summary>
        private static bool MoveOne(string from, string folder)
        {
            string day = Path.GetFileNameWithoutExtension(from);
            string to = Path.Combine(folder, day + ".log");
            try
            {
                Directory.CreateDirectory(folder);
                for (int index = 2; File.Exists(to); index++) to = Path.Combine(folder, $"{day}_{index.ToString(CultureInfo.InvariantCulture)}.log");
                File.Move(from, to);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                GordianLog.Warn(LogCategories.Storage, $"Could not move the chat log '{from}' to '{to}': {ex.Message}. It stays where it is.");
                return false;
            }
        }

        public void Flush()
        {
            foreach (var (_, writer) in _open.Values) writer.Flush();
        }

        public void Dispose()
        {
            foreach (var (_, writer) in _open.Values)
            {
                try { writer.Dispose(); } catch (IOException) { }
            }
            _open.Clear();
        }
    }
}
