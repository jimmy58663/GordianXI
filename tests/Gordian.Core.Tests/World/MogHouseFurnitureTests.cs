// tests/Gordian.Core.Tests/World/MogHouseFurnitureTests.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.World
{
    /// <summary>The Mog House furniture (#93): placements from the Mog Safes' extra data, the layout grid, C2S 0x0FB, S2C 0x02E / 0x0FA events.</summary>
    public class MogHouseFurnitureTests
    {
        private const ushort SimpleBed = 2, BrassFlowerpot = 217, HumeMannequin = 256, Chestnut = 639;

        private static readonly Dictionary<ushort, ItemRecord> Records = new()
        {
            [SimpleBed] = new ItemRecord { ItemId = SimpleBed, Name = "Simple Bed", ItemType = MogHouseFurniture.FurnishingItemType },
            [BrassFlowerpot] = new ItemRecord { ItemId = BrassFlowerpot, Name = "Brass Flowerpot", ItemType = MogHouseFurniture.FlowerpotItemType },
            [HumeMannequin] = new ItemRecord { ItemId = HumeMannequin, Name = "H Mannequin", ItemType = MogHouseFurniture.MannequinItemType },
            [Chestnut] = new ItemRecord { ItemId = Chestnut, Name = "Ronfaure Chestnut", ItemType = 1 },
        };

        /// <summary>LandSandBoat's furniture extra data: byte 1 bit 0 second floor, bit 6 placed; x +6, level +7, row +8, rotation +9.</summary>
        internal static byte[] FurnitureExt(bool installed, bool secondFloor, byte x, byte level, byte z, byte rotation)
        {
            var ext = new byte[24];
            ext[1] = (byte)((installed ? 0x40 : 0) | (secondFloor ? 0x01 : 0));
            ext[6] = x;
            ext[7] = level;
            ext[8] = z;
            ext[9] = rotation;
            return ext;
        }

        [Fact]
        public void Placement_ReadsTheExtraData()
        {
            var item = new InventoryItem(SimpleBed, 1, 4, ContainerId.MogSafe, 0, ItemLockFlag.Normal, FurnitureExt(true, false, 2, 0, 21, 3));
            var p = FurniturePlacement.Read(item);
            Assert.True(p.Installed);
            Assert.False(p.SecondFloor);
            Assert.Equal((2, 0, 21, 3), (p.X, p.Level, p.Z, p.Rotation));
            Assert.Equal((ContainerId.MogSafe, (byte)4, SimpleBed), (p.Container, p.Slot, p.ItemId));

            var bare = FurniturePlacement.Read(new InventoryItem(SimpleBed, 1, 5, ContainerId.MogSafe2, 0, ItemLockFlag.Normal));
            Assert.False(bare.Installed);
        }

        [Fact]
        public void MannequinGear_ReadsTheStorageSlots()
        {
            var ext = FurnitureExt(true, false, 1, 0, 1, 0);
            for (int i = 0; i < 8; i++) ext[10 + i] = (byte)(i == 3 ? 7 : 0);
            ext[18] = 2;
            ext[19] = 1;
            var gear = MannequinGear.Read(new InventoryItem(HumeMannequin, 1, 1, ContainerId.MogSafe, 0, ItemLockFlag.Normal, ext));
            Assert.Equal(7, gear.Head);
            Assert.Equal(7, gear[3]);
            Assert.Equal(0, gear[0]);
            Assert.Equal((2, 1), (gear.Race, gear.Pose));
        }

        [Theory]
        [InlineData(0, 0, false, true)]
        [InlineData(5, 17, false, true)]
        [InlineData(6, 17, false, false)] // the block in front of the door
        [InlineData(13, 0, false, false)]
        [InlineData(13, 18, false, true)]
        [InlineData(19, 23, false, true)]
        [InlineData(19, 24, false, false)]
        [InlineData(10, 5, true, true)]   // the second floor has no block
        [InlineData(19, 25, true, true)]
        [InlineData(20, 0, true, false)]
        public void Grid_MatchesTheFloors(int x, int z, bool secondFloor, bool usable) =>
            Assert.Equal(usable, MogHouseFurniture.IsFloorCell(x, z, secondFloor));

        [Fact]
        public void Snapshot_ListsTheFurnishingsOfBothSafes()
        {
            var inv = new InventoryState();
            inv.SetItemAttributes(ContainerId.MogSafe, 3, SimpleBed, 1, 0, ItemLockFlag.Normal, FurnitureExt(true, false, 1, 0, 20, 0));
            inv.SetItemAttributes(ContainerId.MogSafe, 1, Chestnut, 5, 0, ItemLockFlag.Normal, ReadOnlySpan<byte>.Empty);
            inv.SetItemAttributes(ContainerId.MogSafe2, 2, BrassFlowerpot, 1, 0, ItemLockFlag.Normal, FurnitureExt(false, false, 0, 0, 0, 0));
            inv.SetItemAttributes(ContainerId.MogSafe, 2, HumeMannequin, 1, 0, ItemLockFlag.Normal, FurnitureExt(true, false, 3, 0, 20, 1));

            var list = MogHouseFurniture.Snapshot(inv, id => Records.TryGetValue(id, out var r) ? r : null);
            Assert.Equal(new[] { HumeMannequin, SimpleBed, BrassFlowerpot }, list.ConvertAll(p => p.ItemId));
            Assert.Empty(MogHouseFurniture.Snapshot(inv, null));
            Assert.True(MogHouseFurniture.IsMannequin(HumeMannequin));
            Assert.False(MogHouseFurniture.IsMannequin(SimpleBed));
        }

        [Fact]
        public void FurnitureTable_ReadsItsRecords_AndTheModelFileRuns()
        {
            // A container of one 0x59 section: the walker's 16-byte header, a record for a 4 x 6 bed and an empty one.
            var table = new byte[16];
            Array.Copy(new byte[] { 0, 0, 2, 0, 4, 6, 70, 0 }, table, 8);
            Array.Copy(new byte[] { 0, 0, 3, 0, 0, 0, 0, 0 }, 0, table, 8, 8);
            var dat = new byte[16 + 16 + 16];
            uint size = (uint)((16 + 16) / 16);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(dat.AsSpan(4), 0x59u | (size << 7));
            Array.Copy(table, 0, dat, 16, 16);
            var parsed = FurnitureTable.Parse(dat);
            Assert.Single(parsed);
            Assert.Equal(new FurnitureInfo(FurnitureFlags.Floor, 4, 6, 70), parsed[2]);

            Assert.Equal(32929, FurnitureTable.ModelFileId(2));
            Assert.Equal(0, FurnitureTable.ModelFileId(256)); // mannequins
            Assert.Equal(51464, FurnitureTable.ModelFileId(281));
            Assert.Equal(62825, FurnitureTable.ModelFileId(3598));
        }

        [Fact]
        public void RetailFurniture_MatchesTheTableAndItsMesh()
        {
            const string game = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(game)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(game);
            rm.InitializeFileTable();
            Assert.Equal(new FurnitureInfo(FurnitureFlags.Floor, 4, 6, 70), rm.GetFurnitureInfo(SimpleBed));
            Assert.Equal(FurnitureFlags.Wall, rm.GetFurnitureInfo(181)!.Value.Flags); // San d'Orian Flag
            Assert.Null(rm.GetFurnitureInfo(4096));
            var bed = rm.TryLoadFurnitureModel(SimpleBed);
            Assert.NotNull(bed);
            Assert.StartsWith("m_bed_02", bed!.Meshes[0].Name);
            Assert.NotEmpty(bed.Textures);
            Assert.Null(rm.TryLoadFurnitureModel(HumeMannequin));
        }

        [Fact]
        public void Placement_PutsTheMeshAtTheFootprintsMiddle()
        {
            var bed = new FurnitureInfo(FurnitureFlags.Floor, 4, 6, 70);
            // The room's middle is the grid's: (10, 12) on the first floor. x runs east, display x west.
            var at = MogHouseFurniture.FloorTransform(bed, 8, 9, 0, 0, false).Translation;
            Assert.Equal(0f, at.X, 3);
            Assert.Equal(0f, at.Z, 3);
            // Turned, the footprint is 6 x 4 and level 10 is a yalm up.
            at = MogHouseFurniture.FloorTransform(bed, 0, 0, 10, 1, false).Translation;
            Assert.Equal(6f - 3 * 0.6f, at.X, 3);
            Assert.Equal(1f, at.Y, 3);
            Assert.Equal(-7.2f + 2 * 0.6f, at.Z, 3);
            Assert.Equal((6, 4), MogHouseFurniture.Footprint(bed, 3));

            // The second floor's rows run 26, so its grid starts further south.
            Assert.Equal(-7.8f, MogHouseFurniture.CellCorner(0, 0, true).Z, 3);
            Assert.NotNull(MogHouseFurniture.WallTransform(4, false));
            Assert.Null(MogHouseFurniture.WallTransform(5, false));
            Assert.Null(MogHouseFurniture.WallTransform(0, true));
        }

        [Fact]
        public void C2S_0x0FB_BankIn_Builds()
        {
            byte[] p = ProgressionPacketBuilder.BuildMyRoomBankIn(SimpleBed, 9, (byte)ContainerId.MogSafe2, 5);
            Assert.Equal(8, p.Length);
            Assert.Equal(0x0FB | (2 << 9), p[0] | (p[1] << 8));
            Assert.Equal(5, p[2] | (p[3] << 8));
            Assert.Equal(SimpleBed, p[4] | (p[5] << 8));
            Assert.Equal(9, p[6]);
            Assert.Equal((byte)ContainerId.MogSafe2, p[7]);
        }

        [Fact]
        public async Task Module_RaisesTheMoogleMenuAndOperationEvents_AndSendsBankIn()
        {
            var state = new ProgressionState();
            var sent = new List<byte[]>();
            var module = new ProgressionPacketModule(state, new LocalPlayerState(), (m, _) => { sent.Add(m.ToArray()); return Task.CompletedTask; });
            var dispatcher = new PacketDispatcher();
            module.Register(dispatcher);
            int menus = 0;
            MyRoomOperationInfo? op = null;
            state.MogMenuOpened += () => menus++;
            state.MyRoomOperationReceived += o => op = o;

            Assert.True(dispatcher.Dispatch(new PacketHeader(0x02E, 4, 1), ReadOnlySpan<byte>.Empty));
            Assert.Equal(1, menus);
            Assert.True(state.MogMenuPending);

            var payload = new byte[12];
            payload[0] = (byte)SimpleBed;
            payload[2] = (byte)MyRoomOperationResult.Layout;
            payload[6] = 3;
            payload[7] = (byte)ContainerId.MogSafe;
            Assert.True(dispatcher.Dispatch(new PacketHeader(0x0FA, 16, 2), payload));
            Assert.Equal(new MyRoomOperationInfo(SimpleBed, MyRoomOperationResult.Layout, 3, ContainerId.MogSafe), op);

            await module.SendMyRoomBankInAsync(SimpleBed, 3, ContainerId.MogSafe);
            Assert.Equal(0x0FB, sent[^1][0] | ((sent[^1][1] & 1) << 8));
        }
    }
}
