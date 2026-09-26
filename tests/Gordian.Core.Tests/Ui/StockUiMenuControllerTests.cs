// tests/Gordian.Core.Tests/Ui/StockUiMenuControllerTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Input;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    public class StockUiMenuControllerTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        /// <summary>A vertical list menu whose buttons link up/down in a ring and left/right to themselves (as retail lists do).</summary>
        private static UiMenuDefinition List(string name, short x, short y, UiAnchor anchor, params int[] buttonIds)
        {
            var buttons = new List<UiMenuButton>();
            for (int i = 0; i < buttonIds.Length; i++)
            {
                int id = buttonIds[i];
                int up = buttonIds[(i - 1 + buttonIds.Length) % buttonIds.Length];
                int down = buttonIds[(i + 1) % buttonIds.Length];
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)id, X = 16, Y = (short)(6 + 16 * i), Width = 88, Height = 16,
                    NavUp = (sbyte)up, NavDown = (sbyte)down, NavLeft = (sbyte)id, NavRight = (sbyte)id,
                });
            }
            // A title band: no navigation links, so never selectable.
            buttons.Add(new UiMenuButton { ButtonId = 99, Width = 73, Height = 9, NavUp = -1, NavDown = -1, NavLeft = -1, NavRight = -1 });
            return new UiMenuDefinition
            {
                Name = name,
                Frame = new UiMenuFrame { X = x, Y = y, Width = 112, Height = (short)(10 + 16 * buttonIds.Length), Anchor = anchor },
                Buttons = buttons,
            };
        }

        private static UiResourceLibrary SyntheticLibrary() => UiResourceLibrary.FromDefinitions(new[]
        {
            List(StockUiMenuEntries.MainMenu, 384, 48, UiAnchor.TopRight, 1, 2, 3),
            List(StockUiMenuEntries.MainMenuPage2, 384, 48, UiAnchor.TopRight, 1, 2, 7, 12),
            List(StockUiMenuEntries.ConfigMenu, 384, 48, UiAnchor.TopRight, 1, 4),
            List(StockUiMenuEntries.WindowsMenu, 384, 48, UiAnchor.TopRight, 1, 2, 3),
            List(StockUiMenuEntries.WindowSettingsPage, 16, 48, UiAnchor.TopLeft, 7, 8, 9),
            List(StockUiMenuEntries.YesNoMenu, 16, 256, UiAnchor.BottomLeft, 1, 2),
            List(StockUiMenuEntries.MessageYesNoMenu, 130, 208, UiAnchor.BottomLeft, 1, 2),
        });

        private static StockUiMenuController Controller() => new() { Library = SyntheticLibrary() };

        [Fact]
        public void OpenMainMenu_SelectsFirstButton_AndCursorFollowsNavLinks()
        {
            var menus = Controller();
            Assert.False(menus.IsOpen);
            Assert.True(menus.OpenMainMenu());
            Assert.Equal(StockUiMenuEntries.MainMenu, menus.Top!.Name);
            Assert.Equal(1, menus.Top.SelectedButtonId);

            menus.Move(InputAction.MenuDown);
            Assert.Equal(2, menus.Top.SelectedButtonId);
            menus.Move(InputAction.MenuUp);
            menus.Move(InputAction.MenuUp);
            Assert.Equal(3, menus.Top.SelectedButtonId); // the list wraps through its ring

            menus.CloseTop();
            Assert.False(menus.IsOpen);
        }

        [Fact]
        public void LeftRight_FlipMainMenuPages_KeepingTheRow_AndRememberThePage()
        {
            var menus = Controller();
            menus.OpenMainMenu();
            menus.Move(InputAction.MenuDown); // row 2
            menus.Move(InputAction.MenuRight);
            Assert.Equal(StockUiMenuEntries.MainMenuPage2, menus.Top!.Name);
            Assert.Equal(2, menus.Top.SelectedButtonId);
            Assert.Single(menus.OpenMenus);

            menus.Move(InputAction.MenuLeft);
            Assert.Equal(StockUiMenuEntries.MainMenu, menus.Top.Name);
            menus.Move(InputAction.MenuRight);
            menus.CloseAll();

            // Reopening returns to the page (and row) the player left.
            menus.OpenMainMenu();
            Assert.Equal(StockUiMenuEntries.MainMenuPage2, menus.Top!.Name);
            Assert.Equal(2, menus.Top.SelectedButtonId);
        }

        [Fact]
        public void Confirm_OpensSubMenus_CancelUnwindsThem()
        {
            var menus = Controller();
            menus.OpenMainMenu();
            menus.Move(InputAction.MenuRight);     // page 2
            menus.Move(InputAction.MenuUp);        // wraps to the last button: 12 = Log Out
            menus.Move(InputAction.MenuUp);        // 7 = Config
            Assert.Equal(7, menus.Top!.SelectedButtonId);
            menus.Activate();
            Assert.Equal(StockUiMenuEntries.ConfigMenu, menus.Top.Name);
            Assert.Equal(2, menus.OpenMenus.Count);
            Assert.True(menus.Top.OverlapsAuthored(menus.OpenMenus[0])); // same corner: the HUD hides the parent

            menus.Move(InputAction.MenuDown);      // 4 = Windows
            int selectedSkin = 3;
            menus.CurrentWindowSkin = () => selectedSkin;
            menus.WindowSkinSelected = skin => selectedSkin = skin;
            menus.Activate();
            Assert.Equal(StockUiMenuEntries.WindowsMenu, menus.Top.Name); // Shared / Window 1 / Window 2
            Assert.True(menus.Top.OverlapsAuthored(menus.OpenMenus[1])); // replaces the config list in its corner
            menus.Activate();                      // 1 = Shared
            Assert.Equal(StockUiMenuEntries.WindowSettingsPage, menus.Top.Name);
            Assert.True(menus.Top.IsMarked(StockUiMenuEntries.WindowSkinFirstButton + 2)); // skin 3's dot
            Assert.False(menus.Top.OverlapsAuthored(menus.OpenMenus[2])); // other side of the screen: both drawn

            menus.Move(InputAction.MenuDown);      // dot 8 = skin 2
            menus.Activate();
            Assert.Equal(2, selectedSkin);
            Assert.True(menus.Top.IsMarked(StockUiMenuEntries.WindowSkinFirstButton + 1));
            Assert.False(menus.Top.IsMarked(StockUiMenuEntries.WindowSkinFirstButton + 2));

            menus.CloseTop();
            Assert.Equal(StockUiMenuEntries.WindowsMenu, menus.Top.Name);
            menus.CloseTop();
            Assert.Equal(StockUiMenuEntries.ConfigMenu, menus.Top.Name);
            menus.CloseTop();
            Assert.Equal(StockUiMenuEntries.MainMenuPage2, menus.Top.Name);
            menus.CloseTop();
            Assert.False(menus.IsOpen);
        }

        [Fact]
        public void EntriesWithoutAWindow_PostANotice()
        {
            var menus = Controller();
            var notices = new List<string>();
            menus.NoticePosted += notices.Add;
            menus.OpenMainMenu();
            menus.Activate(); // Status
            Assert.Single(notices);
            Assert.StartsWith("Status:", notices[0]);
            Assert.True(menus.IsOpen);
        }

        [Fact]
        public async Task PromptYesNo_CompletesWithTheChosenButton_AndCancelMeansNo()
        {
            var menus = Controller();
            var yes = menus.PromptYesNoAsync("Log out?", defaultYes: false);
            Assert.True(menus.Top!.IsPrompt);
            Assert.Equal(StockUiMenuEntries.MessageYesNoMenu, menus.Top.Name);
            Assert.Equal("Log out?", menus.Top.Message);
            Assert.Equal(2, menus.Top.SelectedButtonId);
            menus.Move(InputAction.MenuUp);
            menus.Activate();
            Assert.True(await yes);
            Assert.False(menus.IsOpen);

            var cancelled = menus.PromptYesNoAsync();
            Assert.Equal(StockUiMenuEntries.YesNoMenu, menus.Top!.Name);
            menus.CloseTop();
            Assert.False(await cancelled);

            var unavailable = new StockUiMenuController().PromptYesNoAsync("?");
            Assert.False(await unavailable);
        }

        [Fact]
        public async Task LogOut_AsksFirst_ThenRequestsLogout()
        {
            var menus = Controller();
            var requests = new List<bool>();
            menus.LogoutRequested = shutdown => { requests.Add(shutdown); return Task.CompletedTask; };
            menus.OpenMainMenu();
            menus.Move(InputAction.MenuRight);
            menus.Move(InputAction.MenuUp);    // 12 = Log Out
            menus.Activate();
            Assert.True(menus.Top!.IsPrompt);
            Assert.Equal("Log out?", menus.Top.Message);
            Assert.Equal(2, menus.Top.SelectedButtonId); // defaults to No
            Assert.Equal(2, menus.OpenMenus.Count);

            // Cancelling keeps the player logged in and back on the menu.
            menus.CloseTop();
            await Task.Delay(50);
            Assert.Empty(requests);
            Assert.Equal(StockUiMenuEntries.MainMenuPage2, menus.Top!.Name);

            menus.Activate();
            menus.Move(InputAction.MenuUp);    // Yes
            menus.Activate();
            for (int i = 0; i < 50 && requests.Count == 0; i++) await Task.Delay(20);
            Assert.Equal(new[] { false }, requests);
            Assert.False(menus.IsOpen);
        }

        [Fact]
        public void HeldDirection_RepeatsAfterTheDelay()
        {
            var menus = Controller();
            var profile = InputProfile.CreateCompact();
            var input = new InputState();
            menus.OpenMainMenu();

            var dt = TimeSpan.FromMilliseconds(20);
            input.SetKeyDown(GordianKey.Down);
            int Tick()
            {
                input.MenuContext = menus.IsOpen;
                input.Update(profile, dt);
                menus.ProcessInput(input, dt);
                return menus.Top!.SelectedButtonId;
            }

            Assert.Equal(2, Tick()); // the press moves at once
            for (int i = 0; i < 18; i++) Assert.Equal(2, Tick()); // 0.36 s held: no repeat yet
            int moved = 0;
            for (int i = 0; i < 10; i++)
            {
                if (Tick() != 2) { moved++; break; }
            }
            Assert.Equal(1, moved); // the first repeat lands around 0.4 s
            Assert.Equal(3, menus.Top!.SelectedButtonId);

            input.SetKeyUp(GordianKey.Down);
            Tick();
            input.SetKeyDown(GordianKey.Down);
            Assert.Equal(1, Tick()); // a fresh press moves at once again
        }

        [Fact]
        public void OpenMenuAction_OpensTheMainMenu_ThenTurnsPages_AndCancelClosesIt()
        {
            var menus = Controller();
            var profile = InputProfile.CreateCompact();
            var input = new InputState();
            var dt = TimeSpan.FromMilliseconds(16);

            input.SetKeyDown(GordianKey.OemMinus);
            input.Update(profile, dt);
            Assert.False(menus.ProcessInput(input, dt)); // nothing was open when the tick started
            Assert.True(menus.IsOpen);
            input.SetKeyUp(GordianKey.OemMinus);
            input.Update(profile, dt);

            // The menu button pressed again turns the page rather than closing (retail; confirmed in-game).
            input.SetKeyDown(GordianKey.OemMinus);
            input.MenuContext = true;
            input.Update(profile, dt);
            Assert.True(menus.ProcessInput(input, dt));
            Assert.True(menus.IsOpen);
            Assert.Equal(StockUiMenuEntries.MainMenuPage2, menus.Top!.Name);
            input.SetKeyUp(GordianKey.OemMinus);
            input.Update(profile, dt);

            input.SetKeyDown(GordianKey.Escape);
            input.MenuContext = true;
            input.Update(profile, dt);
            Assert.True(menus.ProcessInput(input, dt));
            Assert.False(menus.IsOpen);
        }

        [Fact]
        public void MenuContext_ResolvesTheSharedKeysToMenuNavigation()
        {
            var profile = InputProfile.CreateCompact();
            Assert.True(profile.TryGetAction(new InputChord(GordianKey.Up), false, out var gameplay));
            Assert.Equal(InputAction.CameraPitchUp, gameplay);
            Assert.True(profile.TryGetAction(new InputChord(GordianKey.Up), true, out var menu));
            Assert.Equal(InputAction.MenuUp, menu);

            // Keys without a menu binding keep their gameplay action in a menu (WASD still walks, as in retail).
            Assert.True(profile.TryGetAction(new InputChord(GordianKey.W), true, out var walk));
            Assert.Equal(InputAction.MoveForward, walk);

            var input = new InputState();
            input.SetKeyDown(GordianKey.Up);
            input.Update(profile, TimeSpan.FromMilliseconds(16));
            Assert.True(input.IsActionHeld(InputAction.CameraPitchUp));
            Assert.False(input.IsActionHeld(InputAction.MenuUp));

            input.MenuContext = true;
            input.Update(profile, TimeSpan.FromMilliseconds(16));
            Assert.True(input.IsActionHeld(InputAction.MenuUp));
            Assert.False(input.IsActionHeld(InputAction.CameraPitchUp));

            // The d-pad navigates in a menu (and targets party members outside one); the left stick is left to walking.
            var pad = new GamepadState(true, GamepadButton.DPadDown, new System.Numerics.Vector2(0, -0.9f), default, 0, 0);
            input.SetKeyUp(GordianKey.Up);
            input.SetGamepadState(pad);
            input.Update(profile, TimeSpan.FromMilliseconds(16));
            Assert.True(input.IsActionHeld(InputAction.MenuDown));
            input.MenuContext = false;
            input.Update(profile, TimeSpan.FromMilliseconds(16));
            Assert.False(input.IsActionHeld(InputAction.MenuDown));
            Assert.True(input.IsActionHeld(InputAction.TargetParty2));
        }

        [Fact]
        public void ProfilesSavedBeforeMenus_GetTheNavigationBindings()
        {
            var profile = InputProfile.CreateCompact();
            foreach (var action in new[] { InputAction.MenuUp, InputAction.MenuDown, InputAction.MenuLeft, InputAction.MenuRight })
            {
                profile.ClearAction(action);
            }
            var reloaded = InputProfile.FromJson(profile.SaveToJson());
            Assert.Contains(new InputChord(GordianKey.Down), reloaded.GetChords(InputAction.MenuDown));
            Assert.Contains(new InputChord(GamepadButton.DPadLeft), reloaded.GetChords(InputAction.MenuLeft));
        }

        [Fact]
        public void CurrentTime_NamesTheVanadielDay()
        {
            string text = StockUiMenuController.DescribeCurrentTime(new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc));
            Assert.StartsWith("Vana'diel time: ", text);
            Assert.Contains("Earth time:", text);
        }

        /// <summary>
        /// Against the retail DATs: every selectable button of the menus the controller drives has a client meaning,
        /// and the main menu's navigation ring visits all twelve entries.
        /// </summary>
        [Fact]
        public void RetailMenus_EverySelectableButtonHasAnEntry()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            if (library == null) return;

            // Three-column rows: right from the leftmost option is the middle one (the DAT's link bytes are up, down, right, left).
            Assert.True(library.TryGetMenu(StockUiMenuEntries.WindowSettingsPage, out var settings));
            var leftmost = settings.FindButton(1)!;
            var middle = settings.FindButton(2)!;
            Assert.True(leftmost.X < middle.X);
            Assert.Equal(2, leftmost.NavRight);
            Assert.Equal(1, middle.NavLeft);

            var driven = new List<string>
            {
                StockUiMenuEntries.MainMenu, StockUiMenuEntries.MainMenuPage2, StockUiMenuEntries.ConfigMenu, StockUiMenuEntries.WindowsMenu,
                StockUiConfigPages.ChatFiltersPage,
            };
            driven.AddRange(StockUiConfigPages.All.Select(p => p.Menu));
            foreach (string name in driven)
            {
                Assert.True(library.TryGetMenu(name, out var menu), name);
                foreach (var button in menu.Buttons)
                {
                    bool insideFrame = button.X >= 0 && button.X < menu.Frame.Width;
                    if (!insideFrame || !StockUiMenuController.IsSelectable(button)) continue;
                    Assert.True(StockUiMenuEntries.TryGet(name, button.ButtonId, out _), $"{name} button {button.ButtonId} has no entry");
                }
                // Every config row's buttons exist on the page, and slider buttons are the invisible 192-wide bars.
                if (StockUiConfigPages.TryGet(name, out var page))
                {
                    foreach (var row in page.Rows)
                    {
                        switch (row)
                        {
                            case StockUiOptionRow option:
                                foreach (var choice in option.Choices) Assert.NotNull(menu.FindButton(choice.ButtonId));
                                break;
                            case StockUiSliderRow slider:
                                var bar = menu.FindButton(slider.ButtonId);
                                Assert.NotNull(bar);
                                Assert.Equal(192, bar!.Width);
                                Assert.Equal(slider.ButtonId, bar.NavLeft);
                                Assert.Equal(slider.ButtonId, bar.NavRight);
                                break;
                        }
                    }
                }
                // The frame names its cursor group and it exists.
                var cursor = menu.Frame.Shapes.FirstOrDefault(s => s.Kind == 6);
                Assert.True(library.TryGetGroup(cursor.GroupId, out var group) && group.Images.Count == 6, $"{name} cursor {cursor.GroupId}");
            }

            var menus = new StockUiMenuController { Library = library };
            Assert.True(menus.OpenMainMenu());
            var visited = new HashSet<int>();
            for (int i = 0; i < 12; i++)
            {
                visited.Add(menus.Top!.SelectedButtonId);
                menus.Move(InputAction.MenuDown);
            }
            Assert.Equal(12, visited.Count);
            Assert.Equal(1, menus.Top!.SelectedButtonId);

            // Left/right flip pages; the cursor keeps its row (both pages share the button grid).
            menus.Move(InputAction.MenuDown);
            menus.Move(InputAction.MenuRight);
            Assert.Equal(StockUiMenuEntries.MainMenuPage2, menus.Top.Name);
            Assert.Equal(2, menus.Top.SelectedButtonId);
        }
    }
}
