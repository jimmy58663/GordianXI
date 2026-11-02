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
    /// item icon (shell, sack, pearl) tinted with the shell's colour at the row's left. The Linkshell window's items (image-30)
    /// show the item's icon with its worn slot's digit, its kind ("Linkshell") at x 24, green while worn, and the shell's name
    /// at x 164, under <c>link6</c> with the item's large icon and name; the friend list shows "No friends registered." in pink
    /// while it is empty (image-39), the message list its "From To Type Date" bar (image-40), the Emote List the emotes in the
    /// log font (image-42).
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

        /// <summary>The friend list's empty text: pink (image-39, about FF96C8), 35, 30 into the frame.</summary>
        private static readonly UiColor EmptyTint = new(0x80, 0x4B, 0x64, 0x80);
        private const float EmptyX = 35, EmptyY = 30;

        /// <summary>The Linkshell window's columns (image-30, 1:1): icon at 5, kind at 24, name at 164; worn kinds green (about 70F070).</summary>
        private const float ItemIconX = 5, ItemKindX = 24, ItemNameX = 164;
        private static readonly UiColor WornTint = new(0x38, 0x78, 0x38, 0x80);

        /// <summary><c>link6</c>'s large icon at (10, 12) and the name at x 66 (image-30).</summary>
        private const float HeaderIconX = 10, HeaderIconY = 12, HeaderIconSize = 32, HeaderNameX = 66;

        /// <summary>The emotes' text in the log font 20 px into the row (image-42).</summary>
        private const float EmoteX = 20;

        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, UiMenuButton firstRow, UiMenuButton? secondRow, long timestamp)
        {
            var frame = menu.Menu.Frame;
            float s = placement.Scale;
            float pitch = StockUiMenuWindow.RowPitch(firstRow, secondRow);
            float first = StockUiMenuWindow.ScrolledFirstRow(menu, timestamp);
            float areaX = placement.X + firstRow.X * s, areaY = placement.Y + firstRow.Y * s;
            DrawLinkshellHeader(renderer, library, font, menu, placement);
            if (menu.EmptyText is { Length: > 0 } empty && menu.People.Count == 0)
            {
                if (logFont != null) logFont.Draw(renderer, empty, placement.X + EmptyX * s, placement.Y + EmptyY * s, s, EmptyTint);
                else renderer.DrawText(font, empty, placement.X + EmptyX * s, placement.Y + EmptyY * s, s, EmptyTint);
            }
            if (menu.PeoplePurpose == StockUiPeoplePurpose.Messages)
            {
                // PlayOnline's message list: only its column bar (GordianXI has no PlayOnline messages).
                StockUiMenuWindow.DrawMapRowBar(renderer, placement.X + StockUiMenuWindow.ListClipInset * s, areaY,
                    placement.X + (frame.Width - StockUiMenuWindow.ScrollbarInset) * s, firstRow.Height * s);
                foreach (var (text, x) in StockUiSearch.MessageColumns)
                    renderer.DrawText(font, text, areaX + x * s, areaY + (firstRow.Height * s - font.LineHeight * s) * 0.5f, s, HeaderTint);
                return;
            }
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
                if (menu.PeoplePurpose == StockUiPeoplePurpose.LinkshellItems && p < menu.LinkshellItems.Count)
                {
                    DrawLinkshellItem(renderer, font, menu, menu.LinkshellItems[p], areaX, ry, s, fontY, i == selected);
                    continue;
                }
                if (menu.PeoplePurpose == StockUiPeoplePurpose.Emotes)
                {
                    UiColor emoteTint = i == selected ? StockUiMenuWindow.SelectedGlyphTint : StockUiMenuWindow.White;
                    if (logFont != null) logFont.Draw(renderer, person.Name, areaX + EmoteX * s, ry + logY, s, emoteTint);
                    else renderer.DrawText(font, person.Name, areaX + EmoteX * s, ry + fontY, s, emoteTint);
                    continue;
                }
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

        /// <summary>A Linkshell window row: the item's icon in its colour with the worn slot's digit, its kind, the shell's name.</summary>
        private static void DrawLinkshellItem(StockUiRenderer renderer, UiFont font, StockUiOpenMenu menu, StockUiLinkshellItem item, float areaX, float ry,
            float s, float fontY, bool selected)
        {
            var (r, g, b) = StockUiLinkshellColors.Rgb(item.Color);
            float iconX = areaX + ItemIconX * s;
            StockUiMenuWindow.DrawItemIcon(renderer, menu, (ushort)item.Kind, iconX, ry, IconSize * s, StockUiNamePlates.PearlTint(r, g, b));
            if (item.WornSlot != 0)
            {
                float ts = s * StockUiPartyWindow.TextScale;
                string digit = item.WornSlot.ToString(System.Globalization.CultureInfo.InvariantCulture);
                renderer.DrawText(font, digit, iconX + IconSize * s - font.MeasureWidth(digit) * ts, ry + IconSize * s - font.LineHeight * ts, ts);
            }
            string kind = item.Kind switch { StockUiLinkshellKind.Linkshell => "Linkshell", StockUiLinkshellKind.Pearlsack => "Pearlsack", _ => "Linkpearl" };
            UiColor? kindTint = selected ? StockUiMenuWindow.SelectedGlyphTint : item.WornSlot != 0 ? WornTint : null;
            renderer.DrawText(font, kind, areaX + ItemKindX * s, ry + fontY, s, kindTint);
            renderer.DrawText(font, item.Name, areaX + ItemNameX * s, ry + fontY, s, selected ? StockUiMenuWindow.SelectedGlyphTint : null);
        }

        /// <summary>
        /// <c>link6</c> above the Linkshell window and a linkshell's member list (image-30, image-33): the item under the cursor
        /// (or the list's item) as its large icon in the shell's colour and its name.
        /// </summary>
        private static void DrawLinkshellHeader(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiOpenMenu menu, StockUiPlacement placement)
        {
            StockUiLinkshellItem? item = menu.LinkshellItem;
            if (menu.PeoplePurpose == StockUiPeoplePurpose.LinkshellItems)
            {
                int index = menu.EntryIndex(menu.SelectedButtonId);
                if (index >= 0 && index < menu.LinkshellItems.Count) item = menu.LinkshellItems[index];
            }
            if (item is not { } shown || !library.TryGetMenu(StockUiSearch.LinkshellHeader, out var header)) return;
            var at = StockUiWindowPlacement.Beside(placement, menu.Menu.Frame, header.Frame);
            float s = at.Scale;
            renderer.DrawMenu(header, at, includeButtons: false, opaqueBody: true);
            var (r, g, b) = StockUiLinkshellColors.Rgb(shown.Color);
            StockUiMenuWindow.DrawItemIcon(renderer, menu, (ushort)shown.Kind, at.X + HeaderIconX * s, at.Y + HeaderIconY * s, HeaderIconSize * s,
                StockUiNamePlates.PearlTint(r, g, b));
            renderer.DrawText(font, shown.Name, at.X + HeaderNameX * s, at.Y + (header.Frame.Height * s - font.LineHeight * s) * 0.5f, s);
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
