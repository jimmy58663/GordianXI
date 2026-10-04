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
    /// One row of the Font Colors page: a log text colour the player can set. Named after the row's sample text in
    /// the config row table (ROM/165/74, rows 63-72, 197, 204-205 for chat and 73-87 for the battle samples).
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
        /// <summary>"Message: Color of Friend List messages".</summary>
        Message,
        /// <summary>"Townswoman : Color of NPC text".</summary>
        Npc,
        Yell,
        AssistJ,
        AssistE,

        /// <summary>"Player recovers 10 HP."</summary>
        SelfRecover,
        /// <summary>"Enemy hits Player for 1 point of damage."</summary>
        SelfDamage,
        SelfBeneficial,
        SelfDetrimental,
        /// <summary>"No effect on Player."</summary>
        SelfNoEffect,
        /// <summary>"Enemy misses Player."</summary>
        SelfMiss,

        OthersRecover,
        OthersDamage,
        OthersBeneficial,
        OthersDetrimental,
        /// <summary>"No effect on Enemy."</summary>
        OthersNoEffect,
        /// <summary>"Enemy misses Ally."</summary>
        OthersMiss,

        /// <summary>"Player starts casting Spell."</summary>
        Casting,
        /// <summary>"Player calls for help!"</summary>
        CallForHelp,
        /// <summary>"Player's craft skill rises 0.1 points!"</summary>
        SkillUp,
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
    /// One Font Colors row: its list, the config row table index of its sample text (ROM/165/74), that text, where the
    /// retail client stores it in <c>USER/&lt;id&gt;/cnf.dat</c> and its retail default.
    /// </summary>
    public sealed record StockUiFontColorEntry(StockUiFontColorId Id, StockUiFontColorCategory Category, int RowTextIndex, string Label,
        int CnfOffset, StockUiRgb Default);

    /// <summary>
    /// The Font Colors page's rows and their retail defaults.
    /// <para>
    /// <b>Where the defaults come from.</b> The retail client keeps the colours in <c>USER/&lt;id&gt;/cnf.dat</c>
    /// (744 bytes): a table of 23 four-byte entries at 0x50 and five more entries further on (0x22C, 0x26C, 0x278,
    /// 0x2D8, 0x2DC). Each entry is stored <b>B, G, R, 0x80</b> (the in-memory order of a Direct3D colour), so
    /// <c>40 50 a0 80</c> is R 0xA0, G 0x50, B 0x40. The defaults below are the bytes of the four characters created on
    /// the maintainer's install in 2026-09 (<c>USER/3</c>..<c>USER/6</c>, identical files, never edited) and agree with
    /// every older character but one edited entry (0x70). The values are in the client's 0x80 half scale: the log
    /// glyphs are white, so 0x80 already draws white and larger values only brighten the glyphs' grey shading.
    /// </para>
    /// <para>
    /// <b>Provisional (to confirm by diffing cnf.dat after changing one colour in retail, #53):</b> which entry is
    /// which row. The 23-entry table is read as the page's original rows in the config row table's order (the ten chat
    /// rows 63-72, the six For Self rows 73-78, the six For Others rows 79-84, then "starts casting" 85), and the five
    /// later entries as the rows added since (calls for help, yell, skill-up, Assist J, Assist E, in file order, by
    /// what their colours look like). The B, G, R order is read from the colours: it makes Shout peach, Party cyan and
    /// Linkshell green, as retail draws them; R, G, B would make Party yellow-green and Shout light blue. The tell
    /// entry (<c>a0 40 a0</c>) matches the captured tell pink either way. Server messages (0x017 system kinds, the
    /// captured violet) are not a Font Colors row.
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

        /// <summary>Every row, in page order within each list.</summary>
        public static IReadOnlyList<StockUiFontColorEntry> Entries { get; } = new StockUiFontColorEntry[]
        {
            new(StockUiFontColorId.Say, StockUiFontColorCategory.Chat, 63, "Player: Color of \"say\" text", Table(0), Bgr(0x80, 0x80, 0x80)),
            new(StockUiFontColorId.Shout, StockUiFontColorCategory.Chat, 64, "Player: Color of \"shout\" text", Table(1), Bgr(0x40, 0x50, 0xA0)),
            new(StockUiFontColorId.Yell, StockUiFontColorCategory.Chat, 197, "Player: Color of \"yell\" text", 0x26C, Bgr(0x3F, 0xAF, 0xFF)),
            new(StockUiFontColorId.Tell, StockUiFontColorCategory.Chat, 65, ">>Player: Color of \"tell\" text", Table(2), Bgr(0xA0, 0x40, 0xA0)),
            new(StockUiFontColorId.Party, StockUiFontColorCategory.Chat, 66, "(Player): Color of \"party\" text", Table(3), Bgr(0xA0, 0xC0, 0x20)),
            new(StockUiFontColorId.Linkshell, StockUiFontColorCategory.Chat, 67, "<Player>: Color of \"linkshell\" text", Table(4), Bgr(0x60, 0xFF, 0x50)),
            new(StockUiFontColorId.Linkshell2, StockUiFontColorCategory.Chat, 68, "<Player>: Color of \"linkshell 2\" text", Table(5), Bgr(0xA0, 0x50, 0x60)),
            new(StockUiFontColorId.Unity, StockUiFontColorCategory.Chat, 69, "<Player>: Color of \"Unity\" text", Table(6), Bgr(0xD0, 0xD0, 0xA0)),
            new(StockUiFontColorId.AssistJ, StockUiFontColorCategory.Chat, 204, "Sample(J): AssistJ text color", 0x2D8, Bgr(0xFF, 0x50, 0x00)),
            new(StockUiFontColorId.AssistE, StockUiFontColorCategory.Chat, 205, "Sample(E): AssistE text color", 0x2DC, Bgr(0xFF, 0x70, 0x00)),
            new(StockUiFontColorId.Emote, StockUiFontColorCategory.Chat, 70, "Player's and other characters' emote color", Table(7), Bgr(0x80, 0x80, 0x80)),
            new(StockUiFontColorId.Message, StockUiFontColorCategory.Chat, 71, "Message: Color of Friend List messages", Table(8), Bgr(0xC0, 0x90, 0x60)),
            new(StockUiFontColorId.Npc, StockUiFontColorCategory.Chat, 72, "Townswoman : Color of NPC text", Table(9), Bgr(0x40, 0x40, 0xA0)),

            new(StockUiFontColorId.SelfRecover, StockUiFontColorCategory.ForSelf, 73, "Player recovers 10 HP.", Table(10), Bgr(0x80, 0x80, 0x80)),
            new(StockUiFontColorId.SelfDamage, StockUiFontColorCategory.ForSelf, 74, "Enemy hits Player for 1 point of damage.", Table(11), Bgr(0x80, 0x80, 0x80)),
            new(StockUiFontColorId.SelfBeneficial, StockUiFontColorCategory.ForSelf, 75, "Player gains beneficial effect.", Table(12), Bgr(0x50, 0x80, 0x80)),
            new(StockUiFontColorId.SelfDetrimental, StockUiFontColorCategory.ForSelf, 76, "Player receives detrimental effect.", Table(13), Bgr(0x80, 0x80, 0x80)),
            new(StockUiFontColorId.SelfNoEffect, StockUiFontColorCategory.ForSelf, 77, "No effect on Player.", Table(14), Bgr(0xF0, 0xC0, 0x90)),
            new(StockUiFontColorId.SelfMiss, StockUiFontColorCategory.ForSelf, 78, "Enemy misses Player.", Table(15), Bgr(0x80, 0x80, 0xC0)),

            new(StockUiFontColorId.OthersRecover, StockUiFontColorCategory.ForOthers, 79, "Ally recovers 10 HP.", Table(16), Bgr(0x80, 0x80, 0x80)),
            new(StockUiFontColorId.OthersDamage, StockUiFontColorCategory.ForOthers, 80, "Enemy hits Ally for 10 points of damage.", Table(17), Bgr(0x80, 0x80, 0x80)),
            new(StockUiFontColorId.OthersBeneficial, StockUiFontColorCategory.ForOthers, 81, "Ally gains beneficial effect.", Table(18), Bgr(0x40, 0x80, 0xA0)),
            new(StockUiFontColorId.OthersDetrimental, StockUiFontColorCategory.ForOthers, 82, "Ally receives detrimental effect.", Table(19), Bgr(0x70, 0x70, 0x70)),
            new(StockUiFontColorId.OthersNoEffect, StockUiFontColorCategory.ForOthers, 83, "No effect on Enemy.", Table(20), Bgr(0x10, 0x80, 0x80)),
            new(StockUiFontColorId.OthersMiss, StockUiFontColorCategory.ForOthers, 84, "Enemy misses Ally.", Table(21), Bgr(0xD0, 0x60, 0xC0)),

            new(StockUiFontColorId.Casting, StockUiFontColorCategory.System, 85, "Player starts casting Spell.", Table(22), Bgr(0x50, 0xC0, 0xC0)),
            new(StockUiFontColorId.CallForHelp, StockUiFontColorCategory.System, 86, "Player calls for help!", 0x22C, Bgr(0x30, 0x40, 0xA0)),
            new(StockUiFontColorId.SkillUp, StockUiFontColorCategory.System, 87, "Player's craft skill rises 0.1 points!", 0x278, Bgr(0x00, 0xCC, 0x00)),
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
            _ => null,
        };

        /// <summary>
        /// The fixed colour of lines without a Font Colors row (half scale): white for system and zone messages and
        /// unclassified combat lines; violet, about (200, 100, 255), for server text sent as chat (the welcome message,
        /// a retail capture 2026-09-27); client notices pale blue and client errors red (GordianXI's own lines).
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
