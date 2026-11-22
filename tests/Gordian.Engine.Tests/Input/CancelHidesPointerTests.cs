// tests/Gordian.Engine.Tests/Input/CancelHidesPointerTests.cs
using System;
using System.Numerics;
using System.Threading.Tasks;
using Gordian.Core.Config;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Gordian.Engine.Actions;
using Gordian.Engine.Input;
using Xunit;

namespace Gordian.Engine.Tests.Input
{
    /// <summary>#605: Cancel (Esc, or the gamepad's cancel button) with no menu open hides the mouse cursor until it moves.</summary>
    public class CancelHidesPointerTests
    {
        private static (PlayerLocomotionController Controller, InputState Input, PlayerActionService Service) Harness()
        {
            var world = new WorldState();
            var player = new LocalPlayerState { ServerId = 0x12345678 };
            world.UpsertEntity(new PlayerEntity(player.ServerId, 1) { Name = "Me", IsSpawned = true, Hpp = 100, Position = Vector3.Zero });
            Task Send(ReadOnlyMemory<byte> m, bool u) => Task.CompletedTask;
            var service = new PlayerActionService(new SessionProfile(), world, player, new CombatPacketModule(new CombatState(), player, Send),
                new ChatPacketModule(Send), new PartyPacketModule(new PartyState(), Send), new EntityPacketModule(world, player, Send),
                new LifecyclePacketModule(new SessionProfile(), Send), Send);
            var input = new InputState();
            return (new PlayerLocomotionController(input, InputProfile.CreateCompact(), world, player, service), input, service);
        }

        [Fact]
        public void Cancel_WithNoMenuOpen_HidesThePointer_UntilTheMouseMoves()
        {
            var (controller, input, service) = Harness();
            service.UiPointer.MoveTo(300, 200);
            controller.Update(TimeSpan.FromMilliseconds(16));
            Assert.False(service.UiPointer.Hidden);

            input.SetKeyDown(GordianKey.Escape);
            controller.Update(TimeSpan.FromMilliseconds(16));
            Assert.True(service.UiPointer.Hidden);
            input.SetKeyUp(GordianKey.Escape);
            controller.Update(TimeSpan.FromMilliseconds(16));
            Assert.True(service.UiPointer.Hidden);

            service.UiPointer.MoveTo(305, 200);
            Assert.False(service.UiPointer.Hidden);
        }
    }
}
