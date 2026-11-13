// tests/Gordian.Engine.Tests/Ui/Lobby/LobbyGamepadTests.cs
using System;
using System.Numerics;
using Gordian.Core.Network.Lobby;
using Gordian.Engine.Input;
using Gordian.Engine.Ui;
using Gordian.Engine.Ui.Lobby;
using Xunit;
using static Gordian.Engine.Tests.Ui.LobbyControllerTests;

namespace Gordian.Engine.Tests.Ui.Lobby
{
    /// <summary>The gamepad in the lobby (#553): the profile's menu buttons drive every screen through <see cref="LobbyGamepad"/>.</summary>
    public class LobbyGamepadTests
    {
        private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(16);

        private static GamepadState Pad(GamepadButton buttons) => new(true, buttons, Vector2.Zero, Vector2.Zero, 0f, 0f);

        private static readonly GamepadState Released = Pad(GamepadButton.None);

        /// <summary>Presses and releases <paramref name="button"/> (two ticks).</summary>
        private static void Press(LobbyGamepad pad, LobbyController lobby, InputProfile profile, GamepadButton button)
        {
            pad.Update(lobby, Pad(button), profile, Tick);
            pad.Update(lobby, Released, profile, Tick);
        }

        /// <summary>A pad that has already seen the lobby with nothing held (so the first press counts).</summary>
        private static LobbyGamepad Ready(LobbyController lobby, InputProfile profile)
        {
            var pad = new LobbyGamepad();
            pad.Update(lobby, Released, profile, Tick);
            return pad;
        }

        [Fact]
        public void DPadMovesTheTitleCursor_AndAHeldDirectionRepeatsAtTheMenuRate()
        {
            var profile = InputProfile.CreateCompact();
            var lobby = new LobbyController(Backend(Character(1, "Knot")), null, null, showLicence: false);
            var pad = Ready(lobby, profile);

            Assert.Equal(new[] { LobbyInput.Down }, pad.Update(lobby, Pad(GamepadButton.DPadDown), profile, Tick));
            Assert.Equal(LobbyController.CreateButton, lobby.MainMenu!.SelectedButtonId);
            // Held: nothing more until the repeat delay, then one step per repeat interval.
            var held = StockUiMenuController.RepeatDelay - TimeSpan.FromMilliseconds(5);
            Assert.Empty(pad.Update(lobby, Pad(GamepadButton.DPadDown), profile, held));
            Assert.Single(pad.Update(lobby, Pad(GamepadButton.DPadDown), profile, TimeSpan.FromMilliseconds(10)));
            Assert.Equal(LobbyController.DeleteButton, lobby.MainMenu.SelectedButtonId);
            Assert.Empty(pad.Update(lobby, Pad(GamepadButton.DPadDown), profile, StockUiMenuController.RepeatInterval - TimeSpan.FromMilliseconds(5)));
            Assert.Single(pad.Update(lobby, Pad(GamepadButton.DPadDown), profile, TimeSpan.FromMilliseconds(10)));
            Assert.Equal(LobbyController.ConfigButton, lobby.MainMenu.SelectedButtonId);

            pad.Update(lobby, Released, profile, Tick);
            Press(pad, lobby, profile, GamepadButton.DPadUp);
            Assert.Equal(LobbyController.DeleteButton, lobby.MainMenu.SelectedButtonId);
        }

        [Fact]
        public void ConfirmOpensTheList_AndPlaysTheCharacter_CancelGoesBack()
        {
            var profile = InputProfile.CreateCompact();
            var backend = Backend(Character(1, "Knot"), Character(2, "Blm"));
            var lobby = new LobbyController(backend, null, null, showLicence: false);
            MapSessionTicket? ticket = null;
            lobby.CharacterSelected += t => ticket = t;
            var pad = Ready(lobby, profile);

            Press(pad, lobby, profile, GamepadButton.A); // Select Character
            Assert.Equal(LobbyScreen.CharacterList, lobby.Screen);
            Press(pad, lobby, profile, GamepadButton.B);
            Assert.Equal(LobbyScreen.MainMenu, lobby.Screen);

            Press(pad, lobby, profile, GamepadButton.A);
            Press(pad, lobby, profile, GamepadButton.DPadDown);
            Assert.Equal(2, lobby.CharacterList!.SelectedButtonId);
            Press(pad, lobby, profile, GamepadButton.A);
            WaitIdle(lobby);
            Assert.Equal("Blm", ticket?.CharacterName);
            Assert.Equal(LobbyScreen.Entering, lobby.Screen);
        }

        [Fact]
        public void AcceptsTheLicence_AndDeletesThroughThePrompt()
        {
            var profile = InputProfile.CreateCompact();
            var backend = Backend(Character(1, "Knot"));
            var lobby = new LobbyController(backend, null, null);
            var pad = Ready(lobby, profile);

            Press(pad, lobby, profile, GamepadButton.A); // Accept
            Assert.False(lobby.IsLicencePending);
            Press(pad, lobby, profile, GamepadButton.DPadDown);
            Press(pad, lobby, profile, GamepadButton.DPadDown);
            Press(pad, lobby, profile, GamepadButton.A); // Delete Character
            Assert.True(lobby.IsDeleteList);
            Press(pad, lobby, profile, GamepadButton.A); // Knot
            Assert.Equal(LobbyController.DeletePromptMenu, lobby.Prompt!.Menu.Name);
            Press(pad, lobby, profile, GamepadButton.DPadLeft); // to Delete
            Press(pad, lobby, profile, GamepadButton.A);
            WaitIdle(lobby);
            Assert.Contains("delete Knot", backend.Calls);
        }

        [Fact]
        public void WalksCreation_ExceptTypingTheName()
        {
            var profile = InputProfile.CreateCompact();
            var lobby = new LobbyController(Backend(Character(1, "Knot"), Free(2)), null, null, showLicence: false);
            var pad = Ready(lobby, profile);

            Press(pad, lobby, profile, GamepadButton.DPadDown);
            Press(pad, lobby, profile, GamepadButton.A); // Create Character
            Assert.Equal(LobbyScreen.Creation, lobby.Screen);
            Press(pad, lobby, profile, GamepadButton.DPadDown);
            Press(pad, lobby, profile, GamepadButton.A); // race 2
            Assert.Equal(LobbyCreationStep.Face, lobby.CreationStep);
            for (int i = 0; i < 5; i++) Press(pad, lobby, profile, GamepadButton.A); // face, hair, size, job, nation
            Assert.Equal(LobbyCreationStep.Name, lobby.CreationStep);
            Assert.Equal(2, lobby.PreviewCreation.Race);
            lobby.HandleText("Gamepad"); // the keyboard types the name
            Press(pad, lobby, profile, GamepadButton.A);
            WaitIdle(lobby);
            Assert.Equal(LobbyCreationStep.World, lobby.CreationStep);
            Press(pad, lobby, profile, GamepadButton.B); // back to the name
            Assert.Equal(LobbyCreationStep.Name, lobby.CreationStep);
        }

        [Fact]
        public void FollowsTheProfilesAssignment()
        {
            var profile = InputProfile.CreateCompact();
            var confirm = Assert.Single(GamepadAssignments.Functions, f => f.Actions[0] == InputAction.Confirm);
            GamepadAssignments.Assign(profile, confirm, GamepadButton.X);
            var lobby = new LobbyController(Backend(Character(1, "Knot")), null, null, showLicence: false);
            var pad = Ready(lobby, profile);

            Press(pad, lobby, profile, GamepadButton.A); // A took X's old function (Open Main Menu): nothing here
            Assert.Equal(LobbyScreen.MainMenu, lobby.Screen);
            Press(pad, lobby, profile, GamepadButton.X);
            Assert.Equal(LobbyScreen.CharacterList, lobby.Screen);
        }

        [Fact]
        public void ButtonsHeldWhenALobbyTakesThePad_WaitUntilLetGo()
        {
            var profile = InputProfile.CreateCompact();
            var lobby = new LobbyController(Backend(Character(1, "Knot")), null, null, showLicence: false);
            var pad = new LobbyGamepad();
            pad.Update(null, Pad(GamepadButton.A), profile, Tick); // A held in game (it chose Log Out)

            Assert.Empty(pad.Update(lobby, Pad(GamepadButton.A), profile, Tick));
            Assert.Empty(pad.Update(lobby, Pad(GamepadButton.A), profile, Tick));
            Assert.Equal(LobbyScreen.MainMenu, lobby.Screen);
            pad.Update(lobby, Released, profile, Tick);
            Assert.Equal(new[] { LobbyInput.Confirm }, pad.Update(lobby, Pad(GamepadButton.A), profile, Tick));
        }

        [Fact]
        public void WithoutALobby_NothingIsHandedOver() =>
            Assert.Empty(new LobbyGamepad().Update(null, Pad(GamepadButton.A), InputProfile.CreateCompact(), Tick));
    }
}
