// src/Gordian.App/Graphics/StockUiRegionInfoWindow.cs
using System;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws a Besieged, Campaign or Colonization page's panel and help bar (#492, <see cref="StockUiRegionInfo"/>) over its region
    /// map (<see cref="StockUiRegionMapWindow"/>): the panel at the top right, at the top left while the cursor is in the
    /// screen's right half (as the conquest window, and every capture of 2026-11-03), its values in the log font at the
    /// conquest page's 0.75 (retail's are italic), its bars filled red or blue over a track. The Besieged panels are 136 px
    /// wide where their frame is 112: the frame's body is stretched and its baked labels are not. The box in the target
    /// window's place (Imperial Standing, Allied Notes, Bayld) is drawn by <see cref="DrawBox"/>.
    /// </summary>
    public static class StockUiRegionInfoWindow
    {
        private static readonly UiColor White = new(0x80, 0x80, 0x80, 0x80);

        /// <summary>The bars' fills (image-19 and -22 red about 230,50,50; image-2 blue about 90,160,220).</summary>
        private static readonly UiColor RedBar = new(0x73, 0x19, 0x19, 0x80);
        private static readonly UiColor BlueBar = new(0x2D, 0x50, 0x6E, 0x80);
        private static readonly UiColor Track = new(0x30, 0x30, 0x30, 0x60);
        private const string SolidTexture = "gauge";

        /// <summary>The bars' thickness and a crest's size (layout px).</summary>
        private const float BarHeight = 6, IconX = 3, IconY = 8, IconSize = 24;

        /// <summary>The baked labels of a stretched frame: the parts of the <c>FONT</c> texture.</summary>
        private static bool IsLabel(UiSpritePart part) => part.TextureName.StartsWith("FONT", StringComparison.OrdinalIgnoreCase);

        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, StockUiScreen screen)
        {
            if (menu.RegionInfoView is not { } view) return;
            float s = placement.Scale;
            if (screen.Width > 0 && screen.Height > 0)
            {
                StockUiHelpBar.DrawOnScreen(renderer, library, font, logFont, view.Title, view.HelpLine, s, (uint)screen.Width, (uint)screen.Height);
            }
            bool left = false;
            if (screen.Width > 0 && screen.Height > 0)
            {
                var (mx, _, mw, _) = StockUiMapWindow.Placement(menu, (int)StockUiConquest.MapSize, (int)StockUiConquest.MapSize, (uint)screen.Width, (uint)screen.Height);
                left = mx + menu.MapCursorX * mw > screen.Width * 0.5f;
            }
            foreach (var panel in view.Panels)
            {
                if (!library.TryGetMenu(panel.Window, out var window)) continue;
                var frame = window.Frame;
                float width = panel.Width > 0 ? panel.Width : frame.Width;
                // Every panel stands in the same top right place (beseige2's frame says x 16; retail draws it where beseige1 is);
                // a wider panel grows to the left from that right edge.
                var at = placement with { X = placement.X + (menu.Menu.Frame.Width - width) * s };
                if (left) at = at with { X = screen.Width - at.X - width * s };
                if (panel.Width > 0)
                {
                    renderer.DrawMenu(window, at, includeButtons: false, frameWidth: width, excludeFramePart: IsLabel, opaqueBody: true);
                    foreach (var shape in frame.Shapes)
                    {
                        if (shape.Kind != 0 || !library.TryGetImage(shape, out var image)) continue;
                        var labels = new UiImage { Parts = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(image.Parts, IsLabel)) };
                        renderer.DrawMenu(window, at, includeButtons: false, frameImage: labels, border: false);
                    }
                }
                else
                {
                    renderer.DrawMenu(window, at, includeButtons: false, opaqueBody: true);
                }
                if (panel.Icon is { } icon && library.TryGetTexture(icon, out var crest) && crest != null)
                {
                    renderer.DrawTextureRect(icon, 0, 0, crest.Width, crest.Height, at.X + IconX * s, at.Y + IconY * s, IconSize * s, IconSize * s, White);
                }
                if (panel.Window == StockUiRegionInfo.BaseWindow)
                {
                    // The ball before the "Enemy Base" name (image-2).
                    renderer.DrawTextureRect("colorbal", 0, 16, 16, 16, at.X + 11 * s, at.Y + 26 * s, 8 * s, 8 * s, White);
                }
                foreach (var bar in panel.Bars)
                {
                    float x = at.X + bar.X * s, y = at.Y + bar.Y * s;
                    if (panel.Window == StockUiRegionInfo.BaseWindow) renderer.DrawTextureRect(SolidTexture, 1, 1, 1, 1, x, y, bar.Width * s, BarHeight * s, Track);
                    renderer.DrawTextureRect(SolidTexture, 1, 1, 1, 1, x, y, bar.Width * bar.Fill * s, BarHeight * s,
                        bar.Color == StockUiRegionInfoBarColor.Blue ? BlueBar : RedBar);
                }
                DrawTexts(renderer, font, logFont, panel.Texts, at);
            }
        }

        /// <summary>
        /// The box in the target window's place (the maintainer's round 1 feedback, and every capture): right- and bottom-aligned
        /// with <paramref name="target"/>'s frame, as the conquest map's Conquest Points.
        /// </summary>
        public static void DrawBox(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiRegionInfoView view,
            StockUiPlacement target, float targetWidth, float targetHeight)
        {
            if (!library.TryGetMenu(view.BoxWindow, out var box)) return;
            float s = target.Scale;
            var frame = box.Frame;
            var at = target with { X = target.X + (targetWidth - frame.Width) * s, Y = target.Y + (targetHeight - frame.Height) * s };
            renderer.DrawMenu(box, at, includeButtons: false, opaqueBody: true);
            DrawTexts(renderer, font, logFont, new[] { new StockUiRegionInfoText(StockUiRegionInfo.BoxValueRight, StockUiRegionInfo.BoxValueY, view.BoxValue, RightAligned: true) }, at);
        }

        private static void DrawTexts(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, System.Collections.Generic.IReadOnlyList<StockUiRegionInfoText> texts, StockUiPlacement at)
        {
            float s = at.Scale, ts = s * StockUiConquest.ValueScale;
            foreach (var text in texts)
            {
                float width = logFont != null ? logFont.MeasureWidth(text.Text) * ts : 0;
                float x = at.X + text.X * s - (text.RightAligned ? width : 0);
                StockUiTextBoxes.DrawLine(renderer, font, logFont, text.Text, x, at.Y + text.Y * s, ts, White);
            }
        }
    }
}
