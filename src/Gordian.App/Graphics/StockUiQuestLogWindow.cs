// src/Gordian.App/Graphics/StockUiQuestLogWindow.cs
using System;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws the client content of the Missions, Quests and Region Info windows (#488) over their DAT frames (see
    /// <see cref="StockUiQuestLog"/> and <see cref="StockUiConquest"/>):
    /// <list type="bullet">
    /// <item>The help bar: "Missions", "Quests" or "Region Info" and the open group, area or list ("San d'Oria: Current
    /// Quests").</item>
    /// <item>The pickers (<c>miss00</c>, <c>quest01</c>, <c>quest00</c>): each row the DAT's <c>hfr1</c> band (the rows'
    /// <c>quest</c> #0 image) with the name in the log font, white, pink under the cursor.</item>
    /// <item>The lists (<c>missiton</c>, <c>quset</c>): the names in the log font, a chapter's missions one step in, the
    /// mission in progress pale yellow; the empty list's text; the description page (the name, then the text wrapped to
    /// the window).</item>
    /// <item>The Conquest page: the values under <c>conquer1</c>'s baked-in headings (Regional Control, Next tally in,
    /// Regional Influence as four bars), or <c>conq6sta</c>'s Balance of Power; <c>conq3pts</c>'s Conquest Points
    /// under it.</item>
    /// </list>
    /// PROVISIONAL: no retail capture of these windows yet; the text places, colours and the bars are our reading.
    /// </summary>
    public static class StockUiQuestLogWindow
    {
        private static readonly UiColor White = new(0x80, 0x80, 0x80, 0x80);
        private static readonly UiColor SelectedTint = new(0x80, 0x41, 0x64, 0x80);
        private static readonly UiColor CurrentTint = new(0x80, 0x80, 0x50, 0x80);

        /// <summary>The influence bars: San d'Oria red, Bastok blue, Windurst green, the beastmen grey; the track dark.</summary>
        private static readonly UiColor[] BarColors =
        {
            new(0x70, 0x20, 0x20, 0x80), new(0x20, 0x38, 0x78, 0x80), new(0x30, 0x68, 0x28, 0x80), new(0x50, 0x50, 0x50, 0x80),
        };
        private static readonly UiColor TrackColor = new(0x10, 0x10, 0x18, 0x60);
        private const string SolidTexture = "gauge";

        /// <summary>Picker text 12 px into the band; list text 12 px into the row (24 into the window), 12 more per chapter level; the empty text at (20, 20).</summary>
        private const float PickerTextInset = 12, ListTextInset = 12, DepthIndent = 12, EmptyTextX = 20, EmptyTextY = 20;

        /// <summary>The description page: the name at (20, 12), the text from (20, 36).</summary>
        private const float DescNameX = 20, DescNameY = 12, DescTextX = 20, DescTextY = 36, DescRightPad = 16;

        /// <summary>The influence bars: 88 px wide, 4 px tall, under each side's name and share.</summary>
        private const float BarWidth = 88, BarHeight = 4, BarOffsetY = 15;

        /// <summary>The rows' band: the <c>quest</c> group's image 0 (<c>hfr1</c>, 320 x 18).</summary>
        private const string BandGroup = "quest";

        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp, StockUiScreen screen)
        {
            float s = placement.Scale;
            if (screen.Width > 0 && screen.Height > 0)
            {
                string? line = menu.QuestLogView == StockUiQuestLogView.Conquest ? menu.ConquestView?.RegionName
                    : menu.QuestLogHeading ?? menu.ItemHelpText(menu.IsQuestsWindow ? StockUiQuestLog.HelpQuests : StockUiQuestLog.HelpMissions);
                StockUiHelpBar.DrawOnScreen(renderer, library, font, logFont, menu.QuestLogTitle, line, s, (uint)screen.Width, (uint)screen.Height);
            }
            switch (menu.QuestLogView)
            {
                case StockUiQuestLogView.Conquest:
                    if (menu.ConquestView is { } view) DrawConquest(renderer, library, font, logFont, menu, view, placement);
                    break;
                case StockUiQuestLogView.Description:
                    DrawDescription(renderer, font, logFont, menu, placement);
                    break;
                case StockUiQuestLogView.List:
                    DrawRows(renderer, library, font, logFont, menu, placement, timestamp, band: false);
                    break;
                default:
                    DrawRows(renderer, library, font, logFont, menu, placement, timestamp, band: true);
                    break;
            }
        }

        private static void Text(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, string text, float x, float y, float s, UiColor color) =>
            StockUiTextBoxes.DrawLine(renderer, font, logFont, text, x, y, s, color);

        private static void DrawRows(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp, bool band)
        {
            float s = placement.Scale;
            var definition = menu.Menu;
            var frame = definition.Frame;
            if (menu.Rows.Count == 0)
            {
                if (menu.EmptyText is { } empty) Text(renderer, font, logFont, empty, placement.X + EmptyTextX * s, placement.Y + EmptyTextY * s, s, White);
                return;
            }
            if (definition.FindButton(1) is not { } firstRow) return;
            float pitch = definition.FindButton(2) is { } second ? second.Y - firstRow.Y : firstRow.Height;
            float first = StockUiMenuWindow.ScrolledFirstRow(menu, timestamp);
            float areaY = placement.Y + firstRow.Y * s;
            renderer.SetClip(placement.X, areaY, frame.Width * s, menu.VisibleRows * pitch * s);
            UiImage? bandImage = band && library.TryGetGroup(BandGroup, out var group) && group.Images.Count > 0 ? group.Images[0] : null;
            int selected = menu.EntryIndex(menu.SelectedButtonId);
            int from = Math.Max(0, (int)Math.Floor(first) - 1), to = Math.Min(menu.Rows.Count - 1, (int)Math.Ceiling(first) + menu.VisibleRows);
            float bx = placement.X + firstRow.X * s;
            for (int i = from; i <= to; i++)
            {
                var row = menu.Rows[i];
                float by = placement.Y + (firstRow.Y + (i - first) * pitch) * s;
                if (bandImage != null) renderer.DrawImage(bandImage, bx, by, s);
                float inset = band ? PickerTextInset : ListTextInset + (i < menu.QuestLogRows.Count ? menu.QuestLogRows[i].Entry.Depth * DepthIndent : 0);
                var color = i == selected ? SelectedTint : row.Marked ? CurrentTint : White;
                float ty = by + Math.Max(0, (firstRow.Height - StockUiTextBoxes.LinePitch(font, logFont)) * 0.5f) * s;
                Text(renderer, font, logFont, row.Text, bx + inset * s, ty, s, color);
            }
            renderer.ClearClip();
            if (menu.CanScroll) StockUiMenuWindow.DrawScrollbar(renderer, placement, frame, first, menu.Rows.Count, menu.VisibleRows);
        }

        private static void DrawDescription(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu, StockUiPlacement placement)
        {
            if (menu.QuestLogShown is not { } shown) return;
            float s = placement.Scale;
            var frame = menu.Menu.Frame;
            Text(renderer, font, logFont, shown.Entry.Name, placement.X + DescNameX * s, placement.Y + DescNameY * s, s,
                shown.Status == StockUiQuestLogStatus.Current ? CurrentTint : White);
            StockUiTextBoxes.DrawWrapped(renderer, font, logFont, shown.Entry.Description, placement.X + DescTextX * s, placement.Y + DescTextY * s,
                frame.Width - DescTextX - DescRightPad, frame.Height - DescTextY - DescRightPad, s, White, keepBlankLines: true);
        }

        private static void DrawConquest(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiConquestView view, StockUiPlacement placement)
        {
            float s = placement.Scale;
            // The frames are 112 px wide: the values are drawn smaller than the log font so a nation's name and share fit.
            float ts = s * StockUiConquest.ValueScale;
            var frame = menu.Menu.Frame;
            float x = placement.X + StockUiConquest.ValueX * s;
            float width = (frame.Width - StockUiConquest.ValueX - 4) / StockUiConquest.ValueScale;
            if (view.InConquestRegion)
            {
                StockUiTextBoxes.DrawWrapped(renderer, font, logFont, view.Control, x, placement.Y + StockUiConquest.ControlY * s,
                    width, (StockUiConquest.TallyY - StockUiConquest.ControlY - 12) / StockUiConquest.ValueScale, ts, White);
                Text(renderer, font, logFont, StockUiConquest.TallyText(view.NextTally), x, placement.Y + StockUiConquest.TallyY * s, ts, White);
                for (int i = 0; i < view.Influence.Count; i++)
                {
                    var bar = view.Influence[i];
                    float y = placement.Y + (StockUiConquest.InfluenceY + i * StockUiConquest.InfluencePitch) * s;
                    Text(renderer, font, logFont, $"{bar.Side} {bar.Percent}%", x, y, ts, White);
                    float barY = y + BarOffsetY * s;
                    renderer.DrawTextureRect(SolidTexture, 1, 1, 1, 1, x, barY, BarWidth * s, BarHeight * s, TrackColor);
                    float fill = Math.Clamp(bar.Percent, 0, 100) / 100f * BarWidth;
                    renderer.DrawTextureRect(SolidTexture, 1, 1, 1, 1, x, barY, fill * s, BarHeight * s, BarColors[i % BarColors.Length]);
                }
            }
            else
            {
                for (int i = 0; i < view.Balance.Count; i++)
                {
                    Text(renderer, font, logFont, view.Balance[i], x, placement.Y + (StockUiConquest.BalanceY + i * StockUiConquest.BalancePitch) * s, ts, White);
                }
            }
            if (library.TryGetMenu(StockUiConquest.PointsWindow, out var points))
            {
                // Conquest Points under the page: conquer1 ends at y 254 and conq3pts starts at 256; under conq6sta it follows at
                // the same gap (PROVISIONAL).
                float top = view.InConquestRegion ? points.Frame.Y : frame.Y + frame.Height + 2;
                var at = StockUiWindowPlacement.Beside(placement, frame, points.Frame.X, top);
                renderer.DrawMenu(points, at, includeButtons: false, opaqueBody: true);
                Text(renderer, font, logFont, view.ConquestPoints.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
                    at.X + StockUiConquest.ValueX * s, at.Y + StockUiConquest.PointsY * s, ts, White);
            }
        }
    }
}
