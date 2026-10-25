// src/Gordian.Core/World/MogHouseRooms.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Graphics;

namespace Gordian.Core.World
{
    /// <summary>
    /// The Mog House room a character is in (#354). In the Mog House the server keeps the city as the zone (S2C 0x00A
    /// <c>ZoneNo</c>) and says so with <c>LoginState</c> MyRoom and <c>MyroomMapNumber</c>, the room's model number
    /// (LandSandBoat s2c/0x00a_login.cpp GetMogHouseModelID: 0x100 Jeuno, 0x101 / 0x121 San d'Oria, 0x102 / 0x122 Bastok,
    /// 0x120 / 0x123 Windurst (the second of each pair for the character's own nation), 0x124 Adoulin, 214 Aht Urhgan,
    /// 199 / 219 the Bastok and Windurst fronts). The room is the zone DAT 100 + that number, as zones below 256 are:
    /// files 356-358 hold <c>m_ju</c>, <c>m_sa</c>, <c>m_ba</c>, 388-391 <c>m_wi</c>, <c>m_sa</c>, <c>m_ba</c>,
    /// <c>m_wi</c>, 392 <c>m_ad</c>, 299 / 314 / 319 <c>m_ba</c> / <c>m_gu</c> / <c>m_wi</c> (zone containers with one
    /// ZoneDef and a door record <c>_720</c>, read from the retail install 2026-10-20). **Beyond XiPackets / LandSandBoat /
    /// xi-tools**, which do not say which file a model number names. Numbers outside that range (the second floors
    /// 0x267-0x26A, the Ronfaure front 745) are not mapped yet.
    /// <para>
    /// The room is drawn and walked under a map zone key of its own (<see cref="ZoneKey"/>, <see cref="WorldState.MapZoneId"/>),
    /// so the zone cache and the collision keep it apart from the city.
    /// </para>
    /// </summary>
    public static class MogHouseRooms
    {
        private const int KeyFlag = 0x8000;

        /// <summary>Whether a <c>MyroomMapNumber</c> names a room this client can draw.</summary>
        public static bool IsKnownRoom(int myroomMapNumber) =>
            myroomMapNumber is 199 or 214 or 219 or (>= 0x100 and <= 0x102) or (>= 0x120 and <= 0x124);

        /// <summary>The room's DAT file id (100 + the number); false for a number not mapped.</summary>
        public static bool TryGetRoomFileId(int myroomMapNumber, out int fileId)
        {
            fileId = IsKnownRoom(myroomMapNumber) ? 100 + myroomMapNumber : 0;
            return fileId != 0;
        }

        /// <summary>The map zone key a room is loaded under (never a real zone id: those are below 0x8000).</summary>
        public static ushort ZoneKey(int myroomMapNumber) => (ushort)(KeyFlag | myroomMapNumber);

        /// <summary>
        /// The exit prompt the client shows when the player targets the room's door and presses Confirm: "Moogle: Heading
        /// outside, kupo?", Yes / No with No under the cursor (<c>Retail Captures/Mog_House.mp4</c>, 2026-10-17, Aht Urhgan
        /// Whitegate, at 2:32; the string is in the client's XISTRING table, file 52, beside "Exit your Mog House?"; round 2
        /// in game: retail asks on Confirm at the targeted door, not on walking up). Yes sends C2S 0x05E with the exit id
        /// <c>zmrq</c>.
        /// </summary>
        public const string ExitQuestion = "Moogle: Heading outside, kupo?";

        /// <summary>
        /// The zone-line volumes of a zone or a room: none in a room (its door is talked to, <see cref="ExitDoorServerId"/>),
        /// else the zone's lines.
        /// </summary>
        public static IReadOnlyList<ZoneInteraction> ZoneLineVolumes(int zoneId, IEnumerable<ZoneInteraction> interactions) =>
            TryGetRoomOfKey(zoneId, out _) ? Array.Empty<ZoneInteraction>() : Collision.ZoneLines.Select(interactions);

        /// <summary>
        /// The server id of the room's door entity, which the client makes itself: the server sends no door for a room
        /// (LandSandBoat spawns only the moogle and the orchestrion's curator there), yet retail lets the player target the
        /// door, Confirm on it asks <see cref="ExitQuestion"/>, and it blocks while shut (round 2 in game). Above every
        /// server id LandSandBoat gives out (NPCs 0x01xxxxxx, dynamic entities 0x0700-0x08FF indices).
        /// </summary>
        public const uint ExitDoorServerId = 0x7FFF0720;

        /// <summary>The door entity's target index (outside the server's 0-0x8FF range).</summary>
        public const ushort ExitDoorTargetIndex = 0x7FF0;

        /// <summary>Whether an entity is the room's exit door (<see cref="ExitDoorServerId"/>).</summary>
        public static bool IsExitDoor(WorldEntity? entity) => entity != null && entity.ServerId == ExitDoorServerId;

        /// <summary>
        /// The room's door entity for its first door record (<c>_720</c>): a closed door (status 9) at the record's centre,
        /// named "Door", so it is targetable, blocks (<see cref="ZoneDoors.EvaluateClosed"/>) and holds its leaves shut. Null
        /// when the room has no door record.
        /// </summary>
        public static WorldEntity? CreateExitDoor(Collision.ZoneCollisionMesh? room)
        {
            if (room == null || room.Doors.Count == 0) return null;
            var door = room.Doors[0];
            return new WorldEntity(ExitDoorServerId, ExitDoorTargetIndex, EntityType.Door)
            {
                Name = "Door",
                Position = door.Center,
                TransportId = door.Id,
                AnimationState = Collision.ZoneDoors.StatusClosed,
                IsSpawned = true,
            };
        }

        /// <summary>
        /// The city areas the Mog House exit can lead to when the city's exit is unlocked, in exit-mode order: the area at
        /// index i is <see cref="MogHouseExitMode"/> Option(i + 1) of C2S 0x05E <c>zmrq</c> with the given exit bit. The
        /// server says which city's exits the character has with S2C 0x00A <c>MyRoomExitBit</c> (+170; 0 when none, so
        /// the exit returns to the area entered from). Areas and their order referenced from LandSandBoat
        /// (https://github.com/LandSandBoat/server, packets/c2s/0x05e_maprect.cpp moghouseCityExit, and
        /// s2c/0x00a_login.cpp GetMogHouseLeavingFlag: San d'Oria, Bastok, Windurst and Jeuno need the city's Mog House
        /// exit quest, Whitegate its own, Adoulin is always open). The fronts (bits 6-8) have no areas there. Empty for
        /// <see cref="MogHouseExitBit.Default"/> or a bit with no areas.
        /// </summary>
        public static IReadOnlyList<ushort> ExitAreas(MogHouseExitBit exitBit) => exitBit switch
        {
            MogHouseExitBit.SandOria => new ushort[] { 230, 231, 232 },     // Southern, Northern, Port San d'Oria
            MogHouseExitBit.Bastok => new ushort[] { 234, 235, 236 },       // Bastok Mines, Bastok Markets, Port Bastok
            MogHouseExitBit.Windurst => new ushort[] { 238, 239, 240, 241 }, // Windurst Waters, Walls, Port Windurst, Woods
            MogHouseExitBit.Jeuno => new ushort[] { 243, 244, 245, 246 },   // Ru'Lude Gardens, Upper, Lower, Port Jeuno
            MogHouseExitBit.Whitegate => new ushort[] { 48, 50 },           // Al Zahbi, Aht Urhgan Whitegate
            MogHouseExitBit.Adoulin => new ushort[] { 256, 257 },           // Western, Eastern Adoulin
            _ => Array.Empty<ushort>(),
        };

        /// <summary>The room number of a map zone key; false for a real zone id.</summary>
        public static bool TryGetRoomOfKey(int zoneKey, out int myroomMapNumber)
        {
            myroomMapNumber = (zoneKey & KeyFlag) != 0 ? zoneKey & ~KeyFlag : 0;
            return myroomMapNumber != 0 && IsKnownRoom(myroomMapNumber);
        }
    }
}
