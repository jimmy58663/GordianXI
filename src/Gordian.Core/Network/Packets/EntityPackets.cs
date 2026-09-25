// src/Gordian.Core/Network/Packets/EntityPackets.cs
// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server)
// and Atom0s XiPackets research (https://github.com/atom0s/XiPackets).

using System;
using System.Buffers.Binary;
using System.Text;

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
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/entity_update.cpp).
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

        /// <summary>
        /// Movement frame timer / timestamp (bits 0..12 of Flags0). Non-zero when moving, zero when stationary.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server) flags0_t.
        /// </summary>
        public ushort MovTime => (ushort)(Flags0 & 0x1FFF);
        public bool IsMoving => MovTime != 0;

        /// <summary>
        /// Reads look size / model type: 0 = MODEL_STANDARD, 1 = MODEL_EQUIPPED, 2 = DOOR, 3 = ELEVATOR, etc.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server) mmo.h and entity_update.h
        /// </summary>
        public ushort LookSize => _payload.Length >= 0x2E
            ? BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(0x2C, 2))
            : (ushort)0;

        /// <summary>
        /// Sub-animation state parameter (offset 0x2A in whole packet, payload offset 0x26).
        /// For Uragnites: 4 = out of shell (open), 5 = in shell (closed).
        /// For Worms: 0 = surfaced, 1 = submerged.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server) entity_update.cpp and uragnite.lua.
        /// </summary>
        public byte AnimationSub => _payload.Length >= 0x27 ? _payload[0x26] : (byte)0;

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

            ushort size = BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(0x2C, 2));

            // Only MODEL_STANDARD (0), MODEL_UNK_5 (5), MODEL_AUTOMATON (6) carry a simple numeric model ID.
            // All other size values (1=Equipped, 2=Door, 3=Elevator, 4=Ship, 7=Chocobo) must return 0.
            if (size is not (0 or 5 or 6))
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

        /// <summary>
        /// Reads entity name ASCII string.
        /// </summary>
        public string GetName()
        {
            if (!HasName) return string.Empty;
            int nameOffset = 0x30; // 0x34 - 4
            if (_payload.Length <= nameOffset) return string.Empty;

            ReadOnlySpan<byte> nameSpan = _payload.Slice(nameOffset);
            int len = 0;
            while (len < nameSpan.Length && len < 24 && nameSpan[len] != 0)
            {
                len++;
            }
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
        public uint DeadCounterSeconds { get; }
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
                DeadCounterSeconds = 0;
                CostumeId = 0;
                WarpTargetIndex = 0;
                FellowTargetIndex = 0;
                FishingTimer = 0;
                BuffStatusBits = ReadOnlySpan<byte>.Empty;
                PetActorIndex = 0;
                MountId = 0;
                WardrobeMask = 0;
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
            DeadCounterSeconds = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(56, 4));
            CostumeId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(64, 2));
            WarpTargetIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(66, 2));
            FellowTargetIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(68, 2));
            FishingTimer = payload[70];
            BuffStatusBits = payload.Slice(72, 8); // status_bits_t (32 x 2-bit values)

            PetActorIndex = (ushort)((Flags2 >> 3) & 0xFFFF);
            MountId = payload.Length >= 0x58 ? payload[0x57] : (byte)0;
            WardrobeMask = payload.Length >= 0x59 ? payload[0x58] : (byte)0;

            IsValid = true;
        }

        public byte Hpp => (byte)((Flags0 >> 16) & 0xFF);
        public ushort Speed => (ushort)(Flags1 & 0x0FFF);
        public byte SpeedBase => (byte)((Flags1 >> 17) & 0xFF);
        public bool IsInvisible => ((Flags1 >> 15) & 0x01) != 0;
        public bool HasBazaar => ((Flags1 >> 29) & 0x01) != 0;
        public bool IsCharmed => ((Flags1 >> 30) & 0x01) != 0;
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
        public short ExpNow { get; }
        public short ExpNext { get; }
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

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x061_CliStatus(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 0x60) // 0x64 - 4
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
            ExpNow = BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(12, 2));
            ExpNext = BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(14, 2));

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

        public bool IsSkillCapped(int index)
        {
            return (GetSkillBase(index) & 0x8000) != 0;
        }

        public ushort GetSkillLevel(int index)
        {
            return (ushort)(GetSkillBase(index) & 0x7FFF);
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
    }

    /// <summary>
    /// S2C 0x077 (GP_SERV_COMMAND_ENTITY_VIS): Entity Visibility Range Updates.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x077_entity_vis.cpp).
    /// </summary>
    public readonly ref struct S2C_0x077_EntityVis
    {
        public const ushort PacketId = 0x077;

        public byte Flags { get; }
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
            Count = Math.Min(32, (payload.Length - 4) / 4);
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

    #endregion
}
