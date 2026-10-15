// tests/Gordian.Core.Tests/Ui/StockUiMacroWindowTests.cs
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Input;
using Gordian.Core.Macros;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    /// <summary>The macro windows (#339) against the retail menu DAT, when the game is installed.</summary>
    public class StockUiMacroWindowTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private static UiResourceLibrary? Load()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return UiResourceLibrary.Load(rm);
        }

        [Fact]
        public void RetailMacroMenus_HaveTheExpectedButtons()
        {
            var library = Load();
            if (library == null) return;
            Assert.True(library.TryGetMenu(StockUiMacroMenus.Menu, out var menu));
            Assert.NotNull(menu.FindButton(1));
            Assert.NotNull(menu.FindButton(2));
            Assert.True(library.TryGetMenu(StockUiMacroMenus.BookList, out var books));
            Assert.Equal(20, books.Buttons.Count(b => b.ButtonId is >= 1 and <= 20));
            Assert.True(library.TryGetMenu(StockUiMacroMenus.Editor, out var editor));
            Assert.Equal(9, editor.Buttons.Count);
            foreach (string name in new[] { StockUiMacroMenus.CtrlFull, StockUiMacroMenus.AltFull, StockUiMacroMenus.CtrlCompact, StockUiMacroMenus.AltCompact })
            {
                Assert.True(library.TryGetMenu(name, out var palette), name);
                Assert.Equal(10, palette.Buttons.Count);
            }
            foreach (bool compact in new[] { false, true })
            {
                var composed = StockUiMacroMenus.BuildPaletteEditor(library, compact, out var frameImage);
                Assert.NotNull(composed);
                Assert.NotNull(frameImage);
                Assert.Equal(20, composed!.Buttons.Count);
                var ctrl3 = composed.FindButton(3)!;
                var alt3 = composed.FindButton(13)!;
                Assert.Equal(ctrl3.X, alt3.X);
                Assert.Equal(ctrl3.Y + StockUiMacroMenus.AltRowOffset, alt3.Y);
                Assert.Equal(13, ctrl3.NavDown);
                Assert.Equal(3, alt3.NavUp);
                Assert.Equal(12, alt3.NavLeft);
            }
            Assert.True(StockUiMenuEntries.TryGet(StockUiMenuEntries.MainMenuPage2, 6, out var macros));
            Assert.Equal(StockUiMacroMenus.Menu, macros.Opens);
            Assert.True(StockUiMenuEntries.TryGet(StockUiMacroMenus.Menu, 1, out var edit));
            Assert.Equal(StockUiMenuCommand.MacroBooks, edit.Command);
        }

        [Fact]
        public void EditFlow_BookPaletteEditorAndTyping()
        {
            var ui = Load();
            if (ui == null) return;
            var macros = new MacroLibrary();
            macros.SelectSet(1, 3);
            var controller = new StockUiMenuController { Library = ui, Macros = macros };
            string? notice = null;
            controller.NoticePosted += n => notice = n;

            Assert.True(controller.Open(StockUiMacroMenus.Menu));
            controller.Activate(); // Edit Macro
            var list = controller.Top!;
            Assert.Equal(StockUiMacroView.BookList, list.MacroView);
            Assert.Equal(2, list.SelectedButtonId); // the current book (book 2) is selected
            controller.Move(InputAction.MenuRight);
            Assert.Equal(1, list.MacroBookPage);
            controller.Move(InputAction.MenuLeft);

            controller.Activate(); // book 2
            Assert.Equal(StockUiMacroView.BookOptions, controller.Top!.MacroView);
            Assert.Equal(1, controller.Top.MacroBook);
            controller.Activate(); // Confirm: edit the book
            var palette = controller.Top!;
            Assert.Equal(StockUiMacroView.PaletteEditor, palette.MacroView);
            Assert.Equal(3, palette.MacroSet); // the current set
            Assert.Equal("Macro Set Book02 (#4)", palette.MacroSetCaption);

            // Down to the Alt row, down again to the next set's Ctrl row, up back to the Alt row of set 4.
            controller.Move(InputAction.MenuDown);
            Assert.Equal(11, palette.SelectedButtonId);
            controller.Move(InputAction.MenuDown);
            Assert.Equal((4, 1), (palette.MacroSet, palette.SelectedButtonId));
            controller.Move(InputAction.MenuUp);
            Assert.Equal((3, 11), (palette.MacroSet, palette.SelectedButtonId));
            controller.Move(InputAction.MenuRight);
            Assert.Equal(12, palette.SelectedButtonId);

            controller.Activate(); // Alt2
            var editor = controller.Top!;
            Assert.Equal(StockUiMacroView.Editor, editor.MacroView);
            Assert.Equal(new MacroSlot(1, 3, MacroPalette.Alt, 1), editor.MacroSlot);
            Assert.Equal(StockUiMacroMenus.EditorFirstLine, editor.SelectedButtonId);

            controller.Activate(); // the first line
            Assert.True(controller.MacroField.IsOpen);
            Assert.Equal(StockUiMacroMenus.EditorFirstLine, editor.MacroFieldButton);
            controller.MacroField.InsertText("/ja \"Provoke\" <t>");
            controller.MacroField.HandleKey(GordianKey.Down, InputModifiers.None);
            // Down keeps the line and goes on typing in the next one (round 1); Escape keeps that line too.
            Assert.True(controller.MacroField.IsOpen);
            Assert.Equal("/ja \"Provoke\" <t>", macros.Get(editor.MacroSlot).Lines[0]);
            Assert.Equal(StockUiMacroMenus.EditorFirstLine + 1, editor.SelectedButtonId);
            Assert.Equal(StockUiMacroMenus.EditorFirstLine + 1, editor.MacroFieldButton);
            controller.MacroField.InsertText("/echo kept");
            controller.MacroField.HandleKey(GordianKey.Escape, InputModifiers.None);
            Assert.False(controller.MacroField.IsOpen);
            Assert.Equal("/echo kept", macros.Get(editor.MacroSlot).Lines[1]);

            controller.Move(InputAction.MenuUp);
            controller.Move(InputAction.MenuUp);
            Assert.Equal(StockUiMacroMenus.EditorTitle, editor.SelectedButtonId);
            controller.Activate();
            controller.MacroField.InsertText("Provoke!!");
            controller.MacroField.HandleKey(GordianKey.Enter, InputModifiers.None);
            Assert.Equal("Provoke!", macros.Get(editor.MacroSlot).Title); // eight characters

            // Line Edit: copy line 1, paste into line 2.
            controller.Move(InputAction.MenuDown);
            Assert.True(controller.ToggleMacroOptions());
            Assert.Equal(StockUiMacroView.LineOptions, controller.Top!.MacroView);
            controller.Activate(); // Copy Line
            Assert.Same(editor, controller.Top);
            controller.Move(InputAction.MenuDown);
            Assert.True(controller.ToggleMacroOptions());
            controller.Move(InputAction.MenuDown); // Paste Line
            controller.Activate();
            Assert.Equal("/ja \"Provoke\" <t>", macros.Get(editor.MacroSlot).Lines[1]);

            // Next walks to Alt3; Cancel back to the palette, whose cursor followed.
            controller.Move(InputAction.MenuUp);
            controller.Move(InputAction.MenuUp);
            controller.Move(InputAction.MenuUp);
            Assert.Equal(StockUiMacroMenus.EditorNext, editor.SelectedButtonId);
            controller.Activate();
            Assert.Equal(new MacroSlot(1, 3, MacroPalette.Alt, 2), editor.MacroSlot);
            controller.CloseTop();
            Assert.Same(palette, controller.Top);
            Assert.Equal(13, palette.SelectedButtonId);

            // Button Edit on Alt2: copy, then paste on Alt3; delete Alt2.
            controller.Move(InputAction.MenuLeft);
            Assert.True(controller.ToggleMacroOptions());
            controller.Activate(); // Copy
            controller.Move(InputAction.MenuRight);
            Assert.True(controller.ToggleMacroOptions());
            controller.Move(InputAction.MenuDown);
            controller.Activate(); // Paste
            Assert.Equal("Provoke!", macros.Get(new MacroSlot(1, 3, MacroPalette.Alt, 2)).Title);
            controller.Move(InputAction.MenuLeft);
            Assert.True(controller.ToggleMacroOptions());
            controller.Move(InputAction.MenuUp); // Delete (the list wraps from Copy)
            controller.Activate();
            Assert.True(macros.Get(new MacroSlot(1, 3, MacroPalette.Alt, 1)).IsEmpty);
            Assert.Null(notice);
        }

        [Fact]
        public async Task BookOptions_RenameCopyPasteDelete()
        {
            var ui = Load();
            if (ui == null) return;
            var macros = new MacroLibrary();
            macros.Set(new MacroSlot(0, 0, MacroPalette.Ctrl, 0), new Macro("One", new[] { "/echo one" }));
            var controller = new StockUiMenuController { Library = ui, Macros = macros };
            Assert.True(controller.Open(StockUiMacroMenus.Menu));
            controller.Activate();
            var list = controller.Top!;

            controller.Activate(); // book 1
            controller.Move(InputAction.MenuDown); // Change Name
            controller.Activate();
            Assert.Same(list, controller.Top);
            Assert.True(controller.MacroField.IsOpen);
            Assert.Equal(1, list.MacroFieldButton);
            controller.MacroField.HandleKey(GordianKey.Backspace, InputModifiers.None);
            controller.MacroField.HandleKey(GordianKey.Backspace, InputModifiers.None);
            controller.MacroField.InsertText("WAR");
            controller.MacroField.HandleKey(GordianKey.Enter, InputModifiers.None);
            Assert.Equal("BookWAR", macros.GetBookTitle(0));
            Assert.Equal(0, list.MacroFieldButton);

            controller.Activate(); // book 1 again
            controller.Move(InputAction.MenuDown);
            controller.Move(InputAction.MenuDown); // Copy
            controller.Activate();
            Assert.Same(list, controller.Top);
            controller.Move(InputAction.MenuDown); // book 2
            controller.Activate();
            controller.Move(InputAction.MenuUp);
            controller.Move(InputAction.MenuUp); // Paste (up from Confirm wraps to Delete, then Paste)
            Assert.Equal(StockUiMacroMenus.BookPaste, controller.Top!.SelectedButtonId);
            Assert.Equal(1, controller.Top.MacroBook);
            controller.Activate();
            var prompt = controller.Top!;
            Assert.Equal("Paste over Book02?", prompt.Message);
            Assert.True(prompt.IsPrompt);
            controller.Move(InputAction.MenuUp); // Yes (the retail comyn's buttons sit side by side or stacked)
            if (prompt.SelectedButtonId != 1) controller.Move(InputAction.MenuLeft);
            Assert.Equal(1, prompt.SelectedButtonId);
            controller.Activate();
            // The answer runs on a continuation: wait for it rather than for a fixed time.
            for (int i = 0; i < 200 && macros.GetBookTitle(1) != "BookWAR"; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.Equal("BookWAR", macros.GetBookTitle(1));
            Assert.Equal("One", macros.Get(new MacroSlot(1, 0, MacroPalette.Ctrl, 0)).Title);
        }
    }
}
