// tests/Gordian.Core.Tests/Network/OutboundActionPacketTests.cs
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
    /// <summary>
    /// The send methods that give the issue #4 builders a caller: each must put the builder's bytes on the wire
    /// with the right opcode and size (layouts per LandSandBoat c2s headers and XiPackets world/client).
    /// </summary>
    public class OutboundActionPacketTests
    {
        private readonly List<byte[]> _sent = new List<byte[]>();
        private readonly LocalPlayerState _player = new LocalPlayerState { ServerId = 0x01020304 };
        private readonly ProgressionState _progression = new ProgressionState();
        private readonly InventoryPacketModule _inventory;
        private readonly ProgressionPacketModule _progressionModule;

        public OutboundActionPacketTests()
        {
            Task Capture(ReadOnlyMemory<byte> data, bool immediate)
            {
                _sent.Add(data.ToArray());
                return Task.CompletedTask;
            }

            _inventory = new InventoryPacketModule(new InventoryState(), _player, Capture);
            _progressionModule = new ProgressionPacketModule(_progression, _player, Capture);
        }

        private byte[] SingleSent(ushort packetId, int size)
        {
            byte[] packet = Assert.Single(_sent);
            Assert.True(PacketHeader.TryParse(packet, out var header));
            Assert.Equal(packetId, header.PacketId);
            Assert.Equal(size, packet.Length);
            Assert.Equal(size, header.TotalSize);
            return packet;
        }

        [Fact]
        public async Task UseSubcontainer_Sends0x03B()
        {
            await _inventory.UseSubcontainerAsync(SubcontainerKind.Equip, ContainerId.MogSafe, 4, SubcontainerSlotIndex.Head, ContainerId.Wardrobe, 9);

            byte[] p = SingleSent(0x03B, 32);
            Assert.Equal((uint)SubcontainerKind.Equip, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(4)));
            Assert.Equal((uint)ContainerId.MogSafe, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(8)));
            Assert.Equal(4, p[12]);
            Assert.Equal((byte)SubcontainerSlotIndex.Head, p[13]);
            Assert.Equal((uint)ContainerId.Wardrobe, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(16)));
            Assert.Equal(9, p[20]);
        }

        [Fact]
        public async Task BidAuction_Sends60Byte0x04EBid()
        {
            await _inventory.BidAuctionAsync(4096, 5000, stack: true);

            byte[] p = SingleSent(0x04E, 60);
            Assert.Equal((byte)AuctionCommand.Bid, p[4]);
            Assert.Equal(5000u, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(8)));
            Assert.Equal(4096, BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(12)));
            Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(16)));
        }

        [Fact]
        public async Task CheckEquipset_Sends0x052()
        {
            await _inventory.CheckEquipsetAsync(EquipSlotId.Hands, 5, ContainerId.Wardrobe, 14000);

            byte[] p = SingleSent(0x052, 76);
            Assert.Equal((byte)EquipSlotId.Hands, p[4]);
            Assert.Equal(((byte)ContainerId.Wardrobe << 2) | 0x01, p[8]);
            Assert.Equal(5, p[9]);
            Assert.Equal(14000, BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(10)));
        }

        [Fact]
        public async Task SetLockstyle_Sends0x053WithModeAndItems()
        {
            await _inventory.SetLockstyleAsync(LockstyleMode.Set, (3, EquipSlotId.Head, ContainerId.Inventory, 12000));

            byte[] p = SingleSent(0x053, 136);
            Assert.Equal(1, p[4]);
            Assert.Equal((byte)LockstyleMode.Set, p[5]);
            Assert.Equal(3, p[8]);
            Assert.Equal((byte)EquipSlotId.Head, p[9]);
            Assert.Equal(12000, BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(12)));
        }

        [Fact]
        public async Task MarkKeyItemSeen_SendsTableFlagsOnceAndMarksSeen()
        {
            // Key item 515 is table 1, bit 3; 520 (bit 8) is held and already seen.
            var acquired = new uint[16];
            var seen = new uint[16];
            acquired[0] = (1u << 3) | (1u << 8);
            seen[0] = 1u << 8;
            _progression.UpdateKeyItems(1, acquired, seen);

            await _progressionModule.MarkKeyItemSeenAsync(515, 10);

            byte[] p = SingleSent(0x064, 76);
            Assert.Equal(0x01020304u, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(4)));
            Assert.Equal((1u << 3) | (1u << 8), BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(8)));
            Assert.Equal(10, BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(72)));
            Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(74)));
            Assert.True(_progression.IsKeyItemSeen(515));

            // Already seen, or not held: nothing is sent.
            await _progressionModule.MarkKeyItemSeenAsync(515, 10);
            await _progressionModule.MarkKeyItemSeenAsync(516, 10);
            Assert.Single(_sent);
        }

        [Fact]
        public async Task MyRoomIs_Sends0x0CB()
        {
            await _progressionModule.SendMyRoomIsAsync(MyRoomIsKind.Remodel, 0, 618);

            byte[] p = SingleSent(0x0CB, 8);
            Assert.Equal((byte)MyRoomIsKind.Remodel, p[4]);
            Assert.Equal(618, BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(6)));
        }

        [Fact]
        public async Task MyRoomLayout_Sends0x0FA()
        {
            await _progressionModule.SendMyRoomLayoutAsync(0x0080, 12, 1, 0, 3, 4, 5, 2);

            byte[] p = SingleSent(0x0FA, 16);
            Assert.Equal(0x0080, BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4)));
            Assert.Equal(new byte[] { 12, 1, 0, 3, 4, 5, 2 }, p.AsSpan(6, 7).ToArray());
        }

        [Fact]
        public async Task ChocoboRaceReq_Sends0x09B()
        {
            await _progressionModule.SendChocoboRaceReqAsync(ChocoboRaceReqParam.RacingWindowOpen, ChocoboRaceReqKind.Toteboard);

            byte[] p = SingleSent(0x09B, 12);
            Assert.Equal(0x0Fu, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(4)));
            Assert.Equal((uint)ChocoboRaceReqKind.Toteboard, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(8)));
        }

        [Fact]
        public async Task UnityMenu_SendsBlockZeroThenBlockOne()
        {
            await _progressionModule.SendUnityMenuAsync();

            Assert.Equal(2, _sent.Count);
            for (uint block = 0; block < 2; block++)
            {
                byte[] p = _sent[(int)block];
                Assert.True(PacketHeader.TryParse(p, out var header));
                Assert.Equal(0x116, header.PacketId);
                Assert.Equal(8, p.Length);
                Assert.Equal(block, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(4)));
            }
        }

        [Fact]
        public async Task UnityQuestAndToggle_Send0x117And0x118()
        {
            await _progressionModule.SendUnityQuestAsync();
            byte[] quest = SingleSent(0x117, 8);
            Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(quest.AsSpan(4)));

            _sent.Clear();
            await _progressionModule.SendUnityToggleAsync(true);
            byte[] toggle = SingleSent(0x118, 8);
            Assert.Equal(1, toggle[4]);
        }
    }
}
