// src/Gordian.Core/Ui/RetailUserSettings.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Ui
{
    /// <summary>A character folder under the retail client's <c>USER</c> directory.</summary>
    public sealed record RetailUserFolder(string Name, string Path, DateTime LastModifiedUtc, bool HasConfig);

    /// <summary>
    /// The parts of a retail <c>cnf.dat</c> GordianXI can import: the Font Colors table and the Log page's routing.
    /// Layout and evidence: docs/ui/retail-user-files.md (our reading of the retail files; no public reference
    /// documents them).
    /// </summary>
    public sealed class RetailCnf
    {
        /// <summary>Size of every cnf.dat seen (15 files on the maintainer's install, 2026-09 / 10).</summary>
        public const int ExpectedSize = 744;

        /// <summary>The Log page's per-window masks: Window 1 at 0x290 / 0x294, Window 2 at 0x298 / 0x29C (two words each, complements).</summary>
        public const int LogWindow1Offset = 0x290, LogWindow2Offset = 0x298;

        public RetailCnf(IReadOnlyDictionary<StockUiFontColorId, StockUiRgb> fontColors, uint? window2Battle, uint? window2Other)
        {
            FontColors = fontColors;
            Window2Battle = window2Battle;
            Window2Other = window2Other;
        }

        public IReadOnlyDictionary<StockUiFontColorId, StockUiRgb> FontColors { get; }

        /// <summary>Window 2's two routing words (0x298 battle types, 0x29C chat and system types), when the file has them.</summary>
        public uint? Window2Battle { get; }
        public uint? Window2Other { get; }

        /// <summary>
        /// The battle word's bits, in order (provisional, see the doc): For Self rows 48-53, For Others rows 54-59,
        /// standard battle messages, calls for help.
        /// </summary>
        private static readonly ChatLogType[] BattleBits =
        {
            ChatLogType.SelfRecover, ChatLogType.SelfLose, ChatLogType.SelfBeneficial, ChatLogType.SelfDetrimental, ChatLogType.SelfResist, ChatLogType.SelfEvade,
            ChatLogType.OthersRecover, ChatLogType.OthersLose, ChatLogType.OthersBeneficial, ChatLogType.OthersDetrimental, ChatLogType.OthersResist, ChatLogType.OthersEvade,
            ChatLogType.StandardBattle, ChatLogType.CallsForHelp,
        };

        /// <summary>
        /// The other word's bits (provisional): basic system messages, the chat rows 36-47 in the config row table's
        /// order, then Yell (row 196, added later).
        /// </summary>
        private static readonly ChatLogType[] OtherBits =
        {
            ChatLogType.BasicSystem,
            ChatLogType.Say, ChatLogType.Shout, ChatLogType.Tell, ChatLogType.Party, ChatLogType.Linkshell, ChatLogType.Linkshell2,
            ChatLogType.AssistJ, ChatLogType.AssistE, ChatLogType.Unity, ChatLogType.Emote, ChatLogType.Message, ChatLogType.NpcConversation,
            ChatLogType.Yell,
        };

        /// <summary>Window 2's types as GordianXI's <see cref="StockUiSettingKey.LogWindow2Types"/> mask, or null when the file has no routing.</summary>
        public uint? LogWindow2Types
        {
            get
            {
                if (Window2Battle is not { } battle || Window2Other is not { } other) return null;
                uint mask = 0;
                for (int i = 0; i < BattleBits.Length; i++) if ((battle & (1u << i)) != 0) mask |= StockUiChatLog.Bit(BattleBits[i]);
                for (int i = 0; i < OtherBits.Length; i++) if ((other & (1u << i)) != 0) mask |= StockUiChatLog.Bit(OtherBits[i]);
                return mask;
            }
        }

        /// <summary>Decodes the importable parts of a cnf.dat; fields past the end of a short file are left out.</summary>
        public static RetailCnf Parse(ReadOnlySpan<byte> cnf)
        {
            var colors = StockUiFontColors.ReadCnf(cnf);
            uint? battle = null, other = null;
            if (cnf.Length >= LogWindow2Offset + 8)
            {
                battle = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(cnf.Slice(LogWindow2Offset, 4));
                other = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(cnf.Slice(LogWindow2Offset + 4, 4));
            }
            return new RetailCnf(colors, battle, other);
        }
    }

    /// <summary>
    /// Reads the retail client's per-character settings from <c>FINAL FANTASY XI/USER/&lt;id&gt;/</c> and imports what
    /// GordianXI understands into a character's <see cref="StockUiSettings"/> (#51). Read-only: the game folder is
    /// never written, and nothing is imported unless the player asks (<c>/importretail</c>); the import overwrites
    /// the imported settings.
    /// </summary>
    public static class RetailUserSettings
    {
        /// <summary>The retail folder and file names, in their exact case (the lookups are case-sensitive on Unix).</summary>
        public const string UserFolderName = "USER", ConfigFileName = "cnf.dat";

        public static string UserDirectory(string gameDirectory) => Path.Combine(gameDirectory, UserFolderName);

        /// <summary>
        /// The folder name retail uses for a character: what looks like its content id in lowercase hex without
        /// leading zeros (<c>167dc34</c>; LSB characters 1 and 2 have <c>1</c> and <c>2</c>). To confirm (#51).
        /// </summary>
        public static string FolderNameFor(uint characterId) => characterId.ToString("x", CultureInfo.InvariantCulture);

        /// <summary>Every character folder under USER, newest first.</summary>
        public static IReadOnlyList<RetailUserFolder> ListFolders(string gameDirectory)
        {
            var folders = new List<RetailUserFolder>();
            string user = UserDirectory(gameDirectory);
            if (!Directory.Exists(user)) return folders;
            try
            {
                foreach (string dir in Directory.EnumerateDirectories(user))
                {
                    string config = Path.Combine(dir, ConfigFileName);
                    bool has = File.Exists(config);
                    var modified = has ? File.GetLastWriteTimeUtc(config) : Directory.GetLastWriteTimeUtc(dir);
                    folders.Add(new RetailUserFolder(Path.GetFileName(dir), dir, modified, has));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                GordianLog.Warning("UI", $"Could not list the retail USER folders in '{user}': {ex.Message}");
            }
            folders.Sort((a, b) => b.LastModifiedUtc.CompareTo(a.LastModifiedUtc));
            return folders;
        }

        /// <summary>Reads a character folder's cnf.dat, or null when it has none or it cannot be read.</summary>
        public static RetailCnf? ReadConfig(string folderPath)
        {
            string path = Path.Combine(folderPath, ConfigFileName);
            if (!File.Exists(path)) return null;
            try
            {
                return RetailCnf.Parse(File.ReadAllBytes(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                GordianLog.Warning("UI", $"Could not read '{path}': {ex.Message}");
                return null;
            }
        }

        /// <summary>Overwrites the settings GordianXI can import (every Font Colors row in the file, the Log page's routing); returns what was set.</summary>
        public static IReadOnlyList<string> Apply(RetailCnf cnf, StockUiSettings settings)
        {
            var applied = new List<string>();
            int colors = 0;
            foreach (var (id, rgb) in cnf.FontColors)
            {
                settings.SetFontColor(id, rgb);
                colors++;
            }
            if (colors > 0) applied.Add($"{colors} font colors");
            if (cnf.LogWindow2Types is { } mask)
            {
                settings.SetValue(StockUiSettingKey.LogWindow2Types, (int)mask);
                applied.Add("log window routing");
            }
            return applied;
        }
    }
}
