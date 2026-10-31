// src/Gordian.App/Graphics/StockUiStatusPagesWindow.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The points boxes of the Status window's own windows (#487, <see cref="StockUiStatusPages"/>), each the English menu
    /// DAT's window with its labels baked in: <c>levmerit</c> ("Limit Points", "Merit Points") with Merit Points,
    /// <c>levmeri2</c> ("Capacity Points", "Job Points") with Job Points, <c>fp_info</c> ("Alter Ego Points") with Alter Ego
    /// Points, <c>ut_point</c> ("Unity Points") with Unity. The values are drawn under each label, right-aligned, in
    /// <c>fontshp</c> at 7/8 as the level box's numbers. PROVISIONAL: the value places and formats are not checked against a
    /// retail capture (the labels' places are the DAT's: rows from y 5 and y 50 in the 88-tall boxes, y 4 in the 40-tall ones).
    /// </summary>
    public static class StockUiStatusPagesWindow
    {
        private static readonly UiColor White = new(0x80, 0x80, 0x80, 0x80);

        // Value tops under the labels, and the right edge they end at.
        private const float ValueRight = 104, UpperValueY = 22, LowerValueY = 67, SingleValueY = 20;

        /// <summary>The points box the open windows show, or null: the top one of Merit Points, Job Points, Alter Ego Points, Unity.</summary>
        public static string? BoxOf(IReadOnlyList<StockUiOpenMenu> open)
        {
            for (int i = open.Count - 1; i >= 0; i--)
            {
                var menu = open[i];
                if (menu.Name.Equals(StockUiStatusPages.MeritModeMenu, StringComparison.OrdinalIgnoreCase)) return StockUiStatusPages.MeritBox;
                switch (menu.StatusWindow)
                {
                    case StockUiStatusPage.MeritPoints: return StockUiStatusPages.MeritBox;
                    case StockUiStatusPage.JobPoints: return StockUiStatusPages.JobPointBox;
                    case StockUiStatusPage.AlterEgoPoints: return StockUiStatusPages.AlterEgoBox;
                    case StockUiStatusPage.Unity: return StockUiStatusPages.UnityBox;
                }
                if (menu.IsStatusList) return null;
            }
            return null;
        }

        /// <summary>Whether the box stands in the level box's place (16,208): the merit box does; Unity's sits under the list.</summary>
        public static bool ReplacesLevelBox(string? box) => box == StockUiStatusPages.MeritBox;

        /// <summary>The help bar's title over a Status list: its window's title (ROM/165/76).</summary>
        public static string TitleOf(StockUiOpenMenu list) => list.StatusWindow is StockUiStatusPage.Currencies or StockUiStatusPage.Currencies2
            ? StockUiStatusPages.CurrenciesTitle
            : StockUiStatusPages.CategoriesTitle;

        /// <summary>Whether the help bar shows the selected entry's help line of this window (the Status list and its own windows).</summary>
        public static bool ShowsHelp(StockUiOpenMenu menu) =>
            menu.IsStatusList || menu.StatusWindow != StockUiStatusPage.None
            || menu.Name.Equals(StockUiStatusPages.MeritModeMenu, StringComparison.OrdinalIgnoreCase);

        /// <summary>Draws <paramref name="box"/> at its DAT place with its values.</summary>
        public static void DrawBox(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, string box, IReadOnlyList<StockUiOpenMenu> open,
            StockUiStatusPoints? points, float scale, uint width, uint height)
        {
            if (!library.TryGetMenu(box, out var window)) return;
            var f = window.Frame;
            var at = StockUiLayout.Place(f.Anchor, f.X, f.Y, f.Width, f.Height, scale, width, height);
            renderer.DrawMenu(window, at, includeButtons: false, opaqueBody: true);
            if (points == null) return;
            var inv = CultureInfo.InvariantCulture;
            switch (box)
            {
                case StockUiStatusPages.MeritBox:
                    Value(renderer, font, at, StockUiStatus.Number(points.LimitPoints), UpperValueY);
                    Value(renderer, font, at, points.MaxMeritPoints > 0
                        ? string.Create(inv, $"{points.MeritPoints}/{points.MaxMeritPoints}")
                        : points.MeritPoints.ToString(inv), LowerValueY);
                    break;
                case StockUiStatusPages.JobPointBox:
                    int job = JobUnderCursor(open, points);
                    var totals = StockUiStatusPages.JobPointsOf(points, job);
                    Value(renderer, font, at, StockUiStatus.Number(totals.CapacityPoints), UpperValueY);
                    Value(renderer, font, at, StockUiStatus.Number(totals.JobPoints), LowerValueY);
                    break;
                case StockUiStatusPages.AlterEgoBox:
                    Value(renderer, font, at, StockUiStatus.Number(points.AlterEgoPoints), SingleValueY);
                    break;
                case StockUiStatusPages.UnityBox:
                    Value(renderer, font, at, StockUiStatus.Number(points.Currency1(Gordian.Core.Network.Packets.Currency1Kind.UnityAccolades)), SingleValueY);
                    break;
            }
        }

        /// <summary>The job under the Job Points list's cursor (its rows are jobs 1-22), or the main job.</summary>
        private static int JobUnderCursor(IReadOnlyList<StockUiOpenMenu> open, StockUiStatusPoints points)
        {
            for (int i = open.Count - 1; i >= 0; i--)
            {
                if (open[i].StatusWindow == StockUiStatusPage.JobPoints && open[i].SelectedStatusRow is >= 0 and var row) return row + 1;
            }
            return points.MainJob;
        }

        private static void Value(StockUiRenderer renderer, UiFont font, StockUiPlacement at, string text, float y)
        {
            float s = at.Scale, ts = s * StockUiPartyWindow.TextScale;
            renderer.DrawText(font, text, at.X + ValueRight * s - font.MeasureWidth(text) * ts, at.Y + y * s, ts, White);
        }
    }
}
