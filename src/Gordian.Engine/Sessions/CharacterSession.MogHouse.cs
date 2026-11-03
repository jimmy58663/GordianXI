// src/Gordian.Engine/Sessions/CharacterSession.MogHouse.cs
using System.Threading.Tasks;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Gordian.Engine.Ui;

namespace Gordian.Engine.Sessions
{
    public sealed partial class CharacterSession
    {
        /// <summary>
        /// The Mog House menus (#93): S2C 0x02E opens the moogle's menu; Layout, Open Mog House, Remodel and the mannequin
        /// send C2S 0x0FA / 0x0FB / 0x0CB / 0x03B through the progression and inventory modules; the furniture's places come
        /// back in the Mog Safes' S2C 0x020 extra data (and 0x0FA), so the layout grid follows the inventory. A zone change
        /// ends the layout.
        /// </summary>
        private void AttachMogHouse()
        {
            var menus = ActionService.Menus;
            menus.MogHouse = new MogHouseActions(this);
            Progression.MogMenuOpened += () => menus.OpenMoogleMenu();
            Progression.MyRoomOperationReceived += _ => menus.OnMogHouseFurnitureChanged();
            Inventory.ItemChanged += (container, _, _) =>
            {
                if (container is ContainerId.MogSafe or ContainerId.MogSafe2) menus.OnMogHouseFurnitureChanged();
            };
            World.ZoneChanged += _ => menus.OnMogHouseZoneChanged();
        }

        private sealed class MogHouseActions : IStockUiMogHouse
        {
            private readonly CharacterSession _session;

            public MogHouseActions(CharacterSession session) => _session = session;

            /// <summary>Second-floor rooms are 0x267-0x26A (XiPackets world/server/0x000A).</summary>
            public bool OnSecondFloor => _session.World.MogHouseRoom is >= 0x267 and <= 0x26A;

            public Task PlaceAsync(FurniturePlacement piece, bool secondFloor, byte x, byte level, byte z, byte rotation) =>
                _session.ProgressionModule.SendMyRoomLayoutAsync(piece.ItemId, piece.Slot, (byte)piece.Container, secondFloor ? (byte)1 : (byte)0, x, level, z, rotation);

            public Task FinishLayoutAsync() => _session.ProgressionModule.SendMyRoomLayoutAsync(0, 0, 0, 0, 0, 0, 0, 0);

            public Task ClearAwayAsync(FurniturePlacement piece) =>
                _session.ProgressionModule.SendMyRoomBankInAsync(piece.ItemId, piece.Slot, piece.Container);

            public Task SetHouseOpenAsync(bool open) =>
                _session.ProgressionModule.SendMyRoomIsAsync(open ? MyRoomIsKind.Open : MyRoomIsKind.Close, 0, open ? (ushort)0 : (ushort)1);

            public Task RemodelAsync(ushort style) => _session.ProgressionModule.SendMyRoomIsAsync(MyRoomIsKind.Remodel, 0, style);

            public Task MannequinAsync(SubcontainerKind kind, FurniturePlacement mannequin, SubcontainerSlotIndex slot, byte storageSlot) =>
                _session.InventoryModule.UseSubcontainerAsync(kind, mannequin.Container, mannequin.Slot, slot, ContainerId.Storage, storageSlot);
        }
    }
}
