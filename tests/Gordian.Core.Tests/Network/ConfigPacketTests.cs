// tests/Gordian.Core.Tests/Network/ConfigPacketTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class ConfigPacketTests
    {
        [Fact]
        public void S2C_0x0B4_ParsesSaveConfAndLanguages()
        {
            var payload = new byte[20];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0), 0x2000_4019);   // Invite, Language 3, AutoTargetOff, Recruit
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), 0x0000_0003);   // Say + Shout
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), 0x0001_0000);   // Yell
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(12), 0x0001);
            payload[14] = 7;
            payload[16] = (byte)(PartyLanguages.English | PartyLanguages.French);

            var config = new S2C_0x0B4_Config(payload);
            Assert.True(config.IsValid);
            Assert.Equal(0x2000_4019u, config.Flags);
            Assert.Equal((uint)(ChatFilter1.Say | ChatFilter1.Shout), config.MessageFilter1);
            Assert.Equal((uint)ChatFilter2.Yell, config.MessageFilter2);
            Assert.Equal(1, config.PvpFlags);
            Assert.Equal(7, config.AreaCode);
            Assert.Equal(PartyLanguages.English | PartyLanguages.French, config.PartyLanguages);

            var state = new PlayerConfigState();
            Assert.False(state.Received);
            state.Apply(config);
            Assert.True(state.Received);
            Assert.True(state.IsSet(PlayerConfigFlags.Invite));
            Assert.True(state.IsSet(PlayerConfigFlags.AutoTargetOff));
            Assert.True(state.IsSet(PlayerConfigFlags.Recruit));
            Assert.False(state.IsSet(PlayerConfigFlags.Anonymity));

            Assert.False(new S2C_0x0B4_Config(new byte[16]).IsValid);
        }

        [Fact]
        public void C2S_0x0DC_NamesTheFlagAndSetsOrClearsIt()
        {
            var on = ConfigOutboundPackets.BuildConfig(PlayerConfigFlags.Anonymity, true, 0x1234);
            Assert.Equal(0x14, on.Length);
            ushort header = BinaryPrimitives.ReadUInt16LittleEndian(on);
            Assert.Equal(0x0DC, header & 0x1FF);
            Assert.Equal(0x14 / 4, header >> 9);
            Assert.Equal(0x1234, BinaryPrimitives.ReadUInt16LittleEndian(on.AsSpan(2)));
            Assert.Equal((uint)PlayerConfigFlags.Anonymity, BinaryPrimitives.ReadUInt32LittleEndian(on.AsSpan(4)));
            Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(on.AsSpan(8)));
            Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(on.AsSpan(12)));
            Assert.Equal(1, on[16]);

            var off = ConfigOutboundPackets.BuildConfig(PlayerConfigFlags.AutoTargetOff, false);
            Assert.Equal((uint)PlayerConfigFlags.AutoTargetOff, BinaryPrimitives.ReadUInt32LittleEndian(off.AsSpan(4)));
            Assert.Equal(2, off[16]);
        }

        [Fact]
        public void C2S_0x0DB_CarriesTheConfigWordsOrTheLanguages()
        {
            var filters = ConfigOutboundPackets.BuildChatFilters(0x0000_0019, 0x0000_0003, 0x0001_0000, 7);
            Assert.Equal(0x28, filters.Length);
            ushort header = BinaryPrimitives.ReadUInt16LittleEndian(filters);
            Assert.Equal(0x0DB, header & 0x1FF);
            Assert.Equal(0x28 / 4, header >> 9);
            Assert.Equal(7, BinaryPrimitives.ReadUInt16LittleEndian(filters.AsSpan(2)));
            Assert.Equal(0, filters[4]);
            Assert.Equal(0, filters[5]);
            Assert.Equal(ConfigOutboundPackets.ConfigLanguageKindConfigWords, filters[6]);
            Assert.Equal(0x19u, BinaryPrimitives.ReadUInt32LittleEndian(filters.AsSpan(8)));
            Assert.Equal(0x03u, BinaryPrimitives.ReadUInt32LittleEndian(filters.AsSpan(12)));
            Assert.Equal(0x1_0000u, BinaryPrimitives.ReadUInt32LittleEndian(filters.AsSpan(16)));
            Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(filters.AsSpan(36)));

            var languages = ConfigOutboundPackets.BuildPartyLanguages(PartyLanguages.Japanese | PartyLanguages.English);
            Assert.Equal(ConfigOutboundPackets.ConfigLanguageKindPartyLanguages, languages[6]);
            Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(languages.AsSpan(8)));
            Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(languages.AsSpan(36)));
        }

        [Fact]
        public async Task Module_EchoesTheServerFlagWord_AndExpectsItsOwnChanges()
        {
            var sent = new List<byte[]>();
            Task Send(ReadOnlyMemory<byte> chunk, bool urgent) { sent.Add(chunk.ToArray()); return Task.CompletedTask; }
            var state = new PlayerConfigState();
            var module = new ConfigPacketModule(state, Send);
            var dispatcher = new PacketDispatcher();
            module.Register(dispatcher);

            var payload = new byte[20];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0), (uint)(PlayerConfigFlags.Invite | PlayerConfigFlags.Mentor | (PlayerConfigFlags)(3u << 3)));
            int changes = 0;
            state.Changed += () => changes++;
            dispatcher.Dispatch(new PacketHeader(S2C_0x0B4_Config.PacketId, payload.Length + 4, 0), payload);
            Assert.Equal(1, changes);
            Assert.True(state.IsSet(PlayerConfigFlags.Mentor));

            await module.SetChatFiltersAsync((uint)ChatFilter1.Say, (uint)ChatFilter2.Yell);
            var packet = Assert.Single(sent);
            Assert.Equal(0x0DB, BinaryPrimitives.ReadUInt16LittleEndian(packet) & 0x1FF);
            Assert.Equal(state.Flags, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8)));
            Assert.Equal((uint)ChatFilter1.Say, state.MessageFilter1);
            Assert.Equal((uint)ChatFilter2.Yell, state.MessageFilter2);

            await module.SetFlagAsync(PlayerConfigFlags.Anonymity, true);
            Assert.True(state.IsSet(PlayerConfigFlags.Anonymity));
            Assert.True(state.IsSet(PlayerConfigFlags.Invite));
            await module.SetFlagAsync(PlayerConfigFlags.Invite, false);
            Assert.False(state.IsSet(PlayerConfigFlags.Invite));
            Assert.Equal(3, sent.Count);

            // The system message filter level rides in the echoed flag word of a kind-0 0x0DB.
            await module.SetSystemMessageFilterLevelAsync(2);
            Assert.Equal(2, state.SystemMessageFilterLevel);
            Assert.True(state.IsSet(PlayerConfigFlags.Mentor));
            var levelPacket = sent[^1];
            Assert.Equal(0x0DB, BinaryPrimitives.ReadUInt16LittleEndian(levelPacket) & 0x1FF);
            Assert.Equal(0, levelPacket[6]);
            Assert.Equal(state.Flags, BinaryPrimitives.ReadUInt32LittleEndian(levelPacket.AsSpan(8)));
            Assert.Equal(2u << 11, state.Flags & (uint)PlayerConfigFlags.SysMesFilterLevelMask);
            Assert.Equal((uint)ChatFilter1.Say, BinaryPrimitives.ReadUInt32LittleEndian(levelPacket.AsSpan(12)));

            module.Unregister(dispatcher);
        }
    }
}
