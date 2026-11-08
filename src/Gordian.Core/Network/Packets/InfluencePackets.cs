// src/Gordian.Core/Network/Packets/InfluencePackets.cs
using System;
using System.Buffers.Binary;

namespace Gordian.Core.Network.Packets
{
    /// <summary>A Campaign nation or beastman force (S2C 0x071 mode 2): its reconnaissance level (0-15), morale and prosperity (0-100).</summary>
    public readonly record struct CampaignNation(byte Reconnaissance, byte Morale, byte Prosperity)
    {
        /// <summary>Unpacks the 32-bit word: bits 0-3 reconnaissance, 18-24 morale, 25-31 prosperity.</summary>
        public static CampaignNation FromWord(uint word) =>
            new((byte)(word & 0xF), (byte)((word >> 18) & 0x7F), (byte)((word >> 25) & 0x7F));
    }

    /// <summary>
    /// A Campaign zone (S2C 0x071 mode 2): its owner (0 none, 1-3 the nations, 4 the beastmen), fortifications and resources
    /// with their maxima, heroism, and the four sides' influence (the client shows each times 0.4).
    /// </summary>
    public readonly record struct CampaignZone(byte Owner, ushort Fortifications, ushort Resources, byte Heroism,
        byte InfluenceSandoria, byte InfluenceBastok, byte InfluenceWindurst, byte InfluenceBeastmen,
        ushort MaxFortifications, ushort MaxResources);

    /// <summary>A Colonization zone (S2C 0x071 mode 3): its colonization rate (percent) and current / most bivouacs.</summary>
    public readonly record struct ColonizationZone(byte Rate, byte Bivouacs, byte MaxBivouacs)
    {
        /// <summary>Unpacks the 32-bit word: bits 0-6 the rate, 7-9 the bivouacs, 10-12 the most.</summary>
        public static ColonizationZone FromWord(uint word) =>
            new((byte)(word & 0x7F), (byte)((word >> 7) & 0x7), (byte)((word >> 10) & 0x7));
    }

    /// <summary>
    /// S2C 0x071 (GP_SERV_COMMAND_INFLUENCE): the Campaign map (mode 2) or the Colonization map (mode 3) of Region Info
    /// (#492). The payload (after the 4-byte header) starts with the mode byte.
    /// <para>
    /// Mode 2: the zone half at byte 5 (0 or 13: the client copies the 13 zone records from there), Allied Notes (int32 at 8),
    /// the zones each nation and the beastmen control (5 bits each at 12), seven nation words (San d'Oria, Bastok, Windurst,
    /// then the Orcs, Quadav, Yagudo and Dark Kindred) at 16, and 13 twelve-byte zone records at 44 (a word with the owner in
    /// bits 1-3, fortifications 4-13, resources 14-23, heroism 24-31; four influence bytes; a word with the maximum
    /// fortifications in bits 0-9 and resources in 10-19).
    /// </para>
    /// <para>
    /// Mode 3: the six coalition ranks (4 bits each, Pioneers, Peacekeepers, Couriers, Scouts, Inventors, Mummers) at 12, nine
    /// zone words at 16 (an unused one, then Yahse Hunting Grounds, Ceizak Battlegrounds, Foret de Hennetiel, Yorcia Weald,
    /// Morimar Basalt Fields, Marjami Ravine, Kamihr Drifts, Ra'Kaznar) and Bayld (int32 at 52).
    /// </para>
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets, world/server/0x0071) and LandSandBoat
    /// (https://github.com/LandSandBoat/server, <c>src/map/packets/s2c/0x071_influence_*.cpp</c>: LandSandBoat sends mode 3
    /// in answer to C2S 0x05A, with every rank 1 and every rate 0; its mode 2 request is commented out).
    /// </summary>
    public readonly ref struct S2C_0x071_Influence
    {
        public const ushort PacketId = 0x071;
        public const byte CampaignMode = 2, ColonizationMode = 3;

        /// <summary>The zone records one mode 2 packet carries, and the Campaign map's zones in all (two packets).</summary>
        public const int CampaignZonesPerPacket = 13, CampaignZoneCount = 26;

        /// <summary>The nation words of mode 2: the three nations, then the four beastman forces.</summary>
        public const int CampaignNationCount = 7;

        /// <summary>The coalitions and the zones of mode 3 (the first zone word unused).</summary>
        public const int CoalitionCount = 6, ColonizationZoneCount = 8;

        private const int CampaignSize = 200, ColonizationSize = 56;

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x071_Influence(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            Mode = payload.Length > 0 ? payload[0] : (byte)0;
            IsValid = Mode switch
            {
                CampaignMode => payload.Length >= CampaignSize,
                ColonizationMode => payload.Length >= ColonizationSize,
                _ => false,
            };
        }

        public byte Mode { get; }
        public bool IsValid { get; }

        /// <summary>Mode 2: the first of the 26 Campaign zones this packet's records are (0 or 13).</summary>
        public int ZoneOffset => _payload[5];

        public int AlliedNotes => BinaryPrimitives.ReadInt32LittleEndian(_payload.Slice(8, 4));

        /// <summary>Mode 2: how many zones side <paramref name="side"/> controls (0 San d'Oria, 1 Bastok, 2 Windurst, 3 beastmen).</summary>
        public int ControlledZones(int side) => (int)((BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(12, 4)) >> (side * 5)) & 0x1F);

        public CampaignNation Nation(int index) => CampaignNation.FromWord(BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(16 + index * 4, 4)));

        public CampaignZone Zone(int index)
        {
            var record = _payload.Slice(44 + index * 12, 12);
            uint word = BinaryPrimitives.ReadUInt32LittleEndian(record);
            uint max = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(8, 4));
            return new CampaignZone((byte)((word >> 1) & 0x7), (ushort)((word >> 4) & 0x3FF), (ushort)((word >> 14) & 0x3FF), (byte)(word >> 24),
                record[4], record[5], record[6], record[7], (ushort)(max & 0x3FF), (ushort)((max >> 10) & 0x3FF));
        }

        /// <summary>Mode 3: coalition <paramref name="index"/>'s rank (0 Pioneers ... 5 Mummers).</summary>
        public byte CoalitionRank(int index) => (byte)((BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(12, 4)) >> (index * 4)) & 0xF);

        /// <summary>Mode 3: Colonization zone <paramref name="index"/> (0 Yahse Hunting Grounds ... 7 Ra'Kaznar).</summary>
        public ColonizationZone ColonizationZoneAt(int index) =>
            ColonizationZone.FromWord(BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(20 + index * 4, 4)));

        public int Bayld => BinaryPrimitives.ReadInt32LittleEndian(_payload.Slice(52, 4));
    }
}
