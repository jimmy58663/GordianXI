// src/Gordian.App/Graphics/StockUiTradeWindow.cs
using System.Globalization;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws a trade window's client content (#486) over its DAT frame (<c>trade</c>, or <c>handover</c> with an NPC): each
    /// slot's item icon with the count over its bottom right (as the synthesis grid), the gil offered right-aligned in the
    /// red field before the coin, OK in its kind-3 alternate (lit) once pressed, the inventory beside the window at x 198
    /// while the window has the cursor, the help bar, and the slot's item described under the windows at (16, 240). With a
    /// player the partner's offer is a second <c>trade</c> frame under ours (layout y 144) with their name in its top band
    /// and their OK lit once they pressed it. PROVISIONAL: no retail capture of a trade yet; the partner window's place and
    /// the name's place are ours.
    /// </summary>
    public static class StockUiTradeWindow
    {
        private const float CountRight = 31, CountBottom = 33;
        private const float GilFieldRight = 156, GilFieldY = 7, GilFieldHeight = 16;
        private const float NameX = 8, NameY = 6;

        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp, StockUiScreen screen)
        {
            var definition = menu.Menu;
            var frame = definition.Frame;
            DrawOffer(renderer, library, font, menu, definition, placement, menu.TradeSlots, menu.TradeGil, menu.TradeAccepted);

            if (!menu.IsNpcTrade)
            {
                // The partner's offer: the same frame under ours, not taking the cursor.
                var at = StockUiWindowPlacement.Beside(placement, frame, frame.X, StockUiTrade.PartnerY);
                renderer.DrawMenu(definition, at, opaqueBody: true);
                DrawOffer(renderer, library, font, menu, definition, at, menu.PartnerSlots, menu.PartnerGil, menu.PartnerAccepted);
                float ts = at.Scale * StockUiPartyWindow.TextScale;
                renderer.DrawText(font, menu.TradePartner.Name, at.X + NameX * at.Scale, at.Y + NameY * at.Scale, ts);
            }

            bool listOpen = menu.IsListOpen;
            if (!listOpen && library.TryGetMenu(StockUiItems.ListMenu, out var list))
            {
                var at = StockUiWindowPlacement.Beside(placement, frame, StockUiTrade.ListX, list.Frame.Y);
                StockUiItemWindow.DrawCompanion(renderer, library, font, menu, list, at, menu.CompanionRows);
            }
            if (menu.ItemHelpBar is { } bar) StockUiItemWindow.DrawHelpBar(renderer, library, font, logFont, menu, bar, placement, screen);
            if (listOpen) return;
            StockUiItemInfoWindow.Draw(renderer, library, font, logFont, menu,
                StockUiWindowPlacement.Beside(placement, frame, StockUiTrade.DescriptionX, StockUiItemInfoWindow.DefaultTop), timestamp, emptyWhenNone: true);
        }

        /// <summary>One side's offer at a placement of the trade frame: the slot icons and counts, the gil, OK lit when pressed.</summary>
        private static void DrawOffer(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiOpenMenu menu, UiMenuDefinition definition,
            StockUiPlacement placement, StockUiTradeSlot?[] slots, uint gil, bool accepted)
        {
            float s = placement.Scale;
            float ts = s * StockUiPartyWindow.TextScale;
            StockUiItemGrid.Draw(renderer, menu.ItemLookup, definition, placement,
                id => id >= 1 && id <= StockUiTrade.SlotCount && slots[id - 1] is { } slot ? slot.ItemId : (ushort)0,
                (button, item, bx, by) =>
                {
                    if (item == 0 || slots[button.ButtonId - 1] is not { Count: > 1 } slot) return;
                    string count = slot.Count.ToString(CultureInfo.InvariantCulture);
                    renderer.DrawText(font, count, bx + CountRight * s - font.MeasureWidth(count) * ts, by + CountBottom * s - font.LineHeight * ts, ts);
                });

            if (gil > 0)
            {
                string text = gil.ToString("N0", CultureInfo.InvariantCulture);
                renderer.DrawText(font, text, placement.X + GilFieldRight * s - font.MeasureWidth(text) * s,
                    placement.Y + GilFieldY * s + (GilFieldHeight * s - font.LineHeight * s) * 0.5f, s);
            }

            // OK pressed: the button's kind-3 alternate (keytops3 #54, the lit OK) over it.
            if (accepted && definition.FindButton(StockUiTrade.OkButton) is { } ok)
            {
                foreach (var shape in ok.Shapes)
                {
                    if (shape.Kind == 3 && library.TryGetImage(shape, out var image))
                        renderer.DrawImage(image, placement.X + ok.X * s, placement.Y + ok.Y * s, s);
                }
            }
        }
    }
}
