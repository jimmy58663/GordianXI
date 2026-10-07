// src/Gordian.App/Graphics/StockUiCheckWindow.cs
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws the player check window's client content (#64) over the <c>inspect</c> DAT frame, whose 4 x 4 slot grid,
    /// labels and View Wares button <see cref="StockUiMenuWindow"/> draws: the checked character's item icons in their
    /// slots, the name and jobs and the linkshell under View Wares, and under the window either the selected item's
    /// <c>iteminfo</c> window or the bazaar message in the <c>comment</c> window (both authored at layout (16,240), right
    /// under the grid). PROVISIONAL placements, see <see cref="StockUiCheck"/>.
    /// </summary>
    public static class StockUiCheckWindow
    {
        /// <summary>The 32 x 32 item icon fills the slot (the slot art is the empty slot's look).</summary>
        private const float SlotIconSize = 32;

        /// <summary>Text under View Wares (its capsule ends at y 164): the name and jobs at y 165, the linkshell 11 px lower.</summary>
        private const float NameLineY = 165, LinkshellLineY = 176, TextInsetX = 8, PearlTextGap = 2;

        /// <summary>The bazaar message in the comment window: log-font lines 6 px in from y 4 (three 16 px rows fill its 56 px).</summary>
        private const float CommentTextX = 6, CommentTextY = 4;

        /// <summary>The <c>fontshp</c> image of the linkshell pearl (the name plates' icon 114).</summary>
        private const int PearlImage = 114;

        private static readonly UiColor White = new(0x80, 0x80, 0x80, 0x80);

        /// <summary>Whether a grid button's slot holds an item (its label is then left out under the icon).</summary>
        public static bool HoldsItem(StockUiCheckData check, int buttonId) =>
            StockUiCheck.TryGetSlot(buttonId, out EquipSlotId slot) && check.Info.ItemIn(slot) != 0;

        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiCheckData check, StockUiPlacement placement)
        {
            float s = placement.Scale;
            var definition = menu.Menu;

            // Item icons over the slots that hold something.
            foreach (var button in definition.Buttons)
            {
                if (!StockUiCheck.TryGetSlot(button.ButtonId, out EquipSlotId slot)) continue;
                ushort item = check.Info.ItemIn(slot);
                if (item == 0) continue;
                StockUiMenuWindow.DrawItemIcon(renderer, menu, item, placement.X + button.X * s, placement.Y + button.Y * s, SlotIconSize * s);
            }

            // Name (left) and jobs (right) on one line, the linkshell pearl and name under them, in fontshp at the party
            // rows' 7/8 size.
            float ts = s * StockUiPartyWindow.TextScale;
            float left = placement.X + TextInsetX * s, right = placement.X + (definition.Frame.Width - TextInsetX) * s;
            renderer.DrawText(font, check.Name, left, placement.Y + NameLineY * s, ts);
            string jobs = check.JobText;
            renderer.DrawText(font, jobs, right - font.MeasureWidth(jobs) * ts, placement.Y + NameLineY * s, ts);
            if (check.Info.HasLinkshell)
            {
                float x = left, y = placement.Y + LinkshellLineY * s;
                if (PearlImage < font.Group.Images.Count)
                {
                    var (r, g, b) = check.Info.LinkshellRgb;
                    renderer.DrawImage(font.Group.Images[PearlImage], x, y, ts, StockUiNamePlates.PearlTint(r, g, b));
                    x += (16 + PearlTextGap) * ts;
                }
                renderer.DrawText(font, check.Info.LinkshellName, x, y, ts);
            }

            // Under the grid: the item under the cursor, else the bazaar message.
            if (!library.TryGetMenu(StockUiCheck.InfoMenu, out var info) || !library.TryGetMenu(StockUiCheck.CommentMenu, out var comment)) return;
            var frame = definition.Frame;
            ushort selected = menu.SelectedCheckItem;
            if (selected != 0)
            {
                var at = new StockUiPlacement(placement.X + (info.Frame.X - frame.X) * s, placement.Y + (info.Frame.Y - frame.Y) * s, s, false);
                StockUiMenuWindow.DrawItemInfo(renderer, info, font, menu, at, selected, string.Empty);
                return;
            }
            var lines = check.CommentLines;
            if (lines.Count == 0) return;
            var commentAt = new StockUiPlacement(placement.X + (comment.Frame.X - frame.X) * s, placement.Y + (comment.Frame.Y - frame.Y) * s, s, false);
            renderer.DrawMenu(comment, commentAt, includeButtons: false, opaqueBody: true);
            for (int i = 0; i < lines.Count && i < 3; i++)
            {
                float y = commentAt.Y + (CommentTextY + i * StockUiLogFont.CellHeight) * s;
                if (logFont != null) logFont.Draw(renderer, lines[i], commentAt.X + CommentTextX * s, y, s, White);
                else renderer.DrawText(font, lines[i], commentAt.X + CommentTextX * s, y, s);
            }
        }
    }
}
