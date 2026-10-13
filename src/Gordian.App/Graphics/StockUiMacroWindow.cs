// src/Gordian.App/Graphics/StockUiMacroWindow.cs
using System;
using System.Diagnostics;
using Gordian.Core.Macros;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws the macro windows' client content (#339) over their DAT frames (<see cref="StockUiMacroMenus"/>): the book
    /// list's book names, the palettes' macro titles on their keys, the macro editor's key label, title and lines, and the
    /// field being typed in with its caret. Also the palette shown in play while Ctrl / Alt (LT / RT) is held.
    /// Layout is ours where no capture shows it (PROVISIONAL): titles in <c>fontshp</c> at 7/8 from a key's top left, lines in
    /// the log font 6 px into their boxes, the editor's key label centred in its box (BG-Wiki's editor screenshot).
    /// </summary>
    public static class StockUiMacroWindow
    {
        private static readonly UiColor White = new(0x80, 0x80, 0x80, 0x80);

        /// <summary>The macro title on a palette key: 4 px in from the key's top left.</summary>
        private const float KeyTitleX = 4, KeyTitleY = 3;

        /// <summary>Text inside the editor's boxes and the book rows.</summary>
        private const float FieldTextX = 6, BookTextX = 10;

        /// <summary>The caret blinks every half second, as the chat input line's.</summary>
        private const double CaretBlinkSeconds = 0.5;

        /// <summary>Draws a macro window's content (the frame and the DAT labels are drawn already).</summary>
        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp)
        {
            var macros = menu.MacroLibrary;
            if (macros == null) return;
            switch (menu.MacroView)
            {
                case StockUiMacroView.BookList:
                    DrawBookList(renderer, library, font, menu, macros, placement, timestamp);
                    break;
                case StockUiMacroView.PaletteEditor:
                    DrawKeyTitles(renderer, font, menu.Menu, placement, macros.GetSet(menu.MacroBook, menu.MacroSet));
                    break;
                case StockUiMacroView.Editor:
                    DrawEditor(renderer, font, logFont, menu, macros, placement, timestamp);
                    break;
            }
        }

        private static void DrawBookList(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiOpenMenu menu, MacroLibrary macros,
            StockUiPlacement placement, long timestamp)
        {
            float s = placement.Scale;
            var definition = menu.Menu;
            for (int row = 1; row <= StockUiMacroMenus.BooksPerPage; row++)
            {
                if (definition.FindButton(row) is not { } button) continue;
                int book = menu.MacroBookPage * StockUiMacroMenus.BooksPerPage + row - 1;
                float x = placement.X + (button.X + BookTextX) * s;
                float y = placement.Y + button.Y * s + (button.Height - font.LineHeight) * 0.5f * s;
                if (menu.MacroFieldButton == row && menu.MacroField is { IsOpen: true } field)
                {
                    DrawField(renderer, font, null, field, x, y, s, timestamp);
                    continue;
                }
                UiColor? tint = row == menu.SelectedButtonId ? StockUiMenuWindow.SelectedGlyphTint : null;
                renderer.DrawText(font, macros.GetBookTitle(book), x, y, s, tint);
            }
            // The page arrows beside the list (buttons 21 / 22, outside the frame, which the menu drawing leaves out).
            foreach (int id in new[] { StockUiMacroMenus.BookPrevPage, StockUiMacroMenus.BookNextPage })
            {
                if (definition.FindButton(id) is not { } arrow) continue;
                foreach (var shape in arrow.Shapes)
                {
                    if (shape.Kind == 0 && library.TryGetImage(shape, out var image)) renderer.DrawImage(image, placement.X + arrow.X * s, placement.Y + arrow.Y * s, s);
                }
            }
        }

        /// <summary>The macro titles on a palette's keys (buttons 1-10 Ctrl, 11-20 Alt of a set's 20 macros).</summary>
        private static void DrawKeyTitles(StockUiRenderer renderer, UiFont font, UiMenuDefinition definition, StockUiPlacement placement, Macro[] set,
            int firstButton = 1, int count = MacroLibrary.MacrosPerSet)
        {
            float s = placement.Scale;
            float ts = s * StockUiPartyWindow.TextScale;
            for (int i = 0; i < count && i < set.Length; i++)
            {
                if (set[i].Title.Length == 0 || definition.FindButton(firstButton + i) is not { } button) continue;
                float x = placement.X + (button.X + KeyTitleX) * s, y = placement.Y + (button.Y + KeyTitleY) * s;
                renderer.SetClip(placement.X + button.X * s, placement.Y + button.Y * s, button.Width * s, button.Height * s);
                renderer.DrawText(font, set[i].Title, x, y, ts);
                renderer.ClearClip();
            }
        }

        private static void DrawEditor(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu, MacroLibrary macros,
            StockUiPlacement placement, long timestamp)
        {
            float s = placement.Scale;
            var slot = menu.MacroSlot;
            var macro = macros.Get(slot);
            // ("Edit Macros" above the window is the help bar's title, drawn by the HUD, as the BG-Wiki screenshot shows.)
            // The key ("Ctrl 1") centred in its box.
            string key = (slot.Palette == MacroPalette.Alt ? "Alt " : "Ctrl ") + ((slot.Key + 1) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var keyBox = StockUiMacroMenus.KeyBox;
            renderer.DrawText(font, key, placement.X + (keyBox.X + (keyBox.Width - font.MeasureWidth(key)) * 0.5f) * s,
                placement.Y + (keyBox.Y + (keyBox.Height - font.LineHeight) * 0.5f) * s, s);

            var field = menu.MacroField;
            bool editing(int button) => menu.MacroFieldButton == button && field is { IsOpen: true };

            var titleBox = StockUiMacroMenus.TitleBox;
            float titleY = placement.Y + (titleBox.Y + (titleBox.Height - StockUiLogFont.CellHeight) * 0.5f) * s;
            float titleX = placement.X + (titleBox.X + FieldTextX) * s;
            if (editing(StockUiMacroMenus.EditorTitle)) DrawField(renderer, font, logFont, field!, titleX, titleY, s, timestamp);
            else DrawLine(renderer, font, logFont, macro.Title, titleX, titleY, s);

            for (int line = 0; line < MacroLibrary.LinesPerMacro; line++)
            {
                var box = StockUiMacroMenus.LineBox(line);
                float x = placement.X + (box.X + FieldTextX) * s;
                float y = placement.Y + (box.Y + (box.Height - StockUiLogFont.CellHeight) * 0.5f) * s;
                renderer.SetClip(placement.X + box.X * s, placement.Y + box.Y * s, box.Width * s, box.Height * s);
                if (editing(StockUiMacroMenus.EditorFirstLine + line)) DrawField(renderer, font, logFont, field!, x, y, s, timestamp, box.Width - 2 * FieldTextX);
                else DrawLine(renderer, font, logFont, macro.Lines[line], x, y, s);
                renderer.ClearClip();
            }
        }

        /// <summary>The field being typed in: its text, scrolled to keep the caret in view, and the blinking caret.</summary>
        private static void DrawField(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, StockUiTextField field, float x, float y, float s,
            long timestamp, float maxWidth = float.MaxValue)
        {
            string text = field.Text;
            int caret = Math.Clamp(field.Caret, 0, text.Length);
            Func<string, float> measure = logFont != null ? t => logFont.MeasureWidth(t) : t => font.MeasureWidth(t);
            float caretX = measure(text[..caret]);
            float shift = caretX > maxWidth ? caretX - maxWidth : 0;
            DrawLine(renderer, font, logFont, text, x - shift * s, y, s);
            bool on = (timestamp / (long)(Stopwatch.Frequency * CaretBlinkSeconds)) % 2 == 0;
            if (on)
            {
                float height = logFont != null ? StockUiLogFont.CellHeight - 2 : font.LineHeight;
                renderer.DrawTextureRect("colorbal", 7, 23, 1, 1, x + (caretX - shift) * s, y + 1 * s, 1 * s, height * s, White);
            }
        }

        private static void DrawLine(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, string text, float x, float y, float s)
        {
            if (text.Length == 0) return;
            if (logFont != null) logFont.Draw(renderer, text, x, y, s, White);
            else renderer.DrawText(font, text, x, y + 2 * s, s);
        }

        /// <summary>
        /// The palette in play (#339): the held palette's row (Full <c>mcr1long</c> / <c>mcr2long</c>, or the Compact
        /// <c>mcr1pall</c> / <c>mcr2pall</c>) at its authored place (16,48), or at the right edge when Misc. 2 "Macro Palette
        /// Position" says so (Static always, Dynamic while engaged), with each key's macro title and the key picked with
        /// Left / Right highlighted as a selected menu entry. Returns where it was drawn.
        /// </summary>
        public static StockUiPlacement? DrawPlayPalette(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, MacroController macros,
            bool compact, bool atRight, float scale, uint width, uint height)
        {
            var palette = macros.ShownPalette;
            if (palette == MacroPalette.None || !library.TryGetMenu(StockUiMacroMenus.PaletteMenu(palette, compact), out var definition)) return null;
            var f = definition.Frame;
            var at = StockUiLayout.Place(f.Anchor, f.X, f.Y, f.Width, f.Height, scale, width, height);
            if (atRight) at = at with { X = Math.Max(0, width - (f.Width + 16) * scale) };
            renderer.DrawMenu(definition, at, includeButtons: false);
            int selected = macros.HeldByGamepad ? -1 : macros.SelectedKey + 1;
            foreach (var button in definition.Buttons)
            {
                float bx = at.X + button.X * scale, by = at.Y + button.Y * scale;
                foreach (var shape in button.Shapes)
                {
                    if (shape.Kind != 0 || !library.TryGetImage(shape, out var label)) continue;
                    if (button.ButtonId != selected)
                    {
                        renderer.DrawImage(label, bx, by, scale);
                        continue;
                    }
                    foreach (var part in label.Parts)
                    {
                        UiColor? tint = part.BlendMode != UiBlendMode.Alpha ? null
                            : StockUiMenuWindow.IsGlyph(part) ? StockUiMenuWindow.SelectedGlyphTint : StockUiMenuWindow.SelectedCapsuleTint;
                        renderer.DrawPart(part, bx, by, scale, tint);
                    }
                }
            }
            var set = macros.Library.GetSet(macros.Library.CurrentBook, macros.Library.CurrentSet);
            var row = new Macro[MacroLibrary.KeysPerPalette];
            Array.Copy(set, palette == MacroPalette.Alt ? MacroLibrary.KeysPerPalette : 0, row, 0, row.Length);
            DrawKeyTitles(renderer, font, definition, at, row, 1, MacroLibrary.KeysPerPalette);
            return at;
        }
    }
}
