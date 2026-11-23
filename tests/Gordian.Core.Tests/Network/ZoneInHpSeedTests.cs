// tests/Gordian.Core.Tests/Network/ZoneInHpSeedTests.cs
using System.Net;
using System.Threading.Tasks;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// Round 1 of the 2026-11-22 retail test: after a zone change the player lay in the death pose as the screen faded in, then
    /// popped to idle. A zone change clears the world; on retail the S2C 0x00A login comes about 0.8 s later, and meanwhile the
    /// engine's locomotion tick (or the action service) made the player entity again at 0% HP. The 0x00A then found it existing
    /// and left its HP alone (#579's seed ran only for a new entity), so it stayed dead until S2C 0x037 / 0x0DF.
    /// </summary>
    public sealed class ZoneInHpSeedTests
    {
        private const uint Me = 1001;

        private static void SetStatus(LocalPlayerState player, byte status, byte hpp)
        {
            byte[] payload = new byte[0x60];
            payload[36 + 2] = hpp;
            payload[44] = status;
            player.UpdateFromCharStatus(new S2C_0x037_CharStatus(payload));
        }

        private static async Task<SessionNetworkManager> ZonedAsync()
        {
            var mgr = new SessionNetworkManager("127.0.0.1", 54230) { CharacterId = Me, CharacterName = "Cybin" };
            mgr.LocalPlayer.ServerId = Me;
            mgr.CurrentState = SessionState.ActiveInWorld;
            await mgr.PerformZoneTransitionAsync(IPAddress.Loopback, 54231, TestContext.Current.CancellationToken);
            return mgr;
        }

        /// <summary>Retail's order: the engine remakes the entity at 0% first (what it did before the fix), then the 0x00A arrives.</summary>
        [Theory]
        [InlineData(null, (byte)0, (byte)100)]       // no vitals yet: full
        [InlineData((byte)0, (byte)64, (byte)64)]    // alive at 64% before the zone change
        [InlineData((byte)3, (byte)100, (byte)100)]  // vanishing (status 3 with HP): alive
        public async Task RetailOrder_EntityMadeBeforeTheLogin_IsSeededByTheLogin(byte? status, byte hpp, byte expected)
        {
            using var mgr = await ZonedAsync();
            if (status is { } s) SetStatus(mgr.LocalPlayer, s, hpp);
            mgr.World.UpsertEntity(new PlayerEntity(Me, 0) { IsSpawned = true });

            Assert.True(mgr.ProcessInboundDatagram(ZoneLoginResendTests.LoginDatagram(1)));

            Assert.True(mgr.World.TryGetByServerId(Me, out var entity));
            Assert.Equal(expected, entity!.Hpp);
        }

        [Fact]
        public async Task RetailOrder_ADeadCharacter_StaysDead()
        {
            using var mgr = await ZonedAsync();
            SetStatus(mgr.LocalPlayer, LocalPlayerState.StatusDead, 0);
            mgr.World.UpsertEntity(new PlayerEntity(Me, 0) { IsSpawned = true });

            Assert.True(mgr.ProcessInboundDatagram(ZoneLoginResendTests.LoginDatagram(1)));

            Assert.True(mgr.World.TryGetByServerId(Me, out var entity));
            Assert.Equal(0, entity!.Hpp);
        }

        /// <summary>LandSandBoat's order (the 0x00A within a few ms, before any tick): the login makes the entity, as before.</summary>
        [Fact]
        public async Task LandSandBoatOrder_TheLoginMakesTheEntity_AtTheLastKnownHp()
        {
            using var mgr = await ZonedAsync();
            SetStatus(mgr.LocalPlayer, 0, 64);
            Assert.False(mgr.World.TryGetByServerId(Me, out _));

            Assert.True(mgr.ProcessInboundDatagram(ZoneLoginResendTests.LoginDatagram(1)));

            Assert.True(mgr.World.TryGetByServerId(Me, out var entity));
            Assert.Equal(64, entity!.Hpp);
        }

        /// <summary>An entity that already has HP (a second 0x00A, or vitals that came first) keeps it.</summary>
        [Fact]
        public async Task ExistingEntityWithHp_IsLeftAlone()
        {
            using var mgr = await ZonedAsync();
            SetStatus(mgr.LocalPlayer, 0, 64);
            mgr.World.UpsertEntity(new PlayerEntity(Me, 0) { IsSpawned = true, Hpp = 30 });

            Assert.True(mgr.ProcessInboundDatagram(ZoneLoginResendTests.LoginDatagram(1)));

            Assert.True(mgr.World.TryGetByServerId(Me, out var entity));
            Assert.Equal(30, entity!.Hpp);
        }
    }
}
