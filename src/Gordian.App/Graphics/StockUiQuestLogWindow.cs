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
    /// <item>The help bar: "Missions", "Quests" or "Region Info" and the page title or region name.</item>
    /// <item>The paging window (<c>miss00</c>, <c>quest01</c>; retail image-11): the page title centred between the DAT's
    /// red arrows; "Current:" and "Completed:" at the window's left; each quest or mission behind a bullet on its 320 x 18
    /// row, white, pink under the cursor, the mission in progress pale yellow when the cursor is elsewhere; a note (another
    /// nation's "Not allowed under current allegiance.", an empty quest page's text) where an entry's name goes.</item>
    /// <item><c>quest00</c>'s Current and Completed behind bullets (image-12).</item>
    /// <item>The description page (the name, then the text wrapped to the window).</item>
    /// <item>The Conquest map's windows: the values under <c>conquer1</c>'s baked-in headings (Regional Control, Next tally
    /// in, Regional Influence as four bars), or <c>conq6sta</c>'s Balance of Power; Conquest Points are drawn by the HUD in
    /// the target window's place (<see cref="DrawConquestPoints"/>).</item>
    /// </list>
    /// PROVISIONAL: the text places, the bullet, the colours and the bars are our reading of the round 1 screenshots.
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

        /// <summary>
        /// The paging window (image-11, 362 x 244 of the 366 x 248 window): the title's centre line at y 15, the headings at
        /// x 22, an entry's bullet 5 px into its row and its name 16 px in.
        /// </summary>
        private const float TitleY = 15, HeadingX = 22, BulletInset = 5, EntryTextInset = 16;

        /// <summary>
        /// The bullet: the olive ball of the menu DAT's <c>colorbal</c> texture (the 14 x 14 cell at 16,16; the state balls'
        /// sheet), 9 px across, as retail's rows show it (image-11, image-12).
        /// </summary>
        private const string BulletTexture = "colorbal";
        private const int BulletCellX = 16, BulletCellY = 16, BulletCell = 14;
        private const float BulletSize = 9;

        /// <summary>The description page: the name at (20, 12), the text from (20, 36).</summary>
        private const float DescNameX = 20, DescNameY = 12, DescTextX = 20, DescTextY = 36, DescRightPad = 16;

        /// <summary>How far (layout px) the tally's right end sits inside the window's right edge (image-55).</summary>
        private const float TallyRightPad = 8;

        /// <summary>The influence bars: 88 px wide, 4 px tall, under each side's name and share.</summary>
        private const float BarWidth = 88, BarHeight = 4, BarOffsetY = 15;

        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp, StockUiScreen screen)
        {
            float s = placement.Scale;
            if (screen.Width > 0 && screen.Height > 0)
            {
                string? line = menu.QuestLogHeading ?? menu.ItemHelpText(menu.IsQuestsWindow ? StockUiQuestLog.HelpQuests : StockUiQuestLog.HelpMissions);
                StockUiHelpBar.DrawOnScreen(renderer, library, font, logFont, menu.QuestLogTitle, line, s, (uint)screen.Width, (uint)screen.Height);
            }
            switch (menu.QuestLogView)
            {
                case StockUiQuestLogView.Description:
                    DrawDescription(renderer, font, logFont, menu, placement);
                    break;
                case StockUiQuestLogView.QuestKind:
                    DrawQuestKind(renderer, library, font, logFont, menu, placement);
                    break;
                case StockUiQuestLogView.Pages:
                    DrawPage(renderer, library, font, logFont, menu, placement, timestamp);
                    break;
            }
        }

        private static void Text(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, string text, float x, float y, float s, UiColor color) =>
            StockUiTextBoxes.DrawLine(renderer, font, logFont, text, x, y, s, color);

        /// <summary>The bullet, <see cref="BulletInset"/> px into a row and centred on its height.</summary>
        private static void Bullet(StockUiRenderer renderer, float rowX, float rowY, float rowHeight, float s)
        {
            float size = BulletSize * s;
            renderer.DrawTextureRect(BulletTexture, BulletCellX, BulletCellY, BulletCell, BulletCell, rowX + BulletInset * s, rowY + (rowHeight * s - size) * 0.5f,
                size, size, White);
        }

        /// <summary>A row's text, centred on its height.</summary>
        private static float RowTextY(UiFont font, StockUiLogFont? logFont, float rowY, float rowHeight, float s) =>
            rowY + Math.Max(0, (rowHeight - StockUiTextBoxes.LinePitch(font, logFont)) * 0.5f) * s;

        private static void DrawQuestKind(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement)
        {
            float s = placement.Scale;
            foreach (var row in menu.Rows)
            {
                if (menu.Menu.FindButton(row.ButtonId) is not { } button) continue;
                float bx = placement.X + button.X * s, by = placement.Y + button.Y * s;
                Bullet(renderer, bx, by, button.Height, s);
                Text(renderer, font, logFont, row.Text, bx + EntryTextInset * s, RowTextY(font, logFont, by, button.Height, s), s,
                    row.ButtonId == menu.SelectedButtonId ? SelectedTint : White);
            }
        }

        private static void DrawPage(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp)
        {
            float s = placement.Scale;
            var definition = menu.Menu;
            var frame = definition.Frame;
            if (menu.QuestLogHeading is { Length: > 0 } title)
            {
                float width = StockUiTextBoxes.MeasureLine(font, logFont, title) * s;
                float ty = placement.Y + TitleY * s - StockUiTextBoxes.LinePitch(font, logFont) * 0.5f * s;
                Text(renderer, font, logFont, title, placement.X + (frame.Width * s - width) * 0.5f, ty, s, White);
            }
            if (definition.FindButton(1) is not { } firstRow) return;
            float pitch = definition.FindButton(2) is { } second ? second.Y - firstRow.Y : firstRow.Height;
            float first = StockUiMenuWindow.ScrolledFirstRow(menu, timestamp);
            float areaY = placement.Y + firstRow.Y * s;
            renderer.SetClip(placement.X, areaY, frame.Width * s, menu.VisibleRows * pitch * s);
            int selected = menu.EntryIndex(menu.SelectedButtonId);
            var lines = menu.QuestLogLines;
            int from = Math.Max(0, (int)Math.Floor(first) - 1), to = Math.Min(lines.Count - 1, (int)Math.Ceiling(first) + menu.VisibleRows);
            float bx = placement.X + firstRow.X * s;
            for (int i = from; i <= to; i++)
            {
                var line = lines[i];
                float by = placement.Y + (firstRow.Y + (i - first) * pitch) * s;
                float ty = RowTextY(font, logFont, by, firstRow.Height, s);
                switch (line.Kind)
                {
                    case StockUiQuestLogLineKind.Heading:
                        Text(renderer, font, logFont, line.Text, placement.X + HeadingX * s, ty, s, White);
                        break;
                    case StockUiQuestLogLineKind.Note:
                        Text(renderer, font, logFont, line.Text, bx + EntryTextInset * s, ty, s, White);
                        break;
                    default:
                        Bullet(renderer, bx, by, firstRow.Height, s);
                        var color = i == selected ? SelectedTint : !menu.IsQuestsWindow && line.Row?.Status == StockUiQuestLogStatus.Current ? CurrentTint : White;
                        Text(renderer, font, logFont, line.Text, bx + EntryTextInset * s, ty, s, color);
                        break;
                }
            }
            renderer.ClearClip();
            if (menu.CanScroll) StockUiMenuWindow.DrawScrollbar(renderer, placement, frame, first, lines.Count, menu.VisibleRows);
        }

        private static void DrawDescription(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu, StockUiPlacement placement)
        {
            if (menu.QuestLogShown is not { } shown) return;
            float s = placement.Scale;
            var frame = menu.Menu.Frame;
            Text(renderer, font, logFont, shown.Entry.Name, placement.X + DescNameX * s, placement.Y + DescNameY * s, s,
                !menu.IsQuestsWindow && shown.Status == StockUiQuestLogStatus.Current ? CurrentTint : White);
            StockUiTextBoxes.DrawWrapped(renderer, font, logFont, shown.Entry.Description, placement.X + DescTextX * s, placement.Y + DescTextY * s,
                frame.Width - DescTextX - DescRightPad, frame.Height - DescTextY - DescRightPad, s, White, keepBlankLines: true);
        }

        /// <summary>
        /// The conquest map's page (#488): the help bar ("Conquest" and the region's control text, retail image-49), and the
        /// window for the region under the map's cursor, <c>conquer1</c>, or <c>conq6sta</c>'s Balance of Power between
        /// regions and on a city (image-51), at its DAT place top right. The region window moves to the top left while the
        /// cursor is in the screen's right half (round 3: image-57, image-61, image-64, image-67, image-68, image-71 and
        /// image-73 show it there, the others on the right); the Balance of Power was seen on the right only.
        /// </summary>
        public static void DrawConquestPage(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, StockUiScreen screen)
        {
            if (menu.ConquestView is not { } view) return;
            float s = placement.Scale;
            if (screen.Width > 0 && screen.Height > 0)
            {
                StockUiHelpBar.DrawOnScreen(renderer, library, font, logFont, menu.QuestLogTitle, view.InConquestRegion ? view.Control : null, s,
                    (uint)screen.Width, (uint)screen.Height);
            }
            if (!library.TryGetMenu(view.InConquestRegion ? StockUiConquest.ControlWindow : StockUiConquest.BalanceWindow, out var window)) return;
            var frame = window.Frame;
            var at = placement with
            {
                X = placement.X + (frame.X - menu.Menu.Frame.X) * s,
                Y = placement.Y + (frame.Y - menu.Menu.Frame.Y) * s,
            };
            if (view.InConquestRegion && screen.Width > 0 && screen.Height > 0)
            {
                var (mx, _, mw, _) = StockUiMapWindow.Placement(menu, (int)StockUiConquest.MapSize, (int)StockUiConquest.MapSize, (uint)screen.Width, (uint)screen.Height);
                if (mx + menu.MapCursorX * mw > screen.Width * 0.5f) at = at with { X = screen.Width - at.X - frame.Width * s };
            }
            renderer.DrawMenu(window, at, includeButtons: false, opaqueBody: true);
            DrawConquest(renderer, font, logFont, frame, view, at);
        }

        private static void DrawConquest(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, UiMenuFrame frame,
            StockUiConquestView view, StockUiPlacement placement)
        {
            float s = placement.Scale;
            // The frames are 112 px wide: the values are drawn smaller than the log font so a nation's name and share fit.
            float ts = s * StockUiConquest.ValueScale;
            float x = placement.X + StockUiConquest.ValueX * s;
            float width = (frame.Width - StockUiConquest.ValueX - 4) / StockUiConquest.ValueScale;
            if (view.InConquestRegion)
            {
                // The owner in a word and the tally right-aligned under their headings (retail image-55); the help bar has
                // the control sentence.
                StockUiTextBoxes.DrawWrapped(renderer, font, logFont, view.Owner, x, placement.Y + StockUiConquest.ControlY * s,
                    width, (StockUiConquest.TallyY - StockUiConquest.ControlY - 12) / StockUiConquest.ValueScale, ts, White);
                string tally = StockUiConquest.TallyText(view.NextTally);
                float tallyX = logFont != null ? placement.X + (frame.Width - TallyRightPad) * s - logFont.MeasureWidth(tally) * ts : x;
                Text(renderer, font, logFont, tally, tallyX, placement.Y + StockUiConquest.TallyY * s, ts, White);
                // Only the character's own region has shares (S2C 0x05E); retail writes the others' levels as words.
                for (int i = 0; view.HasInfluence && i < view.Influence.Count; i++)
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
        }

        /// <summary>
        /// <c>conq3pts</c>'s Conquest Points in the target window's place (round 1 of #488: retail's region maps put their own
        /// box there): right- and bottom-aligned with <paramref name="target"/>'s frame (<paramref name="targetWidth"/> x
        /// <paramref name="targetHeight"/> layout px).
        /// </summary>
        public static void DrawConquestPoints(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiConquestView view,
            StockUiPlacement target, float targetWidth, float targetHeight)
        {
            if (!library.TryGetMenu(StockUiConquest.PointsWindow, out var points)) return;
            float s = target.Scale;
            var frame = points.Frame;
            var at = target with { X = target.X + (targetWidth - frame.Width) * s, Y = target.Y + (targetHeight - frame.Height) * s };
            renderer.DrawMenu(points, at, includeButtons: false, opaqueBody: true);
            Text(renderer, font, logFont, view.ConquestPoints.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
                at.X + StockUiConquest.ValueX * s, at.Y + StockUiConquest.PointsY * s, s * StockUiConquest.ValueScale, White);
        }
    }
}
