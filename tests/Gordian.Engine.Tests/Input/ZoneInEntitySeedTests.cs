// tests/Gordian.Engine.Tests/Input/ZoneInEntitySeedTests.cs
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
    /// <summary>
    /// Retail zone-in dead pose (2026-11-22 round 1): between a zone change clearing the world and retail's S2C 0x00A (about
    /// 0.8 s), the locomotion tick and <c>/moveto</c> make the player entity again. It starts at the last known HP, not 0%.
    /// </summary>
    public class ZoneInEntitySeedTests
    {
        private const uint Me = 0x12345678;

        private static (PlayerLocomotionController Controller, PlayerActionService Service, WorldState World, LocalPlayerState Player) Harness(byte? status = null, byte hpp = 0)
        {
            var world = new WorldState();
            var player = new LocalPlayerState { ServerId = Me };
            if (status is { } s)
            {
                byte[] payload = new byte[0x60];
                payload[36 + 2] = hpp;
                payload[44] = s;
                player.UpdateFromCharStatus(new S2C_0x037_CharStatus(payload));
            }
            Task Send(ReadOnlyMemory<byte> m, bool u) => Task.CompletedTask;
            var service = new PlayerActionService(new SessionProfile(), world, player, new CombatPacketModule(new CombatState(), player, Send),
                new ChatPacketModule(Send), new PartyPacketModule(new PartyState(), Send), new EntityPacketModule(world, player, Send),
                new LifecyclePacketModule(new SessionProfile(), Send), Send);
            var controller = new PlayerLocomotionController(new InputState(), InputProfile.CreateCompact(), world, player, service);
            return (controller, service, world, player);
        }

        [Theory]
        [InlineData(null, (byte)0, (byte)100)]
        [InlineData((byte)0, (byte)64, (byte)64)]
        [InlineData((byte)3, (byte)100, (byte)100)] // vanishing: alive
        [InlineData((byte)3, (byte)0, (byte)0)]     // dead
        public void LocomotionTick_RemakesThePlayer_AtTheLastKnownHp(byte? status, byte hpp, byte expected)
        {
            var (controller, _, world, _) = Harness(status, hpp);

            controller.Update(TimeSpan.FromMilliseconds(16));

            Assert.True(world.TryGetByServerId(Me, out var entity));
            Assert.Equal(expected, entity!.Hpp);
        }

        [Fact]
        public async Task MoveTo_RemakesThePlayer_AtTheLastKnownHp()
        {
            var (_, service, world, _) = Harness(0, 64);

            await service.MoveToAsync(new Vector3(1f, 0f, 1f));

            Assert.True(world.TryGetByServerId(Me, out var entity));
            Assert.Equal(64, entity!.Hpp);
        }
    }
}
