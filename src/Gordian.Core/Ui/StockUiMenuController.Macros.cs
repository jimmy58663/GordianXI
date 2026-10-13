// src/Gordian.Core/Ui/StockUiMenuController.Macros.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Input;
using Gordian.Core.Macros;
using Gordian.Core.Resources.Ui;

namespace Gordian.Core.Ui
{
    /// <summary>Which macro window an open menu is (#339).</summary>
    public enum StockUiMacroView : byte
    {
        None = 0,

        /// <summary><c>mcrselec</c>: the 40 macro books, 20 to a page.</summary>
        BookList,

        /// <summary><c>mcrselop</c>: a book's Confirm / Change Name / Copy / Paste / Delete.</summary>
        BookOptions,

        /// <summary>The Ctrl and Alt rows of a set (<c>mcr1edlo</c> over <c>mcr2edlo</c>, or the compact <c>mcr1edit</c> / <c>mcr2edit</c>).</summary>
        PaletteEditor,

        /// <summary><c>macro</c>: one macro's title and six lines.</summary>
        Editor,

        /// <summary><c>mcrledit</c>: "+ : Line Edit" (Copy / Paste / Delete Line).</summary>
        LineOptions,

        /// <summary><c>mcrbedit</c>: "+ : Button Edit" (Copy / Paste / Delete a macro).</summary>
        ButtonOptions,
    }

    /// <summary>
    /// The macro windows of the English menu DAT (ROM/119/51; read with <c>GORDIAN_UI_DUMP_MENUS=mcr*,macro</c>, 2026-10-12;
    /// help and title texts from ROM/165/75-76): the main menu's Macros opens <c>mcrmenu</c> (title 80 "MacroPalette": Edit
    /// Macro, help 829, and Edit Equip. Set, help 830), whose Edit Macro opens the book list <c>mcrselec</c> (20 rows, page
    /// arrows 21 / 22 beside it). A book opens <c>mcrselop</c> (Confirm, Change Name, Copy, Paste, Delete; help 661-665),
    /// Confirm the set's palettes for editing (title 75 "Edit Macros"), a key the macro editor <c>macro</c> (Prev and Next at
    /// the top, the title field, six line fields). + (keypad +, gamepad Y) opens <c>mcrbedit</c> on a palette key ("+ : Button
    /// Edit": Copy, Paste, Delete; help 659, 318, 317) and <c>mcrledit</c> on a line ("+ : Line Edit"; help 660, 320, 319).
    /// The palettes in play are <c>mcr1long</c> / <c>mcr2long</c> (Full) and <c>mcr1pall</c> / <c>mcr2pall</c> (Compact).
    /// </summary>
    public static class StockUiMacroMenus
    {
        public const string Menu = "mcrmenu";
        public const string BookList = "mcrselec";
        public const string BookOptions = "mcrselop";
        public const string Editor = "macro";
        public const string LineOptions = "mcrledit";
        public const string ButtonOptions = "mcrbedit";
        public const string CtrlFull = "mcr1long", AltFull = "mcr2long", CtrlCompact = "mcr1pall", AltCompact = "mcr2pall";
        public const string CtrlEditFull = "mcr1edlo", AltEditFull = "mcr2edlo", CtrlEditCompact = "mcr1edit", AltEditCompact = "mcr2edit";

        /// <summary>The name of the composed two-row palette editor.</summary>
        public const string PaletteEditorName = "mcrpaled";

        /// <summary>Title texts (ROM/165/76): 75 "Edit Macros", 80 "MacroPalette".</summary>
        public const string EditTitle = "Edit Macros", PaletteTitle = "MacroPalette";

        /// <summary>
        /// How far below the Ctrl row the editor draws the Alt row, in layout pixels: both rows are authored at (16,48), and
        /// a retail screenshot (BG-Wiki) shows them stacked with their borders overlapping (PROVISIONAL, measured from a
        /// scaled image).
        /// </summary>
        public const int AltRowOffset = 38;

        public const int BooksPerPage = 20, BookPrevPage = 21, BookNextPage = 22;
        public const int BookEdit = 1, BookRename = 2, BookCopy = 3, BookPaste = 4, BookDelete = 5;
        public const int EditorPrev = 1, EditorNext = 2, EditorTitle = 3, EditorFirstLine = 4;
        public const int OptionCopy = 1, OptionPaste = 2, OptionDelete = 3;

        /// <summary>
        /// The macro editor's fields in its frame (frames #61 draws them as <c>itemslot</c> boxes): the key label box (18..94,
        /// 30..58), the title box (110..234, 30..58) and the line boxes (18..358, 66 + 30 n .. 94 + 30 n).
        /// </summary>
        public static readonly (int X, int Y, int Width, int Height) KeyBox = (18, 30, 76, 28), TitleBox = (110, 30, 124, 28);

        public static (int X, int Y, int Width, int Height) LineBox(int line) => (18, 66 + 30 * line, 340, 28);

        /// <summary>The play palette for a palette and size.</summary>
        public static string PaletteMenu(MacroPalette palette, bool compact) =>
            palette == MacroPalette.Alt ? compact ? AltCompact : AltFull : compact ? CtrlCompact : CtrlFull;

        /// <summary>
        /// Composes the palette editor: the Ctrl row's buttons 1-10 and the Alt row's as 11-20, <see cref="AltRowOffset"/>
        /// lower, Up / Down linking a key to the same key of the other row, and both frames in one frame image.
        /// </summary>
        public static UiMenuDefinition? BuildPaletteEditor(UiResourceLibrary library, bool compact, out UiImage? frameImage)
        {
            frameImage = null;
            if (!library.TryGetMenu(compact ? CtrlEditCompact : CtrlEditFull, out var ctrl) || !library.TryGetMenu(compact ? AltEditCompact : AltEditFull, out var alt))
            {
                return null;
            }
            var buttons = new List<UiMenuButton>(MacroLibrary.MacrosPerSet);
            foreach (var (row, offset, idShift) in new[] { (ctrl, 0, 0), (alt, AltRowOffset, MacroLibrary.KeysPerPalette) })
            {
                foreach (var b in row.Buttons)
                {
                    if (b.ButtonId is < 1 or > MacroLibrary.KeysPerPalette) continue;
                    int other = idShift == 0 ? b.ButtonId + MacroLibrary.KeysPerPalette : b.ButtonId;
                    buttons.Add(new UiMenuButton
                    {
                        X = b.X,
                        Y = (short)(b.Y + offset),
                        Width = b.Width,
                        Height = b.Height,
                        CursorOffsetX = b.CursorOffsetX,
                        CursorOffsetY = b.CursorOffsetY,
                        ButtonId = (short)(b.ButtonId + idShift),
                        NavUp = (sbyte)other,
                        NavDown = (sbyte)other,
                        NavLeft = (sbyte)(b.NavLeft + idShift),
                        NavRight = (sbyte)(b.NavRight + idShift),
                        Shapes = b.Shapes,
                        HelpTextId = b.HelpTextId,
                        TitleTextId = b.TitleTextId,
                    });
                }
            }
            var parts = new List<UiSpritePart>();
            var shapes = new List<UiShapeReference>();
            foreach (var (row, offset) in new[] { (ctrl, 0), (alt, AltRowOffset) })
            {
                foreach (var shape in row.Frame.Shapes)
                {
                    if (shape.Kind != 0)
                    {
                        if (offset == 0) shapes.Add(shape);
                        continue;
                    }
                    if (!library.TryGetImage(shape, out var image)) continue;
                    foreach (var part in image.Parts) parts.Add(Offset(part, offset));
                }
            }
            frameImage = new UiImage { Parts = parts };
            var frame = new UiMenuFrame
            {
                X = ctrl.Frame.X,
                Y = ctrl.Frame.Y,
                Width = ctrl.Frame.Width,
                Height = (short)(AltRowOffset + alt.Frame.Height),
                Anchor = ctrl.Frame.Anchor,
                CursorOffsetX = ctrl.Frame.CursorOffsetX,
                CursorOffsetY = ctrl.Frame.CursorOffsetY,
                DrawOffsetX = ctrl.Frame.DrawOffsetX,
                DrawOffsetY = ctrl.Frame.DrawOffsetY,
                Shapes = shapes,
                HelpTextId = ctrl.Frame.HelpTextId,
                TitleTextId = ctrl.Frame.TitleTextId,
            };
            return new UiMenuDefinition
            {
                DatId = ctrl.DatId,
                Category = ctrl.Category,
                Name = PaletteEditorName,
                MenuType = ctrl.MenuType,
                Frame = frame,
                Buttons = buttons,
            };
        }

        /// <summary>The palette key a palette editor button stands for (buttons 1-10 Ctrl, 11-20 Alt).</summary>
        public static (MacroPalette Palette, int Key) KeyOfButton(int buttonId) =>
            buttonId > MacroLibrary.KeysPerPalette ? (MacroPalette.Alt, buttonId - MacroLibrary.KeysPerPalette - 1) : (MacroPalette.Ctrl, buttonId - 1);

        public static int ButtonOfKey(MacroPalette palette, int key) => (palette == MacroPalette.Alt ? MacroLibrary.KeysPerPalette : 0) + key + 1;

        private static UiSpritePart Offset(UiSpritePart part, int dy) => dy == 0 ? part : new UiSpritePart
        {
            TopLeft = new UiPoint(part.TopLeft.X, (short)(part.TopLeft.Y + dy)),
            TopRight = new UiPoint(part.TopRight.X, (short)(part.TopRight.Y + dy)),
            BottomLeft = new UiPoint(part.BottomLeft.X, (short)(part.BottomLeft.Y + dy)),
            BottomRight = new UiPoint(part.BottomRight.X, (short)(part.BottomRight.Y + dy)),
            SourceX = part.SourceX,
            SourceY = part.SourceY,
            SourceWidth = part.SourceWidth,
            SourceHeight = part.SourceHeight,
            Flags = part.Flags,
            ColorTopLeft = part.ColorTopLeft,
            ColorTopRight = part.ColorTopRight,
            ColorBottomLeft = part.ColorBottomLeft,
            ColorBottomRight = part.ColorBottomRight,
            TextureAttributes = part.TextureAttributes,
            TextureName = part.TextureName,
        };
    }

    public sealed partial class StockUiOpenMenu
    {
        /// <summary>Which macro window this is (None for every other window).</summary>
        public StockUiMacroView MacroView { get; init; }

        public bool IsMacroWindow => MacroView != StockUiMacroView.None;

        /// <summary>The macros a macro window shows.</summary>
        public MacroLibrary? MacroLibrary { get; init; }

        /// <summary>The field the macro windows type into (drawn over the field being edited).</summary>
        public StockUiTextField? MacroField { get; init; }

        /// <summary>The book list's page: 0 for books 1-20, 1 for 21-40.</summary>
        public int MacroBookPage { get; internal set; }

        /// <summary>The book a book's options, the palette editor or the macro editor is for (0-39).</summary>
        public int MacroBook { get; internal set; }

        /// <summary>The set the palette editor or the macro editor shows (0-9).</summary>
        public int MacroSet { get; internal set; }

        /// <summary>The macro the editor edits.</summary>
        public MacroSlot MacroSlot { get; internal set; }

        /// <summary>The field being typed in (the editor's title 3 or lines 4-9, a book row), 0 when none.</summary>
        public int MacroFieldButton { get; internal set; }

        /// <summary>The editor's line the Line Edit options are for (0-5).</summary>
        public int MacroLine { get; internal set; }

        /// <summary>The help line under "Edit Macros": "Macro Set &lt;book&gt; (#&lt;set&gt;)".</summary>
        public string MacroSetCaption => MacroLibrary is { } library
            ? $"Macro Set {library.GetBookTitle(MacroBook)} (#{MacroSet + 1})"
            : string.Empty;
    }

    public sealed partial class StockUiMenuController
    {
        private Macro? _macroClipboard;
        private string? _lineClipboard;
        private (string Title, Macro[][] Sets)? _bookClipboard;

        /// <summary>The character's macros (#339); the macro windows edit them. Null: the Macros entry posts a notice.</summary>
        public MacroLibrary? Macros { get; set; }

        /// <summary>The field the macro windows type into; the viewport feeds it the keyboard while it is open.</summary>
        public StockUiTextField MacroField { get; } = new();

        /// <summary>Whether the Compact macro palette is chosen (Misc. 2 "Macro Palette Size").</summary>
        public bool CompactMacroPalette => _settings.GetValue(StockUiSettingKey.MacroPaletteSize) == 1;

        /// <summary>Opens the book list (the Macros menu's Edit Macro), on the current book's page and row.</summary>
        private void OpenMacroBooks(StockUiOpenMenu from)
        {
            var library = Macros;
            if (library == null)
            {
                NoticePosted?.Invoke("Macros are not available in this session.");
                return;
            }
            bool opened;
            lock (_sync)
            {
                var list = PushMacro(StockUiMacroMenus.BookList, from, StockUiMacroView.BookList, library);
                if (list != null)
                {
                    int book = library.CurrentBook;
                    list.MacroBookPage = book / StockUiMacroMenus.BooksPerPage;
                    list.SelectedButtonId = book % StockUiMacroMenus.BooksPerPage + 1;
                }
                opened = list != null;
            }
            if (opened) Changed?.Invoke();
        }

        private StockUiOpenMenu? PushMacro(string name, StockUiOpenMenu? parent, StockUiMacroView view, MacroLibrary library)
        {
            var ui = _library;
            if (ui == null || !ui.TryGetMenu(name, out var definition)) return null;
            return PushMacro(definition, null, parent, view, library);
        }

        private StockUiOpenMenu PushMacro(UiMenuDefinition definition, UiImage? frameImage, StockUiOpenMenu? parent, StockUiMacroView view, MacroLibrary library)
        {
            var menu = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null)
            {
                MacroView = view,
                MacroLibrary = library,
                MacroField = MacroField,
                FrameImage = frameImage,
            };
            menu.SelectedButtonId = FirstSelectable(definition);
            PushOpen(menu);
            return menu;
        }

        private bool HasMacroWindow
        {
            get
            {
                foreach (var menu in _open) if (menu.IsMacroWindow) return true;
                return false;
            }
        }

        /// <summary>
        /// While a macro field is typed in, Confirm keeps it and Cancel drops it (the gamepad's A / B; the keyboard's keys
        /// reach the field directly). Returns true while the field has the input.
        /// </summary>
        private bool ProcessMacroField(InputState input)
        {
            if (!MacroField.IsOpen) return false;
            if (!HasMacroWindow)
            {
                MacroField.Cancel();
                return false;
            }
            if (input.WasActionTriggered(InputAction.Cancel)) MacroField.Cancel();
            else if (input.WasActionTriggered(InputAction.Confirm)) MacroField.Commit();
            return true;
        }

        /// <summary>+ (keypad +, gamepad Y) on a palette key or a macro line opens its options; again closes them.</summary>
        internal bool ToggleMacroOptions()
        {
            var top = Top;
            if (top == null || Macros is not { } library) return false;
            if (top.MacroView is StockUiMacroView.LineOptions or StockUiMacroView.ButtonOptions)
            {
                CloseMenu(top);
                return true;
            }
            StockUiOpenMenu? options = null;
            lock (_sync)
            {
                if (top.MacroView == StockUiMacroView.PaletteEditor && top.SelectedButtonId is >= 1 and <= MacroLibrary.MacrosPerSet)
                {
                    options = PushMacro(StockUiMacroMenus.ButtonOptions, top, StockUiMacroView.ButtonOptions, library);
                    if (options != null)
                    {
                        var (palette, key) = StockUiMacroMenus.KeyOfButton(top.SelectedButtonId);
                        options.MacroBook = top.MacroBook;
                        options.MacroSet = top.MacroSet;
                        options.MacroSlot = new MacroSlot(top.MacroBook, top.MacroSet, palette, key);
                    }
                }
                else if (top.MacroView == StockUiMacroView.Editor && top.SelectedButtonId is >= StockUiMacroMenus.EditorFirstLine and < StockUiMacroMenus.EditorFirstLine + MacroLibrary.LinesPerMacro)
                {
                    options = PushMacro(StockUiMacroMenus.LineOptions, top, StockUiMacroView.LineOptions, library);
                    if (options != null)
                    {
                        options.MacroSlot = top.MacroSlot;
                        options.MacroBook = top.MacroBook;
                        options.MacroSet = top.MacroSet;
                        options.MacroLine = top.SelectedButtonId - StockUiMacroMenus.EditorFirstLine;
                    }
                }
                else
                {
                    return false;
                }
            }
            if (options != null) Changed?.Invoke();
            return true;
        }

        /// <summary>The cursor in the macro windows: the book list's left / right turn its page; the palette editor's up from the
        /// Ctrl row goes to the previous set's Alt row and down from the Alt row to the next set's Ctrl row. Called under the lock.</summary>
        private bool TryMoveMacro(StockUiOpenMenu top, InputAction direction, ref bool changed)
        {
            bool horizontal = direction is InputAction.MenuLeft or InputAction.MenuRight;
            switch (top.MacroView)
            {
                case StockUiMacroView.BookList when horizontal:
                    top.MacroBookPage = 1 - top.MacroBookPage;
                    changed = true;
                    return true;
                case StockUiMacroView.PaletteEditor when !horizontal:
                    var (palette, key) = StockUiMacroMenus.KeyOfButton(top.SelectedButtonId);
                    if (palette == MacroPalette.Ctrl && direction == InputAction.MenuUp)
                    {
                        top.MacroSet = (top.MacroSet + MacroLibrary.SetsPerBook - 1) % MacroLibrary.SetsPerBook;
                        top.SelectedButtonId = StockUiMacroMenus.ButtonOfKey(MacroPalette.Alt, key);
                        changed = true;
                        return true;
                    }
                    if (palette == MacroPalette.Alt && direction == InputAction.MenuDown)
                    {
                        top.MacroSet = (top.MacroSet + 1) % MacroLibrary.SetsPerBook;
                        top.SelectedButtonId = StockUiMacroMenus.ButtonOfKey(MacroPalette.Ctrl, key);
                        changed = true;
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>Confirm in a macro window; false when the top menu is none of them.</summary>
        private bool TryActivateMacroWindow(StockUiOpenMenu top, UiMenuButton button)
        {
            if (!top.IsMacroWindow || Macros is not { } library) return false;
            int id = button.ButtonId;
            switch (top.MacroView)
            {
                case StockUiMacroView.BookList:
                    ActivateBookRow(top, library, id);
                    break;
                case StockUiMacroView.BookOptions:
                    ActivateBookOption(top, library, id);
                    break;
                case StockUiMacroView.PaletteEditor:
                    if (id is >= 1 and <= MacroLibrary.MacrosPerSet)
                    {
                        var (palette, key) = StockUiMacroMenus.KeyOfButton(id);
                        OpenMacroEditor(top, library, new MacroSlot(top.MacroBook, top.MacroSet, palette, key));
                    }
                    break;
                case StockUiMacroView.Editor:
                    ActivateEditorButton(top, library, id);
                    break;
                case StockUiMacroView.LineOptions:
                    ActivateLineOption(top, library, id);
                    break;
                case StockUiMacroView.ButtonOptions:
                    ActivateButtonOption(top, library, id);
                    break;
            }
            return true;
        }

        private void ActivateBookRow(StockUiOpenMenu list, MacroLibrary library, int id)
        {
            if (id == StockUiMacroMenus.BookPrevPage || id == StockUiMacroMenus.BookNextPage)
            {
                lock (_sync) list.MacroBookPage = 1 - list.MacroBookPage;
                Changed?.Invoke();
                return;
            }
            if (id is < 1 or > StockUiMacroMenus.BooksPerPage) return;
            bool opened;
            lock (_sync)
            {
                var options = PushMacro(StockUiMacroMenus.BookOptions, list, StockUiMacroView.BookOptions, library);
                if (options != null) options.MacroBook = list.MacroBookPage * StockUiMacroMenus.BooksPerPage + id - 1;
                opened = options != null;
            }
            if (opened) Changed?.Invoke();
        }

        private void ActivateBookOption(StockUiOpenMenu options, MacroLibrary library, int id)
        {
            int book = options.MacroBook;
            var list = options.Parent;
            switch (id)
            {
                case StockUiMacroMenus.BookEdit:
                    OpenPaletteEditor(options, library, book);
                    break;
                case StockUiMacroMenus.BookRename:
                    CloseMenu(options);
                    if (list == null) return;
                    lock (_sync) list.MacroFieldButton = book % StockUiMacroMenus.BooksPerPage + 1;
                    MacroField.Open(library.GetBookTitle(book), MacroLibrary.MaxBookTitleLength, (text, _) =>
                    {
                        library.SetBookTitle(book, text.Trim());
                        lock (_sync) list.MacroFieldButton = 0;
                        Changed?.Invoke();
                    });
                    MacroField.OpenChanged += ClearOnClose;
                    void ClearOnClose(bool open)
                    {
                        if (open) return;
                        MacroField.OpenChanged -= ClearOnClose;
                        lock (_sync) list.MacroFieldButton = 0;
                        Changed?.Invoke();
                    }
                    Changed?.Invoke();
                    break;
                case StockUiMacroMenus.BookCopy:
                    _bookClipboard = library.GetBook(book);
                    NoticePosted?.Invoke($"Copied macro book {library.GetBookTitle(book)}.");
                    CloseMenu(options);
                    break;
                case StockUiMacroMenus.BookPaste:
                    if (_bookClipboard is not { } copied)
                    {
                        NoticePosted?.Invoke("No macro book has been copied.");
                        return;
                    }
                    CloseMenu(options);
                    _ = ConfirmMacroAsync($"Paste over {library.GetBookTitle(book)}?", () => library.ReplaceBook(book, copied.Title, copied.Sets));
                    break;
                case StockUiMacroMenus.BookDelete:
                    CloseMenu(options);
                    _ = ConfirmMacroAsync($"Delete {library.GetBookTitle(book)}?", () => library.ReplaceBook(book, string.Empty, null));
                    break;
            }
        }

        private async Task ConfirmMacroAsync(string question, Action action)
        {
            if (await PromptYesNoAsync(question, defaultYes: false).ConfigureAwait(false))
            {
                action();
                Changed?.Invoke();
            }
        }

        /// <summary>Opens a book's palettes for editing, on the current set when it is the current book.</summary>
        private void OpenPaletteEditor(StockUiOpenMenu from, MacroLibrary library, int book)
        {
            var ui = _library;
            if (ui == null) return;
            var definition = StockUiMacroMenus.BuildPaletteEditor(ui, CompactMacroPalette, out var frameImage);
            if (definition == null) return;
            lock (_sync)
            {
                var editor = PushMacro(definition, frameImage, from, StockUiMacroView.PaletteEditor, library);
                editor.MacroBook = book;
                editor.MacroSet = library.CurrentBook == book ? library.CurrentSet : 0;
                editor.SelectedButtonId = 1;
            }
            Changed?.Invoke();
        }

        private void OpenMacroEditor(StockUiOpenMenu from, MacroLibrary library, MacroSlot slot)
        {
            bool opened;
            lock (_sync)
            {
                var editor = PushMacro(StockUiMacroMenus.Editor, from, StockUiMacroView.Editor, library);
                if (editor != null)
                {
                    editor.MacroSlot = slot;
                    editor.MacroBook = slot.Book;
                    editor.MacroSet = slot.Set;
                    editor.SelectedButtonId = StockUiMacroMenus.EditorFirstLine;
                }
                opened = editor != null;
            }
            if (opened) Changed?.Invoke();
        }

        private void ActivateEditorButton(StockUiOpenMenu editor, MacroLibrary library, int id)
        {
            var slot = editor.MacroSlot;
            switch (id)
            {
                case StockUiMacroMenus.EditorPrev:
                case StockUiMacroMenus.EditorNext:
                    // Prev / Next walk the set's twenty macros, Ctrl 1 .. Ctrl 0 then Alt 1 .. Alt 0, wrapping.
                    int index = (slot.IndexInSet + (id == StockUiMacroMenus.EditorNext ? 1 : -1) + MacroLibrary.MacrosPerSet) % MacroLibrary.MacrosPerSet;
                    var next = new MacroSlot(slot.Book, slot.Set, index < MacroLibrary.KeysPerPalette ? MacroPalette.Ctrl : MacroPalette.Alt, index % MacroLibrary.KeysPerPalette);
                    lock (_sync)
                    {
                        editor.MacroSlot = next;
                        if (editor.Parent is { MacroView: StockUiMacroView.PaletteEditor } palette) palette.SelectedButtonId = StockUiMacroMenus.ButtonOfKey(next.Palette, next.Key);
                    }
                    Changed?.Invoke();
                    break;
                case StockUiMacroMenus.EditorTitle:
                    BeginMacroFieldEdit(editor, library, id);
                    break;
                case >= StockUiMacroMenus.EditorFirstLine and < StockUiMacroMenus.EditorFirstLine + MacroLibrary.LinesPerMacro:
                    BeginMacroFieldEdit(editor, library, id);
                    break;
            }
        }

        /// <summary>Starts typing in the editor's title (button 3) or a line (4-9); Up / Down keep the text and move the cursor.</summary>
        private void BeginMacroFieldEdit(StockUiOpenMenu editor, MacroLibrary library, int button)
        {
            if (MacroField.IsOpen) MacroField.Commit();
            var slot = editor.MacroSlot;
            var macro = library.Get(slot);
            bool title = button == StockUiMacroMenus.EditorTitle;
            string text = title ? macro.Title : macro.Lines[button - StockUiMacroMenus.EditorFirstLine];
            lock (_sync) editor.MacroFieldButton = button;
            MacroField.Open(text, title ? MacroLibrary.MaxTitleLength : MacroLibrary.MaxLineLength, (typed, step) =>
            {
                var current = library.Get(slot);
                library.Set(slot, title ? current.WithTitle(typed) : current.WithLine(button - StockUiMacroMenus.EditorFirstLine, typed));
                lock (_sync)
                {
                    editor.MacroFieldButton = 0;
                    int target = button + step;
                    if (step != 0 && target >= StockUiMacroMenus.EditorTitle && target < StockUiMacroMenus.EditorFirstLine + MacroLibrary.LinesPerMacro) editor.SelectedButtonId = target;
                }
                Changed?.Invoke();
            });
            MacroField.OpenChanged += ClearOnClose;
            void ClearOnClose(bool open)
            {
                if (open) return;
                MacroField.OpenChanged -= ClearOnClose;
                lock (_sync) editor.MacroFieldButton = 0;
                Changed?.Invoke();
            }
            Changed?.Invoke();
        }

        private void ActivateLineOption(StockUiOpenMenu options, MacroLibrary library, int id)
        {
            var slot = options.MacroSlot;
            int line = options.MacroLine;
            var macro = library.Get(slot);
            switch (id)
            {
                case StockUiMacroMenus.OptionCopy:
                    _lineClipboard = macro.Lines[line];
                    break;
                case StockUiMacroMenus.OptionPaste:
                    if (_lineClipboard == null)
                    {
                        NoticePosted?.Invoke("No line has been copied.");
                        return;
                    }
                    library.Set(slot, macro.WithLine(line, _lineClipboard));
                    break;
                case StockUiMacroMenus.OptionDelete:
                    library.Set(slot, macro.WithLine(line, string.Empty));
                    break;
                default:
                    return;
            }
            CloseMenu(options);
        }

        private void ActivateButtonOption(StockUiOpenMenu options, MacroLibrary library, int id)
        {
            var slot = options.MacroSlot;
            switch (id)
            {
                case StockUiMacroMenus.OptionCopy:
                    _macroClipboard = library.Get(slot);
                    break;
                case StockUiMacroMenus.OptionPaste:
                    if (_macroClipboard == null)
                    {
                        NoticePosted?.Invoke("No macro has been copied.");
                        return;
                    }
                    library.Set(slot, _macroClipboard);
                    break;
                case StockUiMacroMenus.OptionDelete:
                    library.Set(slot, Macro.Empty);
                    break;
                default:
                    return;
            }
            CloseMenu(options);
        }
    }
}
