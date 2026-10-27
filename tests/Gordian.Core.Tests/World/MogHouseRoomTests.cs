using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.World;
using Gordian.Core.World.Collision;

namespace Gordian.Core.Tests.World
{
    /// <summary>The Mog House room: what is drawn and walked, and its way out (#354).</summary>
    public class MogHouseRoomTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private static ZoneLoginInfo Login(bool myRoom, ushort room) =>
            default(ZoneLoginInfo) with { LoginState = myRoom ? SaveLoginState.MyRoom : SaveLoginState.Game, MyroomMapNumber = room };

        [Fact]
        public void RoomNumbers_MapToTheirDats_UnderKeysApartFromZones()
        {
            Assert.True(MogHouseRooms.TryGetRoomFileId(0x121, out int file));
            Assert.Equal(389, file);
            Assert.True(MogHouseRooms.TryGetRoomFileId(214, out file));
            Assert.Equal(314, file);
            Assert.False(MogHouseRooms.TryGetRoomFileId(0x267, out _)); // second floor: not mapped yet
            Assert.False(MogHouseRooms.TryGetRoomFileId(0x1FF, out _)); // "not in the Mog House"

            ushort key = MogHouseRooms.ZoneKey(0x121);
            Assert.True(key >= 0x8000);
            Assert.True(MogHouseRooms.TryGetRoomOfKey(key, out int room));
            Assert.Equal(0x121, room);
            Assert.False(MogHouseRooms.TryGetRoomOfKey(230, out _));
            Assert.Equal(389, Gordian.Core.Resources.ZoneDataLoader.GetZoneModelFileId(key));
            Assert.Equal(330, Gordian.Core.Resources.ZoneDataLoader.GetZoneModelFileId(230));
        }

        [Fact]
        public void ExitAreas_FollowLandSandBoatsOptionOrder()
        {
            Assert.Empty(MogHouseRooms.ExitAreas(MogHouseExitBit.Default));
            Assert.Empty(MogHouseRooms.ExitAreas(MogHouseExitBit.RonfaureFront));
            Assert.Equal(new ushort[] { 230, 231, 232 }, MogHouseRooms.ExitAreas(MogHouseExitBit.SandOria));
            Assert.Equal(new ushort[] { 238, 239, 240, 241 }, MogHouseRooms.ExitAreas(MogHouseExitBit.Windurst));
            Assert.Equal(new ushort[] { 243, 244, 245, 246 }, MogHouseRooms.ExitAreas(MogHouseExitBit.Jeuno));
            Assert.Equal(new ushort[] { 48, 50 }, MogHouseRooms.ExitAreas(MogHouseExitBit.Whitegate));
            Assert.Equal(new ushort[] { 256, 257 }, MogHouseRooms.ExitAreas(MogHouseExitBit.Adoulin));
        }

        [Fact]
        public void MapZone_FollowsTheMogHouse_WithinOneCity()
        {
            var world = new WorldState();
            var raised = new List<ushort>();
            world.MapZoneChanged += raised.Add;

            // S2C 0x00A: login info first, then the zone (LifecycleModule), then the refresh.
            world.UpdateZoneLoginInfo(Login(false, 0x1FF));
            world.CurrentZoneId = 230;
            world.RefreshMapZone();
            Assert.Equal(new ushort[] { 230 }, raised);

            // Into the Mog House: the zone stays Southern San d'Oria, the map is the room.
            world.UpdateZoneLoginInfo(Login(true, 0x121));
            world.CurrentZoneId = 230;
            world.RefreshMapZone();
            Assert.Equal(MogHouseRooms.ZoneKey(0x121), world.MapZoneId);
            Assert.Equal(MogHouseRooms.ZoneKey(0x121), world.SceneZoneId);
            Assert.Equal(0x121, world.MogHouseRoom);
            Assert.Equal(230, world.CurrentZoneId);

            // Out to another city: one change, straight to it.
            world.UpdateZoneLoginInfo(Login(false, 0x1FF));
            world.CurrentZoneId = 231;
            world.RefreshMapZone();
            Assert.Equal(new ushort[] { 230, MogHouseRooms.ZoneKey(0x121), 231 }, raised);

            // A room number this client cannot draw keeps the city.
            world.UpdateZoneLoginInfo(Login(true, 0x267));
            world.RefreshMapZone();
            Assert.Equal(231, world.MapZoneId);
        }

        [Fact]
        public void Room_IsWalkable_AndItsDoorIsATargetableClosedDoor()
        {
            if (!Directory.Exists(GameDirectory)) return;
            ushort key = MogHouseRooms.ZoneKey(0x121);
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            Assert.True(rm.TryLoadZone(key, out var room, out _));
            Assert.True(room!.MeshGroups.Count > 0);
            Assert.NotNull(room.Collision);
            // No walk-in exit: the door is talked to (round 2).
            Assert.Empty(room.Collision!.ZoneLineVolumes);

            // The room's collision brings the client's own door entity: closed, so it blocks, and targetable.
            var world = new WorldState();
            world.UpdateZoneLoginInfo(Login(true, 0x121));
            world.Collision = room.Collision;
            Assert.True(world.TryGetByServerId(MogHouseRooms.ExitDoorServerId, out var door));
            Assert.Equal(EntityType.Door, door!.Type);
            Assert.Equal("_720", door.TransportId);
            Assert.Single(ZoneDoors.EvaluateClosed(world.Collision, world));

            var headless = new ResourceManager(GameDirectory);
            headless.InitializeFileTable();
            var collision = headless.TryLoadZoneCollision(key);
            Assert.Equal(room.Collision.TriangleCount, collision!.TriangleCount);
            Assert.Empty(collision.ZoneLineVolumes);

            // The city keeps its own lines, the entrance among them.
            Assert.Contains(headless.TryLoadZoneCollision(230)!.ZoneLineVolumes, l => l.Id == "zmr0");
        }
    }
}
