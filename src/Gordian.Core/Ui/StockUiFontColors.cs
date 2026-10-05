// src/Gordian.Core/Ui/StockUiFontColors.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using Gordian.Core.Resources.Ui;

namespace Gordian.Core.Ui
{
    /// <summary>The four lists of the config menu's Font Colors page (<c>conftxtc</c>: Chat, For Self, For Others, System).</summary>
    public enum StockUiFontColorCategory
    {
        Chat,
        ForSelf,
        ForOthers,
        System,
    }

    /// <summary>
    /// One row of the Font Colors page: a log text colour the player can set. The rows are the message types of the
    /// config row table (ROM/165/74 36-62, 196), the same text the Log page lists.
    /// </summary>
    public enum StockUiFontColorId
    {
        Say,
        Shout,
        Tell,
        Party,
        Linkshell,
        Linkshell2,
        Unity,
        Emote,
        /// <summary>Messages ("Message"); sample "Message: Color of Friend List messages".</summary>
        Message,
        /// <summary>NPC conversations; sample "Townswoman : Color of NPC text".</summary>
        Npc,
        Yell,
        AssistJ,
        AssistE,

        /// <summary>HP/MP you recover ("Player recovers 10 HP.").</summary>
        SelfRecover,
        /// <summary>HP/MP you lose ("Enemy hits Player for 1 point of damage.").</summary>
        SelfDamage,
        SelfBeneficial,
        SelfDetrimental,
        /// <summary>Effects you resist ("No effect on Player.").</summary>
        SelfNoEffect,
        /// <summary>Actions you evade ("Enemy misses Player.").</summary>
        SelfMiss,

        OthersRecover,
        OthersDamage,
        OthersBeneficial,
        OthersDetrimental,
        /// <summary>Effects others resist ("No effect on Enemy.").</summary>
        OthersNoEffect,
        /// <summary>Actions others evade ("Enemy misses Ally.").</summary>
        OthersMiss,

        /// <summary>Standard battle messages ("Player starts casting Spell.").</summary>
        StandardBattle,
        /// <summary>Calls for help ("Player calls for help!").</summary>
        CallForHelp,
        /// <summary>Basic system messages ("Player's craft skill rises 0.1 points!").</summary>
        BasicSystem,
    }

    /// <summary>A text colour as the client stores it: one byte per channel in the 0x80 half scale (0x80 = the glyph's own colour).</summary>
    public readonly record struct StockUiRgb(byte R, byte G, byte B)
    {
        /// <summary>The renderer's half-scale vertex colour (alpha 0x80 = opaque).</summary>
        public UiColor ToUiColor() => new(R, G, B, 0x80);

        /// <summary>Packed as 0xRRGGBB (the settings file's form).</summary>
        public int Packed => (R << 16) | (G << 8) | B;

        public static StockUiRgb FromPacked(int packed) => new((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);

        /// <summary>"RRGGBB" hex, as the settings file stores it.</summary>
        public override string ToString() => Packed.ToString("X6", CultureInfo.InvariantCulture);

        public static bool TryParse(string? text, out StockUiRgb rgb)
        {
            rgb = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim().TrimStart('#');
            if (t.Length != 6 || !int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int packed)) return false;
            rgb = FromPacked(packed);
            return true;
        }
    }

    /// <summary>
    /// One Font Colors row: its list; its label (the message type, config row table index <paramref name="LabelIndex"/>,
    /// shown in the list) and its sample (index <paramref name="SampleIndex"/>, shown in the colour in the box above
    /// the list) in ROM/165/74, with the English text as a fallback when the table is not read; where the retail client
    /// stores it in <c>USER/&lt;id&gt;/cnf.dat</c>; and its retail default.
    /// </summary>
    public sealed record StockUiFontColorEntry(StockUiFontColorId Id, StockUiFontColorCategory Category, int LabelIndex, string Label,
        int SampleIndex, string Sample, int CnfOffset, StockUiRgb Default);

    /// <summary>
    /// The Font Colors page's rows and their retail defaults.
    /// <para>
    /// <b>Rows.</b> The lists and their order are retail's (the maintainer's screenshots of all four lists,
    /// 2026-10-04): Chat lists Say, Tell, Party, Linkshell, Linkshell 2, Assist J, Assist E, Unity, Emotes, Messages, NPC
    /// conversations, Shout, Yell; the list shows the message type (config row table 36-62, 196, white) and the box
    /// above it shows the selected row's sample (63-87, 197, 204, 205) in the row's colour.
    /// </para>
    /// <para>
    /// <b>Where the defaults come from.</b> The retail client keeps the colours in <c>USER/&lt;id&gt;/cnf.dat</c>
    /// (744 bytes): a table of 23 four-byte entries at 0x50 and five more entries further on (0x22C, 0x26C, 0x278,
    /// 0x2D8, 0x2DC). Each entry is <b>B, G, R, 0x80</b>, one byte per slider (0-255, 0x80 = the default white).
    /// Settled by the maintainer's diff (2026-10-05): setting Say to R 255, G 128, B 0 in retail changed only 0x50
    /// (0x80 to 0x00) and 0x52 (0x80 to 0xFF). The defaults are the bytes of the four characters created on the
    /// maintainer's install in 2026-09 (<c>USER/3</c>..<c>USER/6</c>, identical files, never edited).
    /// </para>
    /// <para>
    /// <b>Entry to row (confirmed in retail 2026-10-05, #53):</b> the 23-entry table is the original rows in groups:
    /// 0-7 chat (Say, Shout, Tell, Party, Linkshell, Emotes, Messages, NPC conversations), 8-13 For Self and 14-19 For
    /// Others (each in list order), 20-22 System (Standard battle, Calls for help, Basic system); the five later
    /// entries are the chat types added since: Yell 0x22C, Unity 0x26C, Linkshell 2 0x278, Assist J 0x2D8, Assist E
    /// 0x2DC. Evidence: the rows with a colour of their own match their entries in the maintainer's retail editor
    /// screenshots; the eight rows whose defaults are not unique were set to unique colours in retail and the changed
    /// cnf.dat bytes named their entries (integration commits 8dbf80e / cea3e5c: 0x6C NPC, 0x78 / 0x7C / 0x84 the white
    /// For Self rows, 0x90 / 0x94 the white For Others rows, 0x22C Yell, 0x2D8 Assist J); Assist E is the one left.
    /// </para>
    /// <para>
    /// <b>Beyond xi-tools / LandSandBoat:</b> neither documents cnf.dat or the Font Colors table; this is our reading
    /// of the retail files.
    /// </para>
    /// </summary>
    public static class StockUiFontColors
    {
        /// <summary>Offset of the 23-entry colour table in cnf.dat, and its entry size.</summary>
        public const int CnfTableOffset = 0x50, CnfEntrySize = 4, CnfTableEntries = 23;

        private static StockUiRgb Bgr(byte b, byte g, byte r) => new(r, g, b);

        private static int Table(int index) => CnfTableOffset + index * CnfEntrySize;

        private static StockUiFontColorEntry Row(StockUiFontColorId id, StockUiFontColorCategory category, int labelIndex, string label,
            int sampleIndex, string sample, int offset, StockUiRgb rgb) => new(id, category, labelIndex, label, sampleIndex, sample, offset, rgb);

        /// <summary>Every row, in page order within each list.</summary>
        public static IReadOnlyList<StockUiFontColorEntry> Entries { get; } = new[]
        {
            Row(StockUiFontColorId.Say, StockUiFontColorCategory.Chat, 36, "Immediate vicinity (\"Say\")", 63, "Player: Color of \"say\" text", Table(0), Bgr(0x80, 0x80, 0x80)),
            Row(StockUiFontColorId.Tell, StockUiFontColorCategory.Chat, 38, "Tell target only (\"Tell\")", 65, ">>Player: Color of \"tell\" text", Table(2), Bgr(0xA0, 0x40, 0xA0)),
            Row(StockUiFontColorId.Party, StockUiFontColorCategory.Chat, 39, "All party members (\"Party\")", 66, "(Player): Color of \"party\" text", Table(3), Bgr(0xA0, 0xC0, 0x20)),
            Row(StockUiFontColorId.Linkshell, StockUiFontColorCategory.Chat, 40, "Linkshell group (\"Linkshell\")", 67, "<Player>: Color of \"linkshell\" text", Table(4), Bgr(0x60, 0xFF, 0x50)),
            Row(StockUiFontColorId.Linkshell2, StockUiFontColorCategory.Chat, 41, "Linkshell group 2 (\"Linkshell 2\")", 68, "<Player>: Color of \"linkshell 2\" text", 0x278, Bgr(0x00, 0xCC, 0x00)),
            Row(StockUiFontColorId.AssistJ, StockUiFontColorCategory.Chat, 42, "Assistance in Japanese (\"Assist J\")", 204, "Sample(J): AssistJ text color", 0x2D8, Bgr(0xFF, 0x50, 0x00)),
            Row(StockUiFontColorId.AssistE, StockUiFontColorCategory.Chat, 43, "Assistance in English (\"Assist E\")", 205, "Sample(E): AssistE text color", 0x2DC, Bgr(0xFF, 0x70, 0x00)),
            Row(StockUiFontColorId.Unity, StockUiFontColorCategory.Chat, 44, "Unity group (\"Unity\")", 69, "<Player>: Color of \"Unity\" text", 0x26C, Bgr(0x3F, 0xAF, 0xFF)),
            Row(StockUiFontColorId.Emote, StockUiFontColorCategory.Chat, 45, "Emotes", 70, "Player's and other characters' emote color", Table(5), Bgr(0xA0, 0x50, 0x60)),
            Row(StockUiFontColorId.Message, StockUiFontColorCategory.Chat, 46, "Messages (\"Message\")", 71, "Message: Color of Friend List messages", Table(6), Bgr(0xD0, 0xD0, 0xA0)),
            Row(StockUiFontColorId.Npc, StockUiFontColorCategory.Chat, 47, "NPC conversations", 72, "Townswoman : Color of NPC text", Table(7), Bgr(0x80, 0x80, 0x80)),
            Row(StockUiFontColorId.Shout, StockUiFontColorCategory.Chat, 37, "Wide area (\"Shout\")", 64, "Player: Color of \"shout\" text", Table(1), Bgr(0x40, 0x50, 0xA0)),
            Row(StockUiFontColorId.Yell, StockUiFontColorCategory.Chat, 196, "Extremely wide area (\"Yell\")", 197, "Player: Color of \"yell\" text", 0x22C, Bgr(0x30, 0x40, 0xA0)),

            Row(StockUiFontColorId.SelfRecover, StockUiFontColorCategory.ForSelf, 48, "HP/MP you recover", 73, "Player recovers 10 HP.", Table(8), Bgr(0xC0, 0x90, 0x60)),
            Row(StockUiFontColorId.SelfDamage, StockUiFontColorCategory.ForSelf, 49, "HP/MP you lose", 74, "Enemy hits Player for 1 point of damage.", Table(9), Bgr(0x40, 0x40, 0xA0)),
            Row(StockUiFontColorId.SelfBeneficial, StockUiFontColorCategory.ForSelf, 50, "Beneficial effects you are granted", 75, "Player gains beneficial effect.", Table(10), Bgr(0x80, 0x80, 0x80)),
            Row(StockUiFontColorId.SelfDetrimental, StockUiFontColorCategory.ForSelf, 51, "Detrimental effects you receive", 76, "Player receives detrimental effect.", Table(11), Bgr(0x80, 0x80, 0x80)),
            Row(StockUiFontColorId.SelfNoEffect, StockUiFontColorCategory.ForSelf, 52, "Effects you resist", 77, "No effect on Player.", Table(12), Bgr(0x50, 0x80, 0x80)),
            Row(StockUiFontColorId.SelfMiss, StockUiFontColorCategory.ForSelf, 53, "Actions you evade", 78, "Enemy misses Player.", Table(13), Bgr(0x80, 0x80, 0x80)),

            Row(StockUiFontColorId.OthersRecover, StockUiFontColorCategory.ForOthers, 54, "HP/MP others recover", 79, "Ally recovers 10 HP.", Table(14), Bgr(0xF0, 0xC0, 0x90)),
            Row(StockUiFontColorId.OthersDamage, StockUiFontColorCategory.ForOthers, 55, "HP/MP others lose", 80, "Enemy hits Ally for 10 points of damage.", Table(15), Bgr(0x80, 0x80, 0xC0)),
            Row(StockUiFontColorId.OthersBeneficial, StockUiFontColorCategory.ForOthers, 56, "Beneficial effects others are granted", 81, "Ally gains beneficial effect.", Table(16), Bgr(0x80, 0x80, 0x80)),
            Row(StockUiFontColorId.OthersDetrimental, StockUiFontColorCategory.ForOthers, 57, "Detrimental effects others receive", 82, "Ally receives detrimental effect.", Table(17), Bgr(0x80, 0x80, 0x80)),
            Row(StockUiFontColorId.OthersNoEffect, StockUiFontColorCategory.ForOthers, 58, "Effects others resist", 83, "No effect on Enemy.", Table(18), Bgr(0x40, 0x80, 0xA0)),
            Row(StockUiFontColorId.OthersMiss, StockUiFontColorCategory.ForOthers, 59, "Actions others evade", 84, "Enemy misses Ally.", Table(19), Bgr(0x70, 0x70, 0x70)),

            Row(StockUiFontColorId.StandardBattle, StockUiFontColorCategory.System, 60, "Standard battle messages", 85, "Player starts casting Spell.", Table(20), Bgr(0x10, 0x80, 0x80)),
            Row(StockUiFontColorId.CallForHelp, StockUiFontColorCategory.System, 61, "Calls for help", 86, "Player calls for help!", Table(21), Bgr(0xD0, 0x60, 0xC0)),
            Row(StockUiFontColorId.BasicSystem, StockUiFontColorCategory.System, 62, "Basic system messages", 87, "Player's craft skill rises 0.1 points!", Table(22), Bgr(0x50, 0xC0, 0xC0)),
        };

        private static readonly Dictionary<StockUiFontColorId, StockUiFontColorEntry> ById = BuildIndex();

        private static Dictionary<StockUiFontColorId, StockUiFontColorEntry> BuildIndex()
        {
            var d = new Dictionary<StockUiFontColorId, StockUiFontColorEntry>();
            foreach (var e in Entries) d[e.Id] = e;
            return d;
        }

        public static StockUiFontColorEntry Get(StockUiFontColorId id) => ById[id];

        /// <summary>The rows of one list, in page order.</summary>
        public static IReadOnlyList<StockUiFontColorEntry> InCategory(StockUiFontColorCategory category)
        {
            var list = new List<StockUiFontColorEntry>();
            foreach (var e in Entries) if (e.Category == category) list.Add(e);
            return list;
        }

        /// <summary>A config row table text (ROM/165/74) through <paramref name="table"/>, else the English fallback.</summary>
        public static string Text(Func<int, string?>? table, int index, string fallback)
        {
            string? text = table?.Invoke(index);
            return string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
        }

        /// <summary>
        /// Reads every colour from a retail <c>cnf.dat</c> (B, G, R, 0x80 per entry); entries past the end of a short
        /// file are left out. Read-only: the USER folder is never written.
        /// </summary>
        public static Dictionary<StockUiFontColorId, StockUiRgb> ReadCnf(ReadOnlySpan<byte> cnf)
        {
            var colors = new Dictionary<StockUiFontColorId, StockUiRgb>();
            foreach (var e in Entries)
            {
                if (e.CnfOffset < 0 || e.CnfOffset + CnfEntrySize > cnf.Length) continue;
                colors[e.Id] = Bgr(cnf[e.CnfOffset], cnf[e.CnfOffset + 1], cnf[e.CnfOffset + 2]);
            }
            return colors;
        }

        /// <summary>The Font Colors row a chat channel's lines are drawn with, or null for a channel with a fixed colour.</summary>
        public static StockUiFontColorId? ForChannel(ChatLogChannel channel) => channel switch
        {
            ChatLogChannel.Say => StockUiFontColorId.Say,
            ChatLogChannel.Shout => StockUiFontColorId.Shout,
            ChatLogChannel.Yell => StockUiFontColorId.Yell,
            ChatLogChannel.Tell => StockUiFontColorId.Tell,
            ChatLogChannel.Party => StockUiFontColorId.Party,
            ChatLogChannel.Linkshell => StockUiFontColorId.Linkshell,
            ChatLogChannel.Linkshell2 => StockUiFontColorId.Linkshell2,
            ChatLogChannel.Unity => StockUiFontColorId.Unity,
            ChatLogChannel.AssistJ => StockUiFontColorId.AssistJ,
            ChatLogChannel.AssistE => StockUiFontColorId.AssistE,
            ChatLogChannel.Emote => StockUiFontColorId.Emote,
            ChatLogChannel.Dialog => StockUiFontColorId.Npc,
            ChatLogChannel.Message => StockUiFontColorId.Message,
            _ => null,
        };

        /// <summary>
        /// The fixed colour of lines without a Font Colors row (half scale): violet, about (200, 100, 255), for server
        /// text sent as chat (the welcome message, a retail capture 2026-09-27); client notices pale blue and client
        /// errors red (GordianXI's own lines); white otherwise.
        /// </summary>
        public static UiColor FixedColor(ChatLogChannel channel) => channel switch
        {
            ChatLogChannel.ServerMessage => new UiColor(0x64, 0x32, 0x7F, 0x7F),
            ChatLogChannel.Notice => new UiColor(0x68, 0x70, 0x7F, 0x7F),
            ChatLogChannel.Error => new UiColor(0x7F, 0x48, 0x48, 0x7F),
            _ => new UiColor(0x7F, 0x7F, 0x7F, 0x7F),
        };
    }
}
