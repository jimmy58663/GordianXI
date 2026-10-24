// tests/Gordian.Core.Tests/Ui/StockUiVisibilityStateTests.cs
using System;
using System.Threading.Tasks;
using Gordian.Engine.Actions;
using Gordian.Core.Config;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Engine.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    /// <summary>The run-time stock HUD visibility API (#29) and its <c>/stockui</c> command.</summary>
    public class StockUiVisibilityStateTests
    {
        private static PlayerActionService CreateService()
        {
            var profile = new SessionProfile();
            var world = new WorldState();
            var localPlayer = new LocalPlayerState { ServerId = 0x01020304 };
            Task Send(ReadOnlyMemory<byte> chunk, bool urgent) => Task.CompletedTask;
            return new PlayerActionService(profile, world, localPlayer,
                new CombatPacketModule(new CombatState(), localPlayer, Send), new ChatPacketModule(Send),
                new PartyPacketModule(new PartyState(), Send), new EntityPacketModule(world, localPlayer, Send),
                new LifecyclePacketModule(profile, Send), Send);
        }

        [Fact]
        public void Default_ShowsEverything()
        {
            var state = new StockUiVisibilityState();
            Assert.Equal(StockUiElement.None, state.Hidden);
            Assert.Empty(state.Owners);
            Assert.False(StockUiVisibilityState.HidesWindow(state.Hidden, StockUiWindowIds.Party, inParty: true));
        }

        [Fact]
        public void Owners_HideIndependently()
        {
            var state = new StockUiVisibilityState();
            int changes = 0;
            state.Changed += () => changes++;

            state.Hide("xivparty", StockUiElement.Party | StockUiElement.Alliance);
            state.Hide("targetbar", StockUiElement.TargetBar | StockUiElement.Party);
            Assert.Equal(StockUiElement.Party | StockUiElement.Alliance | StockUiElement.TargetBar, state.Hidden);
            Assert.Equal(2, changes);

            // One owner showing the party again leaves the other's hide in place.
            state.Show("xivparty", StockUiElement.Party);
            Assert.True(state.IsHidden(StockUiElement.Party));
            Assert.Equal(2, changes); // the combined set did not change

            // Unloading an addon drops everything it hid.
            state.Release("targetbar");
            Assert.Equal(StockUiElement.Alliance, state.Hidden);
            Assert.Equal(StockUiElement.None, state.HiddenBy("targetbar"));
            Assert.Single(state.Owners);

            state.Set("xivparty", StockUiElement.None);
            Assert.Equal(StockUiElement.None, state.Hidden);
            Assert.Empty(state.Owners);
        }

        [Fact]
        public void OwnerNames_AreCaseInsensitive_AndRequired()
        {
            var state = new StockUiVisibilityState();
            state.Hide("MyAddon", StockUiElement.Chat);
            state.Show("myaddon", StockUiElement.Chat);
            Assert.Equal(StockUiElement.None, state.Hidden);
            Assert.Throws<ArgumentException>(() => state.Hide(" ", StockUiElement.Chat));
        }

        [Theory]
        [InlineData(StockUiWindowIds.Target, false, StockUiElement.TargetBar)]
        [InlineData(StockUiWindowIds.Party, false, StockUiElement.Vitals)]
        [InlineData(StockUiWindowIds.Party, true, StockUiElement.Party)]
        [InlineData(StockUiWindowIds.Alliance1, true, StockUiElement.Alliance)]
        [InlineData(StockUiWindowIds.Alliance2, true, StockUiElement.Alliance)]
        [InlineData(StockUiWindowIds.StatusIcons, false, StockUiElement.Buffs)]
        [InlineData(StockUiWindowIds.Log, false, StockUiElement.Chat)]
        [InlineData(StockUiWindowIds.ChatInput, false, StockUiElement.Chat)]
        [InlineData(StockUiWindowIds.MainMenu, false, StockUiElement.Menus)]
        [InlineData(StockUiWindowIds.Query, false, StockUiElement.Menus)]
        [InlineData(StockUiWindowIds.CommandMenu, false, StockUiElement.Menus)]
        [InlineData(StockUiWindowIds.Shop, false, StockUiElement.Menus)]
        [InlineData(StockUiWindowIds.Death, false, StockUiElement.Menus)]
        [InlineData(StockUiWindowIds.Check, false, StockUiElement.Menus)]
        [InlineData("unknown", false, StockUiElement.None)]
        public void Windows_MapToTheirElement(string windowId, bool inParty, StockUiElement expected)
        {
            Assert.Equal(expected, StockUiVisibilityState.ElementOf(windowId, inParty));
            if (expected != StockUiElement.None)
            {
                Assert.True(StockUiVisibilityState.HidesWindow(expected, windowId, inParty));
                Assert.False(StockUiVisibilityState.HidesWindow(StockUiElement.All & ~expected, windowId, inParty));
            }
        }

        [Fact]
        public void Vitals_HidesThePartyWindowOnlyWhileSolo()
        {
            Assert.True(StockUiVisibilityState.HidesWindow(StockUiElement.Vitals, StockUiWindowIds.Party, inParty: false));
            Assert.False(StockUiVisibilityState.HidesWindow(StockUiElement.Vitals, StockUiWindowIds.Party, inParty: true));
            Assert.False(StockUiVisibilityState.HidesWindow(StockUiElement.Party, StockUiWindowIds.Party, inParty: false));
        }

        [Theory]
        [InlineData("target", StockUiElement.TargetBar)]
        [InlineData("Party", StockUiElement.Party)]
        [InlineData("buffs", StockUiElement.Buffs)]
        [InlineData("status", StockUiElement.Buffs)]
        [InlineData("log", StockUiElement.Chat)]
        [InlineData("names", StockUiElement.NamePlates)]
        [InlineData("cursor", StockUiElement.TargetCursor)]
        [InlineData("all", StockUiElement.All)]
        public void TryParse_ReadsElementNames(string name, StockUiElement expected)
        {
            Assert.True(StockUiVisibilityState.TryParse(name, out var element));
            Assert.Equal(expected, element);
        }

        [Fact]
        public void TryParse_RejectsUnknownNames()
        {
            Assert.False(StockUiVisibilityState.TryParse("minimap", out _));
            Assert.Equal("none", StockUiVisibilityState.Describe(StockUiElement.None));
            Assert.Equal("target, chat", StockUiVisibilityState.Describe(StockUiElement.TargetBar | StockUiElement.Chat));
        }

        [Theory]
        [InlineData("/stockui hide party", ChatCommandResultKind.StockUiVisibility, "hide party")]
        [InlineData("/stockui", ChatCommandResultKind.StockUiVisibility, "")]
        [InlineData("/perf full", ChatCommandResultKind.PerformanceOverlay, "full")]
        [InlineData("/perf", ChatCommandResultKind.PerformanceOverlay, "")]
        public void Router_RecognizesTheCommands(string input, ChatCommandResultKind kind, string args)
        {
            var result = ChatCommandRouter.Parse(input);
            Assert.Equal(kind, result.Kind);
            Assert.Equal(args, result.Message);
        }

        [Fact]
        public void StockUiCommand_HidesAndShowsForThePlayer()
        {
            var service = CreateService();
            Assert.Contains("every element shown", service.ApplyStockUiCommand("").Message);

            var hide = service.ApplyStockUiCommand("hide party chat");
            Assert.True(hide.Success);
            Assert.Equal(StockUiElement.Party | StockUiElement.Chat, service.UiVisibility.HiddenBy(StockUiVisibilityState.UserOwner));

            // An addon's hide survives the player's show.
            service.UiVisibility.Hide("xivparty", StockUiElement.Party);
            var show = service.ApplyStockUiCommand("show party chat");
            Assert.Contains("xivparty", show.Message);
            Assert.Equal(StockUiElement.Party, service.UiVisibility.Hidden);

            Assert.True(service.ApplyStockUiCommand("hide all").Success);
            Assert.Equal(StockUiElement.All, service.UiVisibility.Hidden);
            Assert.True(service.ApplyStockUiCommand("reset").Success);
            Assert.Equal(StockUiElement.Party, service.UiVisibility.Hidden);

            Assert.False(service.ApplyStockUiCommand("hide minimap").Success);
            Assert.False(service.ApplyStockUiCommand("hide").Success);
        }

        [Fact]
        public async Task PerfCommand_GoesToTheViewportsHandler()
        {
            var service = CreateService();
            var without = await service.ExecuteCommandAsync("/perf fps", ChatSendKind.Say);
            Assert.False(without.Success);
            Assert.Contains("viewport", without.Message);

            string? received = null;
            service.PerformanceOverlayCommand = args => { received = args; return "Performance overlay: FPS."; };
            var with = await service.ExecuteCommandAsync("/perf fps", ChatSendKind.Say);
            Assert.True(with.Success);
            Assert.Equal("fps", received);
            Assert.Equal("Performance overlay: FPS.", with.Message);

            Assert.True((await service.ExecuteCommandAsync("/stockui hide target", ChatSendKind.Say)).Success);
            Assert.True(service.UiVisibility.IsHidden(StockUiElement.TargetBar));
        }
    }
}
