// src/Gordian.App/Graphics/StockUiRegionInfoWindow.cs
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws a Besieged, Campaign or Colonization page's panels and help bar (#492, <see cref="StockUiRegionInfo"/>) over its
    /// region map (<see cref="StockUiRegionMapWindow"/>): each panel at its DAT place relative to the page's first, its values
    /// in the log font at the conquest page's 0.75 (<see cref="StockUiConquest.ValueScale"/>), right-aligned ones ending at
    /// <see cref="StockUiRegionInfo.ValueRight"/>, a bar filled over the frame's baked track. Besieged's panels move to the
    /// screen's left while the map is panned right of its centre (mirrored, as the conquest window does). PROVISIONAL: the
    /// bar's colour and every value's place (no capture of these pages with figures yet).
    /// </summary>
    public static class StockUiRegionInfoWindow
    {
        private static readonly UiColor White = new(0x80, 0x80, 0x80, 0x80);
        private static readonly UiColor BarColor = new(0x30, 0x68, 0x28, 0x80);
        private const string SolidTexture = "gauge";

        /// <summary>The bar's thickness (layout px) over the baked track.</summary>
        private const float BarHeight = 4;

        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, StockUiScreen screen)
        {
            if (menu.RegionInfoView is not { } view) return;
            float s = placement.Scale;
            if (screen.Width > 0 && screen.Height > 0)
            {
                StockUiHelpBar.DrawOnScreen(renderer, library, font, logFont, view.Title, view.HelpLine, s, (uint)screen.Width, (uint)screen.Height);
            }
            bool mirrored = view.PanelsSwapSides && screen.Width > 0 && menu.MapCenterX > 0.5f;
            foreach (var panel in view.Panels)
            {
                if (!library.TryGetMenu(panel.Window, out var window)) continue;
                var frame = window.Frame;
                var at = placement with
                {
                    X = placement.X + (frame.X - menu.Menu.Frame.X) * s,
                    Y = placement.Y + (frame.Y - menu.Menu.Frame.Y) * s,
                };
                if (mirrored) at = at with { X = screen.Width - at.X - frame.Width * s };
                renderer.DrawMenu(window, at, includeButtons: false, opaqueBody: true);
                float ts = s * StockUiConquest.ValueScale;
                foreach (var bar in panel.Bars)
                {
                    renderer.DrawTextureRect(SolidTexture, 1, 1, 1, 1, at.X + bar.X * s, at.Y + bar.Y * s, bar.Width * bar.Fill * s, BarHeight * s, BarColor);
                }
                foreach (var text in panel.Texts)
                {
                    float width = logFont != null ? logFont.MeasureWidth(text.Text) * ts : 0;
                    float x = at.X + text.X * s - (text.RightAligned ? width : 0);
                    StockUiTextBoxes.DrawLine(renderer, font, logFont, text.Text, x, at.Y + text.Y * s, ts, White);
                }
            }
        }
    }
}
