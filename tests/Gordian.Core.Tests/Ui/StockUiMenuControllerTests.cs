// tests/Gordian.Core.Tests/Ui/StockUiMenuControllerTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Input;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Gordian.Core.World;
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

        internal static UiResourceLibrary SyntheticLibrary() => UiResourceLibrary.FromDefinitions(new[]
        {
            List(StockUiMenuEntries.MainMenu, 384, 48, UiAnchor.TopRight, 1, 2, 3),
            List(StockUiMenuEntries.MainMenuPage2, 384, 48, UiAnchor.TopRight, 1, 2, 7, 12),
            List(StockUiMenuEntries.ConfigMenu, 384, 48, UiAnchor.TopRight, 1, 4),
            List(StockUiMenuEntries.WindowsMenu, 384, 48, UiAnchor.TopRight, 1, 2, 3),
            List(StockUiMenuEntries.WindowSettingsPage, 16, 48, UiAnchor.TopLeft, 7, 8, 9),
            List(StockUiMenuEntries.YesNoMenu, 16, 256, UiAnchor.BottomLeft, 1, 2),
            List(StockUiMenuEntries.MessageYesNoMenu, 130, 208, UiAnchor.BottomLeft, 1, 2),
            // The command menu's templates (label sprites by button) and the chat-mode list.
            List(StockUiCommandMenu.Template, 16, 128, UiAnchor.BottomLeft, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10),
            List("battlemo", 16, 192, UiAnchor.BottomLeft, 1, 2, 3, 4, 5, 6),
            List("attackmo", 16, 176, UiAnchor.BottomLeft, 1, 2, 3, 4, 5, 6, 7),
            List("mp_pmode", 16, 176, UiAnchor.BottomLeft, 1, 2, 3, 4, 5, 6, 8, 7),
            List(StockUiMenuEntries.ChatModeMenu, 130, 192, UiAnchor.BottomLeft, 1, 2, 3, 4, 5, 6, 7),
            // The shop: Buy / Sell, the ten-row list (its end rows link to themselves) and the quantity control.
            List(StockUiShop.MenuName, 16, 256, UiAnchor.BottomLeft, 1, 2),
            ShopListFrame(),
            QuantityFrame(),
        });

        /// <summary>The "shop" frame as the DAT authors it: ten 132 x 16 rows at an 18 px pitch from y 5, ends linked to themselves.</summary>
        private static UiMenuDefinition ShopListFrame()
        {
            var buttons = new List<UiMenuButton>();
            for (int i = 0; i < StockUiShop.ListRows; i++)
            {
                int id = i + 1;
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)id, X = 0, Y = (short)(5 + 18 * i), Width = 132, Height = 16,
                    NavUp = (sbyte)(i == 0 ? 1 : id - 1), NavDown = (sbyte)(i == StockUiShop.ListRows - 1 ? id : id + 1),
                    NavLeft = (sbyte)id, NavRight = (sbyte)id,
                });
            }
            return new UiMenuDefinition
            {
                Name = StockUiShop.ListMenu,
                Frame = new UiMenuFrame { X = 16, Y = 48, Width = 256, Height = 190, Anchor = UiAnchor.TopLeft },
                Buttons = buttons,
            };
        }

        /// <summary>The "itemctrl" frame: the number field (the only navigable button) and its four arrows.</summary>
        private static UiMenuDefinition QuantityFrame() => new()
        {
            Name = StockUiShop.QuantityMenu,
            Frame = new UiMenuFrame { X = 16, Y = 240, Width = 112, Height = 56, Anchor = UiAnchor.TopLeft },
            Buttons = new List<UiMenuButton>
            {
                new() { ButtonId = StockUiShop.QuantityField, X = 34, Y = 22, Width = 24, Height = 16, NavUp = 1, NavDown = 1, NavLeft = 1, NavRight = 1 },
                new() { ButtonId = StockUiShop.QuantityAllButton, X = 19, Y = 22, Width = 10, Height = 12, NavUp = -1, NavDown = -1, NavLeft = -1, NavRight = -1 },
                new() { ButtonId = StockUiShop.QuantityOneButton, X = 88, Y = 22, Width = 10, Height = 12, NavUp = -1, NavDown = -1, NavLeft = -1, NavRight = -1 },
                new() { ButtonId = StockUiShop.QuantityDownButton, X = 40, Y = 40, Width = 12, Height = 10, NavUp = -1, NavDown = -1, NavLeft = -1, NavRight = -1 },
                new() { ButtonId = StockUiShop.QuantityUpButton, X = 40, Y = 7, Width = 12, Height = 10, NavUp = -1, NavDown = -1, NavLeft = -1, NavRight = -1 },
            },
        };

        private static IEnumerable<string> Labels(StockUiOpenMenu menu) => menu.CommandRows.Select(r => r.Label.Text);

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

        #region Command menu

        private static StockUiTargetContext Target(StockUiTargetKind kind, bool engaged = false, bool engagedWithTarget = false, bool canInvite = true) =>
            new(kind, 0x1000ABCD, "Target", engaged, engagedWithTarget, canInvite);

        [Fact]
        public void CommandMenu_ComposesPerTargetKind_AtTheBottomLeft()
        {
            var menus = Controller();
            Assert.True(menus.OpenCommandMenu(Target(StockUiTargetKind.Self)));
            var self = menus.Top!;
            Assert.True(self.IsCommandMenu);
            Assert.Equal(StockUiCommandMenu.Name, self.Name);
            Assert.Equal(new[] { "Chat", "Magic", "Abilities", "Trust", "Items", "Trade", "Check" }, Labels(self));
            // One 88 x 16 row per entry from (21, 5); the frame sized to them, its bottom at layout y 296 as every DAT variant's.
            Assert.Equal(7, self.Menu.Buttons.Count);
            Assert.Equal((21, 5 + 16 * 6), (self.Menu.Buttons[6].X, (int)self.Menu.Buttons[6].Y));
            Assert.Equal(8 + 16 * 7, self.Menu.Frame.Height);
            Assert.Equal(296 - (8 + 16 * 7), self.Menu.Frame.Y);
            Assert.Equal(UiAnchor.BottomLeft, self.Menu.Frame.Anchor);
            Assert.Equal(1, self.SelectedButtonId);
            menus.Move(InputAction.MenuUp);
            Assert.Equal(7, self.SelectedButtonId); // the rows link in a ring
            Assert.Equal(menus.CommandMenuTarget, Target(StockUiTargetKind.Self));
            menus.CloseAll();

            Assert.True(menus.OpenCommandMenu(Target(StockUiTargetKind.Self, engaged: true)));
            Assert.Contains("Disengage", Labels(menus.Top!));
            menus.CloseAll();

            // Another player shows the same list as yourself (retail, in-game check 2026-09-28).
            Assert.True(menus.OpenCommandMenu(Target(StockUiTargetKind.Player, canInvite: false)));
            Assert.Equal(new[] { "Chat", "Magic", "Abilities", "Trust", "Items", "Trade", "Check" }, Labels(menus.Top!));
            menus.CloseAll();

            Assert.True(menus.OpenCommandMenu(Target(StockUiTargetKind.Monster)));
            Assert.Equal(new[] { "Attack", "Magic", "Abilities", "Trust", "Items", "Check" }, Labels(menus.Top!));
            menus.CloseAll();

            Assert.True(menus.OpenCommandMenu(Target(StockUiTargetKind.Monster, engaged: true, engagedWithTarget: true)));
            Assert.Equal(new[] { "Switch Target", "Magic", "Abilities", "Trust", "Items", "Disengage", "Check" }, Labels(menus.Top!));
            menus.CloseAll();

            Assert.False(menus.OpenCommandMenu(Target(StockUiTargetKind.None)));
            menus.OpenMainMenu();
            Assert.False(menus.OpenCommandMenu(Target(StockUiTargetKind.Self))); // never over an open menu
        }

        [Fact]
        public void CommandMenu_ChatOpensTheChatModes_AndPickingOneSetsTheMode()
        {
            var menus = Controller();
            ChatInputMode? picked = null;
            menus.ChatModeSelected = mode => picked = mode;
            menus.TellTarget = () => "Ayame";
            menus.HasLinkshell = slot => slot == 1;
            menus.OpenCommandMenu(Target(StockUiTargetKind.Self));
            menus.Activate(); // Chat
            var modes = menus.Top!;
            Assert.Equal(StockUiMenuEntries.ChatModeMenu, modes.Name);
            Assert.Equal("Ayame", modes.SideTexts[StockUiMenuEntries.ChatModeTellButton].Text);
            Assert.False(modes.IsGreyed(StockUiMenuEntries.ChatModeLinkshellButton));
            Assert.True(modes.IsGreyed(StockUiMenuEntries.ChatModeLinkshell2Button));
            Assert.Equal(StockUiMenuController.NoLinkshellText, modes.SideTexts[StockUiMenuEntries.ChatModeLinkshell2Button].Text);
            Assert.True(modes.IsGreyed(StockUiMenuEntries.ChatModeUnityButton));
            Assert.Equal(StockUiMenuController.NoUnityText, modes.SideTexts[StockUiMenuEntries.ChatModeUnityButton].Text);

            menus.Move(InputAction.MenuDown);
            menus.Move(InputAction.MenuDown); // Party
            menus.Activate();
            Assert.Equal(ChatInputMode.Party, picked);
            Assert.False(menus.IsOpen);
        }

        [Fact]
        public void CommandMenu_RightOpensArrowedEntries_AndTheTellRowCyclesCandidates()
        {
            var menus = Controller();
            ChatInputMode? picked = null;
            string? partner = null;
            menus.ChatModeSelected = mode => picked = mode;
            menus.TellTargetSelected = name => partner = name;
            menus.TellTarget = () => "Ayame";
            menus.TellCandidates = () => new[] { "Ayame", "Cybin", "Zeid" };
            menus.OpenCommandMenu(Target(StockUiTargetKind.Self));

            menus.Move(InputAction.MenuRight); // Chat ▶ opens its list, as Confirm does
            Assert.Equal(StockUiMenuEntries.ChatModeMenu, menus.Top!.Name);
            var modes = menus.Top;
            menus.Move(InputAction.MenuDown); // Tell
            Assert.Equal("Ayame", modes.SideTexts[StockUiMenuEntries.ChatModeTellButton].Text);
            menus.Move(InputAction.MenuRight);
            Assert.Equal("Cybin", modes.SideTexts[StockUiMenuEntries.ChatModeTellButton].Text);
            menus.Move(InputAction.MenuLeft);
            menus.Move(InputAction.MenuLeft); // wraps to the last
            Assert.Equal("Zeid", modes.SideTexts[StockUiMenuEntries.ChatModeTellButton].Text);
            Assert.Equal(StockUiMenuEntries.ChatModeTellButton, modes.SelectedButtonId); // the cursor stays on Tell

            menus.Activate();
            Assert.Equal("Zeid", partner);
            Assert.Equal(ChatInputMode.Tell, picked);
            Assert.False(menus.IsOpen);

            // Right on an entry without a list does nothing; right on a plain row of the chat modes neither.
            menus.OpenCommandMenu(Target(StockUiTargetKind.Monster));
            menus.Move(InputAction.MenuRight);
            Assert.Single(menus.OpenMenus);
        }

        [Fact]
        public void CommandMenu_GreyedEntriesAndTellWithoutAPartner_PostANoticeAndStayOpen()
        {
            var menus = Controller();
            var notices = new List<string>();
            menus.NoticePosted += notices.Add;
            ChatInputMode? picked = null;
            menus.ChatModeSelected = mode => picked = mode;
            menus.OpenCommandMenu(Target(StockUiTargetKind.Self));
            menus.Activate(); // Chat
            menus.Move(InputAction.MenuDown); // Tell, no partner
            menus.Activate();
            Assert.Single(notices);
            Assert.StartsWith("Tell:", notices[0]);
            Assert.True(menus.IsOpen);
            Assert.Null(picked);

            menus.Move(InputAction.MenuUp);
            menus.Move(InputAction.MenuUp); // Shout, then Unity
            menus.Move(InputAction.MenuUp);
            Assert.Equal(StockUiMenuEntries.ChatModeUnityButton, menus.Top!.SelectedButtonId);
            menus.Activate();
            Assert.Equal(2, notices.Count);
            Assert.Equal("Unity: no unity.", notices[1]);
            Assert.True(menus.IsOpen);
            Assert.Null(picked);
        }

        [Fact]
        public void CommandMenu_TargetCommandsCloseTheMenu_AndPostWarnings()
        {
            var menus = Controller();
            var notices = new List<string>();
            menus.NoticePosted += notices.Add;
            var ran = new List<(StockUiMenuCommand, StockUiTargetContext)>();
            menus.TargetCommand = (command, target) =>
            {
                ran.Add((command, target));
                return Task.FromResult(command == StockUiMenuCommand.Check
                    ? Gordian.Core.Actions.PlayerActionResult.Ok("Checking Target.")
                    : Gordian.Core.Actions.PlayerActionResult.Warn("Too far away."));
            };

            menus.OpenCommandMenu(Target(StockUiTargetKind.Monster));
            menus.Activate(); // Attack
            Assert.False(menus.IsOpen);
            Assert.Equal((StockUiMenuCommand.Attack, Target(StockUiTargetKind.Monster)), ran.Single());
            Assert.Equal(new[] { "Too far away." }, notices);

            menus.OpenCommandMenu(Target(StockUiTargetKind.Monster));
            menus.Move(InputAction.MenuUp); // Check (the last row)
            menus.Activate();
            Assert.Equal(StockUiMenuCommand.Check, ran[1].Item1);
            Assert.Single(notices); // a success posts nothing

            // The cursor is remembered by entry across the differing lists: Check is the player menu's last row.
            menus.OpenCommandMenu(Target(StockUiTargetKind.Player));
            Assert.Equal(7, menus.Top!.SelectedButtonId);
            Assert.Equal("Check", menus.Top.CommandRows[6].Label.Text);
            menus.Activate();
            Assert.False(menus.IsOpen);
            Assert.Equal(StockUiMenuCommand.Check, ran[2].Item1);

            // The menu is about its target: the service closes it, and what it opened, when the target changes.
            menus.OpenCommandMenu(Target(StockUiTargetKind.Self));
            Assert.Equal(7, menus.Top!.SelectedButtonId); // Check again, the self menu's last row
            menus.Move(InputAction.MenuDown); // wraps to Chat
            Assert.Equal("Chat", menus.Top.CommandRows[menus.Top.SelectedButtonId - 1].Label.Text);
            menus.Activate(); // the chat modes on top
            Assert.Equal(2, menus.OpenMenus.Count);
            menus.CloseCommandMenu();
            Assert.False(menus.IsOpen);
            menus.OpenMainMenu();
            menus.CloseCommandMenu(); // only a command menu root closes
            Assert.True(menus.IsOpen);
        }

        #endregion

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
        public void MenuInput_RaisesTheSystemSoundCues()
        {
            var menus = Controller();
            var cues = new List<StockUiSoundCue>();
            menus.SoundCue += cues.Add;
            var profile = InputProfile.CreateCompact();
            var input = new InputState();
            var dt = TimeSpan.FromMilliseconds(16);
            void Press(GordianKey key)
            {
                input.SetKeyDown(key);
                input.MenuContext = menus.IsOpen;
                input.Update(profile, dt);
                menus.ProcessInput(input, dt);
                input.SetKeyUp(key);
                input.Update(profile, dt);
            }

            Press(GordianKey.OemMinus); // open
            Press(GordianKey.Down);     // cursor
            Press(GordianKey.OemMinus); // page
            Press(GordianKey.Escape);   // close
            Assert.Equal(new[] { StockUiSoundCue.MainMenuOpen, StockUiSoundCue.CursorMove, StockUiSoundCue.PageSwitch, StockUiSoundCue.Close }, cues);
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
                StockUiConfigPages.ChatFiltersPage, StockUiMenuEntries.ChatModeMenu,
                StockUiConfigPages.FontColorCategoryMenu, StockUiConfigPages.FontColorListMenu,
                StockUiConfigPages.LogWindowMenu, StockUiConfigPages.LogCategoryMenu, StockUiConfigPages.LogListMenu,
                StockUiConfigPages.EffectsPage,
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
                                if (name.Equals(StockUiConfigPages.FontColorEditPage, StringComparison.OrdinalIgnoreCase))
                                {
                                    // The colour editor's R/G/B bars are 82 wide and link left to Cancel, right to OK.
                                    Assert.Equal(82, bar!.Width);
                                    Assert.Equal(StockUiConfigPages.FontColorCancelButton, bar.NavLeft);
                                    Assert.Equal(StockUiConfigPages.FontColorOkButton, bar.NavRight);
                                    break;
                                }
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

        [Fact]
        public void RetailMenus_CommandMenuRowsResolveTheirLabelSprites()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            if (library == null) return;

            // The chat-mode list's red arrow sits on its own button 8 at x 81, where the client text starts.
            Assert.True(library.TryGetMenu(StockUiMenuEntries.ChatModeMenu, out var chatModes));
            var arrow = chatModes.FindButton(StockUiMenuEntries.ChatModeArrowButton);
            Assert.NotNull(arrow);
            Assert.Equal(81, arrow!.X);
            Assert.False(StockUiMenuController.IsSelectable(arrow));
            foreach (int greyable in new[] { StockUiMenuEntries.ChatModeLinkshellButton, StockUiMenuEntries.ChatModeLinkshell2Button, StockUiMenuEntries.ChatModeUnityButton })
            {
                Assert.Contains(chatModes.FindButton(greyable)!.Shapes, s => s.Kind == 4);
            }

            var contexts = new[]
            {
                new StockUiTargetContext(StockUiTargetKind.Self, 1, "Me"),
                new StockUiTargetContext(StockUiTargetKind.Self, 1, "Me", Engaged: true),
                new StockUiTargetContext(StockUiTargetKind.Player, 2, "Ayame"),
                new StockUiTargetContext(StockUiTargetKind.Monster, 3, "Wild Rabbit"),
                new StockUiTargetContext(StockUiTargetKind.Monster, 3, "Wild Rabbit", Engaged: true, EngagedWithTarget: true),
                new StockUiTargetContext(StockUiTargetKind.Trust, 4, "Kupipi"),
            };
            foreach (var context in contexts)
            {
                var rows = StockUiCommandMenu.Compose(context);
                var menu = StockUiCommandMenu.Build(library, rows, out var frameImage);
                Assert.NotNull(menu);
                Assert.NotNull(frameImage);
                // The rebuilt frame: the background plus one capsule pair per row, the background as tall as the frame.
                Assert.Equal(1 + 2 * rows.Count, frameImage!.Parts.Count);
                Assert.Equal(menu!.Frame.Height, frameImage.Parts[0].BottomLeft.Y);
                Assert.Equal(112, frameImage.Parts[0].BottomRight.X);
                for (int i = 0; i < rows.Count; i++)
                {
                    var button = menu.Buttons[i];
                    Assert.Contains(button.Shapes, s => s.Kind == 0 && library.TryGetImage(s, out _));
                    if (rows[i].Label == StockUiCommandMenu.Invite) Assert.Contains(button.Shapes, s => s.Kind == 4 && library.TryGetImage(s, out _));
                }
                // The frame keeps the template's cursor group.
                Assert.Contains(menu.Frame.Shapes, s => s.Kind == 6 && library.TryGetGroup(s.GroupId, out var g) && g.Images.Count == 6);
                Assert.DoesNotContain(menu.Frame.Shapes, s => s.Kind == 0);
            }
        }

        #region Mouse

        /// <summary>Publishes every open menu as drawn at the given screen origins (scale 2), root first.</summary>
        private static void Place(StockUiMenuController menus, params (float X, float Y)[] origins)
        {
            var placements = new List<StockUiMenuPlacement>();
            for (int i = 0; i < menus.OpenMenus.Count; i++) placements.Add(new StockUiMenuPlacement(menus.OpenMenus[i], origins[i].X, origins[i].Y, 2));
            menus.SetScreenPlacements(placements);
        }

        /// <summary>The screen centre of a List() row (index from 0) for a menu drawn at (x, y) at scale 2.</summary>
        private static (float X, float Y) Row(float x, float y, int index) => (x + (16 + 44) * 2, y + (6 + 16 * index + 8) * 2);

        [Fact]
        public void Mouse_HoverSelectsAndLeftClickActivates()
        {
            var menus = Controller();
            menus.OpenMainMenu();
            menus.Move(InputAction.MenuRight); // page 2: 1, 2, 7 (Config), 12
            Place(menus, (200, 100));

            var config = Row(200, 100, 2);
            Assert.True(menus.OnMouseMove(config.X, config.Y));
            Assert.Equal(7, menus.Top!.SelectedButtonId);
            Assert.False(menus.OnMouseMove(10, 10)); // off the menu: the cursor stays
            Assert.Equal(7, menus.Top.SelectedButtonId);

            Assert.True(menus.OnMouseDown(MouseButton.Left, config.X, config.Y));
            Assert.Equal(StockUiMenuEntries.ConfigMenu, menus.Top!.Name);
            Assert.True(menus.OnMouseUp(MouseButton.Left, config.X, config.Y)); // the release is the menu's too
        }

        [Fact]
        public void Mouse_ClickOutsideMenusIsGameInput_ClickOnBodyIsSwallowed()
        {
            var menus = Controller();
            Assert.False(menus.OnMouseDown(MouseButton.Left, 250, 150)); // nothing open
            menus.OpenMainMenu();
            Place(menus, (200, 100));

            Assert.False(menus.OnMouseDown(MouseButton.Left, 10, 10));
            Assert.False(menus.OnMouseUp(MouseButton.Left, 10, 10));
            // The frame's left margin, beside the rows: the menu's, but it selects nothing.
            Assert.True(menus.OnMouseDown(MouseButton.Left, 204, 130));
            Assert.Equal(StockUiMenuEntries.MainMenu, menus.Top!.Name);
            Assert.Single(menus.OpenMenus);
            Assert.True(menus.OnMouseUp(MouseButton.Left, 204, 130));
        }

        [Fact]
        public void Mouse_RightClickOverMenuCancels()
        {
            var menus = Controller();
            menus.OpenMainMenu();
            menus.Move(InputAction.MenuRight);
            menus.Move(InputAction.MenuDown);
            menus.Move(InputAction.MenuDown);
            menus.Activate(); // Config
            Place(menus, (200, 100), (200, 100));

            Assert.False(menus.OnMouseDown(MouseButton.Right, 10, 10)); // camera look, not cancel
            Assert.False(menus.OnMouseUp(MouseButton.Right, 10, 10));
            var row = Row(200, 100, 0);
            Assert.True(menus.OnMouseDown(MouseButton.Right, row.X, row.Y));
            Assert.Equal(StockUiMenuEntries.MainMenuPage2, menus.Top!.Name);
            Assert.True(menus.OnMouseUp(MouseButton.Right, row.X, row.Y));
        }

        [Fact]
        public void Mouse_ClickOnParentWindowClosesTheWindowsAboveIt()
        {
            var menus = Controller();
            menus.OpenMainMenu();
            menus.Move(InputAction.MenuRight);
            menus.Move(InputAction.MenuDown);
            menus.Move(InputAction.MenuDown);
            menus.Activate(); // Config over page 2
            Assert.Equal(2, menus.OpenMenus.Count);
            Place(menus, (500, 100), (200, 100)); // (as if side by side)

            var notices = new List<string>();
            menus.NoticePosted += notices.Add;
            var missions = Row(500, 100, 0);
            Assert.True(menus.OnMouseDown(MouseButton.Left, missions.X, missions.Y));
            Assert.Single(menus.OpenMenus);
            Assert.Equal(1, menus.Top!.SelectedButtonId);
            Assert.Single(notices); // Missions: not available yet
        }

        [Fact]
        public async Task Mouse_PromptTakesOnlyItsOwnAnswer()
        {
            var menus = Controller();
            menus.OpenMainMenu();
            var answer = menus.PromptYesNoAsync("Log out?", defaultYes: false);
            Place(menus, (500, 100), (200, 100));

            var parentRow = Row(500, 100, 1);
            Assert.True(menus.OnMouseDown(MouseButton.Left, parentRow.X, parentRow.Y)); // swallowed, ignored
            Assert.Equal(2, menus.OpenMenus.Count);
            Assert.False(answer.IsCompleted);

            var yes = Row(200, 100, 0);
            Assert.True(menus.OnMouseDown(MouseButton.Left, yes.X, yes.Y));
            Assert.True(await answer);
            Assert.Single(menus.OpenMenus);
        }

        [Fact]
        public void Mouse_SliderTakesTheValueUnderThePointerAndFollowsTheDrag()
        {
            var library = UiResourceLibrary.FromDefinitions(new[]
            {
                List(StockUiConfigPages.Window1SettingsPage, 16, 48, UiAnchor.TopLeft, 1, 2, 3, 4, 5, 6),
            });
            var menus = new StockUiMenuController { Library = library };
            Assert.True(menus.Open(StockUiConfigPages.Window1SettingsPage));
            Place(menus, (0, 0));

            // Button 3 ("Maximum lines displayed", 1-8) spans x 16..104 at scale 2 = 32..208 on screen.
            float y = Row(0, 0, 2).Y;
            Assert.True(menus.OnMouseDown(MouseButton.Left, 33, y));
            Assert.Equal(1, menus.Settings.GetValue(StockUiSettingKey.Window1MaxLines));
            menus.OnMouseMove(120, y); // halfway: 1 + 3.5 steps rounds to 5
            Assert.Equal(5, menus.Settings.GetValue(StockUiSettingKey.Window1MaxLines));
            menus.OnMouseMove(400, y); // past the end: clamped
            Assert.Equal(8, menus.Settings.GetValue(StockUiSettingKey.Window1MaxLines));
            menus.OnMouseUp(MouseButton.Left, 400, y);
            menus.OnMouseMove(33, y); // released: the value stays
            Assert.Equal(8, menus.Settings.GetValue(StockUiSettingKey.Window1MaxLines));
            Assert.Equal(3, menus.Top!.SelectedButtonId);
        }

        [Fact]
        public void Mouse_WheelScrollsAListWithoutWrapping()
        {
            var rows = Enumerable.Range(1, StockUiConfigPages.ChatFilterRowsPerPage).ToArray();
            var library = UiResourceLibrary.FromDefinitions(new[] { List(StockUiConfigPages.ChatFiltersPage, 16, 48, UiAnchor.TopLeft, rows) });
            var menus = new StockUiMenuController { Library = library };
            menus.Open(StockUiConfigPages.ChatFiltersPage);
            Place(menus, (0, 0));
            var row = Row(0, 0, 0);

            Assert.True(menus.OnMouseWheel(row.X, row.Y, 1)); // already at the top
            Assert.Equal(0, menus.Top!.FirstRow);
            Assert.True(menus.OnMouseWheel(row.X, row.Y, -2));
            Assert.Equal(2, menus.Top.FirstRow);
            Assert.False(menus.OnMouseWheel(1000, 1000, -1)); // off the menu: the camera's zoom
            Assert.Equal(2, menus.Top.FirstRow);
        }

        [Fact]
        public void Mouse_PageArrowsTurnThePage()
        {
            static UiMenuDefinition WithArrows(UiMenuDefinition menu) => new()
            {
                Name = menu.Name,
                Frame = menu.Frame,
                Buttons = menu.Buttons.Concat(new[]
                {
                    new UiMenuButton { ButtonId = 13, X = -16, Y = 6, Width = 16, Height = 16, NavUp = -1, NavDown = -1, NavLeft = -1, NavRight = -1 },
                    new UiMenuButton { ButtonId = 14, X = 113, Y = 6, Width = 16, Height = 16, NavUp = -1, NavDown = -1, NavLeft = -1, NavRight = -1 },
                }).ToList(),
            };
            var library = UiResourceLibrary.FromDefinitions(new[]
            {
                WithArrows(List(StockUiMenuEntries.MainMenu, 384, 48, UiAnchor.TopRight, 1, 2, 3)),
                WithArrows(List(StockUiMenuEntries.MainMenuPage2, 384, 48, UiAnchor.TopRight, 1, 2, 7, 12)),
            });
            var menus = new StockUiMenuController { Library = library };
            menus.OpenMainMenu();
            Place(menus, (200, 100));

            Assert.True(menus.OnMouseDown(MouseButton.Left, 200 + 120 * 2, 100 + 10 * 2)); // right arrow
            Assert.Equal(StockUiMenuEntries.MainMenuPage2, menus.Top!.Name);
            Place(menus, (200, 100));
            Assert.True(menus.OnMouseDown(MouseButton.Left, 200 - 8 * 2, 100 + 10 * 2)); // left arrow
            Assert.Equal(StockUiMenuEntries.MainMenu, menus.Top!.Name);
            Assert.Single(menus.OpenMenus);
        }

        #endregion

        #region Shop (Tier 2 chunk 6c)

        /// <summary>A session's shop wiring: the inventory state feeding the controller, and the packets it sends recorded.</summary>
        private sealed class ShopHarness
        {
            public readonly InventoryState Inventory = new();
            public readonly StockUiMenuController Menus;
            public readonly List<(uint Count, ushort ShopNo, ushort Index)> Buys = new();
            public readonly List<(uint Count, ushort ItemId, byte Slot)> Appraisals = new();
            public readonly List<string> Notices = new();
            public int Confirms;

            private static readonly Dictionary<ushort, ItemRecord> Items = new()
            {
                [4389] = new ItemRecord { ItemId = 4389, Name = "Distilled Water", StackSize = 12 },
                [4431] = new ItemRecord { ItemId = 4431, Name = "Grilled Hare", StackSize = 12 },
                [639] = new ItemRecord { ItemId = 639, Name = "Ronfaure Chestnut", StackSize = 12 },
                [610] = new ItemRecord { ItemId = 610, Name = "Bay Leaves", StackSize = 12 },
                [1000] = new ItemRecord { ItemId = 1000, Name = "Rusty Key", StackSize = 1, Flags = StockUiShop.NoSaleFlag },
                [12345] = new ItemRecord { ItemId = 12345, Name = "Bronze Sword", StackSize = 1 },
            };

            public ShopHarness(uint gil = 5000)
            {
                Menus = Controller();
                Menus.Inventory = Inventory;
                Menus.ItemLookup = id => Items.TryGetValue(id, out var record) ? record : null;
                Menus.ShopBuy = (count, shopNo, index) => { Buys.Add((count, shopNo, index)); return Task.CompletedTask; };
                Menus.ShopAppraise = (count, itemId, slot) => { Appraisals.Add((count, itemId, slot)); return Task.CompletedTask; };
                Menus.ShopSellConfirm = () => { Confirms++; return Task.CompletedTask; };
                Menus.NoticePosted += Notices.Add;
                Inventory.ShopChanged += Menus.OnShopChanged;
                Inventory.ItemChanged += (_, _, _) => Menus.OnInventoryChanged();
                Inventory.SetItem(ContainerId.Inventory, 0, StockUiShop.GilItemId, gil, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 5, 4389, 3, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 9, 639, 12, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 11, 1000, 1, ItemLockFlag.Normal);
            }

            /// <summary>The merchant's shop from the maintainer's capture: 0x03E list 4, then the four 0x03C entries.</summary>
            public void OpenCapturedShop()
            {
                Inventory.OpenShop(4);
                Inventory.AddShopItems(new[]
                {
                    new ShopItemEntry(34, 4389, 0, 0, 0),
                    new ShopItemEntry(82, 4431, 1, 0, 0),
                    new ShopItemEntry(128, 639, 2, 0, 0),
                    new ShopItemEntry(64, 610, 3, 0, 0),
                });
            }
        }

        [Fact]
        public void Shop_OpensOnShopOpen_AndBuySendsCountShopNoAndSlot()
        {
            var h = new ShopHarness();
            h.Inventory.OpenShop(4);
            Assert.True(h.Menus.IsShopOpen);
            Assert.True(h.Menus.Top!.IsShopMenu);
            Assert.Equal(StockUiShop.BuyButton, h.Menus.Top.SelectedButtonId);

            // The list arrives after the window opened; Buy shows it in shop-slot order with names and prices.
            h.Inventory.AddShopItems(new[] { new ShopItemEntry(128, 639, 2, 0, 0), new ShopItemEntry(34, 4389, 0, 0, 0) });
            h.Menus.Activate();
            var list = h.Menus.Top!;
            Assert.True(list.IsShopList);
            Assert.Equal(StockUiShopSide.Buy, list.ShopSide);
            Assert.Equal(new[] { "Distilled Water", "Ronfaure Chestnut" }, list.ShopRows.Select(r => r.Name));
            Assert.Equal(5000u, list.Gil);

            h.Inventory.AddShopItems(new[] { new ShopItemEntry(64, 610, 3, 0, 0) });
            Assert.Equal(3, list.ShopRows.Count); // a later 0x03C refreshes the open list

            h.Menus.Move(InputAction.MenuDown);
            Assert.Equal(639, list.SelectedShopRow!.Value.ItemId);
            h.Menus.Activate();
            var quantity = h.Menus.Top!;
            Assert.True(quantity.IsQuantity);
            Assert.Equal(1u, quantity.Quantity);
            Assert.Equal(12u, quantity.QuantityMax); // a stack of 12; 5000 gil would buy 39
            Assert.Equal(128u, quantity.UnitPrice);

            h.Menus.Move(InputAction.MenuUp);
            Assert.Equal(2u, quantity.Quantity);
            h.Menus.Move(InputAction.MenuLeft); // All
            Assert.Equal(12u, quantity.Quantity);
            h.Menus.Move(InputAction.MenuUp);
            Assert.Equal(12u, quantity.Quantity);
            Assert.Equal(12u * 128, quantity.TotalPrice);
            h.Menus.Move(InputAction.MenuRight); // 1
            Assert.Equal(1u, quantity.Quantity);
            h.Menus.Move(InputAction.MenuDown);
            Assert.Equal(1u, quantity.Quantity);

            h.Menus.Activate();
            Assert.Equal((1u, (ushort)0x0400, (ushort)2), Assert.Single(h.Buys)); // the capture's 01 00 00 00 00 04 02 00
            Assert.Same(list, h.Menus.Top); // back on the list after buying
            Assert.Empty(h.Notices);
        }

        [Fact]
        public void Shop_BuyCapsTheCountByGil_AndRefusesWhatYouCannotAfford()
        {
            var h = new ShopHarness(gil: 300);
            h.OpenCapturedShop();
            h.Menus.Activate();
            h.Menus.Move(InputAction.MenuDown);
            h.Menus.Move(InputAction.MenuDown);
            Assert.Equal(639, h.Menus.Top!.SelectedShopRow!.Value.ItemId);
            h.Menus.Activate();
            Assert.Equal(2u, h.Menus.Top!.QuantityMax); // 300 gil buys two at 128
            h.Menus.CloseTop();

            h.Inventory.SetItem(ContainerId.Inventory, 0, StockUiShop.GilItemId, 100, ItemLockFlag.Normal);
            h.Menus.Activate();
            Assert.True(h.Menus.Top!.IsShopList);
            Assert.Equal("You do not have enough gil.", Assert.Single(h.Notices));
            Assert.Empty(h.Buys);
        }

        [Fact]
        public void Shop_SellAppraisesThenSellsTheChosenCount()
        {
            var h = new ShopHarness();
            h.OpenCapturedShop();
            h.Menus.Move(InputAction.MenuDown); // Sell
            h.Menus.Activate();
            var list = h.Menus.Top!;
            Assert.Equal(StockUiShopSide.Sell, list.ShopSide);
            // The inventory in slot order, gil left out, the NoSale item greyed, counts kept.
            Assert.Equal(new[] { (4389, 5, 3u, false), (639, 9, 12u, false), (1000, 11, 1u, true) },
                list.ShopRows.Select(r => ((int)r.ItemId, (int)r.Slot, r.Count, r.Greyed)));

            // Landing on a row asks nothing; Confirm appraises it (retail, in-game check 2026-09-28) and the answer
            // prices every stack of that item until the shop closes.
            h.Menus.Move(InputAction.MenuDown);
            Assert.Empty(h.Appraisals);
            h.Menus.Activate();
            Assert.Equal((1u, (ushort)639, (byte)9), Assert.Single(h.Appraisals)); // the capture's 01 00 00 00 7f 02 09 00
            Assert.Same(list, h.Menus.Top); // the prompt waits for 0x03D
            h.Menus.Activate();
            Assert.Single(h.Appraisals); // a second Confirm while waiting asks nothing more
            h.Inventory.SetAppraisal(9, 29);
            var quantity = h.Menus.Top!;
            Assert.Equal(29u, list.ShopRows[1].Price);
            Assert.Equal(0u, list.ShopRows[0].Price); // another item, not appraised
            Assert.True(quantity.IsQuantity);
            Assert.Equal(29u, quantity.UnitPrice);
            Assert.Equal(12u, quantity.QuantityMax);
            Assert.Equal(12u, quantity.QuantityTotal); // "1 /12": the count held
            h.Menus.Move(InputAction.MenuLeft);
            Assert.Equal(12u, quantity.Quantity);
            h.Menus.Activate();
            Assert.Equal((12u, (ushort)639, (byte)9), h.Appraisals[^1]);
            Assert.Equal(1, h.Confirms);
            Assert.Same(list, h.Menus.Top);
            Assert.False(list.ShowsQuantity);

            // The price is kept: confirming the same item again opens the prompt at once.
            h.Menus.Activate();
            Assert.True(h.Menus.Top!.IsQuantity);
            Assert.Equal(2, h.Appraisals.Count);
            h.Menus.CloseTop();

            // The sale's 0x020 / 0x01E refresh the list and the gil shown.
            h.Inventory.SetItem(ContainerId.Inventory, 9, 0, 0, ItemLockFlag.Normal);
            h.Inventory.SetItem(ContainerId.Inventory, 0, StockUiShop.GilItemId, 5348, ItemLockFlag.Normal);
            Assert.Equal(new[] { 4389, 1000 }, list.ShopRows.Select(r => (int)r.ItemId));
            Assert.Equal(5348u, list.Gil);

            // The cursor stayed on the second row, now the greyed key: it posts why instead of asking the server.
            Assert.Equal(2, list.SelectedButtonId);
            Assert.True(list.SelectedShopRow!.Value.Greyed);
            int asked = h.Appraisals.Count;
            h.Menus.Activate();
            Assert.Contains("cannot be sold", Assert.Single(h.Notices));
            Assert.Equal(asked, h.Appraisals.Count);
        }

        [Fact]
        public void Shop_AnAppraisalPricesEveryStackOfTheItem_UntilTheShopCloses()
        {
            var h = new ShopHarness();
            h.Inventory.SetItem(ContainerId.Inventory, 12, 639, 5, ItemLockFlag.Normal); // a second stack of chestnuts
            h.OpenCapturedShop();
            h.Menus.Move(InputAction.MenuDown); // Sell
            h.Menus.Activate();
            var list = h.Menus.Top!;
            Assert.Equal(new[] { 4389, 639, 1000, 639 }, list.ShopRows.Select(r => (int)r.ItemId));
            h.Menus.Move(InputAction.MenuDown);
            h.Menus.Activate(); // slot 9
            Assert.Equal((1u, (ushort)639, (byte)9), Assert.Single(h.Appraisals));
            h.Inventory.SetAppraisal(9, 29);
            Assert.True(h.Menus.Top!.IsQuantity);
            Assert.Equal(29u, list.ShopRows[1].Price);
            Assert.Equal(29u, list.ShopRows[3].Price); // the other stack too
            Assert.Equal(0u, list.ShopRows[0].Price);
            h.Menus.CloseTop();

            // Another item's appraisal does not disturb it; only leaving the shop forgets it.
            h.Menus.Move(InputAction.MenuUp);
            h.Menus.Activate(); // slot 5
            h.Inventory.SetAppraisal(5, 7);
            Assert.Equal((7u, 29u, 29u), (list.ShopRows[0].Price, list.ShopRows[1].Price, list.ShopRows[3].Price));
            h.Menus.CloseTop();
            h.Menus.CloseTop(); // the list
            h.Menus.CloseTop(); // Buy / Sell: the shop ends
            Assert.False(h.Inventory.IsShopOpen);
            h.Inventory.SetItem(ContainerId.Inventory, 12, 0, 0, ItemLockFlag.Normal);
            h.OpenCapturedShop();
            Assert.Equal(StockUiShop.SellButton, h.Menus.Top!.SelectedButtonId); // the cursor is remembered
            h.Menus.Activate();
            Assert.Equal(StockUiShopSide.Sell, h.Menus.Top!.ShopSide);
            Assert.All(h.Menus.Top.ShopRows, r => Assert.Equal(0u, r.Price));
        }

        [Fact]
        public void Shop_AStaleAppraisalIsIgnored_WhenTheListWasLeft()
        {
            var h = new ShopHarness();
            h.OpenCapturedShop();
            h.Menus.Move(InputAction.MenuDown);
            h.Menus.Activate();
            h.Menus.Move(InputAction.MenuDown);
            h.Menus.Activate();
            Assert.Single(h.Appraisals);
            h.Menus.CloseTop(); // back to Buy / Sell before the answer
            h.Inventory.SetAppraisal(9, 29);
            Assert.True(h.Menus.Top!.IsShopMenu);
            Assert.Single(h.Menus.OpenMenus);
        }

        [Fact]
        public void Shop_BuyPromptShowsTheStackSizeAsTheTotal()
        {
            var h = new ShopHarness(gil: 300);
            h.OpenCapturedShop();
            h.Menus.Activate();
            h.Menus.Move(InputAction.MenuDown);
            h.Menus.Move(InputAction.MenuDown);
            h.Menus.Activate();
            Assert.Equal(2u, h.Menus.Top!.QuantityMax); // what 300 gil buys
            Assert.Equal(12u, h.Menus.Top.QuantityTotal); // "1 /12": the stack
            Assert.Empty(h.Appraisals); // Buy rows are never appraised
        }

        [Fact]
        public void Shop_CancelOnTheRootEndsTheShop_AndTheServerClosingItClosesTheWindows()
        {
            var h = new ShopHarness();
            h.OpenCapturedShop();
            h.Menus.Activate();
            Assert.Equal(2, h.Menus.OpenMenus.Count);
            h.Menus.CloseTop();
            Assert.True(h.Inventory.IsShopOpen);
            h.Menus.CloseTop();
            Assert.False(h.Inventory.IsShopOpen);
            Assert.False(h.Menus.IsShopOpen);
            Assert.False(h.Menus.IsOpen);

            h.OpenCapturedShop();
            h.Menus.Activate();
            h.Inventory.CloseShop(); // a zone change
            Assert.False(h.Menus.IsOpen);
            Assert.False(h.Menus.IsShopOpen);
        }

        [Fact]
        public void Shop_OpeningClosesOtherMenus_AndTheListScrollsAndWraps()
        {
            var h = new ShopHarness();
            Assert.True(h.Menus.OpenMainMenu());
            h.Inventory.OpenShop(4);
            Assert.Single(h.Menus.OpenMenus);
            Assert.True(h.Menus.Top!.IsShopMenu);

            var items = new ShopItemEntry[15];
            for (int i = 0; i < items.Length; i++) items[i] = new ShopItemEntry((uint)(10 + i), (ushort)(639 + i % 2), (byte)i, 0, 0);
            h.Inventory.AddShopItems(items);
            h.Menus.Activate();
            var list = h.Menus.Top!;
            Assert.Equal(StockUiShop.ListRows, list.VisibleRows);
            Assert.True(list.CanScroll);
            h.Menus.Move(InputAction.MenuUp); // up from the first row wraps to the end
            Assert.Equal(5, list.FirstRow);
            Assert.Equal(10, list.SelectedButtonId);
            Assert.Equal(14, list.SelectedShopRow!.Value.ShopIndex);
            h.Menus.Move(InputAction.MenuDown); // down from the last row wraps to the start
            Assert.Equal(0, list.FirstRow);
            Assert.Equal(1, list.SelectedButtonId);
            for (int i = 0; i < 10; i++) h.Menus.Move(InputAction.MenuDown);
            Assert.Equal(1, list.FirstRow); // the tenth press scrolls by one entry
            Assert.Equal(10, list.SelectedButtonId);
        }

        [Fact]
        public void Shop_LeftAndRightPageTheList_ThenGoToTheEnds()
        {
            var h = new ShopHarness();
            h.Inventory.OpenShop(4);
            var items = new ShopItemEntry[25];
            for (int i = 0; i < items.Length; i++) items[i] = new ShopItemEntry((uint)(10 + i), 639, (byte)i, 0, 0);
            h.Inventory.AddShopItems(items);
            h.Menus.Activate();
            var list = h.Menus.Top!;
            h.Menus.Move(InputAction.MenuDown);
            h.Menus.Move(InputAction.MenuDown); // row 3
            h.Menus.Move(InputAction.MenuRight); // page 2: rows 11-20, the cursor keeps its row
            Assert.Equal((10, 3), (list.FirstRow, list.SelectedButtonId));
            h.Menus.Move(InputAction.MenuRight); // the last page: rows 16-25
            Assert.Equal((15, 3), (list.FirstRow, list.SelectedButtonId));
            h.Menus.Move(InputAction.MenuRight); // no page left: the bottom
            Assert.Equal((15, 10), (list.FirstRow, list.SelectedButtonId));
            Assert.Equal(24, list.SelectedShopRow!.Value.ShopIndex);
            h.Menus.Move(InputAction.MenuLeft); // a page back
            Assert.Equal((5, 10), (list.FirstRow, list.SelectedButtonId));
            h.Menus.Move(InputAction.MenuLeft);
            Assert.Equal((0, 10), (list.FirstRow, list.SelectedButtonId));
            h.Menus.Move(InputAction.MenuLeft); // no page left: the top
            Assert.Equal((0, 1), (list.FirstRow, list.SelectedButtonId));

            // A short list only moves the cursor between its ends.
            h.Menus.CloseTop(); // back to Buy / Sell
            h.Inventory.OpenShop(4);
            h.Inventory.AddShopItems(items.AsSpan(0, 4).ToArray());
            h.Menus.Activate(); // Buy
            list = h.Menus.Top!;
            Assert.True(list.IsShopList);
            Assert.Equal(4, list.ShopRows.Count);
            h.Menus.Move(InputAction.MenuRight);
            Assert.Equal((0, 4), (list.FirstRow, list.SelectedButtonId));
            h.Menus.Move(InputAction.MenuLeft);
            Assert.Equal((0, 1), (list.FirstRow, list.SelectedButtonId));
        }

        [Fact]
        public void Shop_MessagesUseTheLogNames()
        {
            var chestnut = new ItemRecord { ItemId = 639, Name = "Chestnut", LogName = "Ronfaure chestnut", LogPlural = "Ronfaure chestnuts" };
            Assert.Equal("You buy 12 Ronfaure chestnuts from the shop.", StockUiShop.BuyMessage(chestnut, 12));
            Assert.Equal("You buy a Ronfaure chestnut from the shop.", StockUiShop.BuyMessage(chestnut, 1));
            Assert.Equal("You sell a Ronfaure chestnut to the shop.", StockUiShop.SellMessage(chestnut, 1));
            var log = new ItemRecord { ItemId = 688, Name = "Elm Log", LogName = "elm log", LogPlural = "elm logs" };
            Assert.Equal("You sell an elm log to the shop.", StockUiShop.SellMessage(log, 1));
            Assert.Equal("You sell 1,000 elm logs to the shop.", StockUiShop.SellMessage(log, 1000));
        }

        [Fact]
        public void Shop_QuantityArrowsTakeTheMouse()
        {
            var h = new ShopHarness();
            h.OpenCapturedShop();
            h.Menus.Activate();
            h.Menus.Move(InputAction.MenuDown);
            h.Menus.Move(InputAction.MenuDown);
            h.Menus.Activate();
            var quantity = h.Menus.Top!;
            Assert.True(quantity.IsQuantity);
            h.Menus.SetScreenPlacements(new[] { new StockUiMenuPlacement(quantity, 100, 200, 1) });

            Assert.True(h.Menus.OnMouseDown(MouseButton.Left, 100 + 40 + 6, 200 + 7 + 5)); // +
            Assert.Equal(2u, quantity.Quantity);
            Assert.True(h.Menus.OnMouseDown(MouseButton.Left, 100 + 19 + 5, 200 + 22 + 6)); // All
            Assert.Equal(12u, quantity.Quantity);
            Assert.True(h.Menus.OnMouseDown(MouseButton.Left, 100 + 40 + 6, 200 + 40 + 5)); // -
            Assert.Equal(11u, quantity.Quantity);
            Assert.True(h.Menus.OnMouseWheel(100 + 50, 200 + 30, -3));
            Assert.Equal(8u, quantity.Quantity);
            Assert.True(h.Menus.OnMouseMove(100 + 88 + 5, 200 + 22 + 6)); // hovering an arrow leaves the field selected
            Assert.Equal(StockUiShop.QuantityField, quantity.SelectedButtonId);
            Assert.True(h.Menus.OnMouseDown(MouseButton.Left, 100 + 34 + 12, 200 + 22 + 8)); // the field confirms
            Assert.Equal((8u, (ushort)0x0400, (ushort)2), Assert.Single(h.Buys));
            Assert.True(h.Menus.Top!.IsShopList);
        }

        [Fact]
        public void RetailMenus_ShopWindowsResolve()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            if (library == null) return;

            // Buy / Sell: two label sprites on the 112 x 40 frame at the bottom left.
            Assert.True(library.TryGetMenu(StockUiShop.MenuName, out var main));
            Assert.Equal((16, 256, 112, 40), (main.Frame.X, main.Frame.Y, main.Frame.Width, main.Frame.Height));
            foreach (int id in new[] { StockUiShop.BuyButton, StockUiShop.SellButton })
            {
                Assert.Contains(main.FindButton(id)!.Shapes, s => s.Kind == 0 && library.TryGetImage(s, out _));
            }

            // The list: ten rows with the "anc_shop" cursor (arrow plus highlight bar) and an icon slot per row.
            Assert.True(library.TryGetMenu(StockUiShop.ListMenu, out var list));
            Assert.Equal((16, 48, 256, 190), (list.Frame.X, list.Frame.Y, list.Frame.Width, list.Frame.Height));
            Assert.Equal(StockUiShop.ListRows, list.Buttons.Count(StockUiMenuController.IsSelectable));
            Assert.Equal(18, list.FindButton(2)!.Y - list.FindButton(1)!.Y);
            Assert.Contains(list.Frame.Shapes, s => s.Kind == 6 && s.GroupName.Equals("anc_shop", StringComparison.OrdinalIgnoreCase)
                && library.TryGetGroup(s.GroupId, out var g) && g.Images.Count == 6);
            var frameShape = Assert.Single(list.Frame.Shapes, s => s.Kind == 0);
            Assert.True(library.TryGetImage(frameShape, out var frameImage));
            Assert.Equal(StockUiShop.ListRows, frameImage.Parts.Count(p => UiResourceLibrary.TrimResourceName(p.TextureName).Equals("itemslot", StringComparison.OrdinalIgnoreCase)));

            // The quantity control: the number field and its four arrows; the gil and item info windows.
            Assert.True(library.TryGetMenu(StockUiShop.QuantityMenu, out var quantity));
            Assert.Equal((34, 22), (quantity.FindButton(StockUiShop.QuantityField)!.X, quantity.FindButton(StockUiShop.QuantityField)!.Y));
            foreach (int id in new[] { StockUiShop.QuantityAllButton, StockUiShop.QuantityOneButton, StockUiShop.QuantityDownButton, StockUiShop.QuantityUpButton })
            {
                Assert.NotNull(quantity.FindButton(id));
                Assert.False(StockUiMenuController.IsSelectable(quantity.FindButton(id)!));
            }
            Assert.True(library.TryGetMenu(StockUiShop.GilMenu, out var gil));
            Assert.Equal((16, 240, 112, 56), (gil.Frame.X, gil.Frame.Y, gil.Frame.Width, gil.Frame.Height));
            Assert.True(library.TryGetMenu(StockUiShop.InfoMenu, out var info));
            Assert.Equal((16, 240, 366, 56), (info.Frame.X, info.Frame.Y, info.Frame.Width, info.Frame.Height));

            // The flow runs on the retail frames.
            var h = new ShopHarness();
            h.Menus.Library = library;
            h.OpenCapturedShop();
            h.Menus.Activate();
            Assert.True(h.Menus.Top!.IsShopList);
            Assert.Equal(StockUiShop.ListRows, h.Menus.Top.VisibleRows);
            h.Menus.Activate();
            Assert.True(h.Menus.Top!.IsQuantity);
        }

        #endregion
    }
}
