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
            DrawOk(renderer, library, definition, placement, menu.TradeAccepted);

            if (!menu.IsNpcTrade)
            {
                DrawName(renderer, font, menu.TradeOwnName, placement, menu.TradeAccepted);
                // The partner's offer: the same frame under ours with its slots only (their OK / Cancel are not ours to press,
                // round 1); the cursor reaches the slots through the client's buttons 21-28.
                var at = StockUiWindowPlacement.Beside(placement, frame, frame.X, StockUiTrade.PartnerY);
                renderer.DrawMenu(definition, at, includeButtons: false, opaqueBody: true);
                foreach (var button in definition.Buttons)
                {
                    if (button.ButtonId < 1 || button.ButtonId > StockUiTrade.SlotCount) continue;
                    foreach (var shape in button.Shapes)
                    {
                        if (shape.Kind == 0 && library.TryGetImage(shape, out var image))
                            renderer.DrawImage(image, at.X + button.X * at.Scale, at.Y + button.Y * at.Scale, at.Scale);
                    }
                }
                DrawOffer(renderer, library, font, menu, definition, at, menu.PartnerSlots, menu.PartnerGil, menu.PartnerAccepted);
                DrawName(renderer, font, menu.TradePartner.Name, at, menu.PartnerAccepted);
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
                // "1,000 G" right-aligned before the coin (retail, round 1 image-25).
                string text = StockUiShop.FormatPrice(gil);
                renderer.DrawText(font, text, placement.X + GilFieldRight * s - font.MeasureWidth(text) * s,
                    placement.Y + GilFieldY * s + (GilFieldHeight * s - font.LineHeight * s) * 0.5f, s);
            }
        }

        /// <summary>Our OK once pressed: the button's kind-3 alternate (keytops3 #54, the lit OK) over it.</summary>
        private static void DrawOk(StockUiRenderer renderer, UiResourceLibrary library, UiMenuDefinition definition, StockUiPlacement placement, bool accepted)
        {
            if (!accepted || definition.FindButton(StockUiTrade.OkButton) is not { } ok) return;
            foreach (var shape in ok.Shapes)
            {
                if (shape.Kind == 3 && library.TryGetImage(shape, out var image))
                    renderer.DrawImage(image, placement.X + ok.X * placement.Scale, placement.Y + ok.Y * placement.Scale, placement.Scale);
            }
        }

        /// <summary>A side's name in its window's top band; orange (the menus' selected tint) once that side pressed OK (round 1, image-24).</summary>
        private static void DrawName(StockUiRenderer renderer, UiFont font, string name, StockUiPlacement placement, bool accepted)
        {
            if (string.IsNullOrEmpty(name)) return;
            float ts = placement.Scale * StockUiPartyWindow.TextScale;
            renderer.DrawText(font, name, placement.X + NameX * placement.Scale, placement.Y + NameY * placement.Scale, ts,
                accepted ? StockUiMenuWindow.SelectedGlyphTint : null);
        }
    }
}
