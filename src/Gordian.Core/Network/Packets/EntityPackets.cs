// src/Gordian.Core/Network/Packets/EntityPackets.cs
// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server)
// and Atom0s XiPackets research (https://github.com/atom0s/XiPackets).

using System;
using System.Buffers.Binary;
using System.Text;
using Gordian.Core.World;

namespace Gordian.Core.Network.Packets
{
    #region Enums

    /// <summary>
    /// Entity update flags transmitted in S2C 0x00D and 0x00E.
    /// Matches LandSandBoat sendflags_t (PS2 bitfield: Position, ClaimStatus, General, Name, Model, Despawn).
    /// </summary>
    [Flags]
    public enum EntityUpdateFlags : byte
    {
        None = 0x00,
        Position = 0x01,
        ClaimStatus = 0x02,
        General = 0x04,
        Name = 0x08,
        Model = 0x10,
        Despawn = 0x20,
        Name2 = 0x40
    }

    /// <summary>
    /// Entity sub-kind / model type for NPC and environment objects in S2C 0x00E.
    /// </summary>
    public enum EntitySubKind : byte
    {
        Standard = 0,
        Equipped = 1,
        Door = 2,
        Elevator = 3,
        Ship = 4,
        Unknown5 = 5,
        Automaton = 6,
        Chocobo = 7
    }

    /// <summary>
    /// Character job identifiers used across FFXI packets (S2C 0x061).
    /// </summary>
    public enum JobId : byte
    {
        None = 0,
        Warrior = 1,
        Monk = 2,
        WhiteMage = 3,
        BlackMage = 4,
        RedMage = 5,
        Thief = 6,
        Paladin = 7,
        DarkKnight = 8,
        Beastmaster = 9,
        Bard = 10,
        Ranger = 11,
        Samurai = 12,
        Ninja = 13,
        Dragoon = 14,
        Summoner = 15,
        BlueMage = 16,
        Corsair = 17,
        Puppetmaster = 18,
        Dancer = 19,
        Scholar = 20,
        Geomancer = 21,
        RuneFencer = 22
    }

    #endregion

    #region Inbound Decoders (readonly ref struct)

    /// <summary>
    /// S2C 0x00D (GP_SERV_COMMAND_CHAR_PC): Character PC Update / Spawn / Despawn.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/char_update.cpp).
    /// </summary>
    public readonly ref struct S2C_0x00D_CharPc
    {
        public const ushort PacketId = 0x00D;

        public uint UniqueNo { get; }
        public ushort ActorIndex { get; }
        public EntityUpdateFlags UpdateFlags { get; }
        public byte Direction { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public uint Flags0 { get; }
        public byte Speed { get; }
        public byte SpeedBase { get; }
        public byte Hpp { get; }
        public byte ServerStatus { get; }
        public uint Flags1 { get; }
        public uint Flags2 { get; }
        public uint Flags3 { get; }
        public uint BtTargetId { get; }
        public ushort CostumeId { get; }
        public ushort PetActorIndex { get; }
        public bool IsValid { get; }

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x00D_CharPc(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 0x2C) // Minimum through BtTargetID
            {
                UniqueNo = 0;
                ActorIndex = 0;
                UpdateFlags = EntityUpdateFlags.None;
                Direction = 0;
                X = 0;
                Y = 0;
                Z = 0;
                Flags0 = 0;
                Speed = 0;
                SpeedBase = 0;
                Hpp = 0;
                ServerStatus = 0;
                Flags1 = 0;
                Flags2 = 0;
                Flags3 = 0;
                BtTargetId = 0;
                CostumeId = 0;
                PetActorIndex = 0;
                IsValid = false;
                return;
            }

            UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            ActorIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            UpdateFlags = (EntityUpdateFlags)payload[6];
            Direction = payload[7];
            X = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(8, 4));
            // FFXI native wire convention: X at +8 (East/West), Elevation at +12, North/South at +16.
            // GordianXI 3D canonical coordinates (Y-up):
            // X = East(+)/West(-), Y = Elevation (Up(+)/Down(-)), Z = North(+)/South(-).
            Y = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(12, 4)); // Wire offset 12: Elevation -> 3D Y
            Z = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(16, 4)); // Wire offset 16: North/South -> 3D Z
            Flags0 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(20, 4));
            Speed = payload[24];
            SpeedBase = payload[25];
            Hpp = payload[26];
            ServerStatus = payload[27];
            Flags1 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(28, 4));
            Flags2 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(32, 4));
            Flags3 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(36, 4));
            BtTargetId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(40, 4));

            CostumeId = payload.Length >= 46
                ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(44, 2))
                : (ushort)0;

            PetActorIndex = payload.Length >= 60
                ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(56, 2))
                : (ushort)0;

            IsValid = true;
        }

        public bool IsDespawn => (UpdateFlags & EntityUpdateFlags.Despawn) != 0;
        public bool HasPosition => (UpdateFlags & EntityUpdateFlags.Position) != 0;
        public bool HasModel => (UpdateFlags & EntityUpdateFlags.Model) != 0;
        public bool HasName => (UpdateFlags & EntityUpdateFlags.Name) != 0;

        /// <summary>
        /// Movement frame timer / timestamp (bits 0..12 of Flags0).
        /// Values up to <see cref="StationaryMovTimeMax"/> indicate stationary; larger values indicate active movement
        /// (an accumulating ~60 FPS run count that resets when the character stops).
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server) flags0_t.
        /// </summary>
        public ushort MovTime => (ushort)(Flags0 & 0x1FFF);

        /// <summary>
        /// GroundFlag (Flags0 bit 15): the entity ignores world collision (walks through walls, is not placed on the
        /// ground). Flag layout referenced from XiPackets (https://github.com/atom0s/XiPackets) world/server/0x000E flags0_t.
        /// </summary>
        public bool IgnoresWorldCollision => ((Flags0 >> 15) & 0x01) != 0;
        public bool IsMoving => IsMovingMovTime(MovTime);

        /// <summary>
        /// Largest <see cref="MovTime"/> a stationary character reports. Captured traffic shows both 1 and 2 at rest
        /// (the final update after a run carries 2), while running counts climb from single digits upward.
        /// </summary>
        public const ushort StationaryMovTimeMax = 2;

        /// <summary>
        /// Whether a 0x00D movement counter indicates the character is actively moving.
        /// </summary>
        public static bool IsMovingMovTime(ushort movTime) => movTime > StationaryMovTimeMax;

        // Flags1 properties
        public byte ChocoboIndex => (byte)((Flags1 >> 5) & 0x07);
        public byte GraphSize => (byte)((Flags1 >> 9) & 0x03);
        public bool IsSeekingParty => ((Flags1 >> 11) & 0x01) != 0;
        public bool IsAnonymous => ((Flags1 >> 12) & 0x01) != 0;
        public bool IsAway => ((Flags1 >> 14) & 0x01) != 0;
        public byte Gender => (byte)((Flags1 >> 15) & 0x01);
        public bool HasLinkshell => ((Flags1 >> 17) & 0x01) != 0;
        public bool IsLinkDead => ((Flags1 >> 18) & 0x01) != 0;
        public byte GmLevel => (byte)((Flags1 >> 24) & 0x07);
        public bool IsInvisible => ((Flags1 >> 29) & 0x01) != 0;
        public bool HasBazaar => ((Flags1 >> 31) & 0x01) != 0;

        // Flags2 properties
        public byte LsColorR => (byte)(Flags2 & 0xFF);
        public byte LsColorG => (byte)((Flags2 >> 8) & 0xFF);
        public byte LsColorB => (byte)((Flags2 >> 16) & 0xFF);
        public bool IsCharmed => ((Flags2 >> 27) & 0x01) != 0;

        // Flags3 properties
        public bool IsTrust => (Flags3 & 0x01) != 0;
        public bool IsPet => ((Flags3 >> 6) & 0x01) != 0;
        public bool IsSneak => ((Flags3 >> 21) & 0x01) != 0;
        public bool IsNewPlayer => ((Flags3 >> 23) & 0x01) != 0;
        public bool IsMentor => ((Flags3 >> 24) & 0x01) != 0;

        /// <summary>
        /// Non-blocking (Flags3 bit 28): the local player passes through without the client's actor contact check.
        /// Flag layout referenced from XiPackets (https://github.com/atom0s/XiPackets) world/server/0x000E flags3_t unknown_3_4.
        /// </summary>
        public bool IsNonBlocking => ((Flags3 >> 28) & 0x01) != 0;

        /// <summary>
        /// Fully hidden and untargetable (Flags1 bit 1, HideFlag).
        /// </summary>
        public bool IsHidden => ((Flags1 >> 1) & 0x01) != 0;

        /// <summary>
        /// <c>Flags4</c> (payload 0x2F): bit 1 TrialFlag, bit 6 JobMasterFlag. 0 when the packet is too short.
        /// Layout referenced from XiPackets (https://github.com/atom0s/XiPackets) world/server/0x000D flags4_t.
        /// </summary>
        public byte Flags4 => _payload.Length > 0x2F ? _payload[0x2F] : (byte)0;

        /// <summary>
        /// Flags1 YellFlag (bit 13): the player is called for help on; the retail client draws the name orange
        /// (<see cref="NamePlateFlags.CalledForHelp"/>). Layout referenced from XiPackets (https://github.com/atom0s/XiPackets) world/server/0x000D.
        /// </summary>
        public bool IsCalledForHelp => ((Flags1 >> 13) & 0x01) != 0;

        /// <summary>
        /// <c>BallistaInfo</c> (payload 0x2E): extended team/name flags for Ballista and PvP; the client special-cases 6 and 7.
        /// 0 when the packet is too short.
        /// </summary>
        public byte BallistaInfo => _payload.Length > 0x2E ? _payload[0x2E] : (byte)0;

        /// <summary>
        /// <c>CustomProperties[0]</c> (payload 0x30): the player's custom mount data (breed, colour and so on of a chocobo);
        /// the client reads only the first of the two words. 0 when the packet is too short.
        /// </summary>
        public uint CustomProperty => _payload.Length >= 0x34 ? BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(0x30, 4)) : 0u;

        /// <summary><c>MonstrosityFlags</c> (payload 0x3A); 0 outside Monstrosity.</summary>
        public ushort MonstrosityFlags => _payload.Length >= 0x3C ? BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(0x3A, 2)) : (ushort)0;

        /// <summary><c>MonstrosityNameId1</c> (payload 0x3C): the first part of a Monstrosity name.</summary>
        public byte MonstrosityNameId1 => _payload.Length > 0x3C ? _payload[0x3C] : (byte)0;

        /// <summary><c>MonstrosityNameId2</c> (payload 0x3D): the second part of a Monstrosity name.</summary>
        public byte MonstrosityNameId2 => _payload.Length > 0x3D ? _payload[0x3D] : (byte)0;

        /// <summary>
        /// <c>Flags5</c> (payload 0x3E): the geomancer's Indi- spell aura on the player: bits 0-3 the element, bits 4-5 the size,
        /// bit 6 the aura is up. 0 when the packet is too short. Layout referenced from XiPackets
        /// (https://github.com/atom0s/XiPackets) world/server/0x000D flags5_t.
        /// </summary>
        public byte Flags5 => _payload.Length > 0x3E ? _payload[0x3E] : (byte)0;

        /// <summary>The Indi- aura's element (Flags5 bits 0-3).</summary>
        public byte GeoIndiElement => (byte)(Flags5 & 0x0F);

        /// <summary>The Indi- aura's size (Flags5 bits 4-5).</summary>
        public byte GeoIndiSize => (byte)((Flags5 >> 4) & 0x03);

        /// <summary>Whether an Indi- aura is up (Flags5 bit 6).</summary>
        public bool HasGeoIndi => (Flags5 & 0x40) != 0;

        /// <summary>
        /// <c>ModelHitboxSize</c> (payload 0x3F): the model's hitbox size in tenths; <see cref="ModelHitboxRadius"/> is the float the
        /// client computes (<c>value * 0.1</c>). 0 when the packet is too short.
        /// </summary>
        public byte ModelHitboxSize => _payload.Length > 0x3F ? _payload[0x3F] : (byte)0;

        /// <summary>The model's hitbox size as the client reads it: <see cref="ModelHitboxSize"/> * 0.1.</summary>
        public float ModelHitboxRadius => ModelHitboxSize * 0.1f;

        /// <summary><c>Flags6</c> (payload 0x40): the gate id (bits 0-3) and the mount index (bits 4-11). 0 when the packet is too short.</summary>
        public uint Flags6 => _payload.Length >= 0x44 ? BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(0x40, 4)) : 0u;

        /// <summary>Flags6 GateId (bits 0-3).</summary>
        public byte GateId => (byte)(Flags6 & 0x0F);

        /// <summary>Flags6 MountIndex (bits 4-11): the player's mount; 0 when not mounted.</summary>
        public byte MountIndex => (byte)((Flags6 >> 4) & 0xFF);

        /// <summary>
        /// The name plate flags: flags1 LfgFlag (11), AnonymousFlag (12), YellFlag (13), AwayFlag (14), PlayOnelineFlag
        /// (16), LinkShellFlag (17), LinkDeadFlag (18), BazaarFlag (31); flags2 GmIconFlag (28), AutoPartyFlag (31);
        /// flags3 LfgMasterFlag (1), NewCharacterFlag (23), MentorFlag (24); flags4 TrialFlag (1), JobMasterFlag (6).
        /// Layout referenced from XiPackets (https://github.com/atom0s/XiPackets) world/server/0x000D.
        /// </summary>
        public NamePlateFlags NamePlate
        {
            get
            {
                var flags = NamePlateFlags.None;
                if ((Flags1 & (1u << 11)) != 0) flags |= NamePlateFlags.SeekingParty;
                if ((Flags1 & (1u << 12)) != 0) flags |= NamePlateFlags.Anonymous;
                if ((Flags1 & (1u << 13)) != 0) flags |= NamePlateFlags.CalledForHelp;
                if ((Flags1 & (1u << 14)) != 0) flags |= NamePlateFlags.Away;
                if ((Flags1 & (1u << 16)) != 0) flags |= NamePlateFlags.PlayOnline;
                if ((Flags1 & (1u << 17)) != 0) flags |= NamePlateFlags.Linkshell;
                if ((Flags1 & (1u << 18)) != 0) flags |= NamePlateFlags.LinkDead;
                if ((Flags1 & (1u << 31)) != 0) flags |= NamePlateFlags.Bazaar;
                if ((Flags2 & (1u << 28)) != 0) flags |= NamePlateFlags.GmIconHidden;
                if ((Flags2 & (1u << 31)) != 0) flags |= NamePlateFlags.AutoParty;
                if ((Flags3 & (1u << 1)) != 0) flags |= NamePlateFlags.SeekingMasterParty;
                if ((Flags3 & (1u << 23)) != 0) flags |= NamePlateFlags.NewPlayer;
                if ((Flags3 & (1u << 24)) != 0) flags |= NamePlateFlags.Mentor;
                byte flags4 = Flags4;
                if ((flags4 & 0x02) != 0) flags |= NamePlateFlags.Trial;
                if ((flags4 & 0x40) != 0) flags |= NamePlateFlags.JobMaster;
                return flags;
            }
        }

        /// <summary>
        /// Reads the 9-element equipment/model visual appearance table (GrapIDTbl) if model flag is set.
        /// Slot indices: 0:Race/Face, 1:Head, 2:Body, 3:Hands, 4:Legs, 5:Feet, 6:Main, 7:Sub, 8:Ranged.
        /// </summary>
        public bool TryGetGrapIdTable(Span<ushort> destination)
        {
            if (destination.Length < 9) return false;
            // GrapIDTbl is at offset 0x48 (72) from start of packet (0x44 relative to payload)
            int grapOffset = 0x44; // 0x48 - 4
            if (_payload.Length < grapOffset + 18) return false;

            for (int i = 0; i < 9; i++)
            {
                destination[i] = BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(grapOffset + (i * 2), 2));
            }
            return true;
        }

        /// <summary>
        /// Reads character name ASCII string if name flag is set.
        /// </summary>
        public string GetName()
        {
            if (!HasName) return string.Empty;
            int nameOffset = 0x56; // 0x5A - 4
            if (_payload.Length <= nameOffset) return string.Empty;

            ReadOnlySpan<byte> nameSpan = _payload.Slice(nameOffset);
            int len = 0;
            while (len < nameSpan.Length && len < 16 && nameSpan[len] != 0)
            {
                len++;
            }
            return len > 0 ? Encoding.ASCII.GetString(nameSpan.Slice(0, len)) : string.Empty;
        }
    }

    /// <summary>
    /// S2C 0x01B (GP_SERV_COMMAND_JOB_INFO): General Job &amp; Character Information.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x01b_job_info.cpp).
    /// </summary>
    public readonly ref struct S2C_0x01B_JobInfo
    {
        public const ushort PacketId = 0x01B;

        public ushort FaceNo { get; }
        public JobId MainJob { get; }
        public byte HairNo { get; }
        public byte Size { get; }
        public JobId SubJob { get; }
        public uint GetJobFlag { get; }
        public int HpMax { get; }
        public int MpMax { get; }
        public byte SubJobUnlockedFlag { get; }
        public byte MainJobLevel { get; }
        public byte SubJobLevel { get; }
        public bool IsValid { get; }

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x01B_JobInfo(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 92) // Minimum size for GP_MYROOM_DANCER struct
            {
                FaceNo = 0;
                MainJob = JobId.None;
                HairNo = 0;
                Size = 0;
                SubJob = JobId.None;
                GetJobFlag = 0;
                HpMax = 0;
                MpMax = 0;
                SubJobUnlockedFlag = 0;
                MainJobLevel = 0;
                SubJobLevel = 0;
                IsValid = false;
                return;
            }

            FaceNo = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2, 2));
            MainJob = (JobId)payload[4];
            HairNo = payload[5];
            Size = payload[6];
            SubJob = (JobId)payload[7];
            GetJobFlag = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(8, 4));

            HpMax = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(56, 4));
            MpMax = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(60, 4));
            SubJobUnlockedFlag = payload[64];

            byte mJobIdx = (byte)MainJob;
            if (mJobIdx > 0 && mJobIdx < 24 && payload.Length >= 68 + 24)
            {
                MainJobLevel = payload[68 + mJobIdx];
            }
            else if (mJobIdx > 0 && mJobIdx < 16)
            {
                MainJobLevel = payload[12 + mJobIdx];
            }
            else
            {
                MainJobLevel = 0;
            }

            byte sJobIdx = (byte)SubJob;
            if (sJobIdx > 0 && sJobIdx < 24 && payload.Length >= 68 + 24)
            {
                SubJobLevel = payload[68 + sJobIdx];
            }
            else if (sJobIdx > 0 && sJobIdx < 16)
            {
                SubJobLevel = payload[12 + sJobIdx];
            }
            else
            {
                SubJobLevel = 0;
            }

            IsValid = true;
        }

        public ushort GetBaseStat(int index)
        {
            if (index < 0 || index >= 7 || _payload.Length < 42) return 0;
            return BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(28 + (index * 2), 2));
        }

        public short GetStatModifier(int index)
        {
            if (index < 0 || index >= 7 || _payload.Length < 56) return 0;
            return BinaryPrimitives.ReadInt16LittleEndian(_payload.Slice(42 + (index * 2), 2));
        }

        /// <summary>
        /// <c>encumbrance</c> (payload 92): bit flags that lock equipment slots and stats while encumbered. 0 when the packet is
        /// shorter than the full 128 bytes. Layout referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x001B</c>.
        /// </summary>
        public uint Encumbrance => _payload.Length >= 96 ? BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(92, 4)) : 0u;

        /// <summary><c>can_thumbs_up_mentor</c> (payload 96): the player may thumbs-up a mentor in an assist channel (once per earth day).</summary>
        public bool CanThumbsUpMentor => _payload.Length > 96 && _payload[96] != 0;

        /// <summary><c>mentor_rank</c> (payload 97): 0 none, 1 bronze, 2 silver, 3 gold flag.</summary>
        public byte MentorRank => _payload.Length > 97 ? _payload[97] : (byte)0;

        /// <summary>
        /// <c>mastery_rank</c> (payload 98), shown with the mentor flag in assist channels; values like 11 and 21 mean a silver
        /// or gold flag with rank 1 (XiPackets 0x001B).
        /// </summary>
        public byte MasteryRank => _payload.Length > 98 ? _payload[98] : (byte)0;

        /// <summary><c>job_mastery_flags</c> (payload 100): bit n is set when job n has mastery unlocked (bit 0 is not a job).</summary>
        public uint JobMasteryFlags => _payload.Length >= 104 ? BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(100, 4)) : 0u;

        /// <summary>Whether <paramref name="job"/> has mastery unlocked (<see cref="JobMasteryFlags"/>).</summary>
        public bool HasJobMastery(JobId job) => (byte)job is > 0 and < 32 && (JobMasteryFlags & (1u << (byte)job)) != 0;

        /// <summary><c>job_mastery_levels</c> (payload 104, 24 bytes indexed by job; byte 0 unused): the job's mastery level.</summary>
        public byte GetJobMasteryLevel(JobId job)
        {
            int idx = (byte)job;
            return idx > 0 && idx < 24 && _payload.Length >= 104 + 24 ? _payload[104 + idx] : (byte)0;
        }

        public byte GetJobLevel(JobId job)
        {
            byte idx = (byte)job;
            if (idx > 0 && idx < 24 && _payload.Length >= 68 + 24)
            {
                return _payload[68 + idx];
            }
            if (idx > 0 && idx < 16 && _payload.Length >= 12 + 16)
            {
                return _payload[12 + idx];
            }
            return 0;
        }
    }

    /// <summary>
    /// S2C 0x00E (GP_SERV_COMMAND_CHAR_NPC): NPC / Monster Update / Spawn / Despawn.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/entity_update.cpp)
    /// and XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x000E).
    /// </summary>
    public readonly ref struct S2C_0x00E_CharNpc
    {
        public const ushort PacketId = 0x00E;

        public uint UniqueNo { get; }
        public ushort ActorIndex { get; }
        public EntityUpdateFlags UpdateFlags { get; }
        public byte Direction { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public uint Flags0 { get; }
        public byte Speed { get; }
        public byte SpeedBase { get; }
        public byte Hpp { get; }
        public byte ServerStatus { get; }
        public uint Flags1 { get; }
        public uint Flags2 { get; }
        public uint Flags3 { get; }
        public uint ClaimId { get; }
        public EntitySubKind SubKind { get; }
        public bool IsValid { get; }

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x00E_CharNpc(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 0x2C)
            {
                UniqueNo = 0;
                ActorIndex = 0;
                UpdateFlags = EntityUpdateFlags.None;
                Direction = 0;
                X = 0;
                Y = 0;
                Z = 0;
                Flags0 = 0;
                Speed = 0;
                SpeedBase = 0;
                Hpp = 0;
                ServerStatus = 0;
                Flags1 = 0;
                Flags2 = 0;
                Flags3 = 0;
                ClaimId = 0;
                SubKind = EntitySubKind.Standard;
                IsValid = false;
                return;
            }

            UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            ActorIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            UpdateFlags = (EntityUpdateFlags)payload[6];
            Direction = payload[7];
            X = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(8, 4));
            // FFXI native wire convention: X at +8 (East/West), Elevation at +12, North/South at +16.
            // GordianXI 3D canonical coordinates (Y-up):
            // X = East(+)/West(-), Y = Elevation (Up(+)/Down(-)), Z = North(+)/South(-).
            Y = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(12, 4)); // Wire offset 12: Elevation -> 3D Y
            Z = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(16, 4)); // Wire offset 16: North/South -> 3D Z
            Flags0 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(20, 4));
            Speed = payload[24];
            SpeedBase = payload[25];
            Hpp = payload[26];
            ServerStatus = payload[27];
            Flags1 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(28, 4));
            Flags2 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(32, 4));
            Flags3 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(36, 4));
            ClaimId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(40, 4));

            SubKind = payload.Length >= 46
                ? (EntitySubKind)(payload[44] & 0x07)
                : EntitySubKind.Standard;

            IsValid = true;
        }

        public bool IsDespawn => (UpdateFlags & EntityUpdateFlags.Despawn) != 0;
        public bool HasPosition => (UpdateFlags & EntityUpdateFlags.Position) != 0;
        public bool HasName => (UpdateFlags & EntityUpdateFlags.Name) != 0;
        public bool HasGeneral => (UpdateFlags & EntityUpdateFlags.General) != 0;

        /// <summary>
        /// <c>Flags1.MonsterFlag</c> (bit 0 of packet byte 0x20): the client shows the entity as a monster (yellow name,
        /// attackable) rather than an NPC, which share the 0-1023 index range. LandSandBoat writes the entity's status in
        /// that byte in every update: mob-allegiance entities spawn with status Update (1), NPCs with Normal (0), and a
        /// dying mob fades to Disappear (2), clearing it. (Packet byte 0x25, once read for this, is <c>Flags2.g</c>, the
        /// hitbox size.) Referenced from XiPackets (https://github.com/atom0s/XiPackets, world/server/0x000E, flags1_t)
        /// and LandSandBoat (https://github.com/LandSandBoat/server, src/map/packets/entity_update.cpp,
        /// src/map/entities/base_entity.cpp CBaseEntity::Spawn).
        /// </summary>
        public bool IsMonster => (Flags1 & 0x01) != 0;

        /// <summary>
        /// <c>Flags3.TrustFlag</c> (bit 0 of packet byte 0x28): the entity is a Trust. LandSandBoat sets it (with the
        /// Trust spawn bits, 0x45) in every update of a Trust. Referenced from XiPackets
        /// (https://github.com/atom0s/XiPackets, world/server/0x000E, flags3_t) and LandSandBoat
        /// (https://github.com/LandSandBoat/server, src/map/packets/entity_update.cpp).
        /// </summary>
        public bool IsTrust => (Flags3 & 0x01) != 0;

        /// <summary>
        /// Movement frame timer / timestamp (bits 0..12 of Flags0). Non-zero when moving, zero when stationary.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server) flags0_t.
        /// </summary>
        public ushort MovTime => (ushort)(Flags0 & 0x1FFF);

        /// <summary>
        /// GroundFlag (Flags0 bit 15): the entity ignores world collision (walks through walls, is not placed on the
        /// ground). Flag layout referenced from XiPackets (https://github.com/atom0s/XiPackets) world/server/0x000E flags0_t.
        /// </summary>
        public bool IgnoresWorldCollision => ((Flags0 >> 15) & 0x01) != 0;
        public bool IsMoving => MovTime != 0;

        /// <summary>
        /// Body size class (Flags1 bits 9-10): 0 = small, 1 = medium, 2 = large.
        /// Flag layout referenced from XiPackets (https://github.com/atom0s/XiPackets) world/server/0x000E flags1_t.
        /// </summary>
        public byte GraphSize => (byte)((Flags1 >> 9) & 0x03);

        /// <summary>
        /// Fully hidden and untargetable (Flags1 bit 1, HideFlag).
        /// Flag layout referenced from XiPackets (https://github.com/atom0s/XiPackets) world/server/0x000E flags1_t.
        /// </summary>
        public bool IsHidden => ((Flags1 >> 1) & 0x01) != 0;

        /// <summary>
        /// Invisible and untargetable (Flags1 bit 29, InvisFlag).
        /// Flag layout referenced from XiPackets (https://github.com/atom0s/XiPackets) world/server/0x000E flags1_t.
        /// </summary>
        public bool IsInvisible => ((Flags1 >> 29) & 0x01) != 0;

        /// <summary>
        /// Non-blocking (Flags3 bit 28): the local player passes through without the client's actor contact check.
        /// Flag layout referenced from XiPackets (https://github.com/atom0s/XiPackets) world/server/0x000E flags3_t unknown_3_4.
        /// </summary>
        public bool IsNonBlocking => ((Flags3 >> 28) & 0x01) != 0;

        /// <summary>
        /// The name plate flags an NPC or monster carries: flags1 YellFlag (13, orange name), flags3 MentorFlag (24, the
        /// A.M.A.N. Liaison's tutorial "i") and flags3 <c>unknown_3_5</c> (29, health bar and name not drawn).
        /// Layout referenced from XiPackets (https://github.com/atom0s/XiPackets) world/server/0x000E flags1_t / flags3_t.
        /// </summary>
        public NamePlateFlags NamePlate
        {
            get
            {
                var flags = NamePlateFlags.None;
                if ((Flags1 & (1u << 13)) != 0) flags |= NamePlateFlags.CalledForHelp;
                if ((Flags3 & (1u << 24)) != 0) flags |= NamePlateFlags.InfoNpc;
                if ((Flags3 & (1u << 29)) != 0) flags |= NamePlateFlags.NameHidden;
                return flags;
            }
        }

        /// <summary>
        /// Look size / model type: 0 = MODEL_STANDARD, 1 = MODEL_EQUIPPED, 2 = DOOR, 3 = ELEVATOR, etc. This is the
        /// <c>SubKind:3</c> field of the u16 at payload 0x2C; its upper 13 bits are the unused <c>Status</c>.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server) mmo.h and entity_update.h,
        /// and XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x000E) SubKind / Status.
        /// </summary>
        public ushort LookSize => (ushort)SubKind;

        /// <summary>
        /// Sub-animation state parameter: flags3_t <c>MonStat</c>, the low 3 bits of packet byte 0x2A (payload 0x26).
        /// For Uragnites: 4 = out of shell (open), 5 = in shell (closed).
        /// For Worms: 0 = surfaced, 1 = submerged.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server) entity_update.cpp and uragnite.lua,
        /// and XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x000E) flags3_t.
        /// </summary>
        public byte AnimationSub => _payload.Length >= 0x27 ? (byte)(_payload[0x26] & 0x07) : (byte)0;

        /// <summary>
        /// For an elevator or ship (look size 3 / 4): the FourCC of the zone object it moves (packet 0x34), the Earth
        /// second since the Vana'diel epoch its current leg started (0x38) and, for an elevator, the leg's travel time in
        /// seconds (0x3C, the u32 <c>EndTime</c>; LSB fills only its low byte). False when the packet carries no transport data.
        /// Layout referenced from LandSandBoat (https://github.com/LandSandBoat/server) packets/entity_update.cpp
        /// getTransportNPCName, and XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x000E) SubKind 3.
        /// </summary>
        public bool TryGetTransport(out string objectId, out uint legStartSeconds, out uint travelSeconds)
        {
            objectId = string.Empty;
            legStartSeconds = 0;
            travelSeconds = 0;
            if (SubKind is not (EntitySubKind.Elevator or EntitySubKind.Ship) || _payload.Length < 0x38) return false;
            if (!TryReadObjectId(out objectId)) return false;
            legStartSeconds = BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(0x34, 4));
            travelSeconds = SubKind == EntitySubKind.Elevator && _payload.Length >= 0x3C
                ? BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(0x38, 4))
                : 0;
            return true;
        }

        /// <summary>
        /// For a door (SubKind 2), the FourCC of the zone door it opens (<c>DoorId</c>, e.g. <c>_6l0</c>): the door's
        /// Section 0x36 record and the BlockID of its leaves. Packet structure referenced from XiPackets
        /// (https://github.com/atom0s/XiPackets, world/server/0x000E, SubKind 2).
        /// </summary>
        public bool TryGetDoorObjectId(out string objectId)
        {
            objectId = string.Empty;
            return SubKind == EntitySubKind.Door && _payload.Length >= 0x34 && TryReadObjectId(out objectId);
        }

        /// <summary>The printable FourCC at payload 0x30 (a door, elevator or ship's zone object id).</summary>
        private bool TryReadObjectId(out string objectId)
        {
            objectId = string.Empty;
            var id = _payload.Slice(0x30, 4);
            int length = id.IndexOf((byte)0);
            if (length < 0) length = 4;
            if (length == 0) return false;
            foreach (byte b in id.Slice(0, length))
            {
                if (b < 0x20 || b > 0x7E) return false;
            }

            objectId = System.Text.Encoding.ASCII.GetString(id.Slice(0, length));
            return true;
        }

        /// <summary>
        /// Indicates if the NPC/entity uses the equipped appearance model (look_t, 20 bytes).
        /// MODEL_EQUIPPED (size=1) and MODEL_CHOCOBO (size=7) both use the full equipped look_t.
        /// </summary>
        public bool IsEquippedLook => LookSize == 1 || LookSize == 7;

        /// <summary>
        /// Reads standard monster or NPC model ID (uint16 at payload offset 0x2E, when LookSize is a simple model type).
        /// Only valid for MODEL_STANDARD (0), MODEL_UNK_5 (5), and MODEL_AUTOMATON (6).
        /// Transport entities (MODEL_DOOR=2, MODEL_ELEVATOR=3, MODEL_SHIP=4) and equipped models
        /// (MODEL_EQUIPPED=1, MODEL_CHOCOBO=7) return 0 — they are not loaded as monster DATs.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server) entity_update.cpp
        /// </summary>
        public uint GetModelId()
        {
            if (_payload.Length < 0x30) return 0;

            // Only MODEL_STANDARD (0), MODEL_UNK_5 (5), MODEL_AUTOMATON (6) carry a simple numeric model ID.
            // All other size values (1=Equipped, 2=Door, 3=Elevator, 4=Ship, 7=Chocobo) must return 0.
            if (LookSize is not (0 or 5 or 6))
            {
                return 0;
            }

            // Standard look_t: uint16 modelid lives at look_t offset 2, i.e. payload offset 0x2E.
            return BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(0x2E, 2));
        }

        /// <summary>
        /// Attempts to unpack the 20-byte look_t structure for equipped humanoid NPCs and Chocobos.
        /// Extracts face (0x2E), race (0x2F), and 8 visual equipment slots (0x30..0x3F).
        /// Returns a 9-element GrapIdTable matching GordianXI convention:
        /// Index 0: FaceModel ((race &lt;&lt; 8) | face), Indices 1..8: Head, Body, Hands, Legs, Feet, Main, Sub, Ranged.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server) entity_update.cpp
        /// </summary>
        public bool TryGetEquippedLook(out byte race, out byte face, out ushort[] grapIdTable)
        {
            if (IsEquippedLook && _payload.Length >= 0x40)
            {
                face = _payload[0x2E];
                race = _payload[0x2F];
                grapIdTable = new ushort[9];
                grapIdTable[0] = (ushort)((race << 8) | face);
                for (int i = 0; i < 8; i++)
                {
                    grapIdTable[i + 1] = BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(0x30 + (i * 2), 2));
                }
                return true;
            }

            race = 0;
            face = 0;
            grapIdTable = Array.Empty<ushort>();
            return false;
        }

        /// <summary>
        /// Reads door/transport ID if entity is an elevator, door, or ship.
        /// </summary>
        public uint GetDoorId()
        {
            if (_payload.Length >= 0x34) // 0x30 + 4
            {
                return BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(0x30, 4));
            }
            return 0;
        }

        public bool HasName2 => (UpdateFlags & EntityUpdateFlags.Name2) != 0;

        /// <summary>First actor index of the client's spawnable range (pets, trusts); 0-1023 are static NPCs.</summary>
        private const ushort FirstSpawnableIndex = 1792;
        private const ushort FirstPlayerIndex = 1024;
        private const int NameLength = 16;

        /// <summary>
        /// The entity name the packet carries, or empty. <c>Name[16]</c> sits in one of three places:
        /// <list type="bullet">
        /// <item>SubKind 1 with the <c>Name2</c> flag: packet 0x44 (payload 0x40), for a spawnable, or for a static NPC whose
        /// name starts with a printable character.</item>
        /// <item>A static NPC (index &lt; 1024) with the <c>Name</c> flag and a <c>HasName</c> byte of 1 at packet 0x34:
        /// the name follows at packet 0x35.</item>
        /// <item>Otherwise, with the <c>Name</c> flag: packet 0x34 (payload 0x30). The retail client reads this only for
        /// spawnables, but LSB sends every NPC and mob name here, and the entity module prefers the zone's DAT name.</item>
        /// </list>
        /// Layout referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x000E), "Entity Name".
        /// </summary>
        public string GetName()
        {
            if (SubKind == EntitySubKind.Equipped && HasName2
                && (ActorIndex >= FirstSpawnableIndex || (ActorIndex < FirstPlayerIndex && _payload.Length > 0x40 && _payload[0x40] > (byte)' ')))
            {
                return ReadName(0x40);
            }

            if (!HasName) return string.Empty;
            if (ActorIndex < FirstPlayerIndex && _payload.Length > 0x30 && _payload[0x30] == 1)
            {
                return ReadName(0x31);
            }
            return ReadName(0x30);
        }

        private string ReadName(int offset)
        {
            if (_payload.Length <= offset) return string.Empty;
            ReadOnlySpan<byte> nameSpan = _payload.Slice(offset, Math.Min(NameLength, _payload.Length - offset));
            int len = nameSpan.IndexOf((byte)0);
            if (len < 0) len = nameSpan.Length;
            return len > 0 ? Encoding.ASCII.GetString(nameSpan.Slice(0, len)) : string.Empty;
        }
    }

    /// <summary>
    /// S2C 0x037 (GP_SERV_COMMAND_SERVERSTATUS): Active Character Status & Buffs.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/char_status.cpp).
    /// </summary>
    public readonly ref struct S2C_0x037_CharStatus
    {
        public const ushort PacketId = 0x037;

        public ReadOnlySpan<byte> BuffStatus { get; }
        public uint UniqueNo { get; }
        public uint Flags0 { get; }
        public uint Flags1 { get; }
        public byte ServerStatus { get; }
        public byte LsColorR { get; }
        public byte LsColorG { get; }
        public byte LsColorB { get; }
        public uint Flags2 { get; }
        public uint Flags3 { get; }
        /// <summary>
        /// <c>dead_counter1</c>: the homepoint countdown in 1/60 s ticks, offset by 6 minutes (the client force-homepoints
        /// once it drops below 6 minutes), as LSB's <c>char_status.cpp</c> sends it.
        /// </summary>
        public uint DeadCounterTicks { get; }

        /// <summary>Seconds left before the forced homepoint: <see cref="DeadCounterTicks"/> / 60 - 360, floored at 0.</summary>
        public uint HomepointSecondsRemaining => DeadCounterToSeconds(DeadCounterTicks);

        public const uint DeadCounterTicksPerSecond = 60;
        public const uint DeadCounterOffsetSeconds = 6 * 60;

        public static uint DeadCounterToSeconds(uint ticks)
        {
            uint seconds = ticks / DeadCounterTicksPerSecond;
            return seconds > DeadCounterOffsetSeconds ? seconds - DeadCounterOffsetSeconds : 0;
        }
        public ushort CostumeId { get; }
        public ushort WarpTargetIndex { get; }
        public ushort FellowTargetIndex { get; }
        public byte FishingTimer { get; }
        public ReadOnlySpan<byte> BuffStatusBits { get; }
        public ushort PetActorIndex { get; }
        public byte MountId { get; }
        public byte WardrobeMask { get; }
        public bool IsValid { get; }

        public S2C_0x037_CharStatus(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 0x58) // 0x5C - 4
            {
                BuffStatus = ReadOnlySpan<byte>.Empty;
                UniqueNo = 0;
                Flags0 = 0;
                Flags1 = 0;
                ServerStatus = 0;
                LsColorR = 0;
                LsColorG = 0;
                LsColorB = 0;
                Flags2 = 0;
                Flags3 = 0;
                DeadCounterTicks = 0;
                DeadCounter2Ticks = 0;
                CostumeId = 0;
                WarpTargetIndex = 0;
                FellowTargetIndex = 0;
                FishingTimer = 0;
                BuffStatusBits = ReadOnlySpan<byte>.Empty;
                PetActorIndex = 0;
                MountId = 0;
                WardrobeMask = 0;
                _flags4 = 0;
                IsValid = false;
                return;
            }

            BuffStatus = payload.Slice(0, 32);
            UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(32, 4));
            Flags0 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(36, 4));
            Flags1 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(40, 4));
            ServerStatus = payload[44];
            LsColorR = payload[45];
            LsColorG = payload[46];
            LsColorB = payload[47];
            Flags2 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(48, 4));
            Flags3 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(52, 4));
            DeadCounterTicks = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(56, 4));
            DeadCounter2Ticks = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(60, 4));
            CostumeId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(64, 2));
            WarpTargetIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(66, 2));
            FellowTargetIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(68, 2));
            FishingTimer = payload[70];
            BuffStatusBits = payload.Slice(72, 8); // status_bits_t (32 x 2-bit values)

            PetActorIndex = (ushort)((Flags2 >> 3) & 0xFFFF);
            MountId = payload.Length >= 0x58 ? payload[0x57] : (byte)0;
            WardrobeMask = payload.Length >= 0x59 ? payload[0x58] : (byte)0;
            _flags4 = payload.Length > 0x54 ? payload[0x54] : (byte)0;

            IsValid = true;
        }

        public byte Hpp => (byte)((Flags0 >> 16) & 0xFF);
        public ushort Speed => (ushort)(Flags1 & 0x0FFF);
        public byte SpeedBase => (byte)((Flags1 >> 17) & 0xFF);
        public bool IsInvisible => ((Flags1 >> 15) & 0x01) != 0;
        public bool HasBazaar => ((Flags1 >> 29) & 0x01) != 0;
        public bool IsCharmed => ((Flags1 >> 30) & 0x01) != 0;

        /// <summary>GM level (flags0 bits 29-31). The client only distinguishes values of 3 and up.</summary>
        public byte GmLevel => (byte)((Flags0 >> 29) & 0x07);

        /// <summary>
        /// FreezeFlag (flags1 bit 13): the client is locked in place and the compass is removed. Layout referenced from
        /// XiPackets (https://github.com/atom0s/XiPackets) world/server/0x0037.
        /// </summary>
        public bool IsFrozen => ((Flags1 >> 13) & 0x01) != 0;

        /// <summary>Hackmove (flags1 bit 12).</summary>
        public bool HackMove => ((Flags1 >> 12) & 0x01) != 0;

        /// <summary>
        /// <c>dead_counter2</c> (payload 0x3C), written after <see cref="DeadCounterTicks"/>: a second timer of the dead
        /// entity waiting to homepoint. LandSandBoat leaves it 0.
        /// </summary>
        public uint DeadCounter2Ticks { get; }

        /// <summary>Flags4 (payload 0x54): bit 7 JobMasterFlag. 0 when the packet is too short.</summary>
        public byte Flags4 => _flags4;

        private readonly byte _flags4;

        /// <summary>
        /// The local player's name plate flags: flags0 LfgFlag (4), AnonymousFlag (5), CfhFlag (6), AwayFlag (7),
        /// PlayOnelineFlag (24), LinkShellFlag (25), LinkDeadFlag (26); flags1 BazaarFlag (29), GmIconFlag (31); flags2
        /// AutoPartyFlag (2); flags3 LfgMasterFlag (0), TrialFlag (1), NewCharacterFlag (3), MentorFlag (4); flags4
        /// JobMasterFlag (7). The bit positions differ from 0x00D's.
        /// Layout referenced from XiPackets (https://github.com/atom0s/XiPackets) world/server/0x0037.
        /// </summary>
        public NamePlateFlags NamePlate
        {
            get
            {
                var flags = NamePlateFlags.None;
                if ((Flags0 & (1u << 4)) != 0) flags |= NamePlateFlags.SeekingParty;
                if ((Flags0 & (1u << 5)) != 0) flags |= NamePlateFlags.Anonymous;
                if ((Flags0 & (1u << 6)) != 0) flags |= NamePlateFlags.CalledForHelp;
                if ((Flags0 & (1u << 7)) != 0) flags |= NamePlateFlags.Away;
                if ((Flags0 & (1u << 24)) != 0) flags |= NamePlateFlags.PlayOnline;
                if ((Flags0 & (1u << 25)) != 0) flags |= NamePlateFlags.Linkshell;
                if ((Flags0 & (1u << 26)) != 0) flags |= NamePlateFlags.LinkDead;
                if ((Flags1 & (1u << 29)) != 0) flags |= NamePlateFlags.Bazaar;
                if ((Flags1 & (1u << 31)) != 0) flags |= NamePlateFlags.GmIconHidden;
                if ((Flags2 & (1u << 2)) != 0) flags |= NamePlateFlags.AutoParty;
                if ((Flags3 & (1u << 0)) != 0) flags |= NamePlateFlags.SeekingMasterParty;
                if ((Flags3 & (1u << 1)) != 0) flags |= NamePlateFlags.Trial;
                if ((Flags3 & (1u << 3)) != 0) flags |= NamePlateFlags.NewPlayer;
                if ((Flags3 & (1u << 4)) != 0) flags |= NamePlateFlags.Mentor;
                if ((_flags4 & 0x80) != 0) flags |= NamePlateFlags.JobMaster;
                return flags;
            }
        }
    }

    /// <summary>
    /// S2C 0x061 (GP_SERV_COMMAND_CLISTATUS): Character Statistics & Attributes.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x061_clistatus.cpp).
    /// </summary>
    public readonly ref struct S2C_0x061_CliStatus
    {
        public const ushort PacketId = 0x061;

        public int HpMax { get; }
        public int MpMax { get; }
        public JobId MainJob { get; }
        public byte MainJobLevel { get; }
        public JobId SubJob { get; }
        public byte SubJobLevel { get; }
        /// <summary>
        /// Current and to-next-level EXP. XiPackets and LSB declare both as int16, but LSB's exp-to-next table
        /// goes above 32767, so they are read unsigned.
        /// </summary>
        public ushort ExpNow { get; }
        public ushort ExpNext { get; }
        public short Attack { get; }
        public short Defense { get; }
        public ushort TitleId { get; }
        public ushort Rank { get; }
        public ushort RankPoints { get; }
        public ushort HomePointZone { get; }
        public byte Nation { get; }
        public byte SuperiorLevel { get; }
        public byte ItemLevel { get; }
        public byte HighestItemLevel { get; }
        public byte UnityFaction { get; }
        public uint UnityPoints { get; }
        public bool IsValid { get; }

        /// <summary><c>MonsterBuster</c> (payload 72): the Monster Buster bit field.</summary>
        public uint MonsterBuster => _payload.Length >= 76 ? BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(72, 4)) : 0u;

        /// <summary><c>myroom</c> (payload 77): the Mog House style.</summary>
        public byte MyRoom => _payload.Length > 77 ? _payload[77] : (byte)0;

        /// <summary><c>ilvl_mhand</c> (payload 82): the item level of the main hand weapon.</summary>
        public byte ItemLevelMainHand => _payload.Length > 82 ? _payload[82] : (byte)0;

        /// <summary><c>ilvl_ranged</c> (payload 83): the item level of the ranged weapon.</summary>
        public byte ItemLevelRanged => _payload.Length > 83 ? _payload[83] : (byte)0;

        /// <summary><c>unity_points1</c> (payload 88): the partial Unity personal evaluation points.</summary>
        public ushort UnityPoints1 => _payload.Length >= 90 ? BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(88, 2)) : (ushort)0;

        /// <summary><c>unity_points2</c> (payload 90): the Unity personal evaluation points.</summary>
        public ushort UnityPoints2 => _payload.Length >= 92 ? BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(90, 2)) : (ushort)0;

        /// <summary>
        /// <c>unity_chat_color_flag</c> (payload 92, lowest bit): the Unity leader's name colour in <c>/unity</c> chat, light
        /// white-grey when set and dark grey when clear.
        /// </summary>
        public bool UnityChatLightColor => _payload.Length >= 96 && (BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(92, 4)) & 1) != 0;

        /// <summary><c>mastery_info</c> job level (payload 97): the master level of the current job.</summary>
        public byte MasteryJobLevel => _payload.Length > 97 ? _payload[97] : (byte)0;

        /// <summary><c>mastery_info</c> flags (payload 98): 0x01 job mastery unlocked (enables Master Levels), 0x02 the job is capped on exemplar points.</summary>
        public byte MasteryFlags => _payload.Length > 98 ? _payload[98] : (byte)0;

        /// <summary>The job mastery system is unlocked (<see cref="MasteryFlags"/> 0x01).</summary>
        public bool MasteryUnlocked => (MasteryFlags & 0x01) != 0;

        /// <summary>The current job is capped on exemplar points (<see cref="MasteryFlags"/> 0x02).</summary>
        public bool MasteryExemplarCapped => (MasteryFlags & 0x02) != 0;

        /// <summary><c>mastery_exp_now</c> (payload 100): the current master experience points.</summary>
        public uint MasteryExpNow => _payload.Length >= 104 ? BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(100, 4)) : 0u;

        /// <summary><c>mastery_exp_next</c> (payload 104): the master experience points needed for the next level.</summary>
        public uint MasteryExpNext => _payload.Length >= 108 ? BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(104, 4)) : 0u;

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x061_CliStatus(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 0x6C) // 0x70 - 4
            {
                HpMax = 0;
                MpMax = 0;
                MainJob = JobId.None;
                MainJobLevel = 0;
                SubJob = JobId.None;
                SubJobLevel = 0;
                ExpNow = 0;
                ExpNext = 0;
                Attack = 0;
                Defense = 0;
                TitleId = 0;
                Rank = 0;
                RankPoints = 0;
                HomePointZone = 0;
                Nation = 0;
                SuperiorLevel = 0;
                ItemLevel = 0;
                HighestItemLevel = 0;
                UnityFaction = 0;
                UnityPoints = 0;
                IsValid = false;
                return;
            }

            HpMax = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0, 4));
            MpMax = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4, 4));
            MainJob = (JobId)payload[8];
            MainJobLevel = payload[9];
            SubJob = (JobId)payload[10];
            SubJobLevel = payload[11];
            ExpNow = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(12, 2));
            ExpNext = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(14, 2));

            // bp_base is at 16..29 (7 x ushort)
            // bp_adj is at 30..43 (7 x short)
            Attack = BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(44, 2));
            Defense = BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(46, 2));
            // def_elem is at 48..63 (8 x short)

            TitleId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(64, 2));
            Rank = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(66, 2));
            RankPoints = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(68, 2));
            HomePointZone = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(70, 2));
            Nation = payload[76];
            SuperiorLevel = payload[78];
            HighestItemLevel = payload[80];
            ItemLevel = payload[81];

            uint unityRaw = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(84, 4));
            UnityFaction = (byte)(unityRaw & 0x1F);
            UnityPoints = (unityRaw >> 10) & 0x1FFFF;

            IsValid = true;
        }

        public ushort GetBaseStat(int index)
        {
            if (index < 0 || index >= 7 || _payload.Length < 30) return 0;
            return BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(16 + (index * 2), 2));
        }

        public short GetStatModifier(int index)
        {
            if (index < 0 || index >= 7 || _payload.Length < 44) return 0;
            return BinaryPrimitives.ReadInt16LittleEndian(_payload.Slice(30 + (index * 2), 2));
        }

        public short GetElementalResistance(int index)
        {
            if (index < 0 || index >= 8 || _payload.Length < 64) return 0;
            return BinaryPrimitives.ReadInt16LittleEndian(_payload.Slice(48 + (index * 2), 2));
        }
    }

    /// <summary>
    /// S2C 0x062 (GP_SERV_COMMAND_CLISTATUS2): Skill Base Ratings & Recasts.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x062_clistatus2.cpp).
    /// </summary>
    public readonly ref struct S2C_0x062_CliStatus2
    {
        public const ushort PacketId = 0x062;

        public bool IsValid { get; }
        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x062_CliStatus2(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            IsValid = payload.Length >= 252; // 124 (31 x 4 recasts) + 128 (64 x 2 skills)
        }

        public uint GetCommandRecast(int index)
        {
            if (!IsValid || index < 0 || index >= 31) return 0;
            return BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(index * 4, 4));
        }

        public ushort GetSkillBase(int index)
        {
            if (!IsValid || index < 0 || index >= 64) return 0;
            return BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(124 + (index * 2), 2));
        }

        /// <summary>Entries 0-47 are combat skills; 48-63 are crafting skills, packed differently.</summary>
        public const int FirstCraftSkillIndex = 48;

        /// <summary>Crafting entries the server does not use are sent as 0xFFFF.</summary>
        private const ushort UnusedCraftSkill = 0xFFFF;

        /// <summary>
        /// Bit 15: a combat skill is capped for the current level; a crafting skill has reached its rank's cap.
        /// </summary>
        public bool IsSkillCapped(int index)
        {
            ushort raw = GetSkillBase(index);
            return raw != UnusedCraftSkill && (raw & 0x8000) != 0;
        }

        /// <summary>
        /// The skill level. A combat entry is <c>level | cap bit</c>; a crafting entry is
        /// <c>level * 0x20 + rank</c> (plus the cap bit), as LSB's <c>charutils::BuildingCharSkillsTable</c> packs it.
        /// </summary>
        public ushort GetSkillLevel(int index)
        {
            ushort raw = GetSkillBase(index);
            if (index < FirstCraftSkillIndex) return (ushort)(raw & 0x7FFF);
            if (raw == UnusedCraftSkill) return 0;
            return (ushort)((raw & 0x7FFF) >> 5);
        }

        /// <summary>A crafting skill's rank (0 = Amateur ... 10 = Legend); 0 for combat skills and unused entries.</summary>
        public byte GetCraftRank(int index)
        {
            if (index < FirstCraftSkillIndex) return 0;
            ushort raw = GetSkillBase(index);
            if (raw == UnusedCraftSkill) return 0;
            return (byte)(raw & 0x1F);
        }
    }

    /// <summary>
    /// S2C 0x076 (GP_SERV_COMMAND_GROUP_EFFECTS): Party Member Buff Updates.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x076_group_effects.cpp).
    /// </summary>
    public readonly ref struct S2C_0x076_GroupEffects
    {
        public const ushort PacketId = 0x076;
        public const int MemberEntrySize = 48; // 4 + 2 + 2 + 8 + 32

        public int MemberCount { get; }
        public bool IsValid { get; }

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x076_GroupEffects(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            MemberCount = Math.Min(5, payload.Length / MemberEntrySize);
            IsValid = MemberCount > 0;
        }

        public uint GetMemberUniqueNo(int index)
        {
            if (index < 0 || index >= MemberCount) return 0;
            return BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(index * MemberEntrySize, 4));
        }

        public ushort GetMemberActorIndex(int index)
        {
            if (index < 0 || index >= MemberCount) return 0;
            return BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice((index * MemberEntrySize) + 4, 2));
        }

        public ulong GetMemberStatusBits(int index)
        {
            if (index < 0 || index >= MemberCount) return 0;
            return BinaryPrimitives.ReadUInt64LittleEndian(_payload.Slice((index * MemberEntrySize) + 8, 8));
        }

        public ReadOnlySpan<byte> GetMemberBuffs(int index)
        {
            if (index < 0 || index >= MemberCount) return ReadOnlySpan<byte>.Empty;
            return _payload.Slice((index * MemberEntrySize) + 16, 32);
        }

        /// <summary>
        /// Status effect ids in icon order: each buff byte is an id's low 8 bits and the 64-bit field carries its two
        /// high bits (buff i at bits 2i..2i+1, as in S2C 0x037); 0xFF with no high bits is an empty slot.
        /// </summary>
        public static ushort[] DecodeStatusIds(ReadOnlySpan<byte> buffs, ulong statusBits)
        {
            int count = 0;
            Span<ushort> ids = stackalloc ushort[32];
            for (int i = 0; i < buffs.Length && i < 32; i++)
            {
                int id = buffs[i] | (int)((statusBits >> (2 * i)) & 0x03) << 8;
                if (id != 0xFF) ids[count++] = (ushort)id;
            }
            return ids[..count].ToArray();
        }
    }

    /// <summary>
    /// S2C 0x077 (GP_SERV_COMMAND_ENTITY_VIS): Entity Visibility Range Updates.
    /// The client handles only <c>Flags == 1</c>, where <c>Data[128]</c> is a list of up to 32 UniqueNo values for
    /// otherwise-hidden entities it may see; any other flag value leaves the data uninterpreted.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x077_entity_vis.cpp)
    /// and XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x0077).
    /// </summary>
    public readonly ref struct S2C_0x077_EntityVis
    {
        public const ushort PacketId = 0x077;
        public const byte UniqueNoListFlag = 1;

        public byte Flags { get; }

        /// <summary>True when <see cref="Flags"/> is 1 and the data is a UniqueNo list.</summary>
        public bool IsUniqueNoList => Flags == UniqueNoListFlag;

        /// <summary>UniqueNo entries in the list; 0 unless <see cref="IsUniqueNoList"/>.</summary>
        public int Count { get; }
        public bool IsValid { get; }

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x077_EntityVis(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 4)
            {
                Flags = 0;
                Count = 0;
                IsValid = false;
                return;
            }

            Flags = payload[0];
            Count = Flags == UniqueNoListFlag ? Math.Min(32, (payload.Length - 4) / 4) : 0;
            IsValid = true;
        }

        public uint GetUniqueNo(int index)
        {
            if (index < 0 || index >= Count) return 0;
            return BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(4 + (index * 4), 4));
        }
    }

    /// <summary>
    /// S2C 0x0DF (GP_SERV_COMMAND_GROUP_ATTR): Party / Local Player Attributes &amp; Vitals.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x0df_group_attr.cpp).
    /// </summary>
    public readonly ref struct S2C_0x0DF_GroupAttr
    {
        public const ushort PacketId = 0x0DF;

        public uint UniqueNo { get; }
        public uint Hp { get; }
        public uint Mp { get; }
        public uint Tp { get; }
        public ushort ActorIndex { get; }
        public byte Hpp { get; }
        public byte Mpp { get; }
        public byte Kind { get; }
        public byte MoghouseFlag { get; }
        public ushort ZoneNo { get; }
        public ushort MonstrosityFlag { get; }
        public ushort MonstrosityNameId { get; }
        public JobId MainJob { get; }
        public byte MainJobLevel { get; }
        public JobId SubJob { get; }
        public byte SubJobLevel { get; }
        public byte MasterJobLevel { get; }
        public byte MasterJobFlags { get; }
        public bool IsValid { get; }

        public S2C_0x0DF_GroupAttr(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 32)
            {
                UniqueNo = 0;
                Hp = 0;
                Mp = 0;
                Tp = 0;
                ActorIndex = 0;
                Hpp = 0;
                Mpp = 0;
                Kind = 0;
                MoghouseFlag = 0;
                ZoneNo = 0;
                MonstrosityFlag = 0;
                MonstrosityNameId = 0;
                MainJob = JobId.None;
                MainJobLevel = 0;
                SubJob = JobId.None;
                SubJobLevel = 0;
                MasterJobLevel = 0;
                MasterJobFlags = 0;
                IsValid = false;
                return;
            }

            UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            Hp = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            Mp = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(8, 4));
            Tp = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(12, 4));
            ActorIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(16, 2));
            Hpp = payload[18];
            Mpp = payload[19];
            Kind = payload[20];
            MoghouseFlag = payload[21];
            ZoneNo = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(22, 2));
            MonstrosityFlag = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(24, 2));
            MonstrosityNameId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(26, 2));
            MainJob = (JobId)payload[28];
            MainJobLevel = payload[29];
            SubJob = (JobId)payload[30];
            SubJobLevel = payload[31];

            MasterJobLevel = payload.Length >= 33 ? payload[32] : (byte)0;
            MasterJobFlags = payload.Length >= 34 ? payload[33] : (byte)0;

            IsValid = true;
        }
    }

    #endregion

    #region Outbound Builders

    /// <summary>
    /// Static builder methods for entity interaction and character request client sub-packets.
    /// </summary>
    public static class EntityOutboundPackets
    {
        public const int ClStatSubPacketSize = 36; // 4-byte header + 32-byte payload (8 x uint32)
        public const int CharReqSubPacketSize = 8;  // 4-byte header + 2-byte actIndex + 2-byte dammy
        public const int CharReq2SubPacketSize = 20; // 4-byte header + 16-byte payload

        /// <summary>
        /// Builds C2S 0x00F (GP_CLI_COMMAND_CLSTAT): Client Equipment / System Synchronization.
        /// </summary>
        public static void BuildClStat(Span<byte> destination, ReadOnlySpan<uint> stats = default, ushort sequenceId = 0)
        {
            if (destination.Length < ClStatSubPacketSize)
                throw new ArgumentException($"Destination must be at least {ClStatSubPacketSize} bytes.", nameof(destination));

            destination.Slice(0, ClStatSubPacketSize).Clear();
            ushort headerWord = (ushort)(0x00F | (9 << 9)); // size: 36 bytes = 9 words of 4 bytes
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);

            if (!stats.IsEmpty)
            {
                int limit = Math.Min(stats.Length, 8);
                for (int i = 0; i < limit; i++)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4 + (i * 4), 4), stats[i]);
                }
            }
        }

        public static byte[] BuildClStat(ReadOnlySpan<uint> stats = default, ushort sequenceId = 0)
        {
            byte[] packet = new byte[ClStatSubPacketSize];
            BuildClStat(packet.AsSpan(), stats, sequenceId);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x016 (GP_CLI_COMMAND_CHARREQ): Requests entity spawn/update by target index.
        /// </summary>
        public static void BuildCharReq(Span<byte> destination, ushort actIndex, ushort sequenceId = 0)
        {
            if (destination.Length < CharReqSubPacketSize)
                throw new ArgumentException($"Destination must be at least {CharReqSubPacketSize} bytes.", nameof(destination));

            destination.Slice(0, CharReqSubPacketSize).Clear();
            ushort headerWord = (ushort)(0x016 | (2 << 9)); // size: 8 bytes = 2 words of 4 bytes
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), actIndex);
        }

        public static byte[] BuildCharReq(ushort actIndex, ushort sequenceId = 0)
        {
            byte[] packet = new byte[CharReqSubPacketSize];
            BuildCharReq(packet.AsSpan(), actIndex, sequenceId);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x017 (GP_CLI_COMMAND_CHARREQ2): Requests entity update for unexpected state.
        /// </summary>
        public static void BuildCharReq2(
            Span<byte> destination,
            ushort actIndex,
            uint uniqueNo2 = 0,
            uint uniqueNo3 = 0,
            ushort flg = 0,
            ushort flg2 = 0,
            ushort sequenceId = 0)
        {
            if (destination.Length < CharReq2SubPacketSize)
                throw new ArgumentException($"Destination must be at least {CharReq2SubPacketSize} bytes.", nameof(destination));

            destination.Slice(0, CharReq2SubPacketSize).Clear();
            ushort headerWord = (ushort)(0x017 | (5 << 9)); // size: 20 bytes = 5 words of 4 bytes
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), actIndex);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8, 4), uniqueNo2);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(12, 4), uniqueNo3);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(16, 2), flg);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(18, 2), flg2);
        }

        public static byte[] BuildCharReq2(
            ushort actIndex,
            uint uniqueNo2 = 0,
            uint uniqueNo3 = 0,
            ushort flg = 0,
            ushort flg2 = 0,
            ushort sequenceId = 0)
        {
            byte[] packet = new byte[CharReq2SubPacketSize];
            BuildCharReq2(packet.AsSpan(), actIndex, uniqueNo2, uniqueNo3, flg, flg2, sequenceId);
            return packet;
        }

        public const int CliStatusSubPacketSize = 8; // 4-byte header + 4-byte payload

        /// <summary>
        /// Builds C2S 0x061 (GP_CLI_COMMAND_CLISTATUS): Client Status &amp; Attributes Request.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x061_clistatus.cpp).
        /// </summary>
        public static void BuildCliStatus(Span<byte> destination, byte unknown00 = 0, ushort sequenceId = 0)
        {
            if (destination.Length < CliStatusSubPacketSize)
                throw new ArgumentException($"Destination must be at least {CliStatusSubPacketSize} bytes.", nameof(destination));

            destination.Slice(0, CliStatusSubPacketSize).Clear();
            ushort headerWord = (ushort)(0x061 | (2 << 9)); // size: 8 bytes = 2 words of 4 bytes
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);
            destination[4] = unknown00;
        }

        public static byte[] BuildCliStatus(byte unknown00 = 0, ushort sequenceId = 0)
        {
            byte[] packet = new byte[CliStatusSubPacketSize];
            BuildCliStatus(packet.AsSpan(), unknown00, sequenceId);
            return packet;
        }
    }

    /// <summary>
    /// S2C 0x039 (GP_SERV_COMMAND_MAPSCHEDULOR): plays one of the zone's own routines (Section 0x07 of the zone DAT) by
    /// its FourCC, e.g. the Alzadaal Undersea Ruins Runic Portals' <c>1pa1</c> / <c>1pb1</c> / <c>2pb1</c>, which
    /// LandSandBoat sends after every zone-in. Payload (after the 4-byte header): caster server id (+0x00), target server
    /// id (+0x04), the routine FourCC (+0x08), caster and target indexes (+0x0C, +0x0E); LandSandBoat sends 0 for both
    /// actors when it names none.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x0039)
    /// and LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x039_mapschedulor.h).
    /// </summary>
    public readonly ref struct S2C_0x039_MapSchedulor
    {
        public const ushort PacketId = 0x039;

        public uint CasterServerId { get; }
        public uint TargetServerId { get; }
        /// <summary>The routine's FourCC as text (up to four printable characters).</summary>
        public string Routine { get; }
        public ushort CasterIndex { get; }
        public ushort TargetIndex { get; }
        public bool IsValid { get; }

        public S2C_0x039_MapSchedulor(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 16)
            {
                CasterServerId = TargetServerId = 0;
                Routine = string.Empty;
                CasterIndex = TargetIndex = 0;
                IsValid = false;
                return;
            }

            CasterServerId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            TargetServerId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            Routine = ReadFourCc(payload.Slice(8, 4));
            CasterIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(12, 2));
            TargetIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(14, 2));
            IsValid = Routine.Length > 0;
        }

        private static string ReadFourCc(ReadOnlySpan<byte> span)
        {
            int end = span.IndexOf((byte)0);
            if (end < 0) end = span.Length;
            foreach (byte b in span.Slice(0, end))
            {
                if (b < 0x20 || b > 0x7E) return string.Empty;
            }
            return Encoding.ASCII.GetString(span.Slice(0, end));
        }
    }

    #endregion
}
