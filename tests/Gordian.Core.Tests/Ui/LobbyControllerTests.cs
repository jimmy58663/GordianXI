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
        public void Preview_CarriesTheChosenSize()
        {
            var preview = new LobbyPreview();
            preview.Show(new LobbyCharacterCreation("Big", 8, 0, 0, 1, Size: 2, 0));
            Assert.Equal(2, preview.Entity!.GraphSize);
            preview.Show(new LobbyCharacterCreation("Big", 8, 0, 0, 1, Size: 0, 0)); // a size change rebuilds the preview
            Assert.Equal(0, preview.Entity!.GraphSize);
            Assert.True(Gordian.Core.World.PlayerSizeScale.For(0) < Gordian.Core.World.PlayerSizeScale.For(1));
            Assert.True(Gordian.Core.World.PlayerSizeScale.For(2) > Gordian.Core.World.PlayerSizeScale.For(1));
            Assert.Equal(1f, Gordian.Core.World.PlayerSizeScale.For(7));
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

        private static LobbyController OpenCreation(FakeBackend backend)
        {
            var lobby = new LobbyController(backend, null, null, showLicence: false);
            lobby.HandleInput(LobbyInput.Down); // Create Character
            lobby.HandleInput(LobbyInput.Confirm);
            return lobby;
        }

        [Fact]
        public void Create_WalksTheStepsWithALivePreviewAndCreatesThenPlays()
        {
            var backend = Backend(Character(1, "Knot"), Free(2), Free(3));
            backend.OnCreate = (slot, creation) =>
            {
                backend.List[slot.Slot - 1] = Character(slot.Slot, creation.Name) with { Race = creation.Race, Face = creation.CombinedFace };
                return Task.CompletedTask;
            };
            var lobby = OpenCreation(backend);
            LsbSessionTicket? ticket = null;
            lobby.CharacterSelected += t => ticket = t;

            Assert.Equal(LobbyScreen.Creation, lobby.Screen);
            Assert.Equal(LobbyCreationStep.Race, lobby.CreationStep);
            Assert.Equal(1, lobby.PreviewCreation.Race);
            for (int i = 0; i < 6; i++) lobby.HandleInput(LobbyInput.Down);
            Assert.Equal(7, lobby.PreviewCreation.Race); // Mithra under the cursor shows before it is chosen
            lobby.HandleInput(LobbyInput.Confirm);
            Assert.Equal(LobbyCreationStep.Face, lobby.CreationStep);
            lobby.HandleInput(LobbyInput.Down);
            lobby.HandleInput(LobbyInput.Down);
            lobby.HandleInput(LobbyInput.Confirm); // face 3 (index 2)
            lobby.HandleInput(LobbyInput.Down);
            lobby.HandleInput(LobbyInput.Confirm); // hair B
            lobby.HandleInput(LobbyInput.Left); // back to hair: the page arrow
            Assert.Equal(LobbyCreationStep.Hair, lobby.CreationStep);
            Assert.Equal(2, lobby.CreationMenu!.SelectedButtonId); // keeps the choice
            lobby.HandleInput(LobbyInput.Right); // forward again with it
            lobby.HandleInput(LobbyInput.Confirm); // size medium (the default)
            lobby.HandleInput(LobbyInput.Up);
            lobby.HandleInput(LobbyInput.Confirm); // job: up from Warrior wraps to Thief
            lobby.HandleInput(LobbyInput.Down);
            lobby.HandleInput(LobbyInput.Down);
            lobby.HandleInput(LobbyInput.Confirm); // Windurst
            Assert.Equal(LobbyCreationStep.Name, lobby.CreationStep);
            var look = lobby.PreviewCreation;
            Assert.Equal((7, 2, 1, 1, 6, 2), (look.Race, look.Face, look.Hair, look.Size, look.MainJob, look.Nation));

            lobby.HandleText("ab");
            lobby.HandleInput(LobbyInput.Confirm);
            Assert.NotNull(lobby.Prompt); // too short: FFXI-3110
            Assert.Contains(lobby.Prompt!.Lines, l => l.Contains("3110"));
            lobby.HandleInput(LobbyInput.Confirm);
            lobby.HandleText("CDE1f");
            Assert.Equal("Abcdef", lobby.NameText); // letters only, retail case
            lobby.HandleInput(LobbyInput.Backspace);
            Assert.Equal("Abcde", lobby.NameText);
            lobby.HandleInput(LobbyInput.Confirm);
            WaitIdle(lobby);
            Assert.Equal(LobbyCreationStep.World, lobby.CreationStep);
            Assert.Equal("Gordian", Assert.Single(lobby.Worlds).Name);

            lobby.HandleInput(LobbyInput.Confirm);
            WaitIdle(lobby);
            Assert.Contains("check Abcde Gordian", backend.Calls);
            Assert.NotNull(lobby.Prompt); // Register "Abcde" and begin play?
            Assert.Equal(LobbyController.PromptFirstButton, lobby.Prompt!.Menu.SelectedButtonId);
            lobby.HandleInput(LobbyInput.Confirm);
            WaitIdle(lobby);
            Assert.Contains("create Abcde", backend.Calls);
            Assert.Contains("select Abcde", backend.Calls);
            Assert.Equal("Abcde", ticket?.CharacterName);
            Assert.Equal(LobbyScreen.Entering, lobby.Screen);
        }

        [Fact]
        public void Create_TakenNameGoesBackToTheNameStep()
        {
            var backend = Backend(Character(1, "Knot"), Free(2));
            backend.OnCheckName = _ => Task.FromException(LobbyRequestException.FromServer("name check", LobbyErrorCode.CharacterNameUnavailable));
            var lobby = OpenCreation(backend);
            for (int i = 0; i < 6; i++) lobby.HandleInput(LobbyInput.Confirm); // race .. nation with the defaults
            lobby.HandleText("knot");
            lobby.HandleInput(LobbyInput.Confirm);
            WaitIdle(lobby);
            lobby.HandleInput(LobbyInput.Confirm); // the world
            WaitIdle(lobby);
            Assert.Contains(lobby.Prompt!.Lines, l => l.Contains("3313"));
            lobby.HandleInput(LobbyInput.Confirm);
            Assert.Equal(LobbyCreationStep.Name, lobby.CreationStep);
            Assert.DoesNotContain(backend.Calls, c => c.StartsWith("create"));
        }

        [Fact]
        public void Create_WithoutAFreeSlotSaysSo()
        {
            var lobby = OpenCreation(Backend(Character(1, "Knot")));
            Assert.Equal(LobbyScreen.MainMenu, lobby.Screen);
            Assert.NotNull(lobby.Prompt);
        }

        [Fact]
        public void Create_CancelWalksBackToTheTitleMenu()
        {
            var lobby = OpenCreation(Backend(Free(1)));
            lobby.HandleInput(LobbyInput.Confirm); // race -> face
            lobby.HandleInput(LobbyInput.Cancel);
            Assert.Equal(LobbyCreationStep.Race, lobby.CreationStep);
            lobby.HandleInput(LobbyInput.Cancel);
            Assert.Equal(LobbyScreen.MainMenu, lobby.Screen);
        }

        [Fact]
        public void Rename_AsksForANewNameThenPlays()
        {
            var backend = Backend(Character(1, "Badname", rename: true));
            var lobby = new LobbyController(backend, null, null, showLicence: false);
            LsbSessionTicket? ticket = null;
            lobby.CharacterSelected += t => ticket = t;
            lobby.HandleInput(LobbyInput.Confirm); // list
            lobby.HandleInput(LobbyInput.Confirm); // the character: rename required
            Assert.NotNull(lobby.Prompt);
            lobby.HandleInput(LobbyInput.Confirm);
            Assert.Equal(LobbyCreationStep.Name, lobby.CreationStep);
            Assert.True(lobby.IsRenaming);
            lobby.HandleText("goodname");
            lobby.HandleInput(LobbyInput.Confirm);
            WaitIdle(lobby);
            Assert.Contains("rename Badname Goodname", backend.Calls);
            Assert.Equal("Goodname", ticket?.CharacterName);
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
