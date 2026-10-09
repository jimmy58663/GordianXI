// src/Gordian.Core/Network/Packets/SchedulerPackets.cs
using System;
using System.Buffers.Binary;
using System.Text;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// Scheduler keys (FourCC) that LandSandBoat sends in S2C 0x038. A scheduler is a script from the actor's own animation
    /// DAT (or the zone's, for 0x039) that the client looks up by these four characters.
    /// Referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/enums/four_cc.h).
    /// </summary>
    public static class SchedulerKeys
    {
        /// <summary>The entity fades out and disappears (despawn, battlefield exit).</summary>
        public const string FadeOut = "kesu";

        /// <summary>The entity shows a sweating animation (fishing).</summary>
        public const string Sweating = "hitl";

        /// <summary>Basic attack.</summary>
        public const string BasicAttack = "atk0";

        /// <summary>Ranged attack start, interrupt and finish.</summary>
        public const string RangedStart = "calg", RangedInterrupt = "splg", RangedFinish = "shlg";

        /// <summary>Item use and its interrupt.</summary>
        public const string ItemUse = "cait", ItemInterrupt = "spit";

        /// <summary>Weapon or monster skill use and its interrupt.</summary>
        public const string SkillUse = "cate", SkillInterrupt = "spte";
    }

    /// <summary>
    /// The <c>type</c> of S2C 0x03A: which family of effect scripts <c>fileNum</c> belongs to. XiPackets warns the
    /// list is not guaranteed, because the file number can select a different kind (a file number of 228 or above plays
    /// a Chocobo racing overlay whatever the type is).
    /// Referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x003A)
    /// and LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x03a_magicschedulor.h).
    /// </summary>
    public enum MagicSchedulerType : byte
    {
        CastSpell = 0x00,
        UseItem = 0x01,
        Ability = 0x02,
        /// <summary>Event related: misc event animations, banner announcements.</summary>
        Event1 = 0x03,
        /// <summary>Event related: misc event animations, NPC weapon skills.</summary>
        Event2 = 0x04,
        /// <summary>Unknown; has misc ability animations and banner announcements.</summary>
        Unknown5 = 0x05,
        WeaponSkill = 0x06,
        Unknown7 = 0x07,
        /// <summary>Unknown; has misc ability animations and banner announcements.</summary>
        Unknown8 = 0x08,
        MonsterSkill = 0x09,
        /// <summary>Unknown; has misc warping animations.</summary>
        Unknown10 = 0x0A,
        /// <summary>Unknown; has misc banner announcements.</summary>
        Unknown11 = 0x0B,
        /// <summary>Unknown; has misc casting animations.</summary>
        Unknown12 = 0x0C
    }

    /// <summary>
    /// S2C 0x038 (GP_SERV_COMMAND_SCHEDULOR): plays the scheduler named by a FourCC on the caster actor, aimed at the
    /// target actor. LandSandBoat sends it for despawn fade-outs (<c>kesu</c>), fishing (<c>hitl</c>), Trust and gambit
    /// animations and Lua <c>entityAnimationPacket</c> (NPC emotes, cutscene cues). The layout matches S2C 0x039.
    /// Payload (after the 4-byte header): caster server id (+0x00), target server id (+0x04), the FourCC (+0x08), caster
    /// and target indexes (+0x0C, +0x0E).
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x0038)
    /// and LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x038_schedulor.h).
    /// </summary>
    public readonly ref struct S2C_0x038_Schedulor
    {
        public const ushort PacketId = 0x038;
        private const int MinPayload = 16;

        public uint CasterServerId { get; }
        public uint TargetServerId { get; }
        /// <summary>The scheduler key's four bytes as a little-endian integer (LandSandBoat's <c>FourCC</c> value).</summary>
        public uint RoutineId { get; }
        /// <summary>The scheduler key as text; empty when a byte is not printable ASCII (trailing NULs are dropped).</summary>
        public string Routine { get; }
        public ushort CasterIndex { get; }
        public ushort TargetIndex { get; }
        public bool IsValid { get; }

        public S2C_0x038_Schedulor(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < MinPayload)
            {
                CasterServerId = TargetServerId = RoutineId = 0;
                Routine = string.Empty;
                CasterIndex = TargetIndex = 0;
                IsValid = false;
                return;
            }

            CasterServerId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            TargetServerId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            RoutineId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(8, 4));
            Routine = SchedulerText.ReadFourCc(payload.Slice(8, 4));
            CasterIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(12, 2));
            TargetIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(14, 2));
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x03A (GP_SERV_COMMAND_MAGICSCHEDULOR): plays a numbered effect script (<c>fileNum</c>) from the effect DATs,
    /// outside a 0x028 action packet, e.g. LandSandBoat's Lua <c>independentAnimation</c> (little hearts, 251 with type 4).
    /// Payload (after the 4-byte header): caster server id (+0x00), target server id (+0x04), caster and target indexes
    /// (+0x08, +0x0A), file number (+0x0C), type (+0x0E).
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x003A)
    /// and LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x03a_magicschedulor.h).
    /// </summary>
    public readonly ref struct S2C_0x03A_MagicSchedulor
    {
        public const ushort PacketId = 0x03A;
        private const int MinPayload = 15;

        public uint CasterServerId { get; }
        public uint TargetServerId { get; }
        public ushort CasterIndex { get; }
        public ushort TargetIndex { get; }
        /// <summary>The effect script's file number.</summary>
        public ushort FileNumber { get; }
        /// <summary>The raw type byte; XiPackets says the client accepts 0x00 to 0x0C.</summary>
        public byte TypeId { get; }
        public MagicSchedulerType Type => (MagicSchedulerType)TypeId;
        public bool IsValid { get; }

        public S2C_0x03A_MagicSchedulor(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < MinPayload)
            {
                CasterServerId = TargetServerId = 0;
                CasterIndex = TargetIndex = FileNumber = 0;
                TypeId = 0;
                IsValid = false;
                return;
            }

            CasterServerId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            TargetServerId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            CasterIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2));
            TargetIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10, 2));
            FileNumber = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(12, 2));
            TypeId = payload[14];
            IsValid = true;
        }
    }

    internal static class SchedulerText
    {
        /// <summary>Four printable ASCII bytes (NUL padded) as text; empty when any byte is out of range.</summary>
        public static string ReadFourCc(ReadOnlySpan<byte> span)
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
}
