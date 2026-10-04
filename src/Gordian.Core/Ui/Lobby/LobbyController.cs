// src/Gordian.Core/Ui/Lobby/LobbyController.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.LandSandBoat;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Resources.Ui;

namespace Gordian.Core.Ui.Lobby
{
    /// <summary>The lobby's input: the menu cursor keys, confirm and cancel.</summary>
    public enum LobbyInput
    {
        Up,
        Down,
        Left,
        Right,
        Confirm,
        Cancel,
        /// <summary>Deletes the last letter of a name being entered.</summary>
        Backspace,
    }

    /// <summary>Which lobby screen is up.</summary>
    public enum LobbyScreen
    {
        /// <summary>The title menu: Select Character, Create Character, Delete Character, Config, Back.</summary>
        MainMenu,
        /// <summary>The 16-slot character list (to play or to delete a character).</summary>
        CharacterList,
        /// <summary>Character creation (race, face, hair, size, job, nation, name, world).</summary>
        Creation,
        /// <summary>A character was chosen; the game session is starting.</summary>
        Entering,
        /// <summary>The lobby was left (Back) or its connection is gone.</summary>
        Closed,
    }

    /// <summary>A lobby DAT menu on screen and the button under its cursor.</summary>
    public sealed class LobbyMenu
    {
        public LobbyMenu(UiMenuDefinition definition, int selectedButtonId)
        {
            Definition = definition;
            SelectedButtonId = selectedButtonId;
        }

        public UiMenuDefinition Definition { get; }
        public string Name => Definition.Name;

        /// <summary>ButtonId under the cursor (0 for none).</summary>
        public int SelectedButtonId { get; internal set; }

        public UiMenuButton? SelectedButton => Definition.FindButton(SelectedButtonId);
    }

    /// <summary>
    /// A message window over the lobby: its DAT frame (<c>ptc5ok</c> OK, <c>ptc6yesn</c> Yes / No, <c>ptc9dele</c>
    /// Delete / Cancel, or <c>ptc2warn</c> without buttons for a status line) and the client-drawn lines.
    /// </summary>
    public sealed class LobbyPrompt
    {
        internal LobbyPrompt(LobbyMenu menu, IReadOnlyList<string> lines, Action<int>? answered)
        {
            Menu = menu;
            Lines = lines;
            Answered = answered;
        }

        public LobbyMenu Menu { get; }
        public IReadOnlyList<string> Lines { get; }

        /// <summary>Whether the window waits for an answer (a status window has no buttons and no handler).</summary>
        public bool HasButtons => Answered != null;

        internal Action<int>? Answered { get; }
    }

    /// <summary>
    /// The character lobby: the title menu, the character list, character creation and deletion and the prompts
    /// between them, driven by the menu keys and running the lobby requests through an <see cref="ILobbyBackend"/>.
    /// The windows come from the lobby DAT (ROM/119/50, see <see cref="UiResourceLibrary.LoadLobby"/>), the text from
    /// <see cref="LobbyTextTables"/>; the App draws <see cref="Screen"/>, the open windows and the preview character.
    /// <para>
    /// Input arrives on the UI thread and requests finish on the thread pool, so state changes happen under
    /// <see cref="SyncRoot"/>; the renderer reads under the same lock.
    /// </para>
    /// </summary>
    public sealed partial class LobbyController
    {
        // Lobby DAT menu names.
        public const string TitleBackgroundMenu = "loby1win";
        public const string MainMenuName = "loby2win";
        public const string ListBackgroundMenu = "bgnamese";
        public const string CharacterListMenu = "dbnamese";
        public const string HelpBarMenu = "lobyhelp";
        public const string OkPromptMenu = "ptc5ok";
        public const string YesNoPromptMenu = "ptc6yesn";
        public const string DeletePromptMenu = "ptc9dele";
        public const string StatusPromptMenu = "ptc2warn";
        public const string LicencePromptMenu = "ptc8lice";

        /// <summary>loby2win ButtonIds (in screen order 1, 2, 3, 5, 4).</summary>
        public const int SelectButton = 1, CreateButton = 2, DeleteButton = 3, BackButton = 4, ConfigButton = 5;

        /// <summary>Prompt ButtonIds: OK / Yes / Delete are 1, No / Cancel are 2 (ptc9dele's 3 is its warning icon).</summary>
        public const int PromptFirstButton = 1, PromptSecondButton = 2;

        /// <summary>The character list has a button per content id: slot N is ButtonId N (1-8 left column, 9-16 right).</summary>
        public const int ListSlots = 16;

        private readonly ILobbyBackend _backend;
        private readonly UiResourceLibrary? _library;
        private readonly LobbyTextTables? _text;
        private bool _deleting;

        /// <param name="showLicence">Whether the title menu waits behind the licence page (retail; tests skip it).</param>
        public LobbyController(ILobbyBackend backend, UiResourceLibrary? library, LobbyTextTables? text, bool showLicence = true)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _library = library;
            _text = text;
            MainMenu = Menu(MainMenuName, SelectButton);
            if (showLicence) ShowLicence();
            UpdateHelp();
        }

        /// <summary>Whether the licence page is still waiting to be accepted (the title menu stays hidden behind it).</summary>
        public bool IsLicencePending { get; private set; }

        /// <summary>
        /// The page retail shows before the title menu: <c>ptc8lice</c> (Accept / Decline, lobbywin #101 / #102) with
        /// ROM/165/71 row 158 (the notice that the game is played through PlayOnline under its User Agreement and Rules of
        /// Conduct). Accept shows the title menu; Decline leaves the lobby.
        /// </summary>
        private void ShowLicence()
        {
            IsLicencePending = true;
            string text = Status(LobbyTextTables.LicenceNotice);
            var lines = text.Length > 0 ? text.Split('\n') : Array.Empty<string>();
            Prompt = new LobbyPrompt(Menu(LicencePromptMenu, PromptFirstButton), lines, button =>
            {
                Prompt = null;
                IsLicencePending = false;
                if (button != PromptFirstButton) Close(null);
                else UpdateHelp();
            });
        }

        /// <summary>Raised (on a pool thread) when a character was selected: start the game session with the ticket.</summary>
        public event Action<LsbSessionTicket>? CharacterSelected;

        /// <summary>Raised when the player leaves the lobby (Back) or the connection is lost for good.</summary>
        public event Action<string?>? Closed;

        /// <summary>Raised after any change the screen should show.</summary>
        public event Action? Changed;

        public object SyncRoot { get; } = new();

        public UiResourceLibrary? Library => _library;
        public LobbyTextTables? Text => _text;

        public LobbyScreen Screen { get; private set; } = LobbyScreen.MainMenu;

        /// <summary>The title menu window (loby2win).</summary>
        public LobbyMenu? MainMenu { get; }

        /// <summary>The character list window (dbnamese) while the list is up.</summary>
        public LobbyMenu? CharacterList { get; private set; }

        /// <summary>Whether the list is choosing a character to delete rather than to play.</summary>
        public bool IsDeleteList => _deleting;

        /// <summary>The message window on top, if any.</summary>
        public LobbyPrompt? Prompt { get; private set; }

        /// <summary>The help bar's line (lobyhelp).</summary>
        public string HelpText { get; private set; } = string.Empty;

        /// <summary>Whether a lobby request is running (input is ignored until it ends).</summary>
        public bool IsBusy { get => _isBusy; private set => _isBusy = value; }

        private volatile bool _isBusy;

        /// <summary>Every content id of the account in slot order.</summary>
        public IReadOnlyList<LobbyCharacter> Characters => _backend.Characters;

        /// <summary>The character shown in the 3D preview (the list's selection); null when none.</summary>
        public LobbyCharacter? PreviewCharacter
        {
            get
            {
                if (Screen != LobbyScreen.CharacterList || CharacterList == null) return null;
                return CharacterInSlot(CharacterList.SelectedButtonId);
            }
        }

        /// <summary>Bumped on every change, so a renderer can tell when to rebuild what it caches.</summary>
        public int Version { get; private set; }

        /// <summary>The character in a list slot (1-based), or null for a free or absent slot.</summary>
        public LobbyCharacter? CharacterInSlot(int slot)
        {
            foreach (var character in _backend.Characters)
            {
                if (character.Slot == slot) return character.IsEmpty ? null : character;
            }
            return null;
        }

        /// <summary>Feeds one menu key.</summary>
        public void HandleInput(LobbyInput input)
        {
            lock (SyncRoot)
            {
                if (IsBusy || Screen is LobbyScreen.Entering or LobbyScreen.Closed) return;
                if (Prompt != null)
                {
                    HandlePromptInput(input);
                }
                else
                {
                    switch (Screen)
                    {
                        case LobbyScreen.MainMenu: HandleMainMenu(input); break;
                        case LobbyScreen.CharacterList: HandleList(input); break;
                        case LobbyScreen.Creation: HandleCreation(input); break;
                    }
                }
                Touch();
            }
        }

        /// <summary>The window that takes input now: a prompt with buttons, else the screen's menu.</summary>
        public LobbyMenu? ActiveMenu
        {
            get
            {
                lock (SyncRoot)
                {
                    if (Prompt != null) return Prompt.HasButtons ? Prompt.Menu : null;
                    return Screen switch
                    {
                        LobbyScreen.MainMenu => MainMenu,
                        LobbyScreen.CharacterList => CharacterList,
                        LobbyScreen.Creation => CreationMenu,
                        _ => null,
                    };
                }
            }
        }

        /// <summary>Whether the cursor may rest on a button of the active window.</summary>
        private bool IsSelectable(LobbyMenu menu, int buttonId)
        {
            if (Prompt != null && ReferenceEquals(menu, Prompt.Menu)) return buttonId is PromptFirstButton or PromptSecondButton && menu.Definition.FindButton(buttonId) != null;
            if (ReferenceEquals(menu, CharacterList)) return CharacterInSlot(buttonId) != null;
            if (ReferenceEquals(menu, CreationMenu)) return IsCreationChoice(buttonId);
            return menu.Definition.FindButton(buttonId) != null;
        }

        /// <summary>The mouse is over a button of <paramref name="menu"/>: the cursor moves there when the window takes input.</summary>
        public void PointAt(LobbyMenu menu, int buttonId)
        {
            lock (SyncRoot)
            {
                if (IsBusy || !ReferenceEquals(menu, ActiveMenu) || !IsSelectable(menu, buttonId) || menu.SelectedButtonId == buttonId) return;
                menu.SelectedButtonId = buttonId;
                UpdateHelp();
                Touch();
            }
        }

        /// <summary>A click on a button of <paramref name="menu"/>: selects it and confirms, as the Confirm key does.</summary>
        public void Activate(LobbyMenu menu, int buttonId)
        {
            lock (SyncRoot)
            {
                if (IsBusy || !ReferenceEquals(menu, ActiveMenu) || !IsSelectable(menu, buttonId)) return;
                menu.SelectedButtonId = buttonId;
            }
            HandleInput(LobbyInput.Confirm);
        }

        private void HandleMainMenu(LobbyInput input)
        {
            var menu = MainMenu;
            if (menu == null) return;
            switch (input)
            {
                case LobbyInput.Up:
                case LobbyInput.Down:
                case LobbyInput.Left:
                case LobbyInput.Right:
                    Navigate(menu, input, _ => true);
                    UpdateHelp();
                    break;
                case LobbyInput.Confirm:
                    switch (menu.SelectedButtonId)
                    {
                        case SelectButton: OpenList(deleting: false); break;
                        case DeleteButton: OpenList(deleting: true); break;
                        case CreateButton: StartCreation(); break;
                        case ConfigButton: break; // the lobby config window (lobycwin) is not built
                        case BackButton: Close(null); break;
                    }
                    break;
            }
        }

        private void OpenList(bool deleting)
        {
            _deleting = deleting;
            int first = FirstOccupiedSlot();
            if (first == 0) return; // no character to play or delete
            CharacterList = Menu(CharacterListMenu, first);
            Screen = LobbyScreen.CharacterList;
            UpdateHelp();
        }

        private void HandleList(LobbyInput input)
        {
            var list = CharacterList;
            if (list == null) return;
            switch (input)
            {
                case LobbyInput.Up:
                case LobbyInput.Down:
                case LobbyInput.Left:
                case LobbyInput.Right:
                    Navigate(list, input, id => CharacterInSlot(id) != null);
                    break;
                case LobbyInput.Cancel:
                    BackToMainMenu();
                    break;
                case LobbyInput.Confirm:
                    if (CharacterInSlot(list.SelectedButtonId) is not { } character) break;
                    if (_deleting) ConfirmDelete(character);
                    else if (character.RenameRequired) StartRename(character);
                    else Select(character);
                    break;
            }
        }

        private void BackToMainMenu()
        {
            CharacterList = null;
            Screen = LobbyScreen.MainMenu;
            UpdateHelp();
        }

        /// <summary>Character deletion (#34) asks first; until it is built the list only plays characters.</summary>
        private void ConfirmDelete(LobbyCharacter character)
        {
        }

        private void Select(LobbyCharacter character)
        {
            RunRequest(Status(LobbyTextTables.NotifyingLobbyOfChoice), async ct =>
            {
                var ticket = await _backend.SelectCharacterAsync(character, ct).ConfigureAwait(false);
                Enter(ticket);
            });
        }

        /// <summary>A character was selected: the lobby is done and the launcher starts the game with the ticket.</summary>
        private void Enter(LsbSessionTicket ticket)
        {
            lock (SyncRoot)
            {
                Screen = LobbyScreen.Entering;
                Prompt = null;
                CreationMenu = null;
                Touch();
            }
            CharacterSelected?.Invoke(ticket);
        }

        /// <summary>Replaces the status window's line while a request runs (a request with several steps).</summary>
        private void SetStatus(string status)
        {
            lock (SyncRoot)
            {
                if (Prompt is { HasButtons: false } current)
                {
                    Prompt = new LobbyPrompt(current.Menu, status.Length > 0 ? new[] { status } : Array.Empty<string>(), null);
                    Touch();
                }
            }
        }

        /// <summary>
        /// Runs a lobby request with a status window up. A refusal shows the error (its DAT text and code) with an OK
        /// button; a lost connection closes the lobby after the message.
        /// </summary>
        private void RunRequest(string status, Func<CancellationToken, Task> request, Action? onError = null)
        {
            IsBusy = true;
            Prompt = new LobbyPrompt(Menu(StatusPromptMenu, 0), status.Length > 0 ? new[] { status } : Array.Empty<string>(), null);
            Touch();
            _ = Task.Run(async () =>
            {
                try
                {
                    await request(CancellationToken.None).ConfigureAwait(false);
                    lock (SyncRoot)
                    {
                        if (Prompt is { HasButtons: false }) Prompt = null;
                        IsBusy = false; // last: a reader that sees the request done sees its outcome
                        Touch();
                    }
                }
                catch (Exception ex)
                {
                    GordianLog.Warning("LOBBY", $"Lobby request failed: {ex.Message}");
                    lock (SyncRoot)
                    {
                        Prompt = null;
                        ShowError(ex, onError);
                        IsBusy = false;
                        Touch();
                    }
                }
            });
        }

        private void ShowError(Exception ex, Action? onError = null)
        {
            int code = ex is LobbyRequestException { IsServerError: true } lobby ? lobby.ErrorCode : 0;
            IReadOnlyList<string> lines = code != 0 && _text != null
                ? _text.ErrorLines(code)
                : code != 0 ? new[] { $"Error code: FFXI-{code + 3000:D4}" }
                : _text?.ErrorLines(LobbyErrorCodeLostConnection) ?? new[] { ex.Message };
            bool connected = _backend.IsConnected;
            ShowMessage(lines, () =>
            {
                if (!connected) Close(ex.Message);
                else onError?.Invoke();
            });
        }

        /// <summary>The code whose message is "Connection to the FINAL FANTASY XI server lost." (3115).</summary>
        private const int LobbyErrorCodeLostConnection = 115;

        /// <summary>An OK window with <paramref name="lines"/>; <paramref name="dismissed"/> runs after OK.</summary>
        private void ShowMessage(IReadOnlyList<string> lines, Action? dismissed)
        {
            Prompt = new LobbyPrompt(Menu(OkPromptMenu, PromptFirstButton), lines, _ =>
            {
                Prompt = null;
                dismissed?.Invoke();
            });
        }

        /// <summary>A two-button window (Yes / No, or Delete / Cancel); <paramref name="answered"/> gets true for the first.</summary>
        private void ShowChoice(string menuName, IReadOnlyList<string> lines, int defaultButton, Action<bool> answered)
        {
            Prompt = new LobbyPrompt(Menu(menuName, defaultButton), lines, button =>
            {
                Prompt = null;
                answered(button == PromptFirstButton);
            });
        }

        private void HandlePromptInput(LobbyInput input)
        {
            var prompt = Prompt!;
            if (!prompt.HasButtons) return;
            switch (input)
            {
                case LobbyInput.Left:
                case LobbyInput.Right:
                case LobbyInput.Up:
                case LobbyInput.Down:
                    Navigate(prompt.Menu, input, id => id is PromptFirstButton or PromptSecondButton);
                    break;
                case LobbyInput.Confirm:
                    prompt.Answered!(prompt.Menu.SelectedButtonId);
                    break;
                case LobbyInput.Cancel:
                    // Cancel answers the second button (No / Cancel); a lone OK is dismissed.
                    prompt.Answered!(prompt.Menu.Definition.FindButton(PromptSecondButton) != null ? PromptSecondButton : PromptFirstButton);
                    break;
            }
        }

        private void Close(string? reason)
        {
            Screen = LobbyScreen.Closed;
            Prompt = null;
            CharacterList = null;
            Touch();
            Closed?.Invoke(reason);
        }

        private int FirstOccupiedSlot()
        {
            foreach (var character in _backend.Characters)
            {
                if (!character.IsEmpty && character.Slot is >= 1 and <= ListSlots) return character.Slot;
            }
            return 0;
        }

        /// <summary>
        /// Moves a menu's cursor along its buttons' authored links (up, down, left, right), skipping buttons
        /// <paramref name="selectable"/> rejects; stays put when no selectable button is reached.
        /// </summary>
        internal static void Navigate(LobbyMenu menu, LobbyInput direction, Func<int, bool> selectable)
        {
            var definition = menu.Definition;
            int current = menu.SelectedButtonId;
            for (int steps = 0; steps < definition.Buttons.Count + 1; steps++)
            {
                var button = definition.FindButton(current);
                if (button == null) return;
                int next = direction switch
                {
                    LobbyInput.Up => button.NavUp,
                    LobbyInput.Down => button.NavDown,
                    LobbyInput.Left => button.NavLeft,
                    LobbyInput.Right => button.NavRight,
                    _ => -1,
                };
                if (next <= 0 || next == current) return;
                if (selectable(next))
                {
                    menu.SelectedButtonId = next;
                    return;
                }
                current = next;
                if (current == menu.SelectedButtonId) return;
            }
        }

        private LobbyMenu? MenuOrNull(string name, int selected) =>
            _library != null && _library.TryGetMenu(name, out var definition) ? new LobbyMenu(definition, selected) : null;

        /// <summary>A lobby menu by name; a bare definition when the DAT is missing (tests, no game data).</summary>
        private LobbyMenu Menu(string name, int selected) =>
            MenuOrNull(name, selected) ?? new LobbyMenu(FallbackDefinition(name), selected);

        /// <summary>
        /// Stand-in layouts for the menus the controller drives, used only when the lobby DAT is missing: the buttons and
        /// links the controller needs, at the DAT's positions.
        /// </summary>
        private static UiMenuDefinition FallbackDefinition(string name)
        {
            var buttons = new List<UiMenuButton>();
            switch (name)
            {
                case MainMenuName:
                    int[] order = { SelectButton, CreateButton, DeleteButton, ConfigButton, BackButton };
                    for (int i = 0; i < order.Length; i++)
                    {
                        buttons.Add(new UiMenuButton
                        {
                            ButtonId = (short)order[i], X = 354, Y = (short)(443 + 28 * i), Width = 143, Height = 24,
                            NavUp = (sbyte)order[(i + order.Length - 1) % order.Length], NavDown = (sbyte)order[(i + 1) % order.Length],
                            NavLeft = (sbyte)order[i], NavRight = (sbyte)order[i],
                        });
                    }
                    break;
                case CharacterListMenu:
                    for (int slot = 1; slot <= ListSlots; slot++)
                    {
                        int column = (slot - 1) / 8, row = (slot - 1) % 8;
                        buttons.Add(new UiMenuButton
                        {
                            ButtonId = (short)slot, X = (short)(304 + 176 * column), Y = (short)(242 + 18 * row), Width = 128, Height = 16,
                            NavUp = (sbyte)(row == 0 ? (column == 0 ? 16 : 8) : slot - 1),
                            NavDown = (sbyte)(row == 7 ? (column == 0 ? 9 : 1) : slot + 1),
                            NavLeft = (sbyte)(column == 0 ? slot : slot - 8),
                            NavRight = (sbyte)(column == 0 ? slot + 8 : slot),
                        });
                    }
                    break;
                case RaceMenu:
                case FaceMenu:
                case HairMenu:
                case SizeMenu:
                case JobMenu:
                case NationMenu:
                case WorldList:
                    int rows = name switch { RaceMenu or FaceMenu => 8, HairMenu => 2, SizeMenu or NationMenu => 3, JobMenu => 6, _ => 14 };
                    for (int row = 1; row <= rows; row++)
                    {
                        buttons.Add(new UiMenuButton
                        {
                            ButtonId = (short)row, X = 16, Y = (short)(6 + 16 * (row - 1)), Width = 88, Height = 16,
                            NavUp = (sbyte)(row == 1 ? rows : row - 1), NavDown = (sbyte)(row == rows ? 1 : row + 1), NavLeft = (sbyte)row, NavRight = (sbyte)row,
                        });
                    }
                    break;
                case OkPromptMenu:
                    buttons.Add(new UiMenuButton { ButtonId = 1, X = 104, Y = 86, Width = 118, Height = 22, NavUp = 1, NavDown = 1, NavLeft = 1, NavRight = 1 });
                    break;
                case YesNoPromptMenu:
                case DeletePromptMenu:
                case LicencePromptMenu:
                    buttons.Add(new UiMenuButton { ButtonId = 1, X = -24, Y = 118, Width = 118, Height = 22, NavUp = 1, NavDown = 1, NavLeft = 2, NavRight = 2 });
                    buttons.Add(new UiMenuButton { ButtonId = 2, X = 104, Y = 118, Width = 118, Height = 22, NavUp = 2, NavDown = 2, NavLeft = 1, NavRight = 1 });
                    break;
            }
            return new UiMenuDefinition { Name = name, Buttons = buttons };
        }

        private string Status(int index) => _text?.Status(index) ?? string.Empty;

        private void UpdateHelp()
        {
            int line = Screen switch
            {
                LobbyScreen.MainMenu => MainMenu?.SelectedButtonId switch
                {
                    SelectButton => LobbyTextTables.HelpSelectCharacterMenu,
                    CreateButton => LobbyTextTables.HelpCreateCharacterMenu,
                    DeleteButton => LobbyTextTables.HelpDeleteCharacterMenu,
                    BackButton => LobbyTextTables.HelpBack,
                    _ => -1,
                },
                LobbyScreen.CharacterList => _deleting ? LobbyTextTables.HelpSelectCharacterToDelete : LobbyTextTables.HelpSelectCharacterToPlay,
                LobbyScreen.Creation => CreationHelpLine(),
                _ => -1,
            };
            HelpText = line >= 0 && !IsLicencePending ? Status(line) : string.Empty;
        }

        private void Touch()
        {
            Version++;
            Changed?.Invoke();
        }
    }
}
