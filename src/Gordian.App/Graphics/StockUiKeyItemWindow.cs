// src/Gordian.App/Graphics/StockUiKeyItemWindow.cs
using System;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;
using Gordian.Core.World;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws the client content of the Key Items window (#91) and the Equipment Sets windows (#92) over their DAT frames, as
    /// the maintainer's retail captures 2026-10-13 (image-6 ... image-14, UI 1:1) show them (see <see cref="StockUiKeyItems"/>
    /// and <see cref="StockUiEquipSets"/>):
    /// <list type="bullet">
    /// <item>Key Items: per row a bullet (small <c>windowps</c> #71 for categories and read key items, big <c>keytops3</c> #104
    /// for key items not read yet) and the name in the log font 35 px into the row, white, pink under the cursor; the help
    /// bar ("Key Items" and the main menu's help, or the category's name); the "Examine ...?" question with Yes / No; the
    /// description page (bullet and name, then the text wrapped to the window).</item>
    /// <item>Equipment Sets: the set names over <c>mcres20</c>'s capsules, all white, and its page arrows; the set's grid
    /// (beside the options, and while it is edited) with the items' icons and the red X (<c>keytops3</c> #429) on "Remove
    /// piece" slots; while the grid has the cursor, the help bar, the storage's equipment right of it and the slot's item
    /// info under it.</item>
    /// </list>
    /// </summary>
    public static class StockUiKeyItemWindow
    {
        private static readonly UiColor White = new(0x80, 0x80, 0x80, 0x80);

        /// <summary>The row under the cursor (image-6: pink, as the Magic list's).</summary>
        private static readonly UiColor SelectedTint = new(0x80, 0x41, 0x64, 0x80);

        /// <summary>Row text 35 px into the row button; the small bullet's 6 x 6 at 20 px in, 6 down; the big one's 14 x 14 at 16, 2 (image-6, -7, -8).</summary>
        private const float RowTextInset = 35, SmallBulletX = 20, SmallBulletY = 6, BigBulletX = 16, BigBulletY = 2;

        /// <summary>The bullets: <c>windowps</c> #71 and <c>keytops3</c> #104, the <c>colorbal</c> earth ball at 6 and 14 px.</summary>
        private const string SmallBulletGroup = "windowps", BigBulletGroup = "keytops3";
        private const int SmallBulletImage = 71, BigBulletImage = 104;

        /// <summary>The red X over a "Remove piece" slot (<c>keytops3</c> #429: the <c>marker</c> X stretched over 32 x 32; image-13).</summary>
        private const string RemoveGroup = "keytops3";
        private const int RemoveImage = 429;

        /// <summary>"You do not possess any key items." from (20, 20), at the window's left (round 2, image-15).</summary>
        private const float EmptyTextX = 20, EmptyTextY = 20;

        /// <summary>The Examine question at (28, 32) (image-9).</summary>
        private const float QuestionX = 28, QuestionY = 32;
        private const int QuestionMaxLines = 2;

        /// <summary>The description page (image-10): the bullet at (38, 23), the name from (51, 18), the text from (42, 49), 16 px lines.</summary>
        private const float DescBulletX = 38, DescBulletY = 23, DescNameX = 51, DescNameY = 18, DescTextX = 42, DescTextY = 49, DescRightPad = 16;

        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp, StockUiScreen screen)
        {
            if (menu.IsKeyItemWindow) DrawKeyItems(renderer, library, font, logFont, menu, placement, timestamp, screen);
            else if (menu.IsEquipSetList) DrawSetList(renderer, library, font, logFont, menu, placement);
            // The set's grid shows only once Edit is chosen (round 2: not beside the options).
            else if (menu.IsEquipSetGrid) DrawEditedGrid(renderer, library, font, logFont, menu, placement, timestamp, screen);
        }

        private static void Text(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, string text, float x, float y, float s, UiColor color) =>
            StockUiTextBoxes.DrawLine(renderer, font, logFont, text, x, y, s, color);

        private static void Image(StockUiRenderer renderer, UiResourceLibrary library, string group, int index, float x, float y, float s)
        {
            if (library.TryGetGroup(group, out var g) && index < g.Images.Count) renderer.DrawImage(g.Images[index], x, y, s);
        }

        private static void DrawKeyItems(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp, StockUiScreen screen)
        {
            float s = placement.Scale;
            var definition = menu.Menu;
            var frame = definition.Frame;
            if (screen.Width > 0 && screen.Height > 0)
            {
                // "Key Items", then the main menu's help on the categories, the category's name in it (image-7 ... -10).
                string? line = menu.KeyItemCategory?.Name ?? menu.ItemHelpText(StockUiKeyItems.HelpKeyItems);
                StockUiHelpBar.DrawOnScreen(renderer, library, font, logFont, StockUiKeyItems.Title, line, s, (uint)screen.Width, (uint)screen.Height);
            }

            if (menu.IsKeyItemExamine)
            {
                if (menu.KeyItemQuestion is { } question)
                {
                    // A long key item name wraps the question onto a second line (16 px down), still above Yes at y 74 (round 3).
                    var lines = StockUiTextLayout.Wrap(question, StockUiTextBoxes.Measure(font, logFont), frame.Width - QuestionX - DescRightPad, QuestionMaxLines);
                    StockUiTextBoxes.DrawLines(renderer, font, logFont, lines, placement.X + QuestionX * s, placement.Y + QuestionY * s,
                        frame.Width - QuestionX - DescRightPad, QuestionMaxLines * StockUiTextBoxes.LinePitch(font, logFont), s, White);
                }
                foreach (var button in definition.Buttons)
                {
                    string label = button.ButtonId == StockUiKeyItems.YesButton ? "Yes" : button.ButtonId == StockUiKeyItems.NoButton ? "No" : string.Empty;
                    if (label.Length == 0) continue;
                    Text(renderer, font, logFont, label, placement.X + button.X * s, placement.Y + (button.Y + 1) * s, s,
                        button.ButtonId == menu.SelectedButtonId ? SelectedTint : White);
                }
                return;
            }

            if (menu.IsKeyItemDescription && menu.KeyItemShown is { } shown)
            {
                Image(renderer, library, SmallBulletGroup, SmallBulletImage, placement.X + DescBulletX * s, placement.Y + DescBulletY * s, s);
                Text(renderer, font, logFont, shown.Name, placement.X + DescNameX * s, placement.Y + DescNameY * s, s, White);
                StockUiTextBoxes.DrawWrapped(renderer, font, logFont, menu.KeyItemDescription, placement.X + DescTextX * s, placement.Y + DescTextY * s,
                    frame.Width - DescTextX - DescRightPad, frame.Height - DescTextY - DescRightPad, s, White, keepBlankLines: true);
                return;
            }

            if (menu.Rows.Count == 0)
            {
                if (menu.EmptyText is { } empty)
                {
                    Text(renderer, font, logFont, empty, placement.X + EmptyTextX * s, placement.Y + EmptyTextY * s, s, White);
                }
                return;
            }
            if (definition.FindButton(1) is not { } firstRow) return;
            float pitch = StockUiKeyItems.RowPitch;
            float first = StockUiMenuWindow.ScrolledFirstRow(menu, timestamp);
            float areaY = placement.Y + (firstRow.Y - 4) * s;
            renderer.SetClip(placement.X, areaY, frame.Width * s, menu.VisibleRows * pitch * s);
            int selected = menu.EntryIndex(menu.SelectedButtonId);
            int from = Math.Max(0, (int)Math.Floor(first) - 1), to = Math.Min(menu.Rows.Count - 1, (int)Math.Ceiling(first) + menu.VisibleRows);
            float bx = placement.X + firstRow.X * s;
            for (int i = from; i <= to; i++)
            {
                var row = menu.Rows[i];
                float by = placement.Y + (firstRow.Y + (i - first) * pitch) * s;
                if (menu.IsKeyItemList && row.Marked) Image(renderer, library, BigBulletGroup, BigBulletImage, bx + BigBulletX * s, by + BigBulletY * s, s);
                else Image(renderer, library, SmallBulletGroup, SmallBulletImage, bx + SmallBulletX * s, by + SmallBulletY * s, s);
                Text(renderer, font, logFont, row.Text, bx + RowTextInset * s, by + 1 * s, s, i == selected ? SelectedTint : White);
            }
            renderer.ClearClip();
            if (menu.CanScroll) StockUiMenuWindow.DrawScrollbar(renderer, placement, frame, first, menu.Rows.Count, menu.VisibleRows);
        }

        /// <summary>The page's set names over the capsules (all white, image-11), and the page arrows.</summary>
        private static void DrawSetList(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu, StockUiPlacement placement)
        {
            float s = placement.Scale;
            var definition = menu.Menu;
            var book = menu.EquipSetBook;
            if (book == null) return;
            float ns = s * StockUiEquipSets.NameScale;
            for (int b = 1; b <= menu.VisibleRows; b++)
            {
                int index = menu.FirstRow + b - 1;
                if (index >= EquipmentSetBook.SetCount || definition.FindButton(b) is not { } button) continue;
                var set = book.Get(index + 1);
                float x = placement.X + (button.X + StockUiEquipSets.NameInset) * s;
                bool editing = menu.EquipSetNameEdit is { } edit && edit.Number == index + 1;
                // A name is cut where the capsule's text area ends (image-16: "BehavRese"); the name being typed is drawn at the
                // log font's full size and scrolls left so its end and the caret stay in view (image-16's bottom capsule).
                float clipWidth = (editing ? StockUiEquipSets.EditFieldWidth : StockUiEquipSets.NameFieldWidth) * s;
                renderer.SetClip(x, placement.Y + button.Y * s, clipWidth, button.Height * s);
                if (editing && menu.EquipSetNameEdit is { } typing)
                {
                    float es = s * StockUiEquipSets.EditScale;
                    float ty = placement.Y + button.Y * s + (button.Height * s - StockUiLogFont.CellHeight * es) * 0.5f;
                    // The shared text field draws the text scrolled to keep the caret in view, and the blinking caret (#400).
                    StockUiTextFieldView.Draw(renderer, font, logFont, typing.Field, x, ty, es, System.Diagnostics.Stopwatch.GetTimestamp(),
                        StockUiEquipSets.EditFieldWidth - StockUiEquipSets.CaretWidth);
                }
                else if (logFont != null)
                {
                    logFont.Draw(renderer, set.Name, x, placement.Y + button.Y * s + (button.Height * s - StockUiLogFont.CellHeight * ns) * 0.5f, ns, White);
                }
                else
                {
                    renderer.DrawText(font, set.Name, x, placement.Y + button.Y * s + (button.Height * s - font.LineHeight * ns) * 0.5f, ns, White);
                }
                renderer.ClearClip();
            }
            // The DAT's page arrows (buttons 21 / 22, outside the frame): the generic drawing shows them only for the main menu's
            // pages, so they are drawn here; the pages wrap, so both always show.
            foreach (int id in new[] { StockUiEquipSets.PreviousPageButton, StockUiEquipSets.NextPageButton })
            {
                if (definition.FindButton(id) is not { } arrow) continue;
                foreach (var shape in arrow.Shapes)
                {
                    if (shape.Kind != 0 || !library.TryGetImage(shape, out var image)) continue;
                    renderer.DrawImage(image, placement.X + arrow.X * s, placement.Y + arrow.Y * s, s);
                    break;
                }
            }
        }

        /// <summary>The set's items as 32 x 32 icons over the grid's slots; "Remove piece" slots under the red X.</summary>
        private static void DrawSetGrid(StockUiRenderer renderer, UiResourceLibrary library, StockUiOpenMenu menu, UiMenuDefinition grid, StockUiPlacement at)
        {
            StockUiItemGrid.Draw(renderer, menu.ItemLookup, grid, at, menu.EquipSetItemIn, (button, item, x, y) =>
            {
                if (item == 0 && menu.IsEquipSetRemoved(button.ButtonId)) Image(renderer, library, RemoveGroup, RemoveImage, x, y, at.Scale);
            });
        }

        /// <summary>
        /// The grid while a set is edited (image-12, -13): the items and red X marks; while the grid has the cursor, the help bar
        /// ("Equipment Sets" and the slot's help), the storage's equipment right of the grid (x 198) and the slot's item info
        /// under it (16,240; the empty info window for an empty slot).
        /// </summary>
        private static void DrawEditedGrid(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp, StockUiScreen screen)
        {
            float s = placement.Scale;
            var frame = menu.Menu.Frame;
            DrawSetGrid(renderer, library, menu, menu.Menu, placement);
            if (menu.IsEquipSetListOpen) return;
            if (menu.ItemHelpBar is { } bar && screen.Width > 0 && screen.Height > 0)
            {
                string? text = bar.HelpTextId >= 0 ? menu.ItemHelpText(bar.HelpTextId) : null;
                StockUiHelpBar.DrawOnScreen(renderer, library, font, logFont, bar.Title, text, s, (uint)screen.Width, (uint)screen.Height);
            }
            if (library.TryGetMenu(StockUiItems.ListMenu, out var inventor))
            {
                var at = StockUiWindowPlacement.Beside(placement, frame, StockUiEquipSets.SlotListX, inventor.Frame.Y);
                StockUiItemWindow.DrawCompanion(renderer, library, font, menu, inventor, at, menu.CompanionRows);
            }
            // The slot's item under the grid at the info window's authored x; the empty window for an empty slot.
            if (StockUiItemInfoWindow.FrameOf(library, menu.ShownDescriptionPage) is { } info)
            {
                StockUiItemInfoWindow.Draw(renderer, library, font, logFont, menu,
                    StockUiWindowPlacement.Beside(placement, frame, info.Frame.X, StockUiItemInfoWindow.DefaultTop), timestamp, emptyWhenNone: true);
            }
        }
    }
}
