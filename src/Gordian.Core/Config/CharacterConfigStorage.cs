// src/Gordian.Core/Config/CharacterConfigStorage.cs
using System;
using System.IO;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Config
{
    /// <summary>
    /// One kind of per-character file kept under <see cref="CharacterConfigStorage"/>: its name in the character's folder
    /// and the flat folder older builds kept it in (<c>&lt;LegacyFolder&gt;/&lt;character&gt;.json</c> under
    /// <see cref="GordianStorage.RootDataDirectory"/>), which <see cref="CharacterConfigStorage.Resolve"/> moves it out of.
    /// </summary>
    /// <param name="FileName">The file's name in the character's folder, e.g. <c>macros.json</c>.</param>
    /// <param name="LegacyFolder">The folder an older build kept one file per character name in, e.g. <c>macros</c>.</param>
    public readonly record struct CharacterConfigFile(string FileName, string LegacyFolder);

    /// <summary>
    /// The per-character config folders (#639): <c>CharConfig/&lt;server&gt;/&lt;character&gt;/</c> under
    /// <see cref="GordianStorage.RootDataDirectory"/>, holding every file GordianXI keeps for one character (macros,
    /// equipment sets, UI layout and settings, synthesis history). The server is the launching profile's
    /// <see cref="Profiles.AccountProfile.ServerFolderName"/> (<c>Retail</c>, or a LandSandBoat profile's host), the same folder the chat logs use, so the
    /// same character name on two servers never shares a file. Both names go through
    /// <see cref="GordianStorage.SafeFolderName"/>, which keeps their casing.
    /// <para>
    /// Older builds kept each kind of file in its own flat folder named by character only. Those files carry no server, so
    /// the first character of that name to load one, on any server, takes it: <see cref="Resolve"/> moves it into that
    /// character's folder and logs the move. A character on another server with the same name then starts empty.
    /// </para>
    /// </summary>
    public static class CharacterConfigStorage
    {
        /// <summary>The top-level folder under <see cref="GordianStorage.RootDataDirectory"/>.</summary>
        public const string FolderName = "CharConfig";

        /// <summary>
        /// The folder for <paramref name="character"/> on <paramref name="server"/> under
        /// <see cref="GordianStorage.RootDataDirectory"/>; an empty name of either becomes <c>Unknown</c>. Not created here:
        /// a store creates it when it first saves.
        /// </summary>
        public static string GetDirectory(string? server, string? character) =>
            GetDirectory(GordianStorage.RootDataDirectory, server, character);

        /// <summary>The folder for <paramref name="character"/> on <paramref name="server"/> under <paramref name="dataRoot"/>.</summary>
        public static string GetDirectory(string dataRoot, string? server, string? character) =>
            Path.Combine(dataRoot, FolderName, GordianStorage.SafeFolderName(server), GordianStorage.SafeFolderName(character));

        /// <summary>
        /// The path of <paramref name="file"/> for <paramref name="character"/> on <paramref name="server"/> under
        /// <see cref="GordianStorage.RootDataDirectory"/>, moving an older build's file there first (see the other overload).
        /// </summary>
        public static string Resolve(CharacterConfigFile file, string? server, string? character) =>
            Resolve(GordianStorage.RootDataDirectory, file, server, character);

        /// <summary>
        /// The path of <paramref name="file"/> for <paramref name="character"/> on <paramref name="server"/> under
        /// <paramref name="dataRoot"/>. When it does not exist yet and an older build's file for the character's name does,
        /// that file is moved here first. If the move fails, the old path is returned so the character keeps its data this
        /// session; the move is tried again at the next load. A character with no name has no older file to take.
        /// </summary>
        public static string Resolve(string dataRoot, CharacterConfigFile file, string? server, string? character)
        {
            string path = Path.Combine(GetDirectory(dataRoot, server, character), file.FileName);
            if (File.Exists(path) || string.IsNullOrWhiteSpace(character)) return path;

            string legacyFolder = Path.Combine(dataRoot, file.LegacyFolder);
            string legacyPath = Path.Combine(legacyFolder, LegacyFileName(character) + ".json");
            if (!File.Exists(legacyPath)) return path;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.Move(legacyPath, path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                GordianLog.Warn(LogCategories.Storage, $"Could not move '{legacyPath}' to '{path}': {ex.Message}. Using the old file.");
                return File.Exists(legacyPath) ? legacyPath : path;
            }
            GordianLog.Info(LogCategories.Storage, $"Moved '{legacyPath}' to '{path}'");
            DeleteIfEmpty(legacyFolder);
            return path;
        }

        /// <summary>
        /// The file name an older build gave <paramref name="character"/>: trimmed, with the characters this platform rejects
        /// in a file name replaced by <c>_</c>.
        /// </summary>
        private static string LegacyFileName(string character)
        {
            string name = character.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        /// <summary>Removes an older build's flat folder once its last file has moved out.</summary>
        private static void DeleteIfEmpty(string folder)
        {
            try
            {
                if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0) Directory.Delete(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                GordianLog.Debug(LogCategories.Storage, $"Could not remove the empty folder '{folder}': {ex.Message}");
            }
        }
    }
}
