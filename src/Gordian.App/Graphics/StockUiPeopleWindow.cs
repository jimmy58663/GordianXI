// src/Gordian.App/Graphics/StockUiPeopleWindow.cs
using System;
using Gordian.Core.Resources.Ui;
using Gordian.Core.World;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The people lists' rows (#489, #490: Member List, Find Member, Level Sync, the search results, a linkshell's members, the
    /// friend list) as retail draws them in <c>scresult</c> and its sister windows (round 1 image-26, image-28, image-33,
    /// image-44, all at about 1:1 to the 366-px frame): the jobs and race in the log font from x 21 ("[MNK99/RDM49]Elv"), the
    /// mission rank on its nation's colour at x 132, the name in the menu font from x 154, yellow for your party's leader, cyan
    /// for its members and white for the rest, the seeker's languages at x 247 ("E F") and the area at the right, blank for
    /// your own. A search's list starts with the "Search Results [3]  ▶more..." bar. A linkshell's members show their rank's
    /// item icon (shell, sack, pearl) tinted with the shell's colour at the row's left.
    /// </summary>
    internal static class StockUiPeopleWindow
    {
        private const float JobsX = 21, RankBoxX = 132, RankBoxWidth = 10, RankBoxHeight = 13, NameX = 154, LanguagesX = 247;
        private const float HeaderTitleX = 9, HeaderCountX = 157, HeaderMoreX = 281;
        private const float IconX = 3, IconSize = 16;

        /// <summary>The names' colours (half scale): yellow leader, cyan party (round 1 image-28: about FFE070 and 90E8FF).</summary>
        private static readonly UiColor LeaderTint = new(0x80, 0x70, 0x38, 0x80), PartyTint = new(0x48, 0x74, 0x80, 0x80);

        /// <summary>The rank boxes' nation colours (half scale): San d'Oria red (round 1 image-28), Bastok blue, Windurst green (PROVISIONAL).</summary>
        private static readonly UiColor[] NationTints =
        {
            new(0x58, 0x10, 0x14, 0x80), new(0x14, 0x28, 0x60, 0x80), new(0x18, 0x48, 0x18, 0x80),
        };

        private static readonly UiColor HeaderTint = new(0x80, 0x70, 0x60, 0x80);

        public static void Draw(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu, StockUiPlacement placement,
            UiMenuButton firstRow, UiMenuButton? secondRow, long timestamp)
        {
            var frame = menu.Menu.Frame;
            float s = placement.Scale;
            float pitch = StockUiMenuWindow.RowPitch(firstRow, secondRow);
            float first = StockUiMenuWindow.ScrolledFirstRow(menu, timestamp);
            float areaX = placement.X + firstRow.X * s, areaY = placement.Y + firstRow.Y * s;
            renderer.SetClip(placement.X + StockUiMenuWindow.ListClipInset * s, areaY, (frame.Width - StockUiMenuWindow.ListClipInset) * s, menu.VisibleRows * pitch * s);
            int selected = menu.EntryIndex(menu.SelectedButtonId);
            int from = Math.Max(0, (int)Math.Floor(first) - 1), to = Math.Min(menu.Rows.Count - 1, (int)Math.Ceiling(first) + menu.VisibleRows);
            float logY = (firstRow.Height - StockUiLogFont.CellHeight) * 0.5f * s;
            float fontY = (firstRow.Height * s - font.LineHeight * s) * 0.5f;
            for (int i = from; i <= to; i++)
            {
                float ry = areaY + (i - first) * pitch * s;
                if (i == selected || (i == 0 && menu.PeopleOffset > 0))
                    StockUiMenuWindow.DrawMapRowBar(renderer, placement.X + StockUiMenuWindow.ListClipInset * s, ry,
                        placement.X + (frame.Width - StockUiMenuWindow.ScrollbarInset) * s, firstRow.Height * s);
                if (i < menu.PeopleOffset)
                {
                    // The search's bar: its title, the count and "more...".
                    var header = menu.Rows[i];
                    UiColor? tint = i == selected ? StockUiMenuWindow.SelectedGlyphTint : HeaderTint;
                    renderer.DrawText(font, header.Text, areaX + HeaderTitleX * s, ry + fontY, s, tint);
                    if (header.Value is { Length: > 0 } count) renderer.DrawText(font, count, areaX + HeaderCountX * s, ry + fontY, s, tint);
                    renderer.DrawText(font, "▶more...", areaX + HeaderMoreX * s, ry + fontY, s, tint);
                    continue;
                }
                int p = i - menu.PeopleOffset;
                if (p >= menu.People.Count) continue;
                var person = menu.People[p];
                if (person.LinkshellRank != 0)
                {
                    ushort item = person.LinkshellRank switch { 1 => LinkshellItemIds.Linkshell, 2 => LinkshellItemIds.Pearlsack, _ => LinkshellItemIds.Linkpearl };
                    var (r, g, b) = StockUiLinkshellColors.Rgb(person.LinkshellColor);
                    StockUiMenuWindow.DrawItemIcon(renderer, menu, item, areaX + IconX * s, ry, IconSize * s, StockUiNamePlates.PearlTint(r, g, b));
                }
                if (person.Jobs is { Length: > 0 } jobs)
                {
                    if (logFont != null) logFont.Draw(renderer, jobs, areaX + JobsX * s, ry + logY, s, StockUiMenuWindow.White);
                    else renderer.DrawText(font, jobs, areaX + JobsX * s, ry + fontY, s);
                }
                if (person.MissionRank != 0)
                {
                    var box = NationTints[Math.Min(person.Nation, (byte)(NationTints.Length - 1))];
                    float bx = areaX + RankBoxX * s, by = ry + (firstRow.Height - RankBoxHeight) * 0.5f * s;
                    renderer.DrawTextureRect(StockUiMenuWindow.SolidTexture, StockUiMenuWindow.SolidTexelX, StockUiMenuWindow.SolidTexelY, 1, 1,
                        bx, by, RankBoxWidth * s, RankBoxHeight * s, box);
                    string digit = person.MissionRank.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    float ts = s * StockUiPartyWindow.TextScale;
                    renderer.DrawText(font, digit, bx + (RankBoxWidth * s - font.MeasureWidth(digit) * ts) * 0.5f, by + (RankBoxHeight * s - font.LineHeight * ts), ts);
                }
                UiColor? nameTint = i == selected ? StockUiMenuWindow.SelectedGlyphTint
                    : person.Standing switch { StockUiPersonStanding.PartyLeader => LeaderTint, StockUiPersonStanding.PartyMember => PartyTint, _ => null };
                bool plain = person.Jobs == null && person.MissionRank == 0 && person.LinkshellRank == 0;
                renderer.DrawText(font, person.Name, areaX + (plain ? JobsX : NameX) * s, ry + fontY, s, nameTint);
                if (person.Languages is { Length: > 0 } languages) renderer.DrawText(font, languages, areaX + LanguagesX * s, ry + fontY, s, nameTint);
                if ((person.Area ?? person.Detail) is { Length: > 0 } right)
                {
                    float rightEdge = placement.X + (frame.Width - StockUiMenuWindow.MapValueRightPad) * s;
                    if (logFont != null) logFont.Draw(renderer, right, rightEdge - logFont.MeasureWidth(right) * s, ry + logY, s, StockUiMenuWindow.White);
                    else renderer.DrawText(font, right, rightEdge - font.MeasureWidth(right) * s, ry + fontY, s);
                }
            }
            renderer.ClearClip();
            if (menu.CanScroll) StockUiMenuWindow.DrawScrollbar(renderer, placement, frame, first, menu.Rows.Count, menu.VisibleRows);
        }
    }

    /// <summary>A linkshell's colour as the name plates and the check window tint the pearl.</summary>
    internal static class StockUiLinkshellColors
    {
        /// <summary>
        /// The 4-bit channels of a linkshell item's colour (extra data offset 6: R in the low nibble, then G, B) widened as
        /// the entity updates carry them ((c &lt;&lt; 4) + 15, <c>EquipInspectInfo.LinkshellRgb</c>); grey for 0.
        /// </summary>
        public static (byte R, byte G, byte B) Rgb(ushort color)
        {
            if (color == 0) return (0x80, 0x80, 0x80);
            static byte Widen(int nibble) => (byte)((nibble << 4) + 15);
            return (Widen(color & 0xF), Widen((color >> 4) & 0xF), Widen((color >> 8) & 0xF));
        }
    }
}
