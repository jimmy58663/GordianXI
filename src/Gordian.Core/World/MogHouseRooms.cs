// src/Gordian.Core/World/MogHouseRooms.cs
using System;
using System.Collections.Generic;
using System.Numerics;
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
        /// The exit prompt the client shows when the player walks into the room's door: "Moogle: Heading outside, kupo?",
        /// Yes / No with No under the cursor (<c>Retail Captures/Mog_House.mp4</c>, 2026-10-17, Aht Urhgan Whitegate, at
        /// 2:32; the string is in the client's XISTRING table, file 52, beside "Exit your Mog House?"). Yes sends C2S 0x05E
        /// with the exit id <c>zmrq</c>.
        /// </summary>
        public const string ExitQuestion = "Moogle: Heading outside, kupo?";

        /// <summary>The depth (yalms) the door's exit volume is given: the door record is a 0.2-yalm sheet in the doorway.</summary>
        public const float ExitVolumeDepth = 1.2f;

        /// <summary>
        /// The room's way out (#354): the room DATs have no zone line, only their door's Section 0x36 record (<c>_720</c> in
        /// every room, a 2.0-2.3 x 2.4-3.0 x 0.2 sheet 7.7-8.4 yalms from the room's centre). Walking into it is the exit, so
        /// each door record becomes a zone-line volume with the exit id <c>zmrq</c>, thickened to
        /// <see cref="ExitVolumeDepth"/>; nothing else of the room is a zone line.
        /// </summary>
        public static IReadOnlyList<ZoneInteraction> ExitVolumes(IEnumerable<ZoneInteraction> interactions)
        {
            var exits = new List<ZoneInteraction>();
            foreach (var record in interactions)
            {
                if (!record.IsDoor || record.Size.X <= 0f || record.Size.Y <= 0f) continue;
                exits.Add(record with { Id = Collision.ZoneLines.MogHouseExitId, Size = new Vector3(record.Size.X, record.Size.Y, MathF.Max(record.Size.Z, ExitVolumeDepth)) });
            }
            return exits;
        }

        /// <summary>
        /// The zone-line volumes of a zone or a room: a room's door exits (<see cref="ExitVolumes"/>), else the zone's lines.
        /// </summary>
        public static IReadOnlyList<ZoneInteraction> ZoneLineVolumes(int zoneId, IEnumerable<ZoneInteraction> interactions) =>
            TryGetRoomOfKey(zoneId, out _) ? ExitVolumes(interactions) : Collision.ZoneLines.Select(interactions);

        /// <summary>The room number of a map zone key; false for a real zone id.</summary>
        public static bool TryGetRoomOfKey(int zoneKey, out int myroomMapNumber)
        {
            myroomMapNumber = (zoneKey & KeyFlag) != 0 ? zoneKey & ~KeyFlag : 0;
            return myroomMapNumber != 0 && IsKnownRoom(myroomMapNumber);
        }
    }
}
