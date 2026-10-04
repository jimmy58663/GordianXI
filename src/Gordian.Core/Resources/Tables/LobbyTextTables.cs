// src/Gordian.Core/Resources/Tables/LobbyTextTables.cs
using System;
using System.Collections.Generic;
using System.IO;

namespace Gordian.Core.Resources.Tables
{
    /// <summary>
    /// The lobby's own text, read from the retail DATs (nothing is copied into the client):
    /// <list type="bullet">
    /// <item>ROM/165/71 (<c>XISTRING</c>, English): lobby status lines ("Acquiring character list."), the help bar's
    /// lines for each button, prompts ("The data will be lost forever. Proceed?") and the nation descriptions. The
    /// Japanese set is ROM/165/55; ROM/97/36 is an older English snapshot of the same table (XiPackets lobby/Protocol.md
    /// names ROM/97/36 as the lobby status messages).</item>
    /// <item>ROM/165/70 (<c>d_msg</c>, English): the lobby error messages, shown for S2C 0x04 codes plus 3000.</item>
    /// </list>
    /// Indices were read from the retail files (2026-10-04); see docs/design/character-lobby.md.
    /// </summary>
    public sealed class LobbyTextTables
    {
        public static readonly string StatusTablePath = Path.Combine("ROM", "165", "71.DAT");
        public static readonly string ErrorTablePath = Path.Combine("ROM", "165", "70.DAT");

        // ROM/165/71 rows.
        public const int AcquiringPlayerData = 6;
        public const int AcquiringServerData = 7;
        public const int AcquiringCharacterList = 8;
        public const int NotifyingLobbyOfChoice = 10;
        public const int RegisteringCharacter = 22;
        public const int CheckingNameAndWorldPass = 23;
        public const int CheckingName = 24;
        public const int DeletingFromLobby = 28;
        /// <summary>The nation title and description pairs: San d'Oria 29/30, Bastok 40/41, Windurst 51/52.</summary>
        public const int SandoriaTitle = 29, BastokTitle = 40, WindurstTitle = 51;
        public const int HelpSelectCharacterMenu = 99;
        public const int HelpCreateCharacterMenu = 100;
        public const int HelpDeleteCharacterMenu = 101;
        public const int HelpBack = 102;
        public const int HelpSelectCharacterToPlay = 104;
        public const int HelpSelectCharacterToDelete = 105;
        public const int HelpChooseRace = 106;
        public const int HelpChooseFace = 107;
        public const int HelpChooseHair = 108;
        public const int HelpChooseSize = 109;
        public const int HelpChooseJob = 110;
        public const int HelpWorldPass = 111;
        public const int HelpChooseNation = 112;
        public const int NeedRenameLine1 = 139, NeedRenameLine2 = 140;
        public const int DeleteConfirm = 172;
        public const int RegisterAndBeginLine1 = 173, RegisterAndBeginLine2 = 174;
        public const int NotifyingLobbyOfSelections = 194;
        public const int CannotCreateMoreCharacters = 196;
        public const int NeedRename = 216;
        public const int DeleteCancelHint = 229;

        // ROM/165/70 rows.
        public const int ErrorCodeLine = 1;
        public const int ErrorNameTooShort = 7;

        /// <summary>
        /// ROM/165/70 row of each server error code (the client's code + 3000), for the codes LandSandBoat sends and a
        /// few the client raises itself; matched by text against XiPackets lobby/Protocol.md's message list.
        /// The table does not hold every code (3117 has no row), so this is not a formula.
        /// </summary>
        private static readonly Dictionary<int, int> ErrorRows = new()
        {
            [100] = 2, [101] = 3, [102] = 4, [103] = 5, [109] = 6, [110] = 7, [113] = 10, [115] = 12,
            [201] = 17, [202] = 18, [203] = 19, [208] = 22, [206] = 23,
            [301] = 27, [302] = 28, [303] = 29, [304] = 30, [305] = 31, [307] = 33, [308] = 34, [309] = 35,
            [312] = 38, [313] = 39, [314] = 40, [318] = 44, [321] = 47, [322] = 48, [327] = 52,
            [330] = 54, [331] = 55, [332] = 56, [335] = 58,
        };

        private readonly XiStringTable? _status;
        private readonly DMsgStringTable? _errors;

        public LobbyTextTables(XiStringTable? status, DMsgStringTable? errors)
        {
            _status = status;
            _errors = errors;
        }

        /// <summary>Reads both tables through <paramref name="loadDat"/> (a game-relative path to bytes).</summary>
        public static LobbyTextTables Load(Func<string, byte[]?> loadDat)
        {
            ArgumentNullException.ThrowIfNull(loadDat);
            XiStringTable? status = null;
            DMsgStringTable? errors = null;
            try { if (loadDat(StatusTablePath) is { } s) status = XiStringTable.Parse(s); } catch { }
            try { if (loadDat(ErrorTablePath) is { } e) errors = DMsgStringTable.Parse(e); } catch { }
            return new LobbyTextTables(status, errors);
        }

        public bool HasStatusText => _status != null;

        /// <summary>A ROM/165/71 line, or an empty string when the table is missing.</summary>
        public string Status(int index) => _status?[index] ?? string.Empty;

        /// <summary>A ROM/165/70 row, or an empty string when the table is missing.</summary>
        public string ErrorRow(int row) =>
            _errors != null && (uint)row < (uint)_errors.Count ? _errors.Records[row].PrimaryText : string.Empty;

        /// <summary>
        /// The lines retail shows for a lobby error: the message for the code (when the table has one) and the
        /// "Error code: FFXI-%04d" line. <paramref name="code"/> is the server's code (0x04 <c>err_code</c>).
        /// </summary>
        public IReadOnlyList<string> ErrorLines(int code)
        {
            var lines = new List<string>();
            if (ErrorRows.TryGetValue(code, out int row) && ErrorRow(row) is { Length: > 0 } message)
            {
                lines.AddRange(message.Split('\n'));
            }
            string codeLine = ErrorRow(ErrorCodeLine);
            int shown = code + 3000;
            lines.Add(codeLine.Contains("%04d", StringComparison.Ordinal) ? codeLine.Replace("%04d", shown.ToString("D4")) : $"Error code: FFXI-{shown:D4}");
            return lines;
        }
    }
}
