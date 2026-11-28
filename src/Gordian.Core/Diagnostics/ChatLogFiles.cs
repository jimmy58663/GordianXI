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
        /// older builds kept a LandSandBoat host's casing and <see cref="Profiles.AccountProfile.ServerFolderName"/> now
        /// lower-cases it. A file for a day the new folder already has is appended to it, so no line is lost. Empty old
        /// folders are removed. A file or folder that can't be read or moved stays where it is (logged) and is tried again at
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
                            if (MoveOne(file, Path.Combine(target, Path.GetFileName(file)))) movedHere++;
                        }
                        moved += movedHere;
                        if (movedHere > 0) GordianLog.Info(LogCategories.Storage, $"Moved {movedHere} chat log file(s) from '{characterDir}' to '{target}'.");
                        CharacterConfigStorage.DeleteIfEmpty(characterDir);
                    }
                    CharacterConfigStorage.DeleteIfEmpty(serverDir);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                GordianLog.Warn(LogCategories.Storage, $"Could not look through the old chat logs in '{chatRoot}': {ex.Message}. The rest stay where they are.");
            }
            return moved;
        }

        /// <summary>
        /// Moves one day file, or appends it to the new folder's file for that day. When appending, the old file is deleted
        /// before its text is added, so a delete that fails adds nothing and the retry at the next login can't repeat lines;
        /// if the append then fails, the text is written back to the old file.
        /// </summary>
        private static bool MoveOne(string from, string to)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                if (File.Exists(to))
                {
                    string text = File.ReadAllText(from, Utf8NoBom);
                    File.Delete(from);
                    try
                    {
                        File.AppendAllText(to, text, Utf8NoBom);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        File.WriteAllText(from, text, Utf8NoBom);
                        throw;
                    }
                }
                else
                {
                    File.Move(from, to);
                }
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
