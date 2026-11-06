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

        /// <summary>
        /// The points box the open windows show, or null: the top one of Merit Points, Job Points, Alter Ego Points, Unity; over
        /// the merit categories and merits <c>meritinf</c> (#491).
        /// </summary>
        public static string? BoxOf(IReadOnlyList<StockUiOpenMenu> open)
        {
            for (int i = open.Count - 1; i >= 0; i--)
            {
                var menu = open[i];
                if (menu.Name.Equals(StockUiStatusPages.MeritModeMenu, StringComparison.OrdinalIgnoreCase)) return StockUiStatusPages.MeritBox;
                if (menu.Name.Equals(StockUiProgression.MeritEditMenu, StringComparison.OrdinalIgnoreCase)) return StockUiProgression.MeritInfoBox;
                switch (menu.ProgressionList)
                {
                    case StockUiProgressionList.MeritCategories or StockUiProgressionList.Merits: return StockUiProgression.MeritInfoBox;
                }
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

        /// <summary>
        /// Whether the box stands in the level box's place (16,208): the merit box does, and the info boxes of #491 (16,240) take
        /// it too; Unity's sits over the party window.
        /// </summary>
        public static bool ReplacesLevelBox(string? box) => box is StockUiStatusPages.MeritBox or StockUiProgression.MeritInfoBox;

        /// <summary>
        /// The Status list window that covers the stats side, or null: the top window, or the list under <c>merit3</c> or a prompt
        /// opened over it (#491).
        /// </summary>
        public static StockUiOpenMenu? TopList(IReadOnlyList<StockUiOpenMenu> open)
        {
            for (int i = open.Count - 1; i >= 0; i--)
            {
                var menu = open[i];
                if (menu.IsStatusValueList) return menu;
                if (!menu.IsPrompt && !menu.Name.Equals(StockUiProgression.MeritEditMenu, StringComparison.OrdinalIgnoreCase)) return null;
            }
            return null;
        }

        /// <summary>
        /// The help bar's title over a Status list: its window's title (ROM/165/76); "Unity" (title 173) over Unity's windows, whose
        /// headings ("Unity Information") do not fit the title box.
        /// </summary>
        public static string TitleOf(StockUiOpenMenu list) => list.ProgressionList switch
        {
            StockUiProgressionList.UnityInfo or StockUiProgressionList.UnityRanking or StockUiProgressionList.UnityLeaders => "Unity",
            _ when list.IsRoeList => StockUiRecordsOfEminence.HelpTitle,
            _ => list.StatusWindow is StockUiStatusPage.Currencies or StockUiStatusPage.Currencies2
                ? StockUiStatusPages.CurrenciesTitle
                : StockUiStatusPages.CategoriesTitle,
        };

        /// <summary>Whether the help bar shows the selected entry's help line of this window (the Status list and its own windows).</summary>
        public static bool ShowsHelp(StockUiOpenMenu menu) =>
            menu.IsStatusList || menu.StatusWindow != StockUiStatusPage.None
            || menu.Name.Equals(StockUiStatusPages.MeritModeMenu, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Draws <paramref name="box"/> with its values: at its DAT place, or the Unity Points box on top of
        /// <paramref name="party"/> (the party window's placement) when it is shown.
        /// </summary>
        public static void DrawBox(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, string box, IReadOnlyList<StockUiOpenMenu> open,
            StockUiStatusPoints? points, float scale, uint width, uint height, StockUiPlacement? party = null, StockUiMenuController? menus = null)
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
                    Value(renderer, font, at, string.Create(inv, $"{totals.JobPoints}/{StockUiStatusPages.MaxJobPoints} P"), LowerValueY,
                        totals.JobPoints >= StockUiStatusPages.MaxJobPoints ? StockUiProgression.AtMost : null);
                    break;
                case StockUiStatusPages.AlterEgoBox:
                    Value(renderer, font, at, string.Create(inv, $"{points.AlterEgoPoints}/{StockUiStatusPages.MaxAlterEgoPoints}"), SingleValueY);
                    break;
                case StockUiStatusPages.UnityBox:
                    Value(renderer, font, at, string.Create(inv,
                        $"{points.Currency1(Currency1Kind.UnityAccolades)}/{StockUiStatusPages.MaxUnityAccolades}"), SingleValueY);
                    break;
                case StockUiProgression.MeritInfoBox:
                {
                    // meritinf (#491; retail client, 2026-11-06): the merit points held ("15 P") under "Merit Points", the category's
                    // upgrades and most under "Combo Total/Max", a job's two groups side by side, the first yellow.
                    Value(renderer, font, at, string.Create(inv, $"{points.MeritPoints} P"), InfoUpperValueY);
                    if (MeritCategoryOf(open) is > 0 and var category && menus?.Merits?.Invoke() is { } merits)
                    {
                        string? help = menus.MeritText?.Invoke(category * 2 + 1);
                        string Group(int g) => string.Create(inv, $"{StockUiProgression.GroupTotal(category, g, merits)}/{StockUiProgression.GroupMax(help, g)}");
                        if (StockUiProgression.HasMeritGroups(category))
                        {
                            float ts = at.Scale * StockUiPartyWindow.TextScale;
                            string first = Group(1);
                            renderer.DrawText(font, first, at.X + FirstGroupValueRight * at.Scale - font.MeasureWidth(first) * ts, at.Y + InfoLowerValueY * at.Scale, ts,
                                StockUiProgression.GroupYellow);
                            Value(renderer, font, at, Group(2), InfoLowerValueY);
                        }
                        else
                        {
                            int max = StockUiProgression.CategoryMax(help);
                            int total = StockUiProgression.CategoryTotal(category, merits);
                            Value(renderer, font, at, max > 0 ? string.Create(inv, $"{total}/{max}") : total.ToString(inv), InfoLowerValueY);
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>The Job Points pages' title bar in <c>jbpcat</c> (image-30, image-31): left, top, height and its text's places.</summary>
        private const float TitleBarX = 4, TitleBarY = 2, TitleBarHeight = 14, TitleTextX = 28, ComboTotalX = 170, TitleBarRightPad = 14;

        /// <summary>The title bar's blue-grey, top to bottom, read by eye from image-30 (PROVISIONAL).</summary>
        private static readonly (byte R, byte G, byte B)[] TitleBarShades =
        [
            (112, 124, 150), (100, 114, 142), (90, 104, 134), (82, 96, 126), (74, 88, 118), (66, 80, 110), (58, 72, 102), (50, 64, 94),
        ];

        /// <summary>The red page arrows of <c>frames</c>, the paging window's (<c>quest01</c> buttons 9, 10).</summary>
        private const int LeftArrowImage = 25, RightArrowImage = 26;

        /// <summary>
        /// The title bar of the Job Points pages, over <c>jbpcat</c>'s rows: "Job Points", or "Gifts" and "Combo Total:" the job
        /// points spent, between the red arrows at the DAT's buttons 11 and 12 (the frame has the room, nothing in the DAT draws it).
        /// </summary>
        public static void DrawJobPointTitle(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiOpenMenu menu, StockUiPlacement at)
        {
            bool gifts = menu.ProgressionList == StockUiProgressionList.JobGifts;
            if (!gifts && menu.ProgressionList != StockUiProgressionList.JobPointCategories) return;
            float s = at.Scale, ts = s * StockUiPartyWindow.TextScale;
            var f = menu.Menu.Frame;
            StockUiMenuWindow.DrawShades(renderer, TitleBarShades, at.X + TitleBarX * s, at.Y + TitleBarY * s, at.X + (f.Width - TitleBarRightPad) * s, TitleBarHeight * s);
            float y = at.Y + (TitleBarY + 1) * s;
            renderer.DrawText(font, gifts ? StockUiProgression.JobGiftsPageTitle : StockUiProgression.JobPointsPageTitle, at.X + TitleTextX * s, y, ts, White);
            if (gifts)
            {
                string combo = string.Create(CultureInfo.InvariantCulture, $"{StockUiProgression.ComboTotalLabel} {menu.JobGiftsSpent}");
                renderer.DrawText(font, combo, at.X + ComboTotalX * s, y, ts, White);
            }
            if (library.TryGetGroup("frames", out var frames) && frames.Images.Count > RightArrowImage)
            {
                renderer.DrawImage(frames.Images[LeftArrowImage], at.X + 9 * s, at.Y + 3 * s, s);
                renderer.DrawImage(frames.Images[RightArrowImage], at.X + 336 * s, at.Y + 3 * s, s);
            }
        }

        /// <summary>The right edge of a job's first group's figure in <c>meritinf</c> (retail client, 2026-11-06).</summary>
        private const float FirstGroupValueRight = 49;

        /// <summary>A job's group header row (retail client, 2026-11-06): "Group1" and its limit in the title bar's blue-grey.</summary>
        public static void DrawGroupHeader(StockUiRenderer renderer, UiFont font, StockUiListRow row, StockUiPlacement at, float rowY, float rowHeight)
        {
            float s = at.Scale, ts = s * StockUiPartyWindow.TextScale;
            StockUiMenuWindow.DrawShades(renderer, TitleBarShades, at.X + TitleBarX * s, rowY + s, at.X + GroupBarRight * s, (rowHeight - 2) * s);
            float y = rowY + (rowHeight * s - font.LineHeight * ts) * 0.5f;
            renderer.DrawText(font, row.Text, at.X + GroupNameX * s, y, ts, White);
            if (row.Note is { Length: > 0 } limit) renderer.DrawText(font, limit, at.X + GroupLimitX * s, y, ts, White);
        }

        /// <summary>The group header's right end and its text's places.</summary>
        private const float GroupBarRight = 362, GroupNameX = 8, GroupLimitX = 104;

        /// <summary>The value tops in <c>meritinf</c> (112 x 56): under "Merit Points" and under "Combo Total/Max".</summary>
        private const float InfoUpperValueY = 16, InfoLowerValueY = 39;

        /// <summary>The merit category the open windows show: the one under the categories' cursor, or the merits list's.</summary>
        private static int MeritCategoryOf(IReadOnlyList<StockUiOpenMenu> open)
        {
            for (int i = open.Count - 1; i >= 0; i--)
            {
                if (open[i].ProgressionList == StockUiProgressionList.Merits) return open[i].ProgressionKey;
                if (open[i].ProgressionList == StockUiProgressionList.MeritCategories) return open[i].SelectedRowKey;
            }
            return -1;
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
            switch (top.ProgressionList)
            {
                case StockUiProgressionList.MeritCategories when top.SelectedRowKey > 0:
                    menuName = StockUiStatusPages.InfoBox;
                    nameId = top.SelectedRowKey * 2;
                    strings = menus.MeritText;
                    break;
                case StockUiProgressionList.Merits when top.SelectedRowKey == 0:
                    // A group header: the box stays, empty (retail client).
                    if (library.TryGetMenu(StockUiStatusPages.InfoBox, out var empty))
                    {
                        var e = empty.Frame;
                        renderer.DrawMenu(empty, StockUiLayout.Place(e.Anchor, e.X, e.Y, e.Width, e.Height, scale, width, height), includeButtons: false, opaqueBody: true);
                    }
                    return;
                case StockUiProgressionList.Merits when top.SelectedRowKey > 0:
                    menuName = StockUiStatusPages.InfoBox;
                    nameId = top.SelectedRowKey;
                    strings = menus.MeritText;
                    break;
                case StockUiProgressionList.JobPointCategories when top.SelectedRowKey >= 0:
                    menuName = StockUiStatusPages.InfoBox;
                    nameId = StockUiProgression.JobPointCategoryNameId(top.ProgressionKey, top.SelectedRowKey);
                    strings = menus.JobPointText;
                    break;
                case StockUiProgressionList.JobGifts when top.SelectedRowKey >= 0:
                    menuName = StockUiStatusPages.InfoBox;
                    nameId = StockUiProgression.JobGiftNameId(top.ProgressionKey, top.SelectedRowKey);
                    strings = menus.JobGiftText;
                    break;
                default:
                    menuName = string.Empty;
                    nameId = -1;
                    strings = null;
                    break;
            }
            if (top.ProgressionList == StockUiProgressionList.None) switch (top.StatusWindow)
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
            if (nameId < 0 || !library.TryGetMenu(menuName, out var window)) return;
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
                if (open[i].ProgressionList is StockUiProgressionList.JobPointCategories or StockUiProgressionList.JobGifts) return open[i].ProgressionKey;
                if (open[i].StatusWindow == StockUiStatusPage.JobPoints && open[i].SelectedStatusRow is >= 0 and var row) return row + 1;
            }
            return points.MainJob;
        }

        /// <summary>
        /// The Records of Eminence lists' help bar (#493), drawn by the HUD while one is on top: "RoE" with no line (retail client).
        /// </summary>
        public static void DrawRoe(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiMenuController menus,
            float scale, uint width, uint height)
        {
            var open = menus.OpenMenus;
            if (open.Count == 0 || open[^1] is not { IsRoeList: true } top) return;
            StockUiHelpBar.DrawOnScreen(renderer, library, font, logFont, StockUiRecordsOfEminence.HelpTitle, null, scale, width, height);
        }

        private static void Value(StockUiRenderer renderer, UiFont font, StockUiPlacement at, string text, float y, UiColor? color = null)
        {
            float s = at.Scale, ts = s * StockUiPartyWindow.TextScale;
            renderer.DrawText(font, text, at.X + ValueRight * s - font.MeasureWidth(text) * ts, at.Y + y * s, ts, color ?? White);
        }
    }
}
