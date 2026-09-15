// src/Gordian.Core/Network/Packets/ProgressionPackets.cs
using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Gordian.Core.Network.Packets
{
    #region Enums

    /// <summary>
    /// Event user control release mode for S2C 0x052.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x052_eventucoff.h).
    /// </summary>
    public enum EventUcOffMode : uint
    {
        Standard = 0,
        EventRecvPending = 1,
        CancelEvent = 2,
        CancelInput = 3,
        Fishing = 4
    }

    /// <summary>
    /// Mog house operation result for S2C 0x0FA.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x0fa_myroom_operation.h).
    /// </summary>
    public enum MyRoomOperationResult : byte
    {
        Ok = 0,
        PlantAdd = 1,
        PlantCheck = 2,
        PlantCrop = 3,
        PlantStop = 4,
        Layout = 5,
        BankIn = 6,
        End = 7
    }

    /// <summary>
    /// Merit interaction kind for C2S 0x0BE.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x0be_merits.h).
    /// </summary>
    public enum MeritCommandKind : byte
    {
        ChangeMode = 2,
        EditMode = 3
    }

    /// <summary>
    /// Fishing action mode for C2S 0x066 / 0x110.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x110_fishing_2.h).
    /// </summary>
    public enum FishingActionMode : byte
    {
        RequestCheckHook = 2,
        RequestEndMiniGame = 3,
        RequestRelease = 4,
        RequestPotentialTimeout = 5
    }

    /// <summary>
    /// Chocobo race request kind for C2S 0x09B.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x09b_chocobo_race_req.h).
    /// </summary>
    public enum ChocoboRaceReqKind : uint
    {
        Toteboard = 1,
        ChocoboList = 2
    }

    /// <summary>
    /// Mog house interaction kind for C2S 0x0CB.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x0cb_myroom_is.h).
    /// </summary>
    public enum MyRoomIsKind : byte
    {
        Open = 1,
        Close = 2,
        Remodel = 5
    }

    #endregion

    #region Inbound S2C Decoders

    /// <summary>
    /// S2C 0x032 (GP_SERV_COMMAND_EVENT): Standard cutscene or dialogue event initiation.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x032_event.h).
    /// </summary>
    public readonly ref struct S2C_0x032_Event
    {
        public const ushort PacketId = 0x032;

        public uint UniqueNo { get; }
        public ushort ActIndex { get; }
        public ushort EventNum { get; }
        public ushort EventPara { get; }
        public ushort Mode { get; }
        public ushort EventNum2 { get; }
        public ushort EventPara2 { get; }
        public bool IsValid { get; }

        public S2C_0x032_Event(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 16)
            {
                UniqueNo = 0;
                ActIndex = 0;
                EventNum = 0;
                EventPara = 0;
                Mode = 0;
                EventNum2 = 0;
                EventPara2 = 0;
                IsValid = false;
                return;
            }

            UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            ActIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            EventNum = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2));
            EventPara = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2));
            Mode = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10, 2));
            EventNum2 = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(12, 2));
            EventPara2 = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(14, 2));
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x033 (GP_SERV_COMMAND_EVENTSTR): Event initiation carrying string parameters.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x033_eventstr.h).
    /// </summary>
    public readonly ref struct S2C_0x033_EventStr
    {
        public const ushort PacketId = 0x033;

        public uint UniqueNo { get; }
        public ushort ActIndex { get; }
        public ushort EventNum { get; }
        public ushort EventPara { get; }
        public ushort Mode { get; }
        public bool IsValid { get; }

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x033_EventStr(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 12)
            {
                UniqueNo = 0;
                ActIndex = 0;
                EventNum = 0;
                EventPara = 0;
                Mode = 0;
                IsValid = false;
                return;
            }

            UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            ActIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            EventNum = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2));
            EventPara = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2));
            Mode = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10, 2));
            IsValid = true;
        }

        public string GetStringParam(int index)
        {
            if (!IsValid || index < 0 || index >= 4) return string.Empty;
            int offset = 12 + (index * 16);
            if (_payload.Length < offset + 16) return string.Empty;

            var slice = _payload.Slice(offset, 16);
            int nullIdx = slice.IndexOf((byte)0);
            if (nullIdx >= 0) slice = slice.Slice(0, nullIdx);
            return Encoding.ASCII.GetString(slice);
        }

        public uint GetDataParam(int index)
        {
            if (!IsValid || index < 0 || index >= 8) return 0;
            int offset = 12 + 64 + (index * 4); // after 4 strings (64 bytes)
            if (_payload.Length < offset + 4) return 0;
            return BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(offset, 4));
        }
    }

    /// <summary>
    /// S2C 0x034 (GP_SERV_COMMAND_EVENTNUM): Event initiation carrying 8 numeric parameters.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x034_eventnum.h).
    /// </summary>
    public readonly ref struct S2C_0x034_EventNum
    {
        public const ushort PacketId = 0x034;

        public uint UniqueNo { get; }
        public ushort ActIndex { get; }
        public ushort EventNum { get; }
        public ushort EventPara { get; }
        public ushort Mode { get; }
        public ushort EventNum2 { get; }
        public ushort EventPara2 { get; }
        public bool IsValid { get; }

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x034_EventNum(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 48)
            {
                UniqueNo = 0;
                ActIndex = 0;
                EventNum = 0;
                EventPara = 0;
                Mode = 0;
                EventNum2 = 0;
                EventPara2 = 0;
                IsValid = false;
                return;
            }

            UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            ActIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(36, 2));
            EventNum = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(38, 2));
            EventPara = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(40, 2));
            Mode = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(42, 2));
            EventNum2 = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(44, 2));
            EventPara2 = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(46, 2));
            IsValid = true;
        }

        public int GetNumericParam(int index)
        {
            if (!IsValid || index < 0 || index >= 8) return 0;
            return BinaryPrimitives.ReadInt32LittleEndian(_payload.Slice(4 + (index * 4), 4));
        }
    }

    /// <summary>
    /// S2C 0x036 (GP_SERV_COMMAND_TALKNUM): NPC dialogue message loaded from DAT string tables.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x036_talknum.h).
    /// </summary>
    public readonly ref struct S2C_0x036_TalkNum
    {
        public const ushort PacketId = 0x036;

        public uint UniqueNo { get; }
        public ushort ActIndex { get; }
        public ushort MessageId { get; }
        public byte Type { get; }
        public bool IsValid { get; }

        public S2C_0x036_TalkNum(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 9)
            {
                UniqueNo = 0;
                ActIndex = 0;
                MessageId = 0;
                Type = 0;
                IsValid = false;
                return;
            }

            UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            ActIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            MessageId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2));
            Type = payload[8];
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x052 (GP_SERV_COMMAND_EVENTUCOFF): Event user control state release / unlock.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x052_eventucoff.h).
    /// </summary>
    public readonly ref struct S2C_0x052_EventUcOff
    {
        public const ushort PacketId = 0x052;

        public EventUcOffMode Mode { get; }
        public bool IsValid { get; }

        public S2C_0x052_EventUcOff(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 4)
            {
                Mode = EventUcOffMode.Standard;
                IsValid = false;
                return;
            }

            Mode = (EventUcOffMode)BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x056 (GP_SERV_COMMAND_MISSION): Mission log and storyline progression state.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x056_mission.h).
    /// </summary>
    public readonly ref struct S2C_0x056_Mission
    {
        public const ushort PacketId = 0x056;

        public uint Nation { get; }
        public uint NationMission { get; }
        public uint ExpansionRotZ { get; }
        public uint ExpansionCoP { get; }
        public uint ExpansionCoP2 { get; }
        public ushort ExpansionAddons { get; }
        public ushort TalesBeginning { get; }
        public uint ExpansionSoA { get; }
        public uint ExpansionRoV { get; }
        public ushort Port { get; }
        public bool IsValid { get; }

        public bool IsMainPort => Port == 0xFFFF;

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x056_Mission(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 34)
            {
                Nation = 0;
                NationMission = 0;
                ExpansionRotZ = 0;
                ExpansionCoP = 0;
                ExpansionCoP2 = 0;
                ExpansionAddons = 0;
                TalesBeginning = 0;
                ExpansionSoA = 0;
                ExpansionRoV = 0;
                Port = 0;
                IsValid = false;
                return;
            }

            Nation = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            NationMission = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            ExpansionRotZ = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(8, 4));
            ExpansionCoP = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(12, 4));
            ExpansionCoP2 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(16, 4));
            ExpansionAddons = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(20, 2));
            TalesBeginning = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(22, 2));
            ExpansionSoA = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(24, 4));
            ExpansionRoV = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(28, 4));
            Port = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(32, 2));
            IsValid = true;
        }

        public uint GetOtherData(int index)
        {
            if (!IsValid || index < 0 || index >= 8 || _payload.Length < (index + 1) * 4) return 0;
            return BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(index * 4, 4));
        }
    }

    /// <summary>
    /// S2C 0x055 (GP_SERV_COMMAND_SCENARIOITEM): Key item / scenario item possession bitmasks.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x055_scenarioitem.h).
    /// </summary>
    public readonly ref struct S2C_0x055_ScenarioItem
    {
        public const ushort PacketId = 0x055;

        public ushort TableIndex { get; }
        public bool IsValid { get; }

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x055_ScenarioItem(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 130)
            {
                TableIndex = 0;
                IsValid = false;
                return;
            }

            // 64 bytes (16 uint32) GetItemFlag + 64 bytes (16 uint32) LookItemFlag + uint16 TableIndex
            TableIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(128, 2));
            IsValid = true;
        }

        public uint GetAcquiredFlag(int dwordIndex)
        {
            if (!IsValid || dwordIndex < 0 || dwordIndex >= 16) return 0;
            return BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(dwordIndex * 4, 4));
        }

        public uint GetSeenFlag(int dwordIndex)
        {
            if (!IsValid || dwordIndex < 0 || dwordIndex >= 16) return 0;
            return BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(64 + (dwordIndex * 4), 4));
        }
    }

    /// <summary>
    /// S2C 0x02E (GP_SERV_COMMAND_OPENMOGMENU): Server notification to open Mog House menu.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x02e_openmogmenu.h).
    /// </summary>
    public readonly ref struct S2C_0x02E_OpenMogMenu
    {
        public const ushort PacketId = 0x02E;
        public bool IsValid { get; }

        public S2C_0x02E_OpenMogMenu(ReadOnlySpan<byte> payload)
        {
            IsValid = true; // Header-only packet
        }
    }

    /// <summary>
    /// S2C 0x096 (GP_SERV_COMMAND_MYROOM_ENTER): Entering another player's Mog House result.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x096_myroom_enter.h).
    /// </summary>
    public readonly ref struct S2C_0x096_MyRoomEnter
    {
        public const ushort PacketId = 0x096;
        public byte Result { get; }
        public bool IsValid { get; }

        public S2C_0x096_MyRoomEnter(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 1)
            {
                Result = 0;
                IsValid = false;
                return;
            }

            Result = payload[0];
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x0FA (GP_SERV_COMMAND_MYROOM_OPERATION): Mog house furniture / plant interaction response.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x0fa_myroom_operation.h).
    /// </summary>
    public readonly ref struct S2C_0x0FA_MyRoomOperation
    {
        public const ushort PacketId = 0x0FA;

        public ushort MyroomItemNo { get; }
        public MyRoomOperationResult Result { get; }
        public byte MyroomItemIndex { get; }
        public byte MyroomCategory { get; }
        public bool IsValid { get; }

        public S2C_0x0FA_MyRoomOperation(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 7)
            {
                MyroomItemNo = 0;
                Result = MyRoomOperationResult.Ok;
                MyroomItemIndex = 0;
                MyroomCategory = 0;
                IsValid = false;
                return;
            }

            MyroomItemNo = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0, 2));
            Result = (MyRoomOperationResult)payload[2];
            MyroomItemIndex = payload[5];
            MyroomCategory = payload[6];
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x08C (GP_SERV_COMMAND_MERIT): Merit points and merit allocation entries.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x08c_merit.h).
    /// </summary>
    public readonly ref struct S2C_0x08C_Merit
    {
        public const ushort PacketId = 0x08C;

        public ushort MeritCount { get; }
        public bool IsValid { get; }

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x08C_Merit(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 4)
            {
                MeritCount = 0;
                IsValid = false;
                return;
            }

            MeritCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0, 2));
            IsValid = true;
        }

        public (ushort Index, byte Next, byte Count) GetMeritEntry(int index)
        {
            if (!IsValid || index < 0) return (0, 0, 0);
            int offset = 4 + (index * 4);
            if (_payload.Length < offset + 4) return (0, 0, 0);

            ushort idx = BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(offset, 2));
            byte next = _payload[offset + 2];
            byte count = _payload[offset + 3];
            return (idx, next, count);
        }
    }

    /// <summary>
    /// S2C 0x08D (GP_SERV_COMMAND_JOB_POINTS): Job point categories and levels (64 entries).
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x08d_job_points.h).
    /// </summary>
    public readonly ref struct S2C_0x08D_JobPoints
    {
        public const ushort PacketId = 0x08D;
        public const int MaxEntries = 64;
        public bool IsValid { get; }

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x08D_JobPoints(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            IsValid = payload.Length >= 256; // 64 * 4 bytes
        }

        public (int Index, int JobNo, int Next, int Level) GetJobPoint(int entryIndex)
        {
            if (!IsValid || entryIndex < 0 || entryIndex >= MaxEntries) return (0, 0, 0, 0);
            int offset = entryIndex * 4;

            ushort word1 = BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(offset, 2));
            ushort word2 = BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(offset + 2, 2));

            int index = word1 & 0x1F;
            int jobNo = (word1 >> 5) & 0x7FF;
            int next = word2 & 0x3FF;
            int level = (word2 >> 10) & 0x3F;

            return (index, jobNo, next, level);
        }
    }

    /// <summary>
    /// S2C 0x111 (GP_SERV_COMMAND_ROE_ACTIVELOG): Records of Eminence active objectives (64 entries).
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x111_roe_activelog.h).
    /// </summary>
    public readonly ref struct S2C_0x111_RoeActiveLog
    {
        public const ushort PacketId = 0x111;
        public const int MaxEntries = 64;
        public bool IsValid { get; }

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x111_RoeActiveLog(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            IsValid = payload.Length >= 256; // 64 * 4 bytes
        }

        public (ushort ObjectiveId, uint Progress) GetActiveObjective(int index)
        {
            if (!IsValid || index < 0 || index >= MaxEntries) return (0, 0);
            uint raw = BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(index * 4, 4));
            ushort id = (ushort)(raw & 0x0FFF);
            uint count = (raw >> 12) & 0x000FFFFF;
            return (id, count);
        }
    }

    /// <summary>
    /// S2C 0x112 (GP_SERV_COMMAND_ROE_LOG): Records of Eminence completed records log chunk.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x112_roe_log.h).
    /// </summary>
    public readonly ref struct S2C_0x112_RoeLog
    {
        public const ushort PacketId = 0x112;

        public ushort Offset { get; }
        public bool IsValid { get; }

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x112_RoeLog(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 130)
            {
                Offset = 0;
                IsValid = false;
                return;
            }

            Offset = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(128, 2));
            IsValid = true;
        }

        public ReadOnlySpan<byte> GetData()
        {
            return IsValid ? _payload.Slice(0, 128) : ReadOnlySpan<byte>.Empty;
        }
    }

    /// <summary>
    /// S2C 0x05E (GP_SERV_COMMAND_CONQUEST): Conquest regional influence and Besieged overview.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x05e_conquest.h).
    /// </summary>
    public readonly ref struct S2C_0x05E_Conquest
    {
        public const ushort PacketId = 0x05E;

        public byte Balance { get; }
        public byte Alliance { get; }
        public uint ConquestPoints { get; }
        public uint ImperialStanding { get; }
        public bool IsValid { get; }

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x05E_Conquest(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 184)
            {
                Balance = 0;
                Alliance = 0;
                ConquestPoints = 0;
                ImperialStanding = 0;
                IsValid = false;
                return;
            }

            Balance = payload[0];
            Alliance = payload[1];
            ConquestPoints = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(144, 4));
            ImperialStanding = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(180, 4));
            IsValid = true;
        }

        public (byte Owner, byte Ranking) GetRegionInfo(int regionIndex)
        {
            if (!IsValid || regionIndex < 0 || regionIndex >= 27) return (0, 0);
            int offset = 22 + (regionIndex * 4);
            byte ranking = _payload[offset];
            byte owner = _payload[offset + 3];
            return (owner, ranking);
        }
    }

    /// <summary>
    /// S2C 0x115 (GP_SERV_COMMAND_FISH): Fishing mini-game battle parameters and stamina.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x115_fish.h).
    /// </summary>
    public readonly ref struct S2C_0x115_Fish
    {
        public const ushort PacketId = 0x115;

        public ushort Stamina { get; }
        public ushort ArrowDelay { get; }
        public ushort Regen { get; }
        public ushort MoveFrequency { get; }
        public ushort ArrowDamage { get; }
        public ushort ArrowRegen { get; }
        public ushort Time { get; }
        public byte AnglerSense { get; }
        public uint Intuition { get; }
        public bool IsValid { get; }

        public S2C_0x115_Fish(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 20)
            {
                Stamina = 0;
                ArrowDelay = 0;
                Regen = 0;
                MoveFrequency = 0;
                ArrowDamage = 0;
                ArrowRegen = 0;
                Time = 0;
                AnglerSense = 0;
                Intuition = 0;
                IsValid = false;
                return;
            }

            Stamina = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0, 2));
            ArrowDelay = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2, 2));
            Regen = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            MoveFrequency = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2));
            ArrowDamage = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2));
            ArrowRegen = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10, 2));
            Time = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(12, 2));
            AnglerSense = payload[14];
            Intuition = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(16, 4));
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x073 (GP_SERV_COMMAND_CHOCOBO_TOTEBOARD): Chocobo racing betting odds (28 quinella pairs).
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x073_chocobo_toteboard.h).
    /// </summary>
    public readonly ref struct S2C_0x073_ChocoboToteboard
    {
        public const ushort PacketId = 0x073;

        public uint SlotIndex { get; }
        public uint Ident { get; }
        public bool IsValid { get; }

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x073_ChocoboToteboard(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 68) // 12 header + 28 * 2 odds
            {
                SlotIndex = 0;
                Ident = 0;
                IsValid = false;
                return;
            }

            SlotIndex = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            Ident = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            IsValid = true;
        }

        public ushort GetOdds(int pairIndex)
        {
            if (!IsValid || pairIndex < 0 || pairIndex >= 28) return 0;
            return BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(12 + (pairIndex * 2), 2));
        }
    }

    /// <summary>
    /// S2C 0x110 (GP_SERV_COMMAND_UNITY): Unity Concord points, deeds, and leader standing.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x110_unity.h).
    /// </summary>
    public readonly ref struct S2C_0x110_Unity
    {
        public const ushort PacketId = 0x110;

        public uint Sparks { get; }
        public ushort Deeds { get; }
        public ushort Plaudits { get; }
        public byte RoEUnityShared { get; }
        public byte RoEUnityLeader { get; }
        public bool IsValid { get; }

        public S2C_0x110_Unity(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 10)
            {
                Sparks = 0;
                Deeds = 0;
                Plaudits = 0;
                RoEUnityShared = 0;
                RoEUnityLeader = 0;
                IsValid = false;
                return;
            }

            uint sparksRaw = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            Sparks = sparksRaw & 0x00FFFFFF;
            Deeds = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            Plaudits = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2));
            RoEUnityShared = payload[8];
            RoEUnityLeader = payload[9];
            IsValid = true;
        }
    }

    #endregion

    #region Outbound C2S Builders

    /// <summary>
    /// Outbound packet builder for Progression, Cutscenes, Mog House, Merits, and RoE packets.
    /// Protocol specifications referenced from LandSandBoat (https://github.com/LandSandBoat/server/tree/base/src/map/packets/c2s).
    /// </summary>
    public static class ProgressionPacketBuilder
    {
        /// <summary>
        /// Builds C2S 0x05B (GP_CLI_COMMAND_EVENTEND): Conclude event or submit selection.
        /// </summary>
        public static byte[] BuildEventEnd(
            uint uniqueNo,
            uint endPara,
            ushort actIndex,
            ushort mode,
            ushort eventNum,
            ushort eventPara,
            ushort sequenceId = 0)
        {
            // Size: 20 bytes (Header: 4, Payload: 16)
            var packet = new byte[20];
            ushort headerWord = (ushort)(0x05B | (5 << 9)); // 5 * 4 = 20 bytes
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), uniqueNo);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8, 4), endPara);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(12, 2), actIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(14, 2), mode);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(16, 2), eventNum);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(18, 2), eventPara);

            return packet;
        }

        /// <summary>
        /// Builds C2S 0x05C (GP_CLI_COMMAND_EVENTENDXZY): Conclude event with coordinate warp.
        /// </summary>
        public static byte[] BuildEventEndXzy(
            Vector3 position,
            uint uniqueNo,
            uint endPara,
            ushort eventNum,
            ushort eventPara,
            ushort actIndex,
            byte mode,
            sbyte dir,
            ushort sequenceId = 0)
        {
            // Size: 32 bytes (Header: 4, Payload: 28)
            var packet = new byte[32];
            ushort headerWord = (ushort)(0x05C | (8 << 9)); // 8 * 4 = 32 bytes
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            // Payload: float x, y, z (12 bytes)
            BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(4, 4), position.X);
            BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(8, 4), position.Y);
            BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(12, 4), position.Z);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16, 4), uniqueNo);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(20, 4), endPara);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(24, 2), eventNum);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(26, 2), eventPara);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(28, 2), actIndex);
            packet[30] = mode;
            packet[31] = (byte)dir;

            return packet;
        }

        /// <summary>
        /// Builds C2S 0x064 (GP_CLI_COMMAND_SCENARIOITEM): Mark scenario item / key item as read.
        /// </summary>
        public static byte[] BuildScenarioItemRead(
            uint uniqueNo,
            ushort actIndex,
            ushort tableIndex,
            ReadOnlySpan<uint> lookItemFlags,
            ushort sequenceId = 0)
        {
            // Size: 76 bytes (Header: 4, Payload: 72)
            var packet = new byte[76];
            ushort headerWord = (ushort)(0x064 | (19 << 9)); // 19 * 4 = 76 bytes
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), uniqueNo);
            for (int i = 0; i < 16; i++)
            {
                uint val = i < lookItemFlags.Length ? lookItemFlags[i] : 0;
                BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8 + (i * 4), 4), val);
            }
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(72, 2), actIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(74, 2), tableIndex);

            return packet;
        }

        /// <summary>
        /// Builds C2S 0x100 (GP_CLI_COMMAND_MYROOM_JOB): Change Main/Sub Job in Mog House.
        /// </summary>
        public static byte[] BuildMyRoomJob(byte mainJob, byte subJob, ushort sequenceId = 0)
        {
            // Size: 8 bytes (Header: 4, Payload: 4)
            var packet = new byte[8];
            ushort headerWord = (ushort)(0x100 | (2 << 9)); // 2 * 4 = 8 bytes
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            packet[4] = mainJob;
            packet[5] = subJob;
            packet[6] = 0;
            packet[7] = 0;

            return packet;
        }

        /// <summary>
        /// Builds C2S 0x0CB (GP_CLI_COMMAND_MYROOM_IS): Mog House operations (open, remodel, patio).
        /// </summary>
        public static byte[] BuildMyRoomIs(MyRoomIsKind kind, byte param1, ushort param2, ushort sequenceId = 0)
        {
            // Size: 8 bytes (Header: 4, Payload: 4)
            var packet = new byte[8];
            ushort headerWord = (ushort)(0x0CB | (2 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            packet[4] = (byte)kind;
            packet[5] = param1;
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6, 2), param2);

            return packet;
        }

        /// <summary>
        /// Builds C2S 0x0FA (GP_CLI_COMMAND_MYROOM_LAYOUT): Rearrange furniture in Mog House.
        /// </summary>
        public static byte[] BuildMyRoomLayout(
            ushort itemNo,
            byte itemIndex,
            byte category,
            byte floor,
            byte x,
            byte y,
            byte z,
            byte rotation,
            ushort sequenceId = 0)
        {
            // Size: 16 bytes (Header: 4, Payload: 12)
            var packet = new byte[16];
            ushort headerWord = (ushort)(0x0FA | (4 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), itemNo);
            packet[6] = itemIndex;
            packet[7] = category;
            packet[8] = floor;
            packet[9] = x;
            packet[10] = y;
            packet[11] = z;
            packet[12] = rotation;

            return packet;
        }

        /// <summary>
        /// Builds C2S 0x0BE (GP_CLI_COMMAND_MERITS): Spend merit points or toggle EXP/Limit mode.
        /// </summary>
        public static byte[] BuildMerits(MeritCommandKind kind, byte param1, ushort param2, uint param3 = 0, ushort sequenceId = 0)
        {
            // Size: 12 bytes (Header: 4, Payload: 8)
            var packet = new byte[12];
            ushort headerWord = (ushort)(0x0BE | (3 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            packet[4] = (byte)kind;
            packet[5] = param1;
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6, 2), param2);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8, 4), param3);

            return packet;
        }

        /// <summary>
        /// Builds C2S 0x0BF (GP_CLI_COMMAND_JOB_POINTS_SPEND): Spend job points.
        /// </summary>
        public static byte[] BuildJobPointsSpend(ushort index, ushort sequenceId = 0)
        {
            // Size: 8 bytes (Header: 4, Payload: 4)
            var packet = new byte[8];
            ushort headerWord = (ushort)(0x0BF | (2 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), index);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6, 2), 0);

            return packet;
        }

        /// <summary>
        /// Builds C2S 0x0C0 (GP_CLI_COMMAND_JOB_POINTS_REQ): Request job points info.
        /// </summary>
        public static byte[] BuildJobPointsReq(ushort sequenceId = 0)
        {
            // Size: 4 bytes (Header only)
            var packet = new byte[4];
            ushort headerWord = (ushort)(0x0C0 | (1 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x10C (GP_CLI_COMMAND_ROE_START): Start a Records of Eminence objective.
        /// </summary>
        public static byte[] BuildRoeStart(ushort objectiveId, ushort sequenceId = 0)
        {
            // Size: 8 bytes (Header: 4, Payload: 4)
            var packet = new byte[8];
            ushort headerWord = (ushort)(0x10C | (2 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), objectiveId);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6, 2), 0);

            return packet;
        }

        /// <summary>
        /// Builds C2S 0x10D (GP_CLI_COMMAND_ROE_REMOVE): Remove an active Records of Eminence objective.
        /// </summary>
        public static byte[] BuildRoeRemove(ushort objectiveId, ushort sequenceId = 0)
        {
            // Size: 8 bytes (Header: 4, Payload: 4)
            var packet = new byte[8];
            ushort headerWord = (ushort)(0x10D | (2 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), objectiveId);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6, 2), 0);

            return packet;
        }

        /// <summary>
        /// Builds C2S 0x10E (GP_CLI_COMMAND_ROE_CLAIM): Claim a completed Records of Eminence objective reward.
        /// </summary>
        public static byte[] BuildRoeClaim(ushort objectiveId, ushort sequenceId = 0)
        {
            // Size: 8 bytes (Header: 4, Payload: 4)
            var packet = new byte[8];
            ushort headerWord = (ushort)(0x10E | (2 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), objectiveId);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6, 2), 0);

            return packet;
        }

        /// <summary>
        /// Builds C2S 0x05A (GP_CLI_COMMAND_REQCONQUEST): Request Conquest data.
        /// </summary>
        public static byte[] BuildReqConquest(ushort sequenceId = 0)
        {
            var packet = new byte[4];
            ushort headerWord = (ushort)(0x05A | (1 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x066 / 0x110 (GP_CLI_COMMAND_FISHING_2): Fishing mini-game interaction.
        /// </summary>
        public static byte[] BuildFishingAction(
            uint uniqueNo,
            int para,
            ushort actIndex,
            FishingActionMode mode,
            int para2 = 0,
            ushort sequenceId = 0)
        {
            // Size: 20 bytes (Header: 4, Payload: 16)
            var packet = new byte[20];
            ushort headerWord = (ushort)(0x110 | (5 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), uniqueNo);
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8, 4), para);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(12, 2), actIndex);
            packet[14] = (byte)mode;
            packet[15] = 0;
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(16, 4), para2);

            return packet;
        }

        /// <summary>
        /// Builds C2S 0x09B (GP_CLI_COMMAND_CHOCOBO_RACE_REQ): Chocobo race request.
        /// </summary>
        public static byte[] BuildChocoboRaceReq(uint param, ChocoboRaceReqKind kind, ushort sequenceId = 0)
        {
            // Size: 12 bytes (Header: 4, Payload: 8)
            var packet = new byte[12];
            ushort headerWord = (ushort)(0x09B | (3 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), param);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8, 4), (uint)kind);

            return packet;
        }

        /// <summary>
        /// Builds C2S 0x116 (GP_CLI_COMMAND_UNITY_MENU): Open Unity Concord menu.
        /// </summary>
        public static byte[] BuildUnityMenu(bool open, ushort sequenceId = 0)
        {
            var packet = new byte[8];
            ushort headerWord = (ushort)(0x116 | (2 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), open ? 1u : 0u);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x117 (GP_CLI_COMMAND_UNITY_QUEST): Request Unity quests.
        /// </summary>
        public static byte[] BuildUnityQuest(ushort sequenceId = 0)
        {
            var packet = new byte[8];
            ushort headerWord = (ushort)(0x117 | (2 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), 0);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x118 (GP_CLI_COMMAND_UNITY_TOGGLE): Toggle Unity chat.
        /// </summary>
        public static byte[] BuildUnityToggle(bool active, ushort sequenceId = 0)
        {
            var packet = new byte[8];
            ushort headerWord = (ushort)(0x118 | (2 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);
            packet[4] = active ? (byte)1 : (byte)0;
            return packet;
        }
    }

    #endregion
}
