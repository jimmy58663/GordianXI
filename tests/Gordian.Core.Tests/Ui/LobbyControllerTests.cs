// tests/Gordian.Core.Tests/Ui/LobbyControllerTests.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Network.LandSandBoat;
using Gordian.Core.Ui.Lobby;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    public class LobbyControllerTests
    {
        internal sealed class FakeBackend : ILobbyBackend
        {
            public List<LobbyCharacter> List { get; } = new();
            public IReadOnlyList<LobbyCharacter> Characters => List;
            public bool IsConnected { get; set; } = true;
            public Func<LobbyCharacter, Task<LsbSessionTicket>>? OnSelect { get; set; }
            public Func<LobbyCharacter, Task>? OnDelete { get; set; }
            public Func<LobbyCharacter, LobbyCharacterCreation, Task>? OnCreate { get; set; }
            public Func<string, Task>? OnCheckName { get; set; }
            public Func<LobbyCharacter, string, Task<LsbSessionTicket>>? OnRename { get; set; }
            public List<string> Calls { get; } = new();

            public Task<IReadOnlyList<LobbyCharacter>> RefreshCharactersAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<LobbyCharacter>>(List);
            public Task<IReadOnlyList<LobbyWorld>> GetWorldsAsync(CancellationToken ct = default)
            {
                Calls.Add("worlds");
                return Task.FromResult<IReadOnlyList<LobbyWorld>>(new[] { new LobbyWorld(0x20, "Gordian") });
            }
            public Task CheckNameAsync(LobbyCharacter freeSlot, string name, string worldName, CancellationToken ct = default)
            {
                Calls.Add($"check {name} {worldName}");
                return OnCheckName?.Invoke(name) ?? Task.CompletedTask;
            }
            public Task CreateCharacterAsync(LobbyCharacter freeSlot, LobbyCharacterCreation creation, CancellationToken ct = default)
            {
                Calls.Add($"create {creation.Name}");
                return OnCreate?.Invoke(freeSlot, creation) ?? Task.CompletedTask;
            }
            public Task DeleteCharacterAsync(LobbyCharacter character, CancellationToken ct = default)
            {
                Calls.Add($"delete {character.Name}");
                return OnDelete?.Invoke(character) ?? Task.CompletedTask;
            }
            public Task<LsbSessionTicket> RenameAndSelectAsync(LobbyCharacter character, string newName, CancellationToken ct = default)
            {
                Calls.Add($"rename {character.Name} {newName}");
                return OnRename?.Invoke(character, newName) ?? Task.FromResult(new LsbSessionTicket { CharacterName = newName });
            }
            public Task<LsbSessionTicket> SelectCharacterAsync(LobbyCharacter character, CancellationToken ct = default)
            {
                Calls.Add($"select {character.Name}");
                return OnSelect?.Invoke(character) ?? Task.FromResult(new LsbSessionTicket { CharacterName = character.Name, CharacterId = character.ContentId });
            }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        internal static LobbyCharacter Character(int slot, string name, bool rename = false) =>
            new(slot, (uint)(1000 + slot), (uint)(1000 + slot), 0, 1, rename, false, name, "Gordian", 1, 1, 0, 1, 0, 0, 1, 230, default, default);

        internal static LobbyCharacter Free(int slot) =>
            new(slot, 0, 0, 0, 1, false, false, string.Empty, string.Empty, 0, 0, 0, 0, 0, 0, 0, 0, default, default);

        internal static FakeBackend Backend(params LobbyCharacter[] characters)
        {
            var backend = new FakeBackend();
            backend.List.AddRange(characters);
            return backend;
        }

        /// <summary>Waits for the controller's background request to finish.</summary>
        internal static void WaitIdle(LobbyController lobby)
        {
            for (int i = 0; i < 200 && lobby.IsBusy; i++) Thread.Sleep(10);
            Assert.False(lobby.IsBusy, "the lobby request did not finish");
        }

        [Fact]
        public void MainMenu_NavigatesInScreenOrderAndWraps()
        {
            var lobby = new LobbyController(Backend(Character(1, "Knot")), null, null, showLicence: false);
            Assert.Equal(LobbyScreen.MainMenu, lobby.Screen);
            Assert.Equal(LobbyController.SelectButton, lobby.MainMenu!.SelectedButtonId);
            lobby.HandleInput(LobbyInput.Down);
            Assert.Equal(LobbyController.CreateButton, lobby.MainMenu.SelectedButtonId);
            lobby.HandleInput(LobbyInput.Down);
            lobby.HandleInput(LobbyInput.Down);
            Assert.Equal(LobbyController.ConfigButton, lobby.MainMenu.SelectedButtonId);
            lobby.HandleInput(LobbyInput.Down);
            Assert.Equal(LobbyController.BackButton, lobby.MainMenu.SelectedButtonId);
            lobby.HandleInput(LobbyInput.Down);
            Assert.Equal(LobbyController.SelectButton, lobby.MainMenu.SelectedButtonId);
            lobby.HandleInput(LobbyInput.Up);
            Assert.Equal(LobbyController.BackButton, lobby.MainMenu.SelectedButtonId);
        }

        [Fact]
        public void Licence_AcceptShowsTheTitleMenu()
        {
            var lobby = new LobbyController(Backend(Character(1, "Knot")), null, null);
            Assert.True(lobby.IsLicencePending);
            Assert.Equal(LobbyController.LicencePromptMenu, lobby.Prompt!.Menu.Name);
            Assert.Equal(LobbyController.PromptFirstButton, lobby.Prompt.Menu.SelectedButtonId); // Accept
            lobby.HandleInput(LobbyInput.Down); // the title menu does not take input yet
            Assert.Equal(LobbyController.SelectButton, lobby.MainMenu!.SelectedButtonId);
            lobby.HandleInput(LobbyInput.Confirm);
            Assert.False(lobby.IsLicencePending);
            Assert.Null(lobby.Prompt);
            Assert.Equal(LobbyScreen.MainMenu, lobby.Screen);
        }

        [Fact]
        public void Licence_DeclineLeavesTheLobby()
        {
            var lobby = new LobbyController(Backend(Character(1, "Knot")), null, null);
            bool closed = false;
            lobby.Closed += _ => closed = true;
            lobby.HandleInput(LobbyInput.Right); // Decline
            lobby.HandleInput(LobbyInput.Confirm);
            Assert.True(closed);
            Assert.Equal(LobbyScreen.Closed, lobby.Screen);
        }

        [Fact]
        public void Preview_StandsAlive()
        {
            var preview = new LobbyPreview();
            preview.Show(Character(1, "Knot"));
            Assert.Equal(100, preview.Entity!.Hpp);
            Assert.Equal(Gordian.Core.Animation.AnimationCategory.Idle,
                Gordian.Core.Animation.AnimationStateClassifier.Classify(preview.Entity, false, isLocalPlayer: true));
        }

        [Fact]
        public void Back_ClosesTheLobby()
        {
            var lobby = new LobbyController(Backend(Character(1, "Knot")), null, null, showLicence: false);
            string? reason = "unset";
            lobby.Closed += r => reason = r;
            lobby.HandleInput(LobbyInput.Up); // Back
            lobby.HandleInput(LobbyInput.Confirm);
            Assert.Equal(LobbyScreen.Closed, lobby.Screen);
            Assert.Null(reason);
        }

        [Fact]
        public void List_SkipsFreeSlotsAndPreviewsTheSelection()
        {
            var lobby = new LobbyController(Backend(Free(1), Character(2, "Knot"), Free(3), Character(4, "Blm")), null, null, showLicence: false);
            lobby.HandleInput(LobbyInput.Confirm);
            Assert.Equal(LobbyScreen.CharacterList, lobby.Screen);
            Assert.Equal(2, lobby.CharacterList!.SelectedButtonId);
            Assert.Equal("Knot", lobby.PreviewCharacter!.Name);
            lobby.HandleInput(LobbyInput.Down);
            Assert.Equal("Blm", lobby.PreviewCharacter!.Name);
            lobby.HandleInput(LobbyInput.Down); // slots 5-8 and the right column are absent: wraps back to slot 2
            Assert.Equal("Knot", lobby.PreviewCharacter!.Name);
            lobby.HandleInput(LobbyInput.Cancel);
            Assert.Equal(LobbyScreen.MainMenu, lobby.Screen);
            Assert.Null(lobby.PreviewCharacter);
        }

        [Fact]
        public void Select_RaisesTheTicket()
        {
            var backend = Backend(Character(1, "Knot"));
            var lobby = new LobbyController(backend, null, null, showLicence: false);
            LsbSessionTicket? ticket = null;
            lobby.CharacterSelected += t => ticket = t;
            lobby.HandleInput(LobbyInput.Confirm);
            lobby.HandleInput(LobbyInput.Confirm);
            WaitIdle(lobby);
            Assert.Equal("Knot", ticket?.CharacterName);
            Assert.Equal(LobbyScreen.Entering, lobby.Screen);
            Assert.Contains("select Knot", backend.Calls);
        }

        [Fact]
        public void Select_RefusedShowsTheErrorAndReturnsToTheList()
        {
            var backend = Backend(Character(1, "Knot"));
            backend.OnSelect = _ => Task.FromException<LsbSessionTicket>(LobbyRequestException.FromServer("select", LobbyErrorCode.CharacterAlreadyLoggedIn));
            var lobby = new LobbyController(backend, null, null, showLicence: false);
            lobby.HandleInput(LobbyInput.Confirm);
            lobby.HandleInput(LobbyInput.Confirm);
            WaitIdle(lobby);
            Assert.NotNull(lobby.Prompt);
            Assert.Contains(lobby.Prompt!.Lines, l => l.Contains("3201"));
            lobby.HandleInput(LobbyInput.Confirm); // OK
            Assert.Null(lobby.Prompt);
            Assert.Equal(LobbyScreen.CharacterList, lobby.Screen);
        }

        [Fact]
        public void Select_LostConnectionClosesAfterTheMessage()
        {
            var backend = Backend(Character(1, "Knot"));
            backend.OnSelect = _ =>
            {
                backend.IsConnected = false;
                return Task.FromException<LsbSessionTicket>(new LobbyRequestException("select", 0, "connection lost"));
            };
            var lobby = new LobbyController(backend, null, null, showLicence: false);
            string? reason = null;
            lobby.Closed += r => reason = r;
            lobby.HandleInput(LobbyInput.Confirm);
            lobby.HandleInput(LobbyInput.Confirm);
            WaitIdle(lobby);
            Assert.NotNull(lobby.Prompt);
            lobby.HandleInput(LobbyInput.Cancel);
            Assert.Equal(LobbyScreen.Closed, lobby.Screen);
            Assert.Equal("connection lost", reason);
        }

        [Fact]
        public void Mouse_HoverMovesTheCursorAndClickActivates()
        {
            var lobby = new LobbyController(Backend(Character(1, "Knot"), Character(2, "Blm")), null, null, showLicence: false);
            var main = lobby.ActiveMenu!;
            lobby.PointAt(main, LobbyController.DeleteButton);
            Assert.Equal(LobbyController.DeleteButton, main.SelectedButtonId);
            lobby.Activate(main, LobbyController.SelectButton);
            Assert.Equal(LobbyScreen.CharacterList, lobby.Screen);
            var list = lobby.ActiveMenu!;
            lobby.PointAt(list, 5); // a free slot: ignored
            Assert.Equal(1, list.SelectedButtonId);
            lobby.PointAt(list, 2);
            Assert.Equal("Blm", lobby.PreviewCharacter!.Name);
            lobby.PointAt(main, LobbyController.BackButton); // not the window taking input
            Assert.Equal(LobbyController.SelectButton, main.SelectedButtonId);
        }
    }
}
