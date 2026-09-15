// tests/Gordian.App.Tests/ViewModels/InventoryViewModelTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gordian.App.ViewModels;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.ViewModels
{
    public sealed class InventoryViewModelTests : IDisposable
    {
        private readonly SessionRegistry _registry;
        private readonly InventoryViewModel _vm;

        public InventoryViewModelTests()
        {
            InventoryViewModel.UiDispatcher = a => a();
            _registry = new SessionRegistry();
            _vm = new InventoryViewModel(_registry);
        }

        public void Dispose()
        {
            _vm.Dispose();
            InventoryViewModel.UiDispatcher = null;
        }

        [Fact]
        public void InitialState_WithoutSession_HasSafeDefaults()
        {
            Assert.False(_vm.HasActiveSession);
            Assert.Null(_vm.SelectedSession);
            Assert.Equal("No Active Session", _vm.SessionHeaderTitle);
            Assert.Equal(18, _vm.Containers.Count);
            Assert.NotNull(_vm.SelectedContainer);
            Assert.Equal(ContainerId.Inventory, _vm.SelectedContainer.Id);
            Assert.Equal(18, _vm.EquippedSlots.Count);
            Assert.Empty(_vm.ContainerItems);
            Assert.Empty(_vm.FilteredItems);
            Assert.Equal(0, _vm.SparksOfEminence);
            Assert.Equal(0, _vm.Gallimaufry);
        }

        [Fact]
        public void RegisterSession_AutoSelectsSession()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 12345, "user1", netManager);

            _registry.RegisterSession(session);

            Assert.True(_vm.HasActiveSession);
            Assert.Same(session, _vm.SelectedSession);
            Assert.Contains("Cybin", _vm.SessionHeaderTitle);
        }

        [Fact]
        public void InboundItemMax_0x01C_UpdatesContainerCapacities()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 12345, "user1", netManager);
            _registry.RegisterSession(session);

            // Construct S2C 0x01C payload: 68 bytes minimum
            // Container 0: MaxSize = 81 (meaning 80), UsableSize = 81 (meaning 80)
            // Container 8 (Wardrobe): MaxSize = 81 (80), UsableSize = 81 (80)
            byte[] payload = new byte[72];
            payload[0] = 81; // Inventory max = 80
            payload[8] = 81; // Wardrobe max = 80

            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(32 + 0 * 2, 2), 81); // Inventory usable = 80
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(32 + 8 * 2, 2), 81); // Wardrobe usable = 80

            session.NetworkManager.Parser.Dispatcher.Dispatch(
                new PacketHeader(0x01C, payload.Length + 4, 1),
                payload
            );

            var invSummary = _vm.Containers.First(c => c.Id == ContainerId.Inventory);
            Assert.Equal(80, invSummary.MaxSize);
            Assert.Equal(80, invSummary.UsableSize);
            Assert.Equal("0/80", invSummary.CapacityText);

            var wardrobeSummary = _vm.Containers.First(c => c.Id == ContainerId.Wardrobe);
            Assert.Equal(80, wardrobeSummary.MaxSize);
            Assert.Equal(80, wardrobeSummary.UsableSize);
        }

        [Fact]
        public void InboundItemList_0x01F_PopulatesContainerItems()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 12345, "user1", netManager);
            _registry.RegisterSession(session);

            // S2C 0x01F: Count(4), ItemId(2), Container(1), Slot(1), Lock(1)
            byte[] payload = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 12);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 0x1000); // 4096
            payload[6] = (byte)ContainerId.Inventory;
            payload[7] = 5; // Slot 5
            payload[8] = (byte)ItemLockFlag.NoDrop;

            session.NetworkManager.Parser.Dispatcher.Dispatch(
                new PacketHeader(0x01F, payload.Length + 4, 1),
                payload
            );

            Assert.Single(_vm.ContainerItems);
            Assert.Single(_vm.FilteredItems);

            var item = _vm.ContainerItems[0];
            Assert.Equal(5, item.Slot);
            Assert.Equal(0x1000, item.ItemId);
            Assert.Equal(12u, item.Count);
            Assert.Equal("x12", item.CountDisplay);
            Assert.Equal(ItemLockFlag.NoDrop, item.LockFlag);
            Assert.Equal("NoDrop", item.LockDisplay);
            Assert.Equal("#05", item.SlotDisplay);
            Assert.Equal(ItemNameResolver.Resolve(0x1000), item.ItemDisplay);
        }

        [Fact]
        public void InboundItemList_Gil_FormatsAsGil()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 12345, "user1", netManager);
            _registry.RegisterSession(session);

            // Gil is ItemId 65535
            byte[] payload = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 50000);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 65535);
            payload[6] = (byte)ContainerId.Inventory;
            payload[7] = 0;
            payload[8] = 0;

            session.NetworkManager.Parser.Dispatcher.Dispatch(
                new PacketHeader(0x01F, payload.Length + 4, 1),
                payload
            );

            var item = _vm.ContainerItems[0];
            Assert.Equal("Gil", item.ItemDisplay);
            Assert.Equal(50000u, item.Count);
        }

        [Fact]
        public void InboundItemNum_0x01E_UpdatesItemCountAndLock()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 12345, "user1", netManager);
            _registry.RegisterSession(session);

            // Add item first
            session.Inventory.SetItem(ContainerId.Inventory, 3, 500, 10, ItemLockFlag.Normal);

            Assert.Single(_vm.ContainerItems);
            Assert.Equal(10u, _vm.ContainerItems[0].Count);

            // S2C 0x01E: Count(4), Container(1), Slot(1), Lock(1)
            byte[] payload = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 25);
            payload[4] = (byte)ContainerId.Inventory;
            payload[5] = 3;
            payload[6] = (byte)ItemLockFlag.Linkshell;

            session.NetworkManager.Parser.Dispatcher.Dispatch(
                new PacketHeader(0x01E, payload.Length + 4, 1),
                payload
            );

            Assert.Single(_vm.ContainerItems);
            Assert.Equal(25u, _vm.ContainerItems[0].Count);
            Assert.Equal("x25", _vm.ContainerItems[0].CountDisplay);
            Assert.Equal(ItemLockFlag.Linkshell, _vm.ContainerItems[0].LockFlag);
        }

        [Fact]
        public void InboundItemAttr_0x020_UpdatesPriceAndExtData()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 12345, "user1", netManager);
            _registry.RegisterSession(session);

            // S2C 0x020: Count(4), Price(4), ItemId(2), Container(1), Slot(1), Lock(1), ExtData(23)
            byte[] payload = new byte[36];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 75000); // 75k gil
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8, 2), 0x2A10);
            payload[10] = (byte)ContainerId.Inventory;
            payload[11] = 7;
            payload[12] = (byte)ItemLockFlag.Normal;
            payload[13] = 0xAA;
            payload[14] = 0xBB;
            payload[15] = 0xCC;

            session.NetworkManager.Parser.Dispatcher.Dispatch(
                new PacketHeader(0x020, payload.Length + 4, 1),
                payload
            );

            var item = _vm.ContainerItems.First(i => i.Slot == 7);
            Assert.Equal(75000u, item.Price);
            Assert.True(item.HasPrice);
            Assert.Equal("75,000 gil", item.PriceDisplay);
            Assert.True(item.HasExtData);
            Assert.StartsWith("AABBCC", item.ExtDataHex);
        }

        [Fact]
        public void InboundEquipList_0x050_UpdatesEquippedSlotAndItemTag()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 12345, "user1", netManager);
            _registry.RegisterSession(session);

            // Place an armor item in Inventory slot 4
            session.Inventory.SetItem(ContainerId.Inventory, 4, 0x3000, 1, ItemLockFlag.Normal);

            var item = _vm.ContainerItems.First(i => i.Slot == 4);
            Assert.False(item.IsEquipped);

            // S2C 0x050: Slot(1), EquipSlot(1), Container(1)
            byte[] payload = new byte[4];
            payload[0] = 4; // Slot 4
            payload[1] = (byte)EquipSlotId.Body; // Body slot
            payload[2] = (byte)ContainerId.Inventory;

            session.NetworkManager.Parser.Dispatcher.Dispatch(
                new PacketHeader(0x050, payload.Length + 4, 1),
                payload
            );

            // 1. Check EquippedSlots collection
            var bodySlot = _vm.EquippedSlots.First(s => s.SlotId == EquipSlotId.Body);
            Assert.False(bodySlot.IsEmpty);
            Assert.Equal(ContainerId.Inventory, bodySlot.Container);
            Assert.Equal(4, bodySlot.ContainerSlot);
            Assert.Equal((ushort)0x3000, bodySlot.ItemId);
            Assert.Equal(ItemNameResolver.Resolve(0x3000), bodySlot.ItemDisplay);
            Assert.Equal("Inventory #04", bodySlot.LocationDisplay);

            // 2. Check item in ContainerItems
            Assert.True(item.IsEquipped);
            Assert.Equal("Body", item.EquippedSlotName);
            Assert.Equal("[Body]", item.EquippedDisplay);
        }

        [Fact]
        public void InboundCurrencies_0x113_And_0x118_UpdatesProperties()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 12345, "user1", netManager);
            _registry.RegisterSession(session);

            // Currencies 1 (0x113): 248 bytes
            byte[] cur1 = new byte[248];
            BinaryPrimitives.WriteInt32LittleEndian(cur1.AsSpan(0, 4), 12000);  // Sandy
            BinaryPrimitives.WriteInt32LittleEndian(cur1.AsSpan(4, 4), 8000);   // Bastok
            BinaryPrimitives.WriteInt32LittleEndian(cur1.AsSpan(8, 4), 6000);   // Windy
            BinaryPrimitives.WriteInt32LittleEndian(cur1.AsSpan(112, 4), 99999); // Sparks
            BinaryPrimitives.WriteUInt16LittleEndian(cur1.AsSpan(166, 2), 500); // Login points
            BinaryPrimitives.WriteInt32LittleEndian(cur1.AsSpan(224, 4), 45000); // Unity accolades

            session.NetworkManager.Parser.Dispatcher.Dispatch(
                new PacketHeader(0x113, cur1.Length + 4, 1),
                cur1
            );

            Assert.Equal(99999, _vm.SparksOfEminence);
            Assert.Equal(45000, _vm.UnityAccolades);
            Assert.Equal(12000, _vm.ConquestSandoria);
            Assert.Equal(8000, _vm.ConquestBastok);
            Assert.Equal(6000, _vm.ConquestWindurst);
            Assert.Equal((ushort)500, _vm.LoginPoints);

            // Currencies 2 (0x118): 156 bytes
            byte[] cur2 = new byte[156];
            BinaryPrimitives.WriteInt32LittleEndian(cur2.AsSpan(0, 4), 35000);   // Bayld
            BinaryPrimitives.WriteUInt16LittleEndian(cur2.AsSpan(70, 2), 150);  // Escha beads
            BinaryPrimitives.WriteInt32LittleEndian(cur2.AsSpan(140, 4), 25000); // Gallimaufry

            session.NetworkManager.Parser.Dispatcher.Dispatch(
                new PacketHeader(0x118, cur2.Length + 4, 1),
                cur2
            );

            Assert.Equal(35000, _vm.Bayld);
            Assert.Equal((ushort)150, _vm.EschaBeads);
            Assert.Equal(25000, _vm.Gallimaufry);
        }

        [Fact]
        public void ItemSearchFilter_FiltersVisibleItems()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 12345, "user1", netManager);
            _registry.RegisterSession(session);

            session.Inventory.SetItem(ContainerId.Inventory, 1, 0x1234, 1, ItemLockFlag.Normal);
            session.Inventory.SetItem(ContainerId.Inventory, 2, 65535, 100, ItemLockFlag.Normal); // Gil
            session.Inventory.SetItem(ContainerId.Inventory, 3, 0xABCD, 5, ItemLockFlag.Normal);

            Assert.Equal(3, _vm.ContainerItems.Count);
            Assert.Equal(3, _vm.FilteredItems.Count);

            _vm.ItemSearchFilter = "Gil";
            Assert.Single(_vm.FilteredItems);
            Assert.Equal("Gil", _vm.FilteredItems[0].ItemDisplay);

            _vm.ItemSearchFilter = "1234";
            Assert.Single(_vm.FilteredItems);
            Assert.Equal(0x1234, _vm.FilteredItems[0].ItemId);

            _vm.ItemSearchFilter = string.Empty;
            Assert.Equal(3, _vm.FilteredItems.Count);
        }

        [Fact]
        public async Task OutboundCommands_ExecuteAsync()
        {
            var sentChunks = new List<byte[]>();
            var netManager = new SessionNetworkManager("127.0.0.1", 54230)
            {
                OutboundChunkOverride = (data, _) =>
                {
                    sentChunks.Add(data.ToArray());
                    return Task.CompletedTask;
                }
            };
            var session = new CharacterSession("Cybin", 12345, "user1", netManager);
            _registry.RegisterSession(session);

            // Execute commands and verify status updates and chunk transmission
            await _vm.ExecuteRequestCurrencies1Async();
            Assert.Contains("Requested Currencies 1", _vm.StatusText);
            Assert.Single(sentChunks);

            await _vm.ExecuteRequestCurrencies2Async();
            Assert.Contains("Requested Currencies 2", _vm.StatusText);
            Assert.Equal(2, sentChunks.Count);

            await _vm.ExecuteSortContainerAsync();
            Assert.Contains("Sent sort request", _vm.StatusText);
            Assert.Equal(3, sentChunks.Count);
        }
    }
}
