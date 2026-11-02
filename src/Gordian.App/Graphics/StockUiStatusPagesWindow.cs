// src/Gordian.App/Graphics/StockUiStatusPagesWindow.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The points and info boxes of the Status window's own windows (#487, <see cref="StockUiStatusPages"/>), each the English
    /// menu DAT's window with its labels baked in: <c>levmerit</c> ("Limit Points", "Merit Points") with Merit Points,
    /// <c>levmeri2</c> ("Capacity Points", "Job Points") with Job Points, <c>fp_info</c> ("Alter Ego Points") with Alter Ego
    /// Points, <c>ut_point</c> ("Unity Points") with Unity, docked on top of the party window where the target window stands
    /// (round 1 image-10). The values are drawn under each label, right-aligned, in <c>fontshp</c> at 7/8 as the level box's
    /// numbers, with their maximums ("8074/10000", "15/30 P", "0/500 P", "175/1000", "50000/99999": round 1 images 6-10). The
    /// info box <c>bluehelp</c> at 130,240 under Job Points and Alter Ego Points shows the row's name and its
    /// line (images 7-9).
    /// </summary>
    public static class StockUiStatusPagesWindow
    {
        private static readonly UiColor White = new(0x80, 0x80, 0x80, 0x80);

        // Value tops under the labels, and the right edge they end at (round 1 images 6 and 7: the numbers end ~4 px in).
        private const float ValueRight = 106, UpperValueY = 22, LowerValueY = 67, SingleValueY = 20;

        // The info boxes (round 1 image-7): the name from (6, 4), the line wrapped from (18, 20).
        private const float InfoTitleX = 6, InfoTitleY = 4, InfoTextX = 18, InfoTextY = 20, InfoRightPad = 8;

        /// <summary>The gap between the docked Unity Points box and the party window (the target window's).</summary>
        private const float DockGap = 2;

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

        /// <summary>Whether the Unity Points box is up: it takes the target window's place over the party window.</summary>
        public static bool DocksOverParty(StockUiMenuController menus) =>
            !menus.Suspended && menus.IsStatusOpen && BoxOf(menus.OpenMenus) == StockUiStatusPages.UnityBox;

        /// <summary>Whether the box stands in the level box's place (16,208): the merit box does; Unity's sits over the party window.</summary>
        public static bool ReplacesLevelBox(string? box) => box == StockUiStatusPages.MeritBox;

        /// <summary>The help bar's title over a Status list: its window's title (ROM/165/76).</summary>
        public static string TitleOf(StockUiOpenMenu list) => list.StatusWindow is StockUiStatusPage.Currencies or StockUiStatusPage.Currencies2
            ? StockUiStatusPages.CurrenciesTitle
            : StockUiStatusPages.CategoriesTitle;

        /// <summary>Whether the help bar shows the selected entry's help line of this window (the Status list and its own windows).</summary>
        public static bool ShowsHelp(StockUiOpenMenu menu) =>
            menu.IsStatusList || menu.StatusWindow != StockUiStatusPage.None
            || menu.Name.Equals(StockUiStatusPages.MeritModeMenu, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Draws <paramref name="box"/> with its values: at its DAT place, or the Unity Points box on top of
        /// <paramref name="party"/> (the party window's placement) when it is shown.
        /// </summary>
        public static void DrawBox(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, string box, IReadOnlyList<StockUiOpenMenu> open,
            StockUiStatusPoints? points, float scale, uint width, uint height, StockUiPlacement? party = null)
        {
            if (!library.TryGetMenu(box, out var window)) return;
            var at = Place(window, box, scale, width, height, party);
            renderer.DrawMenu(window, at, includeButtons: false, opaqueBody: true);
            if (points == null) return;
            var inv = CultureInfo.InvariantCulture;
            switch (box)
            {
                case StockUiStatusPages.MeritBox:
                    Value(renderer, font, at, string.Create(inv, $"{points.LimitPoints}/{StockUiStatusPages.MaxLimitPoints}"), UpperValueY);
                    Value(renderer, font, at, string.Create(inv, $"{points.MeritPoints}/{points.MaxMeritPoints} P"), LowerValueY);
                    break;
                case StockUiStatusPages.JobPointBox:
                    int job = JobUnderCursor(open, points);
                    var totals = StockUiStatusPages.JobPointsOf(points, job);
                    Value(renderer, font, at, string.Create(inv, $"{totals.CapacityPoints}/{StockUiStatusPages.MaxCapacityPoints}"), UpperValueY);
                    Value(renderer, font, at, string.Create(inv, $"{totals.JobPoints}/{StockUiStatusPages.MaxJobPoints} P"), LowerValueY);
                    break;
                case StockUiStatusPages.AlterEgoBox:
                    Value(renderer, font, at, string.Create(inv, $"{points.AlterEgoPoints}/{StockUiStatusPages.MaxAlterEgoPoints}"), SingleValueY);
                    break;
                case StockUiStatusPages.UnityBox:
                    Value(renderer, font, at, string.Create(inv,
                        $"{points.Currency1(Currency1Kind.UnityAccolades)}/{StockUiStatusPages.MaxUnityAccolades}"), SingleValueY);
                    break;
            }
        }

        /// <summary>Where a box goes: Unity's on top of the party window (when it is up), the rest at their DAT places.</summary>
        public static StockUiPlacement Place(UiMenuDefinition window, string box, float scale, uint width, uint height, StockUiPlacement? party)
        {
            var f = window.Frame;
            var at = StockUiLayout.Place(f.Anchor, f.X, f.Y, f.Width, f.Height, scale, width, height);
            if (box == StockUiStatusPages.UnityBox && party is { Hidden: false } p)
            {
                at = at with { X = p.X, Y = Math.Max(0, p.Y - (f.Height + DockGap) * at.Scale) };
            }
            return at;
        }

        /// <summary>
        /// The info box under a Job Points or Alter Ego Points list (<c>bluehelp</c> at 130,240): the row's name,
        /// then its line wrapped (ROM/314/62, ROM/384/121).
        /// </summary>
        public static void DrawInfo(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu top,
            StockUiMenuController menus, float scale, uint width, uint height)
        {
            if (!top.IsStatusValueList || top.SelectedStatusRow is not (>= 0 and var row)) return;
            string menuName;
            int nameId;
            Func<int, string?>? strings;
            switch (top.StatusWindow)
            {
                case StockUiStatusPage.JobPoints:
                    menuName = StockUiStatusPages.InfoBox;
                    nameId = StockUiStatusPages.JobPointNameId(row + 1);
                    strings = menus.JobPointText;
                    break;
                case StockUiStatusPage.AlterEgoPoints when top.AlterEgoGroup < 0:
                    menuName = StockUiStatusPages.InfoBox;
                    nameId = StockUiStatusPages.AlterEgoGroupNameId(row);
                    strings = menus.AlterEgoText;
                    break;
                case StockUiStatusPage.AlterEgoPoints:
                    menuName = StockUiStatusPages.InfoBox;
                    nameId = StockUiStatusPages.AlterEgoItemNameId(StockUiStatusPages.AlterEgoGroups[top.AlterEgoGroup].First + row);
                    strings = menus.AlterEgoText;
                    break;
                default:
                    return;
            }
            if (!library.TryGetMenu(menuName, out var window)) return;
            var f = window.Frame;
            var at = StockUiLayout.Place(f.Anchor, f.X, f.Y, f.Width, f.Height, scale, width, height);
            renderer.DrawMenu(window, at, includeButtons: false, opaqueBody: true);
            float s = at.Scale;
            string name = strings?.Invoke(nameId) is { Length: > 0 } n ? n : top.Rows[row].Text;
            StockUiTextBoxes.DrawLine(renderer, font, logFont, name, at.X + InfoTitleX * s, at.Y + InfoTitleY * s, s, White);
            StockUiTextBoxes.DrawWrapped(renderer, font, logFont, strings?.Invoke(nameId + 1), at.X + InfoTextX * s, at.Y + InfoTextY * s,
                (f.Width - InfoTextX - InfoRightPad) * s, (f.Height - InfoTextY) * s, s, White);
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
