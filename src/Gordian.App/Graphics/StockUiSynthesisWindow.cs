// src/Gordian.App/Graphics/StockUiSynthesisWindow.cs
using System.Globalization;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws the synthesis grid's client content (#330) over its DAT frame (<c>tskill1</c>), laid out after the maintainer's
    /// retail capture <c>Synthesis_History.png</c> and the video <c>Synth_Wind_Fire_Lightning_Breaks.mp4</c> (2026-10-16, UI
    /// 1:1): the crystal's icon and its element word ("Heat") in the frame's top band, each slot's item icon with the count
    /// over its bottom right ("3" of three zinc ore), the inventory (or the History recipes) in the list beside the grid at
    /// x 198 while the grid has the cursor, the help bar, and the slot's item (History: the recipe's result) described under
    /// the grid at (16, 240); an empty slot shows the empty description window. Measured on 2x crops: the icon's left edge
    /// about 20 px into the frame, the word from about 36 px; the icon 5 px and the word 6 px below the frame's top, under its white border
    /// line (round 1, 2026-10-18: 2 and 3 px overlapped the line).
    /// </summary>
    public static class StockUiSynthesisWindow
    {
        private const float CrystalIconX = 20, CrystalIconY = 5, CrystalIconSize = 14, WordX = 37, WordY = 6;
        private const float SlotIconSize = 32, CountRight = 31, CountBottom = 33;

        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp, StockUiScreen screen)
        {
            float s = placement.Scale;
            var definition = menu.Menu;
            var frame = definition.Frame;
            var crystal = menu.SynthesisCrystal;
            int element = StockUiSynthesis.ElementOf(crystal.ItemId);
            if (element >= 0)
            {
                StockUiMenuWindow.DrawItemIcon(renderer, menu, crystal.ItemId, placement.X + CrystalIconX * s, placement.Y + CrystalIconY * s, CrystalIconSize * s);
                var titles = StockUiTitleText.For(library);
                titles.Draw(renderer, StockUiSynthesis.ElementWord(element), placement.X + WordX * s, placement.Y + WordY * s, s, font);
            }

            float ts = s * StockUiPartyWindow.TextScale;
            for (int i = 0; i < StockUiSynthesis.SlotCount; i++)
            {
                if (menu.SynthesisSlots[i] is not { } slot || definition.FindButton(i + 1) is not { } button) continue;
                float bx = placement.X + button.X * s, by = placement.Y + button.Y * s;
                StockUiMenuWindow.DrawItemIcon(renderer, menu, slot.ItemId, bx, by, SlotIconSize * s);
                if (slot.Count > 1)
                {
                    string count = slot.Count.ToString(CultureInfo.InvariantCulture);
                    renderer.DrawText(font, count, bx + CountRight * s - font.MeasureWidth(count) * ts, by + CountBottom * s - font.LineHeight * ts, ts);
                }
            }

            bool listOpen = menu.IsListOpen;
            if (!listOpen && library.TryGetMenu(menu.IsSynthesisHistory ? StockUiSynthesis.HistoryListMenu : StockUiItems.ListMenu, out var list))
            {
                var at = new StockUiPlacement(placement.X + (StockUiSynthesis.ListX - frame.X) * s, placement.Y + (list.Frame.Y - frame.Y) * s, s, false);
                StockUiItemWindow.DrawCompanion(renderer, library, font, menu, list, at, menu.CompanionRows);
            }
            if (menu.ItemHelpBar is { } bar) StockUiItemWindow.DrawHelpBar(renderer, library, font, logFont, menu, bar, placement, screen);
            if (listOpen) return;
            float top = placement.Y + (StockUiItemWindow.DescriptionTop - frame.Y) * s;
            if (menu.DescriptionPages.Count > 0)
            {
                StockUiItemWindow.DrawDescription(renderer, library, font, logFont, menu, placement, timestamp, StockUiSynthesis.DescriptionX, top);
            }
            else if (library.TryGetMenu(StockUiShop.InfoMenu, out var info))
            {
                // An empty slot: the empty description window (the video shows it with its bare slot box).
                renderer.DrawMenu(info, new StockUiPlacement(placement.X + (StockUiSynthesis.DescriptionX - frame.X) * s, top, s, false), includeButtons: true, opaqueBody: true);
            }
        }
    }
}
