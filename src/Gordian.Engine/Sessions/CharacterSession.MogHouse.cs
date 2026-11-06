// src/Gordian.Engine/Sessions/CharacterSession.MogHouse.cs
using System.Linq;
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
        /// back in the Mog Safes' S2C 0x020 extra data (and 0x0FA), so the room's pieces follow the inventory. A zone change
        /// ends the layout.
        /// </summary>
        private void AttachMogHouse()
        {
            var menus = ActionService.Menus;
            menus.MogHouse = new MogHouseActions(this);
            menus.MogSafe2Unlocked = () => MogSafe2Unlocked;
            _mannequins = new MogHouseMannequins(World, Inventory, menus.MogHouseLayout);
            menus.Changed += SyncMannequins;
            Inventory.MannequinLooksChanged += SyncMannequins;
            Progression.MogMenuOpened += () => menus.OpenMoogleMenu();
            Progression.MyRoomOperationReceived += _ => menus.OnMogHouseFurnitureChanged();
            Inventory.ItemChanged += (container, _, _) =>
            {
                if (container is ContainerId.MogSafe or ContainerId.MogSafe2) menus.OnMogHouseFurnitureChanged();
            };
            World.ZoneChanged += _ => menus.OnMogHouseZoneChanged();
            // Into or out of a room the zone id stays the city's: the room's own change reads the furniture for its floor.
            World.MapZoneChanged += _ => menus.OnMogHouseFurnitureChanged();
        }

        private MogHouseMannequins? _mannequins;

        private void SyncMannequins() => _mannequins?.Sync(World.MogHouseRoom != 0);

        /// <summary>
        /// Whether Mog Safe 2 is open. The server does not say: LandSandBoat gives every character the container, and the flag
        /// (<c>mhflag</c> 0x20) that the unlock cutscene sets never reaches the client. So: the three quests done (the cutscene follows
        /// at home), or the safe in use, or the second floor.
        /// </summary>
        private bool MogSafe2Unlocked =>
            StockUiMogHouse.Safe2Quests.All(q => Progression.IsQuestComplete(q.Area, q.Quest))
            || ActionService.Menus.MogHouseLayout.SecondFloor
            || Inventory.SnapshotItems(ContainerId.MogSafe2).Any(i => i.ItemId != 0);

        /// <summary>A frame of the Mog House view: the Layout's camera, and the mannequin spinning while its window is open.</summary>
        public void UpdateMogHouseView(float seconds)
        {
            var menus = ActionService.Menus;
            var open = menus.OpenMannequin;
            menus.MogHouseCamera.Update(menus.MogHouseLayout, seconds, open is { } piece ? menus.MogHouseLayout.CenterOf(piece) : null);
            _mannequins?.Spin(open, seconds);
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
