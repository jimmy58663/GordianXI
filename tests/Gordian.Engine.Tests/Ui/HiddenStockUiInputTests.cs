// tests/Gordian.Engine.Tests/Ui/HiddenStockUiInputTests.cs
using System;
using System.Threading.Tasks;
using Gordian.Engine.Actions;
using Gordian.Core.Config;
using Gordian.Engine.Input;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Engine.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>
    /// Hidden stock elements take no input (#29, round 2): hidden menus neither open nor consume their keys, a hidden
    /// chat neither opens nor keeps its input line, so the keys are left for whatever replaces them.
    /// </summary>
    public class HiddenStockUiInputTests
    {
        private sealed record Harness(PlayerLocomotionController Controller, InputState Input, PlayerActionService Actions, StockUiChat Chat, WorldState World)
        {
            public void Press(GordianKey key)
            {
                Input.SetKeyDown(key);
                Controller.Update(TimeSpan.FromMilliseconds(16));
                Input.SetKeyUp(key);
                Controller.Update(TimeSpan.FromMilliseconds(16));
            }
        }

        private static Harness Create()
        {
            var world = new WorldState();
            var player = new LocalPlayerState { ServerId = 0x100 };
            world.UpsertEntity(new WorldEntity(0x100, 0x100, EntityType.Player) { Name = "Me", IsSpawned = true });
            Task Send(ReadOnlyMemory<byte> m, bool u) => Task.CompletedTask;
            var actions = new PlayerActionService(new SessionProfile(), world, player,
                new CombatPacketModule(new CombatState(), player, Send), new ChatPacketModule(Send),
                new PartyPacketModule(new PartyState(), Send), new EntityPacketModule(world, player, Send),
                new LifecyclePacketModule(new SessionProfile(), Send), Send);
            actions.Menus.Library = StockUiMenuControllerTests.SyntheticLibrary();
            var input = new InputState();
            var chat = new StockUiChat();
            var controller = new PlayerLocomotionController(input, InputProfile.CreateCompact(), world, player, actions) { Chat = chat };
            controller.Update(TimeSpan.FromMilliseconds(16));
            return new Harness(controller, input, actions, chat, world);
        }

        [Fact]
        public void MenuKey_OpensTheShownMenu()
        {
            var h = Create();
            h.Press(GordianKey.NumPadSubtract);
            Assert.True(h.Actions.Menus.IsOpen);
        }

        [Fact]
        public void HiddenMenus_DoNotOpenOrTakeTheirKeys()
        {
            var h = Create();
            h.Actions.UiVisibility.Hide("replacement", StockUiElement.Menus);

            h.Press(GordianKey.NumPadSubtract);
            Assert.False(h.Actions.Menus.IsOpen);
            // The menu keys are not taken over as menu navigation: the input stays in its gameplay context.
            Assert.False(h.Input.MenuContext);

            // Confirm on a target opens no command menu either.
            var mob = new WorldEntity(0x30, 0x30, EntityType.Monster) { Name = "Mob", IsSpawned = true };
            h.World.UpsertEntity(mob);
            h.Actions.SetTarget(mob);
            Assert.False(h.Actions.OpenTargetCommandMenu());
            Assert.False(h.Actions.Menus.IsOpen);
        }

        [Fact]
        public void MenusOpenWhenHidden_StopTakingInput_AndResumeWhenShown()
        {
            var h = Create();
            h.Press(GordianKey.NumPadSubtract);
            Assert.True(h.Actions.Menus.IsOpen);

            h.Actions.UiVisibility.Hide("replacement", StockUiElement.Menus);
            h.Press(GordianKey.Escape); // would close the menu if it still took input
            Assert.True(h.Actions.Menus.IsOpen);
            Assert.False(h.Input.MenuContext);

            h.Actions.UiVisibility.Release("replacement");
            h.Press(GordianKey.Escape);
            Assert.False(h.Actions.Menus.IsOpen);
        }

        [Fact]
        public void HiddenChat_DoesNotOpen_AndLetsGoOfItsInputLine()
        {
            var h = Create();
            h.Press(GordianKey.OemSlash);
            Assert.True(h.Chat.Input.IsOpen);

            h.Actions.UiVisibility.Hide(StockUiVisibilityState.UserOwner, StockUiElement.Chat);
            h.Controller.Update(TimeSpan.FromMilliseconds(16));
            Assert.False(h.Chat.Input.IsOpen);

            h.Press(GordianKey.OemSlash);
            Assert.False(h.Chat.Input.IsOpen);
            h.Press(GordianKey.NumPadAdd); // the log window is not selected either
            Assert.False(h.Chat.IsSelecting);

            // The emergency restore (Ctrl+Shift+F12) drops every hide.
            h.Actions.UiVisibility.Hide("addon", StockUiElement.Party);
            h.Actions.UiVisibility.ReleaseAll();
            Assert.Equal(StockUiElement.None, h.Actions.UiVisibility.Hidden);
            h.Press(GordianKey.OemSlash);
            Assert.True(h.Chat.Input.IsOpen);
        }
    }
}
