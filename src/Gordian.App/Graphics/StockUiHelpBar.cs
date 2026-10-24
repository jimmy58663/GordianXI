// src/Gordian.App/Graphics/StockUiHelpBar.cs
using System;
using System.Globalization;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The help bar at the top of the screen, one layout for every window that has one (#418; until then the Items, check and
    /// Magic / Key Items windows each drew their own copy): the title in <c>titlewin</c> (16,16) and the help line in
    /// <c>helpwind</c> (130,16) stretched to 16 px short of the screen's right edge, both in the log font 8 px in and centred
    /// on the box's height (retail captures 2026-10-11/12 of the Items, Magic and check windows). An item list's title
    /// carries the "23/30" fraction after it in the small font, the "/" at x 88 and y 12 (retail raises the used count above
    /// the "/"; the maintainer asked for it inline after rounds 2 and 3, 2026-10-12).
    /// <para>
    /// The bar goes either at the DAT's places on the screen (<see cref="DrawOnScreen"/>) or at those places relative to a
    /// window that was moved (<see cref="DrawBeside"/>), so it follows the window as the authored layout has it.
    /// </para>
    /// </summary>
    public static class StockUiHelpBar
    {
        private const float TextX = 8, HelpRightMargin = 16, FractionSlashX = 88, FractionY = 12;

        /// <summary>The help bar at its authored places on a screen <paramref name="width"/> x <paramref name="height"/> px.</summary>
        public static void DrawOnScreen(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont,
            string title, string? text, float scale, uint width, uint height, int? numerator = null, int? denominator = null)
        {
            StockUiPlacement? titleAt = null, helpAt = null;
            if (library.TryGetMenu(StockUiCheck.TitleMenu, out var titleWindow))
            {
                var f = titleWindow.Frame;
                titleAt = StockUiLayout.Place(f.Anchor, f.X, f.Y, f.Width, f.Height, scale, width, height);
            }
            if (library.TryGetMenu(StockUiCheck.HelpMenu, out var help))
            {
                var f = help.Frame;
                helpAt = StockUiLayout.Place(f.Anchor, f.X, f.Y, f.Width, f.Height, scale, width, height);
            }
            Draw(renderer, library, font, logFont, title, text, titleAt, helpAt, width, numerator, denominator);
        }

        /// <summary>
        /// The help bar at its authored places relative to a window drawn at <paramref name="placement"/> whose authored frame is
        /// <paramref name="frame"/> (<see cref="StockUiWindowPlacement.Beside(StockUiPlacement, UiMenuFrame, UiMenuFrame)"/>);
        /// the help line stretches to the screen's right edge when the screen is known.
        /// </summary>
        public static void DrawBeside(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont,
            string title, string? text, StockUiPlacement placement, UiMenuFrame frame, StockUiScreen screen, int? numerator = null, int? denominator = null)
        {
            StockUiPlacement? titleAt = library.TryGetMenu(StockUiCheck.TitleMenu, out var titleWindow)
                ? StockUiWindowPlacement.Beside(placement, frame, titleWindow.Frame) : null;
            StockUiPlacement? helpAt = library.TryGetMenu(StockUiCheck.HelpMenu, out var help)
                ? StockUiWindowPlacement.Beside(placement, frame, help.Frame) : null;
            Draw(renderer, library, font, logFont, title, text, titleAt, helpAt, screen.Width > 0 ? screen.Width : null, numerator, denominator);
        }

        private static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont,
            string title, string? text, StockUiPlacement? titleAt, StockUiPlacement? helpAt, float? screenWidth, int? numerator, int? denominator)
        {
            if (titleAt is { } t && library.TryGetMenu(StockUiCheck.TitleMenu, out var titleWindow))
            {
                float s = t.Scale;
                renderer.DrawMenu(titleWindow, t, includeButtons: false, opaqueBody: true);
                StockUiTextBoxes.DrawLine(renderer, font, logFont, title, t.X + TextX * s, t.Y + (titleWindow.Frame.Height - StockUiLogFont.CellHeight) * 0.5f * s, s);
                if (numerator is { } used && denominator is { } total)
                {
                    float ts = s * StockUiPartyWindow.TextScale;
                    string usedText = used.ToString(CultureInfo.InvariantCulture);
                    renderer.DrawText(font, usedText + "/" + total.ToString(CultureInfo.InvariantCulture),
                        t.X + FractionSlashX * s - font.MeasureWidth(usedText) * ts, t.Y + FractionY * s, ts);
                }
            }
            if (helpAt is { } h && library.TryGetMenu(StockUiCheck.HelpMenu, out var help))
            {
                float s = h.Scale;
                var f = help.Frame;
                float? width = screenWidth is { } sw ? Math.Max(f.Width, (sw - h.X) / s - HelpRightMargin) : null;
                renderer.DrawMenu(help, h, includeButtons: false, opaqueBody: true, frameWidth: width);
                if (!string.IsNullOrEmpty(text))
                {
                    StockUiTextBoxes.DrawLine(renderer, font, logFont, text.Replace("\n", " "), h.X + TextX * s, h.Y + (f.Height - StockUiLogFont.CellHeight) * 0.5f * s, s);
                }
            }
        }
    }
}
