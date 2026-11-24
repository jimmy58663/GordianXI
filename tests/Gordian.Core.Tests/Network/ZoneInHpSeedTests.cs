// tests/Gordian.Core.Tests/Network/ZoneInHpSeedTests.cs
using System;
using System.Buffers.Binary;
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

        /// <summary>
        /// A full S2C 0x00A with the position header's HP percent (+26) and server status (+27) and the death counter (+160),
        /// as LandSandBoat's <c>s2c/0x00a_login.cpp</c> writes them (payload offsets after the 4-byte header).
        /// </summary>
        internal static byte[] FullLoginDatagram(ushort serverSeq, byte status, byte hpp, uint homepointSeconds)
        {
            byte[] sub = new byte[4 + 240];
            BinaryPrimitives.WriteUInt16LittleEndian(sub, (ushort)(0x00A | ((sub.Length / 4) << 9)));
            BinaryPrimitives.WriteUInt16LittleEndian(sub.AsSpan(2, 2), serverSeq);
            BinaryPrimitives.WriteUInt32LittleEndian(sub.AsSpan(4, 4), Me);
            BinaryPrimitives.WriteUInt16LittleEndian(sub.AsSpan(8, 2), 42);
            sub[4 + 26] = hpp;
            sub[4 + 27] = status;
            BinaryPrimitives.WriteUInt32LittleEndian(sub.AsSpan(4 + 160, 4), (homepointSeconds + S2C_0x037_CharStatus.DeadCounterOffsetSeconds) * 60);

            byte[] compressed = new byte[1024];
            int length = Gordian.Core.Network.Compression.FfxiCodec.Default.Compress(sub, compressed);
            byte[] datagram = new byte[28 + length + 16];
            BinaryPrimitives.WriteUInt16LittleEndian(datagram, serverSeq);
            BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(2, 2), 1);
            compressed.AsSpan(0, length).CopyTo(datagram.AsSpan(28));
            return datagram;
        }

        /// <summary>
        /// Round 3 of the 2026-11-22 retail test: a character logged in dead lay in the death pose with no home point window. Retail
        /// sends it no S2C 0x037 with status 3; the 0x00A's position header says it is dead, with the home point time left.
        /// </summary>
        [Fact]
        public async Task DeadLogin_The0x00AMakesTheCharacterDead_WithTheTimeLeft()
        {
            using var mgr = await ZonedAsync();
            var changes = new System.Collections.Generic.List<(byte, byte)>();
            mgr.LocalPlayer.ServerStatusChanged += (from, to) => changes.Add((from, to));

            Assert.True(mgr.ProcessInboundDatagram(FullLoginDatagram(1, LocalPlayerState.StatusDead, 0, 1800)));

            Assert.True(mgr.LocalPlayer.IsDead);
            Assert.Equal(1800u, mgr.LocalPlayer.HomepointSecondsRemaining);
            Assert.Equal(new[] { ((byte)0, LocalPlayerState.StatusDead) }, changes);
            Assert.True(mgr.World.TryGetByServerId(Me, out var entity));
            Assert.Equal(0, entity!.Hpp);
        }

        /// <summary>A living character's 0x00A sets its HP percent and clears a death (or a vanish) left from the last zone.</summary>
        [Fact]
        public async Task AliveLogin_The0x00AClearsTheDeath_AndSeedsTheHp()
        {
            using var mgr = await ZonedAsync();
            SetStatus(mgr.LocalPlayer, LocalPlayerState.StatusDead, 0);
            Assert.True(mgr.LocalPlayer.IsDead);

            Assert.True(mgr.ProcessInboundDatagram(FullLoginDatagram(1, 0, 87, 0)));

            Assert.False(mgr.LocalPlayer.IsDead);
            Assert.Equal(87, mgr.LocalPlayer.Hpp);
            Assert.True(mgr.World.TryGetByServerId(Me, out var entity));
            Assert.Equal(87, entity!.Hpp);
        }

        [Fact]
        public void LoginAck_ReadsTheHpPercentAndTheStatus()
        {
            byte[] payload = new byte[240];
            payload[26] = 0;
            payload[27] = LocalPlayerState.StatusDead;
            var ack = new S2C_0x00A_LoginAck(payload);
            Assert.Equal(0, ack.Hpp);
            Assert.Equal(LocalPlayerState.StatusDead, ack.ServerStatus);

            payload[26] = 55;
            payload[27] = 0;
            ack = new S2C_0x00A_LoginAck(payload);
            Assert.Equal(55, ack.Hpp);
            Assert.Equal(0, ack.ServerStatus);
            Assert.Equal(1800u, new LoginStatus(3, 0, (1800 + 360) * 60).HomepointSecondsRemaining);
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
