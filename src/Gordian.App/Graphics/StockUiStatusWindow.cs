// src/Gordian.App/Graphics/StockUiStatusWindow.cs
using System.Collections.Generic;
using System.Globalization;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws the Status window (#23, <see cref="StockUiStatus"/>) as the maintainer's retail capture
    /// (<c>Main_Menu_Page1.mp4</c>, 0:00-0:20, 2550 x 1434, UI 1:1) lays it out:
    /// <list type="bullet">
    /// <item>the stats side <c>statuswi</c> (the Equipment window's stats layout) and the level box <c>level</c> under it:
    /// "Level 99" in the main job's blue, "EXP" and "NEXT Level" grey with the numbers under them right-aligned (comma
    /// separated, grey), and the EXP gauge filled pink;</item>
    /// <item>the help bar without its title box: the list entry's help line ("View all job levels achieved so far."), and
    /// "Title: New Adventurer" while Profile is open;</item>
    /// <item>the pages in <c>fontshp</c> at 7/8: Job Levels (each job's name and level in two columns, the main job yellow, the row
    /// under the cursor light blue), Combat / Magic Skills (two columns, the levels blue when capped), Craft Skills (one
    /// column: craft, rank and level) and Profile (nation, race, home point, rank and mastery rank after the baked labels,
    /// the Rank Points strip).</item>
    /// </list>
    /// </summary>
    public static class StockUiStatusWindow
    {
        private static readonly UiColor White = new(0x80, 0x80, 0x80, 0x80);

        /// <summary>"Level 99" and the main job (76, 162, 208 in the capture, against white text at 220).</summary>
        private static readonly UiColor LevelTint = new(0x2C, 0x5E, 0x79, 0x80);

        /// <summary>"EXP", "NEXT Level" and their numbers (108, 105, 113).</summary>
        private static readonly UiColor GreyTint = new(0x3F, 0x3D, 0x42, 0x80);

        /// <summary>A capped skill's level (71-98, 147-170, 201-215: the capture shows every listed level in this blue).</summary>
        private static readonly UiColor CappedTint = new(0x2C, 0x58, 0x77, 0x80);

        /// <summary>The main job's row in Job Levels (210, 208, 136) and the row under the cursor (134, 185, 216).</summary>
        private static readonly UiColor MainJobRowTint = new(0x7A, 0x79, 0x4F, 0x80), SelectedRowTint = new(0x4E, 0x6B, 0x7D, 0x80);

        /// <summary>The EXP gauge's pink (250, 147, 161 over the gauge texture).</summary>
        private static readonly UiColor ExpGaugeColor = new(0x9C, 0x5C, 0x64, 0x80);

        // The level box (112 x 88): text tops and the gauge, measured from the capture against the box's top-left.
        private const float LevelX = 9, LevelY = 6, LabelX = 16, ExpLabelY = 22, ExpValueY = 36, NextLabelY = 47, NextValueY = 57, ValueRight = 89;
        private const float GaugeX = 22, GaugeY = 74, GaugeMiddle = 64;

        // The pages: rows of 14 px from y 19 (joblevel's buttons), two columns at x 8 and 134; text 4 px in, levels right-aligned.
        private const float RowTop = 19, RowPitch = 14, LeftColumn = 8, RightColumn = 134, TextInset = 4, JobLevelRight = 101, SkillLevelRight = 114;
        private const float CraftRankX = 112, CraftLevelRight = 240;

        // Profile: value x after each baked label, and the label rows' centres (from the DAT frame image).
        private const float NationX = 74, RaceX = 52, HomePointX = 96, RankX = 60, MasteryX = 114;
        private const float NationRow = 26, RaceRow = 42, HomePointRow = 58, RankRow = 74, MasteryRow = 112, RankGaugeX = 92, RankGaugeY = 86;

        /// <summary>The stats side, the level box and the help bar, at their authored places (top left), while the Status window is up.</summary>
        public static void DrawPanels(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont,
            StockUiMenuController menus, float scale, uint width, uint height)
        {
            var info = menus.StatusInfo?.Invoke();
            if (library.TryGetMenu(StockUiStatus.WindowMenu, out var window))
            {
                var f = window.Frame;
                StockUiItemWindow.DrawStatsSide(renderer, font, window, StockUiLayout.Place(f.Anchor, f.X, f.Y, f.Width, f.Height, scale, width, height), info?.Stats);
            }
            if (library.TryGetMenu(StockUiStatus.LevelMenu, out var level))
            {
                var f = level.Frame;
                var at = StockUiLayout.Place(f.Anchor, f.X, f.Y, f.Width, f.Height, scale, width, height);
                renderer.DrawMenu(level, at, includeButtons: false, opaqueBody: true);
                if (info != null) DrawLevel(renderer, font, info, at);
            }

            var open = menus.OpenMenus;
            string? help = null;
            if (open.Count > 0 && open[^1].StatusPage == StockUiStatusPage.Profile)
            {
                help = info is { Title.Length: > 0 } ? "Title: " + info.Title : null;
            }
            else
            {
                for (int i = open.Count - 1; i >= 0; i--)
                {
                    if (!open[i].IsStatusList) continue;
                    if (open[i].SelectedButton is { HelpTextId: >= 0 } button) help = menus.MenuHelpText?.Invoke(button.HelpTextId);
                    break;
                }
            }
            StockUiHelpBar.DrawOnScreen(renderer, library, font, logFont, null, help, scale, width, height);
        }

        private static void DrawLevel(StockUiRenderer renderer, UiFont font, StockUiStatusInfo info, StockUiPlacement at)
        {
            float s = at.Scale, ts = s * StockUiPartyWindow.TextScale;
            var inv = CultureInfo.InvariantCulture;
            renderer.DrawText(font, string.Create(inv, $"Level {info.Level}"), at.X + LevelX * s, at.Y + LevelY * s, ts, LevelTint);
            renderer.DrawText(font, "EXP", at.X + LabelX * s, at.Y + ExpLabelY * s, ts, GreyTint);
            renderer.DrawText(font, "NEXT Level", at.X + LabelX * s, at.Y + NextLabelY * s, ts, GreyTint);
            void Right(string text, float y) => renderer.DrawText(font, text, at.X + ValueRight * s - font.MeasureWidth(text) * ts, at.Y + y * s, ts, GreyTint);
            Right(StockUiStatus.Number(info.ExpNow), ExpValueY);
            Right(StockUiStatus.Number(info.ExpNext), NextValueY);
            int percent = info.ExpNext > 0 ? (int)((long)info.ExpNow * 100 / info.ExpNext) : 0;
            StockUiPartyWindow.DrawGauge(renderer, at.X + GaugeX * s, at.Y + GaugeY * s, GaugeMiddle, withKnob: false, ExpGaugeColor, percent, s);
        }

        /// <summary>A Status page's content over its DAT window (drawn by <see cref="StockUiMenuWindow"/>).</summary>
        public static void DrawPage(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement)
        {
            if (menu.StatusInfoNow is not { } info) return;
            switch (menu.StatusPage)
            {
                case StockUiStatusPage.Profile:
                    DrawProfile(renderer, library, font, logFont, info, placement);
                    break;
                case StockUiStatusPage.JobLevels:
                    DrawJobLevels(renderer, font, logFont, menu, info, placement);
                    break;
                case StockUiStatusPage.CombatSkills:
                    DrawSkills(renderer, font, logFont, info.CombatSkills, placement);
                    break;
                case StockUiStatusPage.MagicSkills:
                    DrawSkills(renderer, font, logFont, info.MagicSkills, placement);
                    break;
                case StockUiStatusPage.CraftSkills:
                    DrawCrafts(renderer, font, logFont, info.CraftSkills, placement);
                    break;
            }
        }

        /// <summary>Page text in <c>fontshp</c> at 7/8, as the stats side (the capture's "Beastmaster" is 68 px wide; the log font's would be ~90).</summary>
        private static void Text(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, string text, float x, float y, float s, UiColor color) =>
            renderer.DrawText(font, text, x, y, s * StockUiPartyWindow.TextScale, color);

        private static void RightText(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, string text, float right, float y, float s, UiColor color) =>
            Text(renderer, font, logFont, text, right - font.MeasureWidth(text) * s * StockUiPartyWindow.TextScale, y, s, color);

        /// <summary>The room a row keeps for its level: three digits and a gap.</summary>
        private const float LevelRoom = 26;

        /// <summary>A row's name, squeezed into <paramref name="room"/> when longer ("Puppetmaster" before its 99).</summary>
        private static void Name(StockUiRenderer renderer, UiFont font, string text, float x, float y, float room, float s, UiColor color)
        {
            float ts = s * StockUiPartyWindow.TextScale;
            StockUiTextBoxes.DrawFitted(renderer, font, text, x, y, ts, room, font.LineHeight * ts, color);
        }

        /// <summary>The top of a page row's text: centred on the 14 px row.</summary>
        private static float RowTextY(StockUiPlacement p, int row) => p.Y + (RowTop + row * RowPitch + RowPitch * 0.5f - TextHalfHeight) * p.Scale;

        /// <summary>The top of a line centred on a baked label's row centre (<c>fontshp</c> glyphs are drawn from 6 px above their centre, as the stats side's lines).</summary>
        private static float CentredY(StockUiPlacement p, float centre) => p.Y + (centre - TextHalfHeight) * p.Scale;

        private const float TextHalfHeight = 6;

        private static void DrawJobLevels(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu, StockUiStatusInfo info,
            StockUiPlacement p)
        {
            float s = p.Scale;
            var inv = CultureInfo.InvariantCulture;
            for (int button = 1; button <= StockUiStatus.JobRowsPerColumn * 2; button++)
            {
                byte job = StockUiStatus.JobOfRow(button);
                bool right = button > StockUiStatus.JobRowsPerColumn;
                int row = right ? button - StockUiStatus.JobRowsPerColumn - 1 : button - 1;
                float column = right ? RightColumn : LeftColumn;
                var tint = button == menu.SelectedButtonId ? SelectedRowTint : job == info.MainJob ? MainJobRowTint : White;
                float y = RowTextY(p, row);
                Name(renderer, font, StockUiCheck.JobName(job), p.X + (column + TextInset) * s, y, (JobLevelRight - TextInset - LevelRoom + 8) * s, s, tint);
                int level = job < info.JobLevels.Count ? info.JobLevels[job] : 0;
                RightText(renderer, font, logFont, level.ToString(inv), p.X + (column + JobLevelRight) * s, y, s, tint);
            }
        }

        /// <summary>Two columns filled across, left then right (the capture's Hand-to-Hand, Dagger / Sword, Great Sword ...).</summary>
        private static void DrawSkills(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, IReadOnlyList<StockUiSkillLine> lines, StockUiPlacement p)
        {
            float s = p.Scale;
            var inv = CultureInfo.InvariantCulture;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                float column = i % 2 == 0 ? LeftColumn : RightColumn;
                float y = RowTextY(p, i / 2);
                Name(renderer, font, line.Name, p.X + (column + TextInset) * s, y, (SkillLevelRight - TextInset - LevelRoom) * s, s, White);
                RightText(renderer, font, logFont, line.Level.ToString(inv), p.X + (column + SkillLevelRight) * s, y, s, line.Capped ? CappedTint : White);
            }
        }

        private static void DrawCrafts(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, IReadOnlyList<StockUiSkillLine> lines, StockUiPlacement p)
        {
            float s = p.Scale;
            var inv = CultureInfo.InvariantCulture;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                float y = p.Y + (RowTop + i * (RowPitch - 1) + (RowPitch - 1) * 0.5f - TextHalfHeight) * s;
                Text(renderer, font, logFont, line.Name, p.X + (LeftColumn + TextInset) * s, y, s, White);
                if (line.Rank is { } rank) Text(renderer, font, logFont, rank, p.X + CraftRankX * s, y, s, White);
                RightText(renderer, font, logFont, line.Level.ToString(inv), p.X + CraftLevelRight * s, y, s, line.Capped ? CappedTint : White);
            }
        }

        private static void DrawProfile(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiStatusInfo info,
            StockUiPlacement p)
        {
            float s = p.Scale;
            var inv = CultureInfo.InvariantCulture;
            Text(renderer, font, logFont, info.Nation, p.X + NationX * s, CentredY(p, NationRow), s, White);
            Text(renderer, font, logFont, info.Race, p.X + RaceX * s, CentredY(p, RaceRow), s, White);
            Text(renderer, font, logFont, info.HomePoint, p.X + HomePointX * s, CentredY(p, HomePointRow), s, White);
            Text(renderer, font, logFont, info.Rank.ToString(inv), p.X + RankX * s, CentredY(p, RankRow), s, White);
            Text(renderer, font, logFont, info.MasteryRank.ToString(inv), p.X + MasteryX * s, CentredY(p, MasteryRow), s, White);
            if (library.TryGetGroup(StockUiStatus.CraftFrameGroup, out var frames) && StockUiStatus.RankGaugeImage < frames.Images.Count)
            {
                renderer.DrawImage(frames.Images[StockUiStatus.RankGaugeImage], p.X + RankGaugeX * s, p.Y + RankGaugeY * s, s);
            }
        }
    }
}
