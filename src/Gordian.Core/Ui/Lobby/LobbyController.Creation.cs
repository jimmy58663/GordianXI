// src/Gordian.Core/Ui/Lobby/LobbyController.Creation.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Gordian.Core.Network.LandSandBoat;
using Gordian.Core.Resources.Tables;

namespace Gordian.Core.Ui.Lobby
{
    /// <summary>The steps of character creation, in the order the lobby walks them.</summary>
    public enum LobbyCreationStep
    {
        Race,
        Face,
        Hair,
        Size,
        Job,
        Nation,
        Name,
        World,
    }

    /// <summary>
    /// Character creation (#33) and the rename flow: race (<c>chmkrace</c>), face (<c>chmkface</c>), hair (<c>chmkhair</c>),
    /// size (<c>chmksize</c>), job (<c>chmkjobs</c>), nation (<c>chmktown</c>), the name (<c>chmkname</c>) and the world
    /// (<c>chmkserv</c> / <c>worldsel</c>), then "Register "Name" and begin play?" and the lobby's 0x22 / 0x21 requests,
    /// after which the new character is selected and the game starts. The preview shows the choice under the cursor.
    /// </summary>
    public sealed partial class LobbyController
    {
        public const string RaceMenu = "chmkrace", FaceMenu = "chmkface", HairMenu = "chmkhair", SizeMenu = "chmksize";
        public const string JobMenu = "chmkjobs", NationMenu = "chmktown", NameField = "chmkname", WorldField = "chmkserv", WorldList = "worldsel";

        /// <summary>Character names: letters only, 3 to 15 of them (LandSandBoat <c>validateCharacterName</c>; the 16-byte field keeps a NUL).</summary>
        public const int MinNameLength = 3, MaxNameLength = 15;

        /// <summary>The client error for a name under three letters (shown as FFXI-3110, ROM/165/70 row 7).</summary>
        public const int NameTooShortError = 110;

        private LobbyCharacter? _freeSlot;
        private LobbyCharacter? _renaming;
        private byte _race = 1, _face, _hair, _size = 1, _job = 1, _nation;
        private int _worldIndex;
        private int _caret;

        public LobbyCreationStep CreationStep { get; private set; }

        /// <summary>The step's choice window (race ... nation, or the world list); null on the name step.</summary>
        public LobbyMenu? CreationMenu { get; private set; }

        /// <summary>The name typed so far (first letter upper case, the rest lower case, as retail writes names).</summary>
        public string NameText { get; private set; } = string.Empty;

        /// <summary>The world list (S2C 0x23) once read, for the world step.</summary>
        public IReadOnlyList<LobbyWorld> Worlds { get; private set; } = Array.Empty<LobbyWorld>();

        /// <summary>The world chosen on the world step, or null before it.</summary>
        public LobbyWorld? ChosenWorld { get; private set; }

        /// <summary>The character being renamed (the server's rename flag), shown in the preview; null otherwise.</summary>
        public LobbyCharacter? RenamingCharacter => _renaming;

        public bool IsRenaming => _renaming != null;

        /// <summary>Whether the nation has been chosen (the flag shows from then on).</summary>
        public bool HasChosenNation => Screen == LobbyScreen.Creation && CreationStep > LobbyCreationStep.Nation;

        /// <summary>The look being created, with the choice under the cursor applied (the live preview).</summary>
        public LobbyCharacterCreation PreviewCreation
        {
            get
            {
                lock (SyncRoot)
                {
                    byte race = _race, face = _face, hair = _hair, size = _size, job = _job, nation = _nation;
                    if (CreationMenu is { } menu && CreationStep <= LobbyCreationStep.Nation && IsCreationChoice(menu.SelectedButtonId))
                    {
                        byte choice = (byte)menu.SelectedButtonId;
                        switch (CreationStep)
                        {
                            case LobbyCreationStep.Race: race = choice; break;
                            case LobbyCreationStep.Face: face = (byte)(choice - 1); break;
                            case LobbyCreationStep.Hair: hair = (byte)(choice - 1); break;
                            case LobbyCreationStep.Size: size = (byte)(choice - 1); break;
                            case LobbyCreationStep.Job: job = choice; break;
                            case LobbyCreationStep.Nation: nation = (byte)(choice - 1); break;
                        }
                    }
                    return new LobbyCharacterCreation(NameText, race, face, hair, job, size, nation);
                }
            }
        }

        /// <summary>The caret's position in <see cref="NameText"/> (for drawing).</summary>
        public int NameCaret => _caret;

        private static int ChoiceCount(LobbyCreationStep step) => step switch
        {
            LobbyCreationStep.Race => 8,
            LobbyCreationStep.Face => 8,
            LobbyCreationStep.Hair => 2,
            LobbyCreationStep.Size => 3,
            LobbyCreationStep.Job => 6,
            LobbyCreationStep.Nation => 3,
            _ => 0,
        };

        /// <summary>Whether a button of the current step's window is a choice (not a page arrow, not past the world list).</summary>
        private bool IsCreationChoice(int buttonId) =>
            CreationStep == LobbyCreationStep.World ? buttonId >= 1 && buttonId <= Worlds.Count : buttonId >= 1 && buttonId <= ChoiceCount(CreationStep);

        private static string MenuOf(LobbyCreationStep step) => step switch
        {
            LobbyCreationStep.Race => RaceMenu,
            LobbyCreationStep.Face => FaceMenu,
            LobbyCreationStep.Hair => HairMenu,
            LobbyCreationStep.Size => SizeMenu,
            LobbyCreationStep.Job => JobMenu,
            LobbyCreationStep.Nation => NationMenu,
            LobbyCreationStep.World => WorldList,
            _ => string.Empty,
        };

        /// <summary>The button standing for the value chosen so far on a step.</summary>
        private int ButtonOfValue(LobbyCreationStep step) => step switch
        {
            LobbyCreationStep.Race => _race,
            LobbyCreationStep.Face => _face + 1,
            LobbyCreationStep.Hair => _hair + 1,
            LobbyCreationStep.Size => _size + 1,
            LobbyCreationStep.Job => _job,
            LobbyCreationStep.Nation => _nation + 1,
            LobbyCreationStep.World => _worldIndex + 1,
            _ => 0,
        };

        private void StartCreation()
        {
            _freeSlot = null;
            foreach (var character in _backend.Characters)
            {
                if (character.IsEmpty && character.Status == LobbyCharacter.StatusAvailable && character.Slot is >= 1 and <= ListSlots)
                {
                    _freeSlot = character;
                    break;
                }
            }
            if (_freeSlot == null)
            {
                ShowMessage(SplitLines(Status(LobbyTextTables.CannotCreateMoreCharacters)), null);
                return;
            }
            _renaming = null;
            (_race, _face, _hair, _size, _job, _nation, _worldIndex) = (1, 0, 0, 1, 1, 0, 0);
            NameText = string.Empty;
            _caret = 0;
            ChosenWorld = null;
            Screen = LobbyScreen.Creation;
            GoToStep(LobbyCreationStep.Race);
        }

        private void StartRename(LobbyCharacter character)
        {
            ShowMessage(SplitLines(Status(LobbyTextTables.NeedRename)), () =>
            {
                _renaming = character;
                _freeSlot = null;
                NameText = string.Empty;
                _caret = 0;
                Screen = LobbyScreen.Creation;
                GoToStep(LobbyCreationStep.Name);
            });
        }

        private void GoToStep(LobbyCreationStep step)
        {
            CreationStep = step;
            CreationMenu = step is LobbyCreationStep.Name ? null : Menu(MenuOf(step), ButtonOfValue(step));
            UpdateHelp();
        }

        private int CreationHelpLine() => CreationStep switch
        {
            LobbyCreationStep.Race => LobbyTextTables.HelpChooseRace,
            LobbyCreationStep.Face => LobbyTextTables.HelpChooseFace,
            LobbyCreationStep.Hair => LobbyTextTables.HelpChooseHair,
            LobbyCreationStep.Size => LobbyTextTables.HelpChooseSize,
            LobbyCreationStep.Job => LobbyTextTables.HelpChooseJob,
            LobbyCreationStep.Nation => LobbyTextTables.HelpChooseNation,
            LobbyCreationStep.World => LobbyTextTables.HelpWorldPass,
            _ => -1,
        };

        private void HandleCreation(LobbyInput input)
        {
            switch (CreationStep)
            {
                case LobbyCreationStep.Name:
                    HandleNameInput(input);
                    return;
                case LobbyCreationStep.World:
                    HandleWorldInput(input);
                    return;
            }

            var menu = CreationMenu;
            if (menu == null) return;
            switch (input)
            {
                case LobbyInput.Up:
                case LobbyInput.Down:
                    Navigate(menu, input, IsCreationChoice);
                    break;
                case LobbyInput.Left:
                    // The red page arrows: back a step (race .. job; the nation window has none).
                    if (CreationStep is > LobbyCreationStep.Race and <= LobbyCreationStep.Job) GoToStep(CreationStep - 1);
                    break;
                case LobbyInput.Right:
                    if (CreationStep <= LobbyCreationStep.Job) CommitChoice();
                    break;
                case LobbyInput.Confirm:
                    CommitChoice();
                    break;
                case LobbyInput.Cancel:
                    if (CreationStep == LobbyCreationStep.Race) LeaveCreation();
                    else GoToStep(CreationStep - 1);
                    break;
            }
        }

        private void CommitChoice()
        {
            var menu = CreationMenu;
            if (menu == null || !IsCreationChoice(menu.SelectedButtonId)) return;
            byte choice = (byte)menu.SelectedButtonId;
            switch (CreationStep)
            {
                case LobbyCreationStep.Race: _race = choice; break;
                case LobbyCreationStep.Face: _face = (byte)(choice - 1); break;
                case LobbyCreationStep.Hair: _hair = (byte)(choice - 1); break;
                case LobbyCreationStep.Size: _size = (byte)(choice - 1); break;
                case LobbyCreationStep.Job: _job = choice; break;
                case LobbyCreationStep.Nation: _nation = (byte)(choice - 1); break;
            }
            GoToStep(CreationStep + 1);
        }

        private void LeaveCreation()
        {
            CreationMenu = null;
            if (_renaming != null)
            {
                _renaming = null;
                Screen = LobbyScreen.CharacterList;
            }
            else
            {
                Screen = LobbyScreen.MainMenu;
            }
            UpdateHelp();
        }

        /// <summary>Typed text for the name step: letters only, up to 15, written with a capital first letter.</summary>
        public void HandleText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            lock (SyncRoot)
            {
                if (IsBusy || Prompt != null || Screen != LobbyScreen.Creation || CreationStep != LobbyCreationStep.Name) return;
                var name = new System.Text.StringBuilder(NameText);
                foreach (char c in text)
                {
                    if (!char.IsAsciiLetter(c) || name.Length >= MaxNameLength) continue;
                    name.Insert(_caret++, c);
                }
                NameText = NormalizeName(name.ToString());
                Touch();
            }
        }

        /// <summary>Retail's name case: the first letter upper case, the others lower case ("knot" and "KNOT" both become "Knot").</summary>
        public static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            return char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant();
        }

        private void HandleNameInput(LobbyInput input)
        {
            switch (input)
            {
                case LobbyInput.Backspace:
                    if (_caret > 0)
                    {
                        NameText = NormalizeName(NameText.Remove(_caret - 1, 1));
                        _caret--;
                    }
                    break;
                case LobbyInput.Left:
                    if (_caret > 0) _caret--;
                    break;
                case LobbyInput.Right:
                    if (_caret < NameText.Length) _caret++;
                    break;
                case LobbyInput.Cancel:
                    if (_renaming != null) LeaveCreation();
                    else GoToStep(LobbyCreationStep.Nation);
                    break;
                case LobbyInput.Confirm:
                    ConfirmName();
                    break;
            }
        }

        private void ConfirmName()
        {
            string name = NormalizeName(NameText);
            if (name.Length < MinNameLength)
            {
                var lines = _text?.ErrorLines(NameTooShortError) ?? new[] { $"Error code: FFXI-{NameTooShortError + 3000:D4}" };
                ShowMessage(lines, null);
                return;
            }
            NameText = name;
            if (_renaming is { } character)
            {
                RunRequest(Status(LobbyTextTables.CheckingName), async ct =>
                {
                    var ticket = await _backend.RenameAndSelectAsync(character, name, ct).ConfigureAwait(false);
                    Enter(ticket);
                });
                return;
            }
            RunRequest(Status(LobbyTextTables.AcquiringServerData), async ct =>
            {
                var worlds = await _backend.GetWorldsAsync(ct).ConfigureAwait(false);
                lock (SyncRoot)
                {
                    Worlds = worlds.Take(14).ToList();
                    if (_worldIndex >= Worlds.Count) _worldIndex = 0;
                    GoToStep(LobbyCreationStep.World);
                    Touch();
                }
            });
        }

        private void HandleWorldInput(LobbyInput input)
        {
            var menu = CreationMenu;
            if (menu == null) return;
            switch (input)
            {
                case LobbyInput.Up:
                case LobbyInput.Down:
                    Navigate(menu, input, IsCreationChoice);
                    break;
                case LobbyInput.Cancel:
                    GoToStep(LobbyCreationStep.Name);
                    break;
                case LobbyInput.Confirm:
                    if (!IsCreationChoice(menu.SelectedButtonId)) break;
                    _worldIndex = menu.SelectedButtonId - 1;
                    ChosenWorld = Worlds[_worldIndex];
                    CheckNameAndAsk();
                    break;
            }
        }

        /// <summary>The name and world check (0x22), then "Register "Name" and begin play?".</summary>
        private void CheckNameAndAsk()
        {
            var slot = _freeSlot!;
            string name = NameText, world = ChosenWorld?.Name ?? string.Empty;
            RunRequest(Status(LobbyTextTables.CheckingNameAndWorldPass), async ct =>
            {
                await _backend.CheckNameAsync(slot, name, world, ct).ConfigureAwait(false);
                lock (SyncRoot)
                {
                    IsBusy = false;
                    var lines = new List<string>();
                    string first = Status(LobbyTextTables.RegisterAndBeginLine1);
                    lines.Add(first.Length > 0 ? first.Replace("%s", name, StringComparison.Ordinal) : $"Register \"{name}\"");
                    string second = Status(LobbyTextTables.RegisterAndBeginLine2);
                    if (second.Length > 0) lines.Add(second);
                    ShowChoice(YesNoPromptMenu, lines, PromptFirstButton, yes =>
                    {
                        if (yes) Create();
                    });
                    Touch();
                }
            }, onError: () => GoToStep(LobbyCreationStep.Name));
        }

        private void Create()
        {
            var slot = _freeSlot!;
            var creation = PreviewCreation with { Name = NameText };
            RunRequest(Status(LobbyTextTables.RegisteringCharacter), async ct =>
            {
                await _backend.CreateCharacterAsync(slot, creation, ct).ConfigureAwait(false);
                var created = _backend.Characters.FirstOrDefault(c => !c.IsEmpty && string.Equals(c.Name, creation.Name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new LobbyRequestException("character creation", 0, $"The new character '{creation.Name}' is not in the refreshed list.");
                SetStatus(Status(LobbyTextTables.NotifyingLobbyOfChoice));
                var ticket = await _backend.SelectCharacterAsync(created, ct).ConfigureAwait(false);
                Enter(ticket);
            });
        }

        private static IReadOnlyList<string> SplitLines(string text) =>
            string.IsNullOrEmpty(text) ? Array.Empty<string>() : text.Split('\n');
    }
}
