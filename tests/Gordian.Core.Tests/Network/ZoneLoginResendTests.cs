// tests/Gordian.Core.Tests/Network/ZoneLoginResendTests.cs
using System;
using System.Buffers.Binary;
using System.Net;
using System.Threading.Tasks;
using Gordian.Core.Network;
using Gordian.Core.Network.Compression;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// #541: on retail the client kept resending C2S 0x00A after a zone change had finished, because the resend loop ran until
    /// S2C 0x008 moved the state on, which retail sends a second or more after its S2C 0x00A login. Each resend was answered with
    /// another login, which restarted the zone-in event. The loop now ends when the S2C 0x00A login is handled.
    /// </summary>
    public sealed class ZoneLoginResendTests
    {
        private static byte[] LoginDatagram(ushort serverSeq)
        {
            // S2C 0x00A: 4-byte header and the first 20 payload bytes (UniqueNo, ActIndex, heading, position) the decoder needs.
            byte[] sub = new byte[24];
            BinaryPrimitives.WriteUInt16LittleEndian(sub, (ushort)(0x00A | ((sub.Length / 4) << 9)));
            BinaryPrimitives.WriteUInt16LittleEndian(sub.AsSpan(2, 2), serverSeq);
            BinaryPrimitives.WriteUInt32LittleEndian(sub.AsSpan(4, 4), 1001);
            BinaryPrimitives.WriteUInt16LittleEndian(sub.AsSpan(8, 2), 42);

            byte[] compressed = new byte[256];
            int length = FfxiCodec.Default.Compress(sub, compressed);
            byte[] datagram = new byte[28 + length + 16];
            BinaryPrimitives.WriteUInt16LittleEndian(datagram, serverSeq);
            BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(2, 2), 1);
            compressed.AsSpan(0, length).CopyTo(datagram.AsSpan(28));
            return datagram;
        }

        [Fact]
        public async Task ZoneChange_StopsResendingTheLoginOnceItsAnswerArrives_BeforeTheStateMovesOn()
        {
            using var mgr = new SessionNetworkManager("127.0.0.1", 54230) { CharacterId = 1001, CharacterName = "Cybin" };
            mgr.CurrentState = SessionState.ActiveInWorld;

            await mgr.PerformZoneTransitionAsync(IPAddress.Loopback, 54231, TestContext.Current.CancellationToken);
            Assert.True(mgr.LoginAwaitingAnswer);
            Assert.Equal(SessionState.LoadingWorldData, mgr.CurrentState);

            Assert.True(mgr.ProcessInboundDatagram(LoginDatagram(1)));

            // The login is answered while the state still waits for S2C 0x008 (retail's order): no more resends.
            Assert.False(mgr.LoginAwaitingAnswer);
            Assert.Equal(SessionState.LoadingWorldData, mgr.CurrentState);
        }

        [Fact]
        public async Task NextZoneChange_StartsANewLoginThatTheOldAnswerDoesNotClose()
        {
            using var mgr = new SessionNetworkManager("127.0.0.1", 54230) { CharacterId = 1001, CharacterName = "Cybin" };
            mgr.CurrentState = SessionState.ActiveInWorld;

            await mgr.PerformZoneTransitionAsync(IPAddress.Loopback, 54231, TestContext.Current.CancellationToken);
            Assert.True(mgr.ProcessInboundDatagram(LoginDatagram(1)));
            Assert.False(mgr.LoginAwaitingAnswer);

            mgr.CurrentState = SessionState.ActiveInWorld;
            await mgr.PerformZoneTransitionAsync(IPAddress.Loopback, 54232, TestContext.Current.CancellationToken);
            Assert.True(mgr.LoginAwaitingAnswer);

            Assert.True(mgr.ProcessInboundDatagram(LoginDatagram(1)));
            Assert.False(mgr.LoginAwaitingAnswer);
        }
    }
}
