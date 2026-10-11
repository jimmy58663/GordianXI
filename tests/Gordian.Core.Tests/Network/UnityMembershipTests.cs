// tests/Gordian.Core.Tests/Network/UnityMembershipTests.cs
using System;
using System.Buffers.Binary;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>#65: the Unity chat mode follows the Unity membership in S2C 0x061 (unity_info Faction, bits 0-4).</summary>
    public class UnityMembershipTests
    {
        private static byte[] CliStatus(uint unityInfo)
        {
            var payload = new byte[0x6C];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(84), unityInfo);
            return payload;
        }

        [Fact]
        public void HasUnity_FollowsTheFactionInCliStatus()
        {
            var session = new CharacterSession("Tester", 1, "acct", new SessionNetworkManager("127.0.0.1", 54231));
            var hasUnity = session.ActionService.Menus.HasUnity;
            Assert.NotNull(hasUnity);
            Assert.False(hasUnity!());

            var dispatcher = (PacketDispatcher)session.NetworkManager.Parser.Dispatcher;
            byte[] withUnity = CliStatus(10 | (1234u << 10)); // leader 10 (Yoran-Oran), 1234 accolades
            dispatcher.Dispatch(new PacketHeader(S2C_0x061_CliStatus.PacketId, withUnity.Length + 4, 1), withUnity);
            Assert.True(hasUnity());

            byte[] without = CliStatus(0);
            dispatcher.Dispatch(new PacketHeader(S2C_0x061_CliStatus.PacketId, without.Length + 4, 2), without);
            Assert.False(hasUnity());
        }
    }
}
