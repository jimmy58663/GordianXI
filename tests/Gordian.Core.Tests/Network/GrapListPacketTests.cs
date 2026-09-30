// tests/Gordian.Core.Tests/Network/GrapListPacketTests.cs
using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using Gordian.Core.Config;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>S2C 0x051 GP_SERV_GRAP_LIST: the local player's own model table (#153).</summary>
    public class GrapListPacketTests
    {
        private static readonly ushort[] Table = { 0x0103, 0x1040, 0x2040, 0x3040, 0x4040, 0x5040, 0x6123, 0x7000, 0x8000 };

        private static byte[] BuildPayload()
        {
            byte[] payload = new byte[20]; // 9 x uint16 + padding16
            for (int i = 0; i < Table.Length; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(i * 2, 2), Table[i]);
            }
            return payload;
        }

        [Fact]
        public void S2C_0x051_GrapList_DecodesTable()
        {
            var packet = new S2C_0x051_GrapList(BuildPayload());
            Span<ushort> grap = stackalloc ushort[9];

            Assert.True(packet.IsValid);
            Assert.True(packet.TryGetGrapIdTable(grap));
            Assert.Equal(Table, grap.ToArray());
        }

        [Fact]
        public void S2C_0x051_GrapList_RejectsShortPayload()
        {
            var packet = new S2C_0x051_GrapList(new byte[16]);
            Span<ushort> grap = stackalloc ushort[9];

            Assert.False(packet.IsValid);
            Assert.False(packet.TryGetGrapIdTable(grap));
        }

        [Fact]
        public void LifecyclePacketModule_DispatchesGrapList_AndRaisesLocalAppearance()
        {
            var module = new LifecyclePacketModule(new SessionProfile(), (m, h) => Task.CompletedTask);
            ushort[]? received = null;
            module.LocalAppearanceReceived += grap => received = grap;

            var dispatcher = new PacketDispatcher();
            module.Register(dispatcher);

            byte[] payload = BuildPayload();
            dispatcher.Dispatch(new PacketHeader(S2C_0x051_GrapList.PacketId, (ushort)(payload.Length + 4), 0), payload);

            Assert.Equal(Table, received);
        }
    }
}
