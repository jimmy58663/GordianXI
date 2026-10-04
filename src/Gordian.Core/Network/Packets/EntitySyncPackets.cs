// src/Gordian.Core/Network/Packets/EntitySyncPackets.cs
using System;
using System.Buffers.Binary;
using System.Text;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// The mode of S2C 0x067 / 0x068 (the low 6 bits of the first payload word): which kind of entity the data updates.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x0067</c>.
    /// </summary>
    public enum EntitySyncMode : byte
    {
        /// <summary>Not a mode this client handles.</summary>
        None = 0,
        /// <summary>Updates a player entity: name flags (Campaign, Pankration, Level Sync) and, for the local player, mount, Mog House and level data.</summary>
        Player = 2,
        /// <summary>Updates an NPC entity: name flags, its owner (a Trust's summoner) and a name override.</summary>
        Npc = 3,
        /// <summary>Updates the local player's own pet: pet index, HP%, MP%, TP, target and name.</summary>
        Pet = 4
    }

    /// <summary>
    /// The shared decoder behind S2C 0x067 and 0x068. XiPackets documents 0x068 as a duplicate of 0x067 that the server
    /// uses for the local player's pet (mode 4); LandSandBoat sends char sync (mode 2, <c>char_sync.cpp</c>) and entity
    /// rename (mode 3, <c>entity_set_name.cpp</c>) on 0x067, and pet sync (mode 4, <c>pet_sync.cpp</c>) on 0x068.
    /// Payload offsets are the packet offsets minus the 4-byte header.
    /// <list type="bullet">
    /// <item>Common: 0 u16 <c>Mode:6 | Length:10</c>, 2 u16 <c>ActIndex</c>, 4 u32 <c>UniqueNo</c>.</item>
    /// <item>Mode 2: 8 u16 fellow index, 12 u32 <c>NameFlags</c>, 16 u32 <c>NameIcon</c>, 20 u32 <c>CustomProperties</c>
    /// (chocobo look), 24 u32 second custom word, 28 u32 <c>UniqueNoMog</c>, 32 u8 <c>MogHouseFlag</c>, 33 u8 main job level,
    /// 34 u8 level restriction (LSB), 35 u8 <c>MogExpansionFlag</c>.</item>
    /// <item>Mode 3: 8 u16 owner index, 12 u32 <c>NameFlags</c>, name at 16 (XiPackets) or 20 (LandSandBoat).</item>
    /// <item>Mode 4: 8 u16 index (see <see cref="SecondIndex"/>), 10 u8 HP%, 11 u8 MP%, 12 u32 TP, 16 u32 target id, name at 20.</item>
    /// </list>
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x0067</c> and
    /// <c>0x0068</c>; the values LandSandBoat sends from its <c>char_sync.cpp</c>, <c>entity_set_name.cpp</c> and
    /// <c>pet_sync.cpp</c> (https://github.com/LandSandBoat/server). Where the two disagree this follows what LandSandBoat
    /// sends and says so on the property.
    /// </summary>
    public readonly ref struct EntitySyncPacket
    {
        /// <summary>Payload bytes a mode 2 packet needs: the last field is the byte at 35.</summary>
        public const int PlayerMinPayload = 36;
        /// <summary>Payload bytes a mode 3 packet needs: the fixed part up to <see cref="NpcNameXiPackets"/>.</summary>
        public const int NpcMinPayload = 16;
        /// <summary>Payload bytes a mode 4 packet needs: the fixed part up to the name.</summary>
        public const int PetMinPayload = 20;

        private const int NpcNameXiPackets = 16;
        private const int NpcNameLandSandBoat = 20;
        private const int PetNameOffset = 20;
        private const int NameMaxLength = 16;

        public bool IsValid { get; }
        public EntitySyncMode Mode { get; }

        /// <summary>The <c>Length</c> field (10 bits): the content length the server declares, including the 4-byte mode word.</summary>
        public int Length { get; }

        /// <summary>
        /// Mode 2 and 3: the index of the entity being updated. Mode 4: LandSandBoat puts the OWNER's index here, XiPackets
        /// labels it the pet's own index (see <see cref="SecondIndex"/>).
        /// </summary>
        public ushort ActIndex { get; }

        /// <summary>The server id matching <see cref="ActIndex"/>.</summary>
        public uint UniqueNo { get; }

        /// <summary>
        /// Modes 2, 3 and 4 offset 8: mode 2 the fellow NPC's index, mode 3 the owner's index (a Trust's summoner), mode 4
        /// the other index of the owner/pet pair (LandSandBoat: the pet; XiPackets: <c>ActIndexOwner</c>).
        /// </summary>
        public ushort SecondIndex { get; }

        // Modes 2 and 3.
        public uint NameFlags { get; }

        // Mode 2.
        public uint NameIcon { get; }
        public uint CustomProperties { get; }
        public uint CustomProperties2 { get; }
        public uint UniqueNoMog { get; }
        public byte MogHouseFlag { get; }
        public byte MainJobLevel { get; }
        /// <summary>The level cap of Level Sync (LandSandBoat writes <c>m_LevelRestriction</c> at packet 0x26, XiPackets <c>unknown26</c>); 0 when not synced.</summary>
        public byte LevelRestriction { get; }
        public byte MogExpansionFlag { get; }

        /// <summary>
        /// Mode 2: the u16 at packet 0x13 that LandSandBoat fills with the mounted effect's sub-power (the mount id); it
        /// overlaps the top byte of <see cref="NameFlags"/> and the low byte of <see cref="NameIcon"/>, which XiPackets
        /// does not mark as a mount field. 0 when not mounted.
        /// </summary>
        public ushort MountWord { get; }

        // Mode 4.
        public byte Hpp { get; }
        public byte Mpp { get; }
        public uint Tp { get; }
        /// <summary>Mode 4: the server id of the entity the pet is targeting; 0 when none.</summary>
        public uint TargetId { get; }

        /// <summary>
        /// Modes 3 and 4: the name carried by the packet, empty when none. Mode 3 reads XiPackets' offset (payload 16) and
        /// falls back to LandSandBoat's (payload 20), which writes the name four bytes later than XiPackets documents.
        /// </summary>
        public string Name { get; }

        /// <summary>Mode 2 level sync icon: <c>NameFlags</c> bit 2 (LandSandBoat <c>0x04 - Level Sync</c>).</summary>
        public bool IsLevelSynced => (NameFlags & 0x04) != 0;

        /// <summary>Mode 2 and 3 Campaign battle name flag: bit 1 (XiPackets <c>CampaignNameFlag</c>).</summary>
        public bool IsCampaign => (NameFlags & 0x02) != 0;

        public EntitySyncPacket(ReadOnlySpan<byte> payload)
        {
            this = default;
            Name = string.Empty;
            if (payload.Length < 4) return;

            ushort modeWord = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0, 2));
            int mode = modeWord & 0x3F;
            Length = modeWord >> 6;
            ActIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2, 2));

            switch (mode)
            {
                case (int)EntitySyncMode.Player:
                    if (payload.Length < PlayerMinPayload) return;
                    UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
                    SecondIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2));
                    NameFlags = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(12, 4));
                    NameIcon = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(16, 4));
                    CustomProperties = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(20, 4));
                    CustomProperties2 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(24, 4));
                    UniqueNoMog = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(28, 4));
                    MogHouseFlag = payload[32];
                    MainJobLevel = payload[33];
                    LevelRestriction = payload[34];
                    MogExpansionFlag = payload[35];
                    MountWord = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(15, 2));
                    break;

                case (int)EntitySyncMode.Npc:
                    if (payload.Length < NpcMinPayload) return;
                    UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
                    SecondIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2));
                    NameFlags = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(12, 4));
                    Name = ReadName(payload, NpcNameXiPackets);
                    if (Name.Length == 0) Name = ReadName(payload, NpcNameLandSandBoat);
                    break;

                case (int)EntitySyncMode.Pet:
                    if (payload.Length < PetMinPayload) return;
                    UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
                    SecondIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2));
                    Hpp = payload[10];
                    Mpp = payload[11];
                    Tp = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(12, 4));
                    TargetId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(16, 4));
                    Name = ReadName(payload, PetNameOffset);
                    break;

                default:
                    return;
            }

            Mode = (EntitySyncMode)mode;
            IsValid = true;
        }

        /// <summary>
        /// Reads the NUL terminated name at <paramref name="offset"/> (at most 16 bytes, or to the end of the payload). The
        /// client treats a first byte at or below the space character as "no name".
        /// </summary>
        private static string ReadName(ReadOnlySpan<byte> payload, int offset)
        {
            if (offset >= payload.Length) return string.Empty;
            ReadOnlySpan<byte> field = payload.Slice(offset, Math.Min(NameMaxLength, payload.Length - offset));
            if (field[0] <= 0x20) return string.Empty;
            int end = field.IndexOf((byte)0);
            if (end < 0) end = field.Length;
            return Encoding.UTF8.GetString(field.Slice(0, end));
        }
    }

    /// <summary>
    /// S2C 0x067 (unnamed in XiPackets; LandSandBoat <c>CCharSyncPacket</c> and <c>CEntitySetNamePacket</c>): entity
    /// update. The data is decoded by <see cref="EntitySyncPacket"/>.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x0067</c>.
    /// </summary>
    public readonly ref struct S2C_0x067_EntityUpdate1
    {
        public const ushort PacketId = 0x067;

        public EntitySyncPacket Sync { get; }
        public bool IsValid => Sync.IsValid;

        public S2C_0x067_EntityUpdate1(ReadOnlySpan<byte> payload) => Sync = new EntitySyncPacket(payload);
    }

    /// <summary>
    /// S2C 0x068 (unnamed in XiPackets; LandSandBoat <c>CPetSyncPacket</c>): a duplicate of 0x067 the server uses for
    /// the local player's pet. The data is decoded by <see cref="EntitySyncPacket"/>.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x0068</c>.
    /// </summary>
    public readonly ref struct S2C_0x068_EntityUpdate2
    {
        public const ushort PacketId = 0x068;

        public EntitySyncPacket Sync { get; }
        public bool IsValid => Sync.IsValid;

        public S2C_0x068_EntityUpdate2(ReadOnlySpan<byte> payload) => Sync = new EntitySyncPacket(payload);
    }
}
