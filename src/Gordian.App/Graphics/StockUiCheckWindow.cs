// src/Gordian.App/Graphics/StockUiCheckWindow.cs
using System;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;
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
        /// <summary>
        /// The linkshell line under View Wares (its capsule ends at y 164), measured on the retail screenshot (2026-10-07):
        /// the 16 px rank icon at (16,163), the name from x 34.
        /// </summary>
        private const float LinkshellIconX = 16, LinkshellIconY = 163, LinkshellIconSize = 16, LinkshellTextX = 34, LinkshellTextY = 165;

        /// <summary>The bazaar message in the comment window: log-font lines 6 px in from y 4 (three 16 px rows fill its 56 px).</summary>
        private const float CommentTextX = 6, CommentTextY = 4;

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
            StockUiItemGrid.Draw(renderer, menu.ItemLookup, definition, placement,
                id => StockUiCheck.TryGetSlot(id, out EquipSlotId slot) ? check.Info.ItemIn(slot) : (ushort)0);

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

            StockUiHelpBar.DrawBeside(renderer, library, font, logFont, check.Name, check.JobText ?? StockUiCheck.FormatJobs(check.Info), placement, frame, screen);

            if (menu.SelectedButtonId == StockUiCheck.ViewWaresButton)
            {
                DrawComment(renderer, library, font, logFont, check, placement, frame, screen);
                return;
            }
            // The item under the cursor right under the grid, at the info window's authored x.
            if (StockUiItemInfoWindow.FrameOf(library, menu.ShownDescriptionPage) is { } info)
            {
                StockUiItemInfoWindow.Draw(renderer, library, font, logFont, menu,
                    StockUiWindowPlacement.Beside(placement, frame, info.Frame.X, StockUiItemInfoWindow.DefaultTop), timestamp);
            }
        }

        /// <summary>The bazaar message, its window's bottom on Window 1's top edge (or the screen's bottom), as retail shows it.</summary>
        private static void DrawComment(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont,
            StockUiCheckData check, StockUiPlacement placement, UiMenuFrame frame, StockUiScreen screen)
        {
            var lines = check.CommentLines;
            if (lines.Count == 0 || !library.TryGetMenu(StockUiCheck.CommentMenu, out var comment)) return;
            float s = placement.Scale;
            var beside = StockUiWindowPlacement.Beside(placement, frame, comment.Frame);
            float h = comment.Frame.Height * s;
            float? hidden = screen.Height > 0
                ? StockUiLayout.Place(comment.Frame.Anchor, comment.Frame.X, comment.Frame.Y, comment.Frame.Width, comment.Frame.Height, s, screen.Width, screen.Height).Y
                : null;
            var at = beside with { Y = StockUiWindowPlacement.TopOnLog(screen, s, h, beside.Y, hidden) };
            renderer.DrawMenu(comment, at, includeButtons: false, opaqueBody: true);
            for (int i = 0; i < lines.Count && i < 3; i++)
            {
                StockUiTextBoxes.DrawLine(renderer, font, logFont, lines[i], at.X + CommentTextX * s, at.Y + (CommentTextY + i * StockUiLogFont.CellHeight) * s, s);
            }
        }
    }
}
