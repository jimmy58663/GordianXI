// tests/Gordian.Core.Tests/Ui/UiLayoutCommandTests.cs
using System;
using System.Threading.Tasks;
using Gordian.Core.Actions;
using Gordian.Core.Config;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    public class UiLayoutCommandTests
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

        [Theory]
        [InlineData("/uilayout party hide", "party hide")]
        [InlineData("/uil scale 2", "scale 2")]
        [InlineData("/uilayout", "")]
        public void Router_RecognizesUiLayout(string input, string args)
        {
            var result = ChatCommandRouter.Parse(input);
            Assert.Equal(ChatCommandResultKind.UiLayout, result.Kind);
            Assert.Equal(args, result.Message);
        }

        [Fact]
        public void Command_EditsTheSessionLayout()
        {
            var service = CreateService();
            var layout = service.UiLayout;

            Assert.Contains("retail placement", service.ApplyUiLayoutCommand("").Message);

            Assert.True(service.ApplyUiLayoutCommand("scale 1.5").Success);
            Assert.Equal(1.5f, layout.Scale);

            Assert.True(service.ApplyUiLayoutCommand("tp on").Success);
            Assert.True(layout.ShowPartyTp);

            Assert.True(service.ApplyUiLayoutCommand("party move 100, 50 topleft").Success);
            var party = layout.Windows[StockUiWindowIds.Party];
            Assert.Equal((100f, 50f, UiAnchor.TopLeft), (party.X!.Value, party.Y!.Value, party.Anchor!.Value));

            Assert.True(service.ApplyUiLayoutCommand("log hide").Success);
            Assert.True(layout.Windows[StockUiWindowIds.Log].Hidden);
            Assert.True(service.ApplyUiLayoutCommand("log scale 0.75").Success);
            Assert.Equal(0.75f, layout.Windows[StockUiWindowIds.Log].Scale);

            string report = service.ApplyUiLayoutCommand("").Message;
            Assert.Contains("party: at (100, 50), anchored TopLeft", report);
            Assert.Contains("log: scale x0.75, hidden", report);

            Assert.True(service.ApplyUiLayoutCommand("party reset").Success);
            Assert.False(layout.Windows.ContainsKey(StockUiWindowIds.Party));
            Assert.True(service.ApplyUiLayoutCommand("reset").Success);
            Assert.Empty(layout.Windows);
            Assert.Equal(1.0f, layout.Scale);
            Assert.False(layout.ShowPartyTp);
        }

        [Theory]
        [InlineData("sideways")]
        [InlineData("party")]
        [InlineData("party move 1")]
        [InlineData("party move 1 2 middle")]
        [InlineData("party scale big")]
        public void Command_RejectsBadInput(string args)
        {
            var result = CreateService().ApplyUiLayoutCommand(args);
            Assert.False(result.Success);
            Assert.StartsWith("Usage: /uilayout", result.Message);
        }
    }
}
