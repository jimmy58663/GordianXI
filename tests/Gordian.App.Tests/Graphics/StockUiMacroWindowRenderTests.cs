// tests/Gordian.App.Tests/Graphics/StockUiMacroWindowRenderTests.cs
using System;
using Gordian.App.Graphics;
using Gordian.Core.Input;
using Gordian.Core.Macros;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>The macro palettes and editor (#339) rendered from the retail DATs on D3D11 (GORDIAN_UI_DUMP writes the frames).</summary>
    public class StockUiMacroWindowRenderTests
    {
        private static MacroLibrary SampleLibrary()
        {
            var macros = new MacroLibrary();
            macros.Set(new MacroSlot(0, 0, MacroPalette.Ctrl, 0), new Macro("Provoke", new[] { "/ja \"Provoke\" <t>", "/recast \"Provoke\"" }));
            macros.Set(new MacroSlot(0, 0, MacroPalette.Ctrl, 1), new Macro("Berserk", new[] { "/ja \"Berserk\" <me>" }));
            macros.Set(new MacroSlot(0, 0, MacroPalette.Alt, 9), new Macro("Warcry", new[] { "/ja \"Warcry\" <me>" }));
            macros.SetBookTitle(0, "WAR");
            return macros;
        }

        [Fact]
        public void RendersPlayPaletteFullAndCompact()
        {
            using var screen = StockUiOffscreen.TryCreate(1280, 400);
            if (screen == null) return;
            var controller = new MacroController(SampleLibrary());
            var input = new InputState();
            input.SetKeyDown(GordianKey.LeftCtrl);
            input.Update(InputProfile.CreateCompact(), TimeSpan.FromMilliseconds(16));
            controller.Update(input, blocked: false);
            Assert.Equal(MacroPalette.Ctrl, controller.ShownPalette);

            StockUiPlacement? full = null, compact = null;
            var shot = screen.Render(r =>
            {
                full = StockUiMacroWindow.DrawPlayPalette(r, screen.Library, screen.Font, controller, compact: false, shifted: false, 1, screen.Width, screen.Height);
                compact = StockUiMacroWindow.DrawPlayPalette(r, screen.Library, screen.Font, controller, compact: true, shifted: true, 1, screen.Width, screen.Height);
                StockUiMenuWindow.DrawHelpBar(r, screen.Library, screen.Font, screen.LogFont, StockUiMacroMenus.PaletteTitle, "Macro Set WAR (#1)", 1, screen.Width, screen.Height);
            }, "gpu_macro_palette.png");
            Assert.NotNull(full);
            Assert.NotNull(compact);
            Assert.Equal(16, full!.Value.X);
            Assert.Equal(48, full.Value.Y);
            Assert.Equal(16 + StockUiMacroWindow.CombatShift, compact!.Value.X); // moved right for the combat vitals
            // "Provoke" on Ctrl1 (button at 4,4 of the Full palette): light glyphs near the key's top left.
            Assert.True(screen.HasLightPixel(shot, 16 + 8, 16 + 50, 48 + 9, 150) || screen.HasLightPixel(shot, 16 + 8, 16 + 50, 48 + 10, 150));
        }

        [Fact]
        public void RendersPaletteEditorAndMacroEditor()
        {
            using var screen = StockUiOffscreen.TryCreate(1280, 400);
            if (screen == null) return;
            var macros = SampleLibrary();
            var menus = new StockUiMenuController { Library = screen.Library, Macros = macros };
            StockUiPlacement Place(StockUiOpenMenu menu)
            {
                var frame = menu.Menu.Frame;
                return StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
            }
            Assert.True(menus.Open(StockUiMacroMenus.Menu));
            menus.Activate(); // Edit Macro
            menus.Activate(); // book 1
            var books = menus.OpenMenus[^2];
            var options = menus.Top!;
            Assert.Equal(StockUiMacroView.BookOptions, options.MacroView);
            screen.Render(r =>
            {
                var at = Place(books);
                StockUiMenuWindow.Draw(r, screen.Library, screen.Font, books, at, 0, logFont: screen.LogFont);
                StockUiMenuWindow.Draw(r, screen.Library, screen.Font, options, at with { X = at.X - 116, Y = at.Y }, 0, logFont: screen.LogFont);
            }, "gpu_macro_books.png");
            menus.Activate(); // Confirm
            var palette = menus.Top!;
            Assert.Equal(StockUiMacroView.PaletteEditor, palette.MacroView);

            screen.Render(r =>
            {
                StockUiMenuWindow.Draw(r, screen.Library, screen.Font, palette, Place(palette), 0, logFont: screen.LogFont);
                StockUiMenuWindow.DrawHelpBar(r, screen.Library, screen.Font, screen.LogFont, StockUiMacroMenus.EditTitle, palette.MacroSetCaption, 1, screen.Width, screen.Height);
                Assert.True(screen.Library.TryGetMenu(StockUiMacroMenus.ButtonOptions, out var buttonEdit));
                StockUiMacroWindow.DrawOptions(r, screen.Library, buttonEdit, Place(menus.Top!) with { X = screen.Width - 128, Y = 134 });
            }, "gpu_macro_palette_editor.png");

            menus.Activate(); // Ctrl1
            var editor = menus.Top!;
            Assert.Equal(StockUiMacroView.Editor, editor.MacroView);
            menus.Move(InputAction.MenuDown); // the second line
            menus.Activate();
            menus.MacroField.InsertText(" <wait 1>");
            var shot = screen.Render(r =>
            {
                StockUiMenuWindow.Draw(r, screen.Library, screen.Font, editor, Place(editor), 0, logFont: screen.LogFont);
                Assert.True(screen.Library.TryGetMenu(StockUiMacroMenus.LineOptions, out var lineEdit));
                StockUiMacroWindow.DrawOptions(r, screen.Library, lineEdit, Place(editor) with { X = screen.Width - 128, Y = 48 });
            }, "gpu_macro_editor.png");
            // The first line's text in its box (18..358 x 66..94 from 16,48).
            Assert.True(screen.HasLightPixel(shot, 16 + 24, 16 + 120, 48 + 80, 150) || screen.HasLightPixel(shot, 16 + 24, 16 + 120, 48 + 78, 150));
        }
    }
}
