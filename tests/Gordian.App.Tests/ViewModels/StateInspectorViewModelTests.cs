// tests/Gordian.App.Tests/ViewModels/StateInspectorViewModelTests.cs
using System;
using System.Numerics;
using Gordian.App.ViewModels;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.ViewModels
{
    public sealed class StateInspectorViewModelTests : IDisposable
    {
        private readonly SessionRegistry _registry;
        private readonly StateInspectorViewModel _vm;

        public StateInspectorViewModelTests()
        {
            StateInspectorViewModel.UiDispatcher = a => a();
            _registry = new SessionRegistry();
            _vm = new StateInspectorViewModel(_registry);
        }

        public void Dispose()
        {
            _vm.Dispose();
            StateInspectorViewModel.UiDispatcher = null;
        }

        [Fact]
        public void InitialState_WithoutSession_HasSafeDefaults()
        {
            Assert.False(_vm.HasActiveSession);
            Assert.Equal("No Session", _vm.CharacterName);
            Assert.Equal(0, _vm.CurrentHp);
            Assert.Equal(0.0, _vm.HpPercent);
            Assert.Equal(0, _vm.TotalEntityCount);
            Assert.Empty(_vm.FilteredEntities);
        }

        [Fact]
        public void RegisterSession_AutoSelectsSessionAndPopulatesProperties()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 12345, "user1", netManager);

            _registry.RegisterSession(session);

            Assert.True(_vm.HasActiveSession);
            Assert.Equal("Cybin", _vm.CharacterName);
            Assert.Contains("12345", _vm.CharacterIdDisplay);
            Assert.Contains("127.0.0.1", _vm.ServerAddressDisplay);
        }

        [Fact]
        public void PlayerVitals_UpdateWhenEventFires()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 12345, "user1", netManager);
            _registry.RegisterSession(session);

            session.LocalPlayer.UpdateVitals(750, 320, 1500);

            Assert.Equal(750, _vm.CurrentHp);
            Assert.Equal(320, _vm.CurrentMp);
            Assert.Equal(1500, _vm.CurrentTp);
            Assert.Equal(0.5, _vm.TpPercent, 2);
            Assert.Contains("750", _vm.HpText);
            Assert.Contains("320", _vm.MpText);
        }

        [Fact]
        public void WorldEntity_Spawn_Filter_Despawn_CycleWorks()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 12345, "user1", netManager);
            _registry.RegisterSession(session);

            var mob = new WorldEntity(1001, 0x020, EntityType.Monster)
            {
                Name = "Forest Hare",
                Position = new Vector3(10f, 0f, 5f),
                Hpp = 100
            };

            var player = new WorldEntity(1002, 0x021, EntityType.Player)
            {
                Name = "Destin",
                Position = new Vector3(2f, 0f, 1f),
                Hpp = 100
            };

            session.World.UpsertEntity(mob);
            session.World.UpsertEntity(player);

            Assert.Equal(2, _vm.TotalEntityCount);
            Assert.Equal(2, _vm.FilteredEntities.Count);

            // Filter by Monster
            _vm.EntityTypeFilter = "Monster";
            Assert.Single(_vm.FilteredEntities);
            Assert.Equal("Forest Hare", _vm.FilteredEntities[0].Name);

            // Filter by Player
            _vm.EntityTypeFilter = "Player";
            Assert.Single(_vm.FilteredEntities);
            Assert.Equal("Destin", _vm.FilteredEntities[0].Name);

            // Reset filter and search by name
            _vm.EntityTypeFilter = "All";
            _vm.EntitySearchFilter = "Hare";
            Assert.Single(_vm.FilteredEntities);
            Assert.Equal("Forest Hare", _vm.FilteredEntities[0].Name);

            // Clear search filter and despawn mob
            _vm.EntitySearchFilter = string.Empty;
            session.World.RemoveEntity(1001);

            Assert.Equal(1, _vm.TotalEntityCount);
            Assert.Single(_vm.FilteredEntities);
            Assert.Equal("Destin", _vm.FilteredEntities[0].Name);
        }

        [Fact]
        public void MainWindowViewModel_ShowStateInspector_DefaultsTrueAndToggles()
        {
            using var mainVm = new MainWindowViewModel(_registry);

            Assert.True(mainVm.ShowStateInspector);
            Assert.NotNull(mainVm.StateInspector);

            mainVm.ShowStateInspector = false;
            Assert.False(mainVm.ShowStateInspector);
        }
    }
}
