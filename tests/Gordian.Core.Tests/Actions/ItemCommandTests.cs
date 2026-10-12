// tests/Gordian.Core.Tests/Actions/ItemCommandTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Actions;
using Gordian.Core.Config;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Actions
{
    /// <summary><c>/item</c> and <c>/equip</c> (#351, #352) and the Items window's target choice, through <see cref="PlayerActionService"/>.</summary>
    public class ItemCommandTests
    {
        private const uint SelfId = 0x01020304, HareId = 0x02020202;
        private const ushort Potion = 4112, EchoDrops = 4151, BronzeCap = 12448, Grenade = 4199;

        private readonly WorldState _world = new();
        private readonly LocalPlayerState _localPlayer = new() { ServerId = SelfId };
        private readonly InventoryState _inventory = new();
        private readonly List<byte[]> _sent = new();
        private readonly PlayerActionService _actions;

        private static readonly Dictionary<ushort, ItemRecord> Records = new()
        {
            [Potion] = new ItemRecord { ItemId = Potion, Name = "Potion", StackSize = 12, ValidTargets = StockUiItems.TargetSelf | StockUiItems.TargetParty, Flags = StockUiItems.CanUseFlag },
            [EchoDrops] = new ItemRecord { ItemId = EchoDrops, Name = "Echo Drops", LogName = "flask of echo drops", StackSize = 12, ValidTargets = StockUiItems.TargetSelf, Flags = StockUiItems.CanUseFlag },
            [Grenade] = new ItemRecord { ItemId = Grenade, Name = "Grenade", StackSize = 12, ValidTargets = StockUiItems.TargetEnemy, Flags = StockUiItems.CanUseFlag },
            [BronzeCap] = new ItemRecord { ItemId = BronzeCap, Name = "Bronze Cap", Level = 7, JobsMask = 0x7FFFFE, EquipSlotsMask = 1 << (int)EquipSlotId.Head },
        };

        public ItemCommandTests()
        {
            Task Capture(ReadOnlyMemory<byte> mem, bool urgent)
            {
                _sent.Add(mem.ToArray());
                return Task.CompletedTask;
            }
            var profile = new SessionProfile();
            _actions = new PlayerActionService(profile, _world, _localPlayer,
                new CombatPacketModule(new CombatState(), _localPlayer, Capture), new ChatPacketModule(Capture),
                new PartyPacketModule(new PartyState(), Capture), new EntityPacketModule(_world, _localPlayer, Capture),
                new LifecyclePacketModule(profile, Capture), Capture);
            _actions.InventoryModule = new InventoryPacketModule(_inventory, _localPlayer, Capture);
            _actions.Menus.Inventory = _inventory;
            _actions.Menus.ItemLookup = id => Records.TryGetValue(id, out var r) ? r : null;
            _world.UpsertEntity(new WorldEntity(SelfId, 10, EntityType.Player) { Name = "TestPlayer" });
            _world.UpsertEntity(new WorldEntity(HareId, 25, EntityType.Monster) { Name = "Forest_Hare", Hpp = 100 });
            _inventory.SetItem(ContainerId.Inventory, 3, Potion, 5, ItemLockFlag.Normal);
            _inventory.SetItem(ContainerId.Inventory, 4, EchoDrops, 2, ItemLockFlag.Normal);
            _inventory.SetItem(ContainerId.Inventory, 6, Grenade, 2, ItemLockFlag.Normal);
            _inventory.SetItem(ContainerId.Wardrobe, 7, BronzeCap, 1, ItemLockFlag.Normal);
        }

        private byte[] SentPacket(ushort opcode)
        {
            byte[] packet = Assert.Single(_sent);
            Assert.True(PacketHeader.TryParse(packet, out var header));
            Assert.Equal(opcode, header.PacketId);
            return packet;
        }

        [Fact]
        public async Task Item_UsesTheNamedItemOnYourself_WithItemNumZero()
        {
            var result = await _actions.ExecuteCommandAsync("/item \"Echo Drops\" <me>");
            Assert.True(result.Success, result.Message);
            byte[] p = SentPacket(0x037);
            Assert.Equal(SelfId, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(4)));
            Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(8))); // LandSandBoat rejects a non-zero ItemNum
            Assert.Equal(10, BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(12)));
            Assert.Equal(4, p[14]);
            Assert.Equal((uint)ContainerId.Inventory, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(16)));
        }

        [Fact]
        public async Task Item_OnTheTarget_AndRefusedWhenTheItemCannotReachIt()
        {
            _world.TryGetByServerId(HareId, out var hare); _actions.SetTarget(hare);
            var refused = await _actions.ExecuteCommandAsync("/item Potion <t>");
            Assert.Equal(PlayerActionResultKind.Warning, refused.Kind);
            Assert.Empty(_sent);

            var thrown = await _actions.ExecuteCommandAsync("/item Grenade <t>");
            Assert.True(thrown.Success, thrown.Message);
            Assert.Equal(HareId, BinaryPrimitives.ReadUInt32LittleEndian(SentPacket(0x037).AsSpan(4)));
        }

        [Fact]
        public async Task Item_UnknownItemSendsNothing()
        {
            var result = await _actions.ExecuteCommandAsync("/item \"Elixir\"");
            Assert.Equal(PlayerActionResultKind.Warning, result.Kind);
            Assert.Empty(_sent);
        }

        [Fact]
        public void ItemsWindowTarget_SelfOnlyItemsGoToYou_OthersToAnAllowedTarget()
        {
            var resolve = _actions.Menus.ItemUseTarget!;
            Assert.Equal(SelfId, resolve(StockUiItems.TargetSelf, null).Target!.Value.ServerId);
            _world.TryGetByServerId(HareId, out var hare); _actions.SetTarget(hare);
            Assert.Equal(HareId, resolve(StockUiItems.TargetEnemy, null).Target!.Value.ServerId);
            // A potion cannot go to the hare: yourself instead.
            Assert.Equal(SelfId, resolve((ushort)(StockUiItems.TargetSelf | StockUiItems.TargetParty), null).Target!.Value.ServerId);
            // From the command menu, the menu's target or an error.
            var onHare = resolve(StockUiItems.TargetSelf, new StockUiTargetContext(StockUiTargetKind.Monster, HareId, "Forest_Hare"));
            Assert.Null(onHare.Target);
            Assert.Equal("You cannot use that item on Forest_Hare.", onHare.Error);
        }

        [Fact]
        public async Task Equip_FindsTheItemInTheWardrobe()
        {
            var result = await _actions.ExecuteCommandAsync("/equip head \"Bronze Cap\"");
            Assert.True(result.Success, result.Message);
            byte[] p = SentPacket(0x050);
            Assert.Equal((7, (byte)EquipSlotId.Head, (byte)ContainerId.Wardrobe), (p[4], p[5], p[6]));
        }

        [Fact]
        public async Task Equip_WithoutANameRemovesTheSlotsItem()
        {
            _inventory.SetEquip(EquipSlotId.Head, ContainerId.Wardrobe, 7);
            var result = await _actions.ExecuteCommandAsync("/equip head");
            Assert.True(result.Success, result.Message);
            byte[] p = SentPacket(0x050);
            Assert.Equal((0, (byte)EquipSlotId.Head, (byte)ContainerId.Inventory), (p[4], p[5], p[6]));
        }

        [Theory]
        [InlineData("/equip hat \"Bronze Cap\"")]
        [InlineData("/equip body \"Bronze Cap\"")]
        public async Task Equip_WrongSlotSendsNothing(string input)
        {
            var result = await _actions.ExecuteCommandAsync(input);
            Assert.Equal(PlayerActionResultKind.Warning, result.Kind);
            Assert.Empty(_sent);
        }
    }
}
