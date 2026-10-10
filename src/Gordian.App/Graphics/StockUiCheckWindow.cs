// src/Gordian.App/Graphics/StockUiCheckWindow.cs
using System;
using System.Diagnostics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Gordian.Core.World;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws the player check window's client content (#64) over the <c>inspect</c> DAT frame, whose 4 x 4 slot grid,
    /// labels and View Wares button <see cref="StockUiMenuWindow"/> draws. Laid out after the maintainer's retail
    /// screenshots (2026-10-07):
    /// <list type="bullet">
    /// <item>the checked character's item icons in their slots;</item>
    /// <item>the linkshell under View Wares: the rank icon (the Linkshell, Pearlsack or Linkpearl item's icon) tinted with
    /// the shell's colour, then its name in <c>fontshp</c>;</item>
    /// <item>the help bar at the top of the screen: the name in <c>titlewin</c>, the jobs in <c>helpwind</c> stretched to
    /// the screen's right edge, both in the log font;</item>
    /// <item>with the cursor on an item, its description page (<see cref="StockUiItemDescription"/>) in the item info
    /// window sized to its lines, right under the grid, the footer and a page arrow at the bottom right;</item>
    /// <item>with the cursor on View Wares, the bazaar message in the <c>comment</c> window, its bottom on Window 1's
    /// top edge (or the screen's bottom while the log is hidden), as the command menu sits.</item>
    /// </list>
    /// </summary>
    public static class StockUiCheckWindow
    {
        /// <summary>The 32 x 32 item icon fills the slot (the slot art is the empty slot's look).</summary>
        private const float SlotIconSize = 32;

        /// <summary>
        /// The linkshell line under View Wares (its capsule ends at y 164), measured on the retail screenshot (2026-10-07):
        /// the 16 px rank icon at (16,163), the name from x 34.
        /// </summary>
        private const float LinkshellIconX = 16, LinkshellIconY = 163, LinkshellIconSize = 16, LinkshellTextX = 34, LinkshellTextY = 165;

        /// <summary>Text inside the help bar's boxes and the description: log font lines 16 px apart.</summary>
        private const float BarTextX = 8, InfoIconX = 8, InfoIconY = 12, InfoIconSize = 32, InfoTextX = 48, InfoTextY = 4, InfoRightInset = 8;

        /// <summary>The help line stops this far (layout px) short of the screen's right edge.</summary>
        private const float HelpRightMargin = 16;

        /// <summary>The bazaar message in the comment window: log-font lines 6 px in from y 4 (three 16 px rows fill its 56 px).</summary>
        private const float CommentTextX = 6, CommentTextY = 4;

        private static readonly UiColor White = new(0x80, 0x80, 0x80, 0x80);

        /// <summary>Whether a grid button's slot holds an item (its label is then left out under the icon).</summary>
        public static bool HoldsItem(StockUiCheckData check, int buttonId) =>
            StockUiCheck.TryGetSlot(buttonId, out EquipSlotId slot) && check.Info.ItemIn(slot) != 0;

        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiCheckData check, StockUiPlacement placement, StockUiScreen screen = default, long timestamp = 0)
        {
            float s = placement.Scale;
            var definition = menu.Menu;
            var frame = definition.Frame;

            // Item icons over the slots that hold something.
            foreach (var button in definition.Buttons)
            {
                if (!StockUiCheck.TryGetSlot(button.ButtonId, out EquipSlotId slot)) continue;
                ushort item = check.Info.ItemIn(slot);
                if (item == 0) continue;
                StockUiMenuWindow.DrawItemIcon(renderer, menu, item, placement.X + button.X * s, placement.Y + button.Y * s, SlotIconSize * s);
            }

            // The linkshell: the rank icon tinted with its colour, then the name, in fontshp at the party rows' 7/8 size.
            if (check.Info.HasLinkshell)
            {
                // The rank icon is the held linkshell item's own icon (Linkshell: the shell, Pearlsack, Linkpearl) tinted with
                // the shell's colour: retail shows the leader's shell tinted green for a green shell.
                ushort rankItem = check.Info.LinkshellRank switch
                {
                    LinkshellRank.Leader => LinkshellItemIds.Linkshell,
                    LinkshellRank.Sackholder => LinkshellItemIds.Pearlsack,
                    _ => LinkshellItemIds.Linkpearl,
                };
                var (r, g, b) = check.Info.LinkshellRgb;
                StockUiMenuWindow.DrawItemIcon(renderer, menu, rankItem, placement.X + LinkshellIconX * s, placement.Y + LinkshellIconY * s,
                    LinkshellIconSize * s, StockUiNamePlates.PearlTint(r, g, b));
                float ts = s * StockUiPartyWindow.TextScale;
                renderer.DrawText(font, check.Info.LinkshellName, placement.X + LinkshellTextX * s, placement.Y + LinkshellTextY * s, ts);
            }

            DrawHelpBar(renderer, library, font, logFont, check, placement, frame, screen);

            if (menu.SelectedButtonId == StockUiCheck.ViewWaresButton)
            {
                DrawComment(renderer, library, font, logFont, check, placement, frame, screen);
                return;
            }
            var pages = menu.SelectedCheckPages;
            if (pages.Count == 0) return;
            var page = pages[Math.Clamp(menu.CheckPage, 0, pages.Count - 1)];
            if (!library.TryGetMenu(StockUiItemDescription.FrameFor(page.LineCount), out var info)) return;
            var at = new StockUiPlacement(placement.X + (info.Frame.X - frame.X) * s, placement.Y + (240 - frame.Y) * s, s, false);
            renderer.DrawMenu(info, at, includeButtons: false, opaqueBody: true);
            StockUiMenuWindow.DrawItemIcon(renderer, menu, menu.SelectedCheckItem, at.X + InfoIconX * s, at.Y + InfoIconY * s, InfoIconSize * s);
            for (int i = 0; i < page.Lines.Count; i++)
            {
                DrawLine(renderer, font, logFont, page.Lines[i], at.X + InfoTextX * s, at.Y + (InfoTextY + i * StockUiLogFont.CellHeight) * s, s);
            }
            if (page.Footer.Length == 0 && !page.HasMore) return;
            float footerY = at.Y + (InfoTextY + page.Lines.Count * StockUiLogFont.CellHeight) * s;
            float right = at.X + (info.Frame.Width - InfoRightInset) * s;
            if (page.HasMore && library.TryGetGroup("kaipage", out var arrow) && arrow.Images.Count > 0)
            {
                // The page arrow after the footer: the log's page-wait arrow, stepped like the target cursor.
                int step = (int)(timestamp / (Stopwatch.Frequency * StockUiTargetWindow.CursorStepSeconds));
                int cycle = Math.Max(1, 2 * arrow.Images.Count - 2);
                int index = step % cycle;
                if (index >= arrow.Images.Count) index = cycle - index;
                right -= 12 * s;
                renderer.DrawImage(arrow.Images[index], right + 2 * s, footerY + 3 * s, s);
            }
            float width = logFont != null ? logFont.MeasureWidth(page.Footer) : font.MeasureWidth(page.Footer);
            DrawLine(renderer, font, logFont, page.Footer, right - width * s, footerY, s);
        }

        /// <summary>The help bar: the name in the title box, the jobs in the help line stretched toward the screen's right edge.</summary>
        private static void DrawHelpBar(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont,
            StockUiCheckData check, StockUiPlacement placement, UiMenuFrame frame, StockUiScreen screen)
        {
            float s = placement.Scale;
            if (library.TryGetMenu(StockUiCheck.TitleMenu, out var title))
            {
                var at = new StockUiPlacement(placement.X + (title.Frame.X - frame.X) * s, placement.Y + (title.Frame.Y - frame.Y) * s, s, false);
                renderer.DrawMenu(title, at, includeButtons: false, opaqueBody: true);
                DrawLine(renderer, font, logFont, check.Name, at.X + BarTextX * s, at.Y + (title.Frame.Height - StockUiLogFont.CellHeight) * 0.5f * s, s);
            }
            if (library.TryGetMenu(StockUiCheck.HelpMenu, out var help))
            {
                var at = new StockUiPlacement(placement.X + (help.Frame.X - frame.X) * s, placement.Y + (help.Frame.Y - frame.Y) * s, s, false);
                float? width = screen.Width > 0 ? Math.Max(help.Frame.Width, (screen.Width - at.X) / s - HelpRightMargin) : null;
                renderer.DrawMenu(help, at, includeButtons: false, opaqueBody: true, frameWidth: width);
                DrawLine(renderer, font, logFont, check.JobText ?? StockUiCheck.FormatJobs(check.Info), at.X + BarTextX * s,
                    at.Y + (help.Frame.Height - StockUiLogFont.CellHeight) * 0.5f * s, s);
            }
        }

        /// <summary>The bazaar message, its window's bottom on Window 1's top edge (or the screen's bottom), as retail shows it.</summary>
        private static void DrawComment(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont,
            StockUiCheckData check, StockUiPlacement placement, UiMenuFrame frame, StockUiScreen screen)
        {
            var lines = check.CommentLines;
            if (lines.Count == 0 || !library.TryGetMenu(StockUiCheck.CommentMenu, out var comment)) return;
            float s = placement.Scale;
            float x = placement.X + (comment.Frame.X - frame.X) * s;
            float h = comment.Frame.Height * s;
            float y = screen.Window1Top is { } top ? top - h
                : screen.Height > 0 ? StockUiLayout.Place(comment.Frame.Anchor, comment.Frame.X, comment.Frame.Y, comment.Frame.Width, comment.Frame.Height, s, screen.Width, screen.Height).Y
                : placement.Y + (comment.Frame.Y - frame.Y) * s;
            var at = new StockUiPlacement(x, Math.Max(0, y), s, false);
            renderer.DrawMenu(comment, at, includeButtons: false, opaqueBody: true);
            for (int i = 0; i < lines.Count && i < 3; i++)
            {
                DrawLine(renderer, font, logFont, lines[i], at.X + CommentTextX * s, at.Y + (CommentTextY + i * StockUiLogFont.CellHeight) * s, s);
            }
        }

        private static void DrawLine(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, string text, float x, float y, float s)
        {
            if (logFont != null) logFont.Draw(renderer, text, x, y, s, White);
            else renderer.DrawText(font, text, x, y, s);
        }
    }
}
