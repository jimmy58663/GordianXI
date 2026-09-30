// tests/Gordian.Core.Tests/Network/EquipClearPacketTests.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>S2C 0x04F GP_SERV_COMMAND_EQUIP_CLEAR empties the equipment slots before the 0x050 resend (#108).</summary>
    public class EquipClearPacketTests
    {
        private static readonly Func<ReadOnlyMemory<byte>, bool, Task> NoSend = (_, _) => Task.CompletedTask;

        [Fact]
        public void EquipClear_EmptiesEverySlot_AndRaisesOnlyForFilledOnes()
        {
            var state = new InventoryState();
            var dispatcher = new PacketDispatcher();
            new InventoryPacketModule(state, new LocalPlayerState(), NoSend).Register(dispatcher);
            state.SetEquip(EquipSlotId.Main, ContainerId.Inventory, 5);
            state.SetEquip(EquipSlotId.Body, ContainerId.Wardrobe, 12);

            var changed = new List<(EquipSlotId Slot, byte Index)>();
            state.EquipChanged += (slot, _, index) => changed.Add((slot, index));
            dispatcher.Dispatch(new PacketHeader(S2C_0x04F_EquipClear.PacketId, 8, 1), new byte[4]);

            for (int i = 0; i < (int)EquipSlotId.Count; i++)
            {
                Assert.Equal((ContainerId.Inventory, (byte)0xFF), state.GetEquipped((EquipSlotId)i));
            }
            Assert.Equal(2, changed.Count);
            Assert.Contains((EquipSlotId.Main, (byte)0xFF), changed);
            Assert.Contains((EquipSlotId.Body, (byte)0xFF), changed);
        }

        [Fact]
        public void EquipClear_ThenEquipList_LeavesOnlyTheResentSlots()
        {
            var state = new InventoryState();
            var dispatcher = new PacketDispatcher();
            new InventoryPacketModule(state, new LocalPlayerState(), NoSend).Register(dispatcher);
            state.SetEquip(EquipSlotId.Main, ContainerId.Inventory, 5);
            state.SetEquip(EquipSlotId.Head, ContainerId.Inventory, 6);

            // Job change: the old head piece is no longer worn, so only Main is resent.
            dispatcher.Dispatch(new PacketHeader(S2C_0x04F_EquipClear.PacketId, 8, 1), new byte[4]);
            state.SetEquip(EquipSlotId.Main, ContainerId.Inventory, 5);

            Assert.Equal((ContainerId.Inventory, (byte)5), state.GetEquipped(EquipSlotId.Main));
            Assert.Equal((ContainerId.Inventory, (byte)0xFF), state.GetEquipped(EquipSlotId.Head));
        }
    }
}
