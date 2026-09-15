// src/Gordian.Core/Network/Packets/CombatPackets.cs
// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server)
// and Atom0s XiPackets research (https://github.com/atom0s/XiPackets).

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;

namespace Gordian.Core.Network.Packets
{
    #region Enums

    /// <summary>
    /// Action category transmitted in S2C 0x028 (cmd_no).
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/enums/action/category.h).
    /// </summary>
    public enum ActionCategory : byte
    {
        None = 0,
        BasicAttack = 1,
        RangedFinish = 2,
        SkillFinish = 3,
        MagicFinish = 4,
        ItemFinish = 5,
        AbilityFinish = 6,
        SkillStart = 7,
        MagicStart = 8,
        ItemStart = 9,
        AbilityStart = 10,
        MobSkillFinish = 11,
        RangedStart = 12,
        PetSkillFinish = 13,
        Dancer = 14,
        RuneFencer = 15
    }

    /// <summary>
    /// Combat hit resolution transmitted in S2C 0x028 (result.miss).
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/enums/action/resolution.h).
    /// </summary>
    public enum ActionResolution : byte
    {
        Hit = 0,
        Miss = 1,
        Guard = 2,
        Parry = 3,
        Block = 4
    }

    /// <summary>
    /// Spikes and reaction effect transmitted in S2C 0x028 (result.react_kind).
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/enums/action/react_kind.h).
    /// </summary>
    public enum ActionReactKind : byte
    {
        None = 0,
        BlazeSpikes = 1,
        IceSpikes = 2,
        DreadSpikes = 3,
        CurseSpikes = 4,
        ShockSpikes = 5,
        ReprisalSpikes = 6,
        WindSpikes = 7,
        EarthSpikes = 8,
        WaterSpikes = 9,
        DeathSpikes = 10,
        Counter = 63
    }

    /// <summary>
    /// Additional effect kind transmitted in S2C 0x028 (result.proc_kind).
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/enums/action/proc_kind.h).
    /// </summary>
    public enum ActionProcAddEffect : byte
    {
        None = 0,
        FireDamage = 1,
        IceDamage = 2,
        WindDamage = 3,
        EarthDamage = 4,
        LightningDamage = 5,
        WaterDamage = 6,
        LightDamage = 7,
        DarkDamage = 8,
        Sleep = 9,
        Poison = 10,
        Paralyze = 11,
        Blind = 12,
        Silence = 13,
        Petrify = 14,
        Plague = 15,
        Stun = 16,
        Curse = 17,
        Weaken = 18,
        Death = 19,
        Shield = 20,
        HpDrain = 21,
        MpDrain = 22
    }

    /// <summary>
    /// Client action request command identifier transmitted in C2S 0x01A.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x01a_action.h).
    /// </summary>
    public enum CliActionId : ushort
    {
        Talk = 0x00,
        Attack = 0x02,
        CastMagic = 0x03,
        AttackOff = 0x04,
        Help = 0x05,
        Weaponskill = 0x07,
        JobAbility = 0x09,
        HomepointMenu = 0x0B,
        Assist = 0x0C,
        RaiseMenu = 0x0D,
        Fish = 0x0E,
        ChangeTarget = 0x0F,
        Shoot = 0x10,
        ChocoboDig = 0x11,
        Dismount = 0x12,
        TractorMenu = 0x13,
        SendResRdy = 0x14,
        Quarry = 0x15,
        Sprint = 0x16,
        Scout = 0x17,
        Blockaid = 0x18,
        MonsterSkill = 0x19,
        Mount = 0x1A
    }

    /// <summary>
    /// Standard FFXI emote identifiers transmitted in C2S 0x05D.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x05d_motion.h).
    /// </summary>
    public enum EmoteId : byte
    {
        None = 0,
        Cheer = 1,
        Clap = 2,
        Wave = 3,
        Bow = 4,
        Point = 5,
        Salute = 6,
        Kneel = 7,
        Laugh = 8,
        Cry = 9,
        No = 10,
        Yes = 11,
        Surprised = 12,
        Blush = 13,
        Sit = 14,
        Farewell = 15,
        Joy = 16,
        Comfort = 17,
        Panic = 18,
        Disgusted = 19,
        Angry = 20,
        Shocked = 21
    }

    /// <summary>
    /// Crafting synthesis animation and result effect transmitted in S2C 0x030.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x030_effect.h).
    /// </summary>
    public enum SynthesisEffect : short
    {
        None = 0,
        SynthesisSuccess = 1,
        SynthesisFailure = 2,
        SynthesisRune = 3
    }

    #endregion

    #region Combat Action Models (Zero-Allocation & Managed Records)

    /// <summary>
    /// Sub-result for a single target within an action packet.
    /// </summary>
    public readonly struct CombatActionResult
    {
        public ActionResolution Resolution { get; init; }
        public byte Kind { get; init; }
        public ushort Animation { get; init; }
        public byte Info { get; init; }
        public byte Scale { get; init; }
        public int Param { get; init; }
        public ushort MessageId { get; init; }
        public uint Modifier { get; init; }
        public bool HasProc { get; init; }
        public ActionProcAddEffect ProcKind { get; init; }
        public byte ProcInfo { get; init; }
        public int ProcParam { get; init; }
        public ushort ProcMessageId { get; init; }
        public bool HasReaction { get; init; }
        public ActionReactKind ReactionKind { get; init; }
        public byte ReactionInfo { get; init; }
        public int ReactionParam { get; init; }
        public ushort ReactionMessageId { get; init; }
    }

    /// <summary>
    /// Managed target record for long-term storage, logging, and HUD consumption.
    /// </summary>
    public sealed class CombatActionTargetRecord
    {
        public uint TargetId { get; init; }
        public List<CombatActionResult> Results { get; init; } = new();
    }

    /// <summary>
    /// Managed action record snapshot for event subscribers.
    /// </summary>
    public sealed class CombatActionRecord
    {
        public uint ActorId { get; init; }
        public ActionCategory Category { get; init; }
        public uint ActionId { get; init; }
        public uint Recast { get; init; }
        public List<CombatActionTargetRecord> Targets { get; init; } = new();
    }

    /// <summary>
    /// Managed battle message record snapshot.
    /// </summary>
    public sealed class CombatMessageRecord
    {
        public uint CasterId { get; init; }
        public uint TargetId { get; init; }
        public ushort CasterIndex { get; init; }
        public ushort TargetIndex { get; init; }
        public uint Param { get; init; }
        public uint Value { get; init; }
        public ushort MessageId { get; init; }
        public byte MessageType { get; init; }
        public bool IsEndOfCombat { get; init; }
    }

    /// <summary>
    /// Single ability recast timer entry.
    /// </summary>
    public readonly struct AbilityRecastEntry
    {
        public ushort TimerSeconds { get; }
        public byte Calc1 { get; }
        public byte TimerId { get; }
        public ushort Calc2 { get; }

        public AbilityRecastEntry(ushort timer, byte calc1, byte timerId, ushort calc2)
        {
            TimerSeconds = timer;
            Calc1 = calc1;
            TimerId = timerId;
            Calc2 = calc2;
        }
    }

    #endregion

    #region Inbound S2C Decoders (readonly ref struct)

    /// <summary>
    /// S2C 0x028 (GP_SERV_COMMAND_BATTLE2): Complex bit-packed combat action packet.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x028_battle2.cpp)
    /// and Atom0s XiPackets research (https://github.com/atom0s/XiPackets/tree/main/world/server/0x0028).
    /// </summary>
    public readonly ref struct S2C_0x028_CombatAction
    {
        public const ushort PacketId = 0x028;

        private readonly ReadOnlySpan<byte> _streamData;
        private readonly int _targetsBitOffset;

        public uint ActorId { get; }
        public byte TargetCount { get; }
        public byte ResSum { get; }
        public ActionCategory Category { get; }
        public uint ActionId { get; }
        public uint Recast { get; }
        public bool IsValid { get; }

        public S2C_0x028_CombatAction(ReadOnlySpan<byte> payload)
        {
            // Minimum payload: workSize (1B) + 14 bytes header = 15 bytes
            if (payload.Length < 15)
            {
                _streamData = ReadOnlySpan<byte>.Empty;
                _targetsBitOffset = 0;
                ActorId = 0;
                TargetCount = 0;
                ResSum = 0;
                Category = ActionCategory.None;
                ActionId = 0;
                Recast = 0;
                IsValid = false;
                return;
            }

            // Payload byte 0 is workSize. Bitstream starts at payload byte 1.
            _streamData = payload.Slice(1);
            var reader = new BitStreamReader(_streamData);

            ActorId = reader.ReadUInt32(32);
            TargetCount = reader.ReadByte(6);
            ResSum = reader.ReadByte(4);
            Category = (ActionCategory)reader.ReadByte(4);
            ActionId = reader.ReadUInt32(32);
            Recast = reader.ReadUInt32(32);

            _targetsBitOffset = reader.BitOffset;
            IsValid = true;
        }

        /// <summary>
        /// Reads a single target and all its sub-results at the specified index without heap allocations.
        /// </summary>
        public bool TryGetTarget(int targetIndex, out uint targetId, Span<CombatActionResult> resultsBuffer, out int resultsCount)
        {
            targetId = 0;
            resultsCount = 0;

            if (!IsValid || targetIndex < 0 || targetIndex >= TargetCount)
            {
                return false;
            }

            var reader = new BitStreamReader(_streamData, _targetsBitOffset);

            for (int t = 0; t <= targetIndex; t++)
            {
                uint curTargetId = reader.ReadUInt32(32);
                byte curResultCount = reader.ReadByte(4);

                if (t == targetIndex)
                {
                    targetId = curTargetId;
                    int maxToRead = Math.Min((int)curResultCount, resultsBuffer.Length);
                    for (int r = 0; r < curResultCount; r++)
                    {
                        var res = ReadResult(ref reader);
                        if (r < maxToRead)
                        {
                            resultsBuffer[r] = res;
                        }
                    }
                    resultsCount = maxToRead;
                    return true;
                }
                else
                {
                    for (int r = 0; r < curResultCount; r++)
                    {
                        SkipResult(ref reader);
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Materializes the full combat action into a managed record for UI logging or script event buses.
        /// </summary>
        public CombatActionRecord ToRecord()
        {
            var record = new CombatActionRecord
            {
                ActorId = ActorId,
                Category = Category,
                ActionId = ActionId,
                Recast = Recast
            };

            if (!IsValid || TargetCount == 0)
            {
                return record;
            }

            var reader = new BitStreamReader(_streamData, _targetsBitOffset);
            Span<CombatActionResult> buffer = stackalloc CombatActionResult[8];

            for (int t = 0; t < TargetCount; t++)
            {
                uint targetId = reader.ReadUInt32(32);
                byte resultCount = reader.ReadByte(4);

                var targetRecord = new CombatActionTargetRecord
                {
                    TargetId = targetId
                };

                for (int r = 0; r < resultCount; r++)
                {
                    targetRecord.Results.Add(ReadResult(ref reader));
                }

                record.Targets.Add(targetRecord);
            }

            return record;
        }

        private static CombatActionResult ReadResult(ref BitStreamReader reader)
        {
            var res = new CombatActionResult
            {
                Resolution = (ActionResolution)reader.ReadByte(3),
                Kind = reader.ReadByte(2),
                Animation = reader.ReadUInt16(12),
                Info = reader.ReadByte(5),
                Scale = reader.ReadByte(5),
                Param = (int)reader.ReadUInt32(17),
                MessageId = reader.ReadUInt16(10),
                Modifier = reader.ReadUInt32(31)
            };

            bool hasProc = reader.ReadBool();
            if (hasProc)
            {
                res = res with
                {
                    HasProc = true,
                    ProcKind = (ActionProcAddEffect)reader.ReadByte(6),
                    ProcInfo = reader.ReadByte(4),
                    ProcParam = (int)reader.ReadUInt32(17),
                    ProcMessageId = reader.ReadUInt16(10)
                };
            }

            bool hasReact = reader.ReadBool();
            if (hasReact)
            {
                res = res with
                {
                    HasReaction = true,
                    ReactionKind = (ActionReactKind)reader.ReadByte(6),
                    ReactionInfo = reader.ReadByte(4),
                    ReactionParam = (int)reader.ReadUInt32(14),
                    ReactionMessageId = reader.ReadUInt16(10)
                };
            }

            return res;
        }

        private static void SkipResult(ref BitStreamReader reader)
        {
            reader.ReadBits(3 + 2 + 12 + 5 + 5 + 17 + 10 + 31);
            if (reader.ReadBool())
            {
                reader.ReadBits(6 + 4 + 17 + 10);
            }
            if (reader.ReadBool())
            {
                reader.ReadBits(6 + 4 + 14 + 10);
            }
        }
    }

    /// <summary>
    /// S2C 0x029 (GP_SERV_COMMAND_BATTLE_MESSAGE): Standard combat message notification.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x029_battle_message.h).
    /// </summary>
    public readonly ref struct S2C_0x029_BattleMessage
    {
        public const ushort PacketId = 0x029;

        public uint UniqueNoCas { get; }
        public uint UniqueNoTar { get; }
        public uint Data { get; }
        public uint Data2 { get; }
        public ushort ActIndexCas { get; }
        public ushort ActIndexTar { get; }
        public ushort MessageNum { get; }
        public byte Type { get; }
        public bool IsValid { get; }

        public S2C_0x029_BattleMessage(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 24)
            {
                UniqueNoCas = 0;
                UniqueNoTar = 0;
                Data = 0;
                Data2 = 0;
                ActIndexCas = 0;
                ActIndexTar = 0;
                MessageNum = 0;
                Type = 0;
                IsValid = false;
                return;
            }

            UniqueNoCas = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            UniqueNoTar = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            Data = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(8, 4));
            Data2 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(12, 4));
            ActIndexCas = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(16, 2));
            ActIndexTar = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(18, 2));
            MessageNum = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(20, 2));
            Type = payload[22];
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x02D (GP_SERV_COMMAND_BATTLE_MESSAGE2): End-of-combat / progression combat message.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x02d_battle_message2.h).
    /// </summary>
    public readonly ref struct S2C_0x02D_BattleMessage2
    {
        public const ushort PacketId = 0x02D;

        public uint UniqueNoCas { get; }
        public uint UniqueNoTar { get; }
        public ushort ActIndexCas { get; }
        public ushort ActIndexTar { get; }
        public uint Data { get; }
        public uint Data2 { get; }
        public ushort MessageNum { get; }
        public byte Type { get; }
        public bool IsValid { get; }

        public S2C_0x02D_BattleMessage2(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 24)
            {
                UniqueNoCas = 0;
                UniqueNoTar = 0;
                ActIndexCas = 0;
                ActIndexTar = 0;
                Data = 0;
                Data2 = 0;
                MessageNum = 0;
                Type = 0;
                IsValid = false;
                return;
            }

            UniqueNoCas = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            UniqueNoTar = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            ActIndexCas = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2));
            ActIndexTar = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10, 2));
            Data = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(12, 4));
            Data2 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(16, 4));
            MessageNum = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(20, 2));
            Type = payload[22];
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x030 (GP_SERV_COMMAND_EFFECT): Entity status change or crafting synthesis animation.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x030_effect.h).
    /// </summary>
    public readonly ref struct S2C_0x030_Effect
    {
        public const ushort PacketId = 0x030;

        public uint UniqueNo { get; }
        public ushort ActIndex { get; }
        public SynthesisEffect EffectNum { get; }
        public byte Type { get; }
        public byte Status { get; }
        public ushort Timer { get; }
        public bool IsValid { get; }

        public S2C_0x030_Effect(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 12)
            {
                UniqueNo = 0;
                ActIndex = 0;
                EffectNum = SynthesisEffect.None;
                Type = 0;
                Status = 0;
                Timer = 0;
                IsValid = false;
                return;
            }

            UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            ActIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            EffectNum = (SynthesisEffect)BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(6, 2));
            Type = payload[8];
            Status = payload[9];
            Timer = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10, 2));
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x0AA (GP_SERV_COMMAND_MAGIC_DATA): Character learned magic spell bitmask (128 bytes = 1024 bits).
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x0aa_magic_data.h).
    /// </summary>
    public readonly ref struct S2C_0x0AA_MagicData
    {
        public const ushort PacketId = 0x0AA;

        public ReadOnlySpan<byte> MagicDataTbl { get; }
        public bool IsValid { get; }

        public S2C_0x0AA_MagicData(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 128)
            {
                MagicDataTbl = ReadOnlySpan<byte>.Empty;
                IsValid = false;
                return;
            }

            MagicDataTbl = payload.Slice(0, 128);
            IsValid = true;
        }

        public bool IsSpellLearned(ushort spellId)
        {
            if (!IsValid) return false;
            int byteIndex = spellId >> 3;
            int bitIndex = spellId & 7;
            if (byteIndex < 0 || byteIndex >= MagicDataTbl.Length) return false;
            return (MagicDataTbl[byteIndex] & (1 << bitIndex)) != 0;
        }
    }

    /// <summary>
    /// S2C 0x0AC (GP_SERV_COMMAND_COMMAND_DATA): Character weaponskill, ability, pet ability, and trait bitmasks (224 bytes).
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x0ac_command_data.h).
    /// </summary>
    public readonly ref struct S2C_0x0AC_CommandData
    {
        public const ushort PacketId = 0x0AC;

        public ReadOnlySpan<byte> WeaponSkills { get; }
        public ReadOnlySpan<byte> JobAbilities { get; }
        public ReadOnlySpan<byte> PetAbilities { get; }
        public ReadOnlySpan<byte> Traits { get; }
        public bool IsValid { get; }

        public S2C_0x0AC_CommandData(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 224)
            {
                WeaponSkills = ReadOnlySpan<byte>.Empty;
                JobAbilities = ReadOnlySpan<byte>.Empty;
                PetAbilities = ReadOnlySpan<byte>.Empty;
                Traits = ReadOnlySpan<byte>.Empty;
                IsValid = false;
                return;
            }

            WeaponSkills = payload.Slice(0, 64);
            JobAbilities = payload.Slice(64, 64);
            PetAbilities = payload.Slice(128, 64);
            Traits = payload.Slice(192, 32);
            IsValid = true;
        }

        public bool HasWeaponSkill(ushort wsId) => HasBit(WeaponSkills, wsId);
        public bool HasJobAbility(ushort abilityId) => HasBit(JobAbilities, abilityId);
        public bool HasPetAbility(ushort petAbilId) => HasBit(PetAbilities, petAbilId);
        public bool HasTrait(ushort traitId) => HasBit(Traits, traitId);

        private static bool HasBit(ReadOnlySpan<byte> span, ushort id)
        {
            int byteIdx = id >> 3;
            int bitIdx = id & 7;
            if (byteIdx < 0 || byteIdx >= span.Length) return false;
            return (span[byteIdx] & (1 << bitIdx)) != 0;
        }
    }

    /// <summary>
    /// S2C 0x119 (GP_SERV_COMMAND_ABIL_RECAST): Recast timers for 31 abilities and mount recast.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x119_abil_recast.h).
    /// </summary>
    public readonly ref struct S2C_0x119_AbilRecast
    {
        public const ushort PacketId = 0x119;

        private readonly ReadOnlySpan<byte> _payload;
        public uint MountRecast { get; }
        public uint MountRecastId { get; }
        public bool IsValid { get; }

        public S2C_0x119_AbilRecast(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 256)
            {
                _payload = ReadOnlySpan<byte>.Empty;
                MountRecast = 0;
                MountRecastId = 0;
                IsValid = false;
                return;
            }

            _payload = payload.Slice(0, 256);
            MountRecast = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(248, 4));
            MountRecastId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(252, 4));
            IsValid = true;
        }

        public AbilityRecastEntry GetTimer(int index)
        {
            if (!IsValid || index < 0 || index >= 31) return default;
            int offset = index * 8;
            ushort timer = BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(offset, 2));
            byte calc1 = _payload[offset + 2];
            byte timerId = _payload[offset + 3];
            ushort calc2 = BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(offset + 4, 2));
            return new AbilityRecastEntry(timer, calc1, timerId, calc2);
        }
    }

    #endregion

    #region Outbound C2S Packet Builders

    /// <summary>
    /// Zero-allocation packet builders for client-to-server combat and action requests.
    /// </summary>
    public static class CombatPacketBuilder
    {
        /// <summary>
        /// C2S 0x01A: Initiates basic melee auto-attack on target.
        /// </summary>
        public static int BuildAttackRequest(Span<byte> destination, ushort sequenceId, uint targetId, ushort targetIndex)
        {
            PacketHeader.Write(destination, 0x01A, 7, sequenceId);
            var payload = destination.Slice(4, 24);
            payload.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), targetId);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(4, 2), targetIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(6, 2), (ushort)CliActionId.Attack);
            return 28;
        }

        /// <summary>
        /// C2S 0x01A: Disengages from combat auto-attack.
        /// </summary>
        public static int BuildAttackOffRequest(Span<byte> destination, ushort sequenceId, uint targetId, ushort targetIndex)
        {
            PacketHeader.Write(destination, 0x01A, 7, sequenceId);
            var payload = destination.Slice(4, 24);
            payload.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), targetId);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(4, 2), targetIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(6, 2), (ushort)CliActionId.AttackOff);
            return 28;
        }

        /// <summary>
        /// C2S 0x01A: Requests casting of a magic spell.
        /// </summary>
        public static int BuildCastMagicRequest(Span<byte> destination, ushort sequenceId, uint targetId, ushort targetIndex, ushort spellId, Vector3 targetPos = default)
        {
            PacketHeader.Write(destination, 0x01A, 7, sequenceId);
            var payload = destination.Slice(4, 24);
            payload.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), targetId);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(4, 2), targetIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(6, 2), (ushort)CliActionId.CastMagic);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(8, 4), spellId);
            BinaryPrimitives.WriteSingleLittleEndian(payload.Slice(12, 4), targetPos.X);
            BinaryPrimitives.WriteSingleLittleEndian(payload.Slice(16, 4), targetPos.Z);
            BinaryPrimitives.WriteSingleLittleEndian(payload.Slice(20, 4), targetPos.Y);
            return 28;
        }

        /// <summary>
        /// C2S 0x01A: Requests execution of a weapon skill.
        /// </summary>
        public static int BuildWeaponskillRequest(Span<byte> destination, ushort sequenceId, uint targetId, ushort targetIndex, ushort wsId)
        {
            PacketHeader.Write(destination, 0x01A, 7, sequenceId);
            var payload = destination.Slice(4, 24);
            payload.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), targetId);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(4, 2), targetIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(6, 2), (ushort)CliActionId.Weaponskill);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(8, 4), wsId);
            return 28;
        }

        /// <summary>
        /// C2S 0x01A: Requests execution of a job ability.
        /// </summary>
        public static int BuildJobAbilityRequest(Span<byte> destination, ushort sequenceId, uint targetId, ushort targetIndex, ushort abilityId)
        {
            PacketHeader.Write(destination, 0x01A, 7, sequenceId);
            var payload = destination.Slice(4, 24);
            payload.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), targetId);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(4, 2), targetIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(6, 2), (ushort)CliActionId.JobAbility);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(8, 4), abilityId);
            return 28;
        }

        /// <summary>
        /// C2S 0x01A: Requests execution of ranged attack (shoot).
        /// </summary>
        public static int BuildShootRequest(Span<byte> destination, ushort sequenceId, uint targetId, ushort targetIndex)
        {
            PacketHeader.Write(destination, 0x01A, 7, sequenceId);
            var payload = destination.Slice(4, 24);
            payload.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), targetId);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(4, 2), targetIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(6, 2), (ushort)CliActionId.Shoot);
            return 28;
        }

        /// <summary>
        /// C2S 0x01A: Requests targeting assist on a player.
        /// </summary>
        public static int BuildAssistRequest(Span<byte> destination, ushort sequenceId, uint targetId, ushort targetIndex)
        {
            PacketHeader.Write(destination, 0x01A, 7, sequenceId);
            var payload = destination.Slice(4, 24);
            payload.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), targetId);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(4, 2), targetIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(6, 2), (ushort)CliActionId.Assist);
            return 28;
        }

        /// <summary>
        /// C2S 0x01A: Requests summoning/mounting a mount.
        /// </summary>
        public static int BuildMountRequest(Span<byte> destination, ushort sequenceId, uint mountId)
        {
            PacketHeader.Write(destination, 0x01A, 7, sequenceId);
            var payload = destination.Slice(4, 24);
            payload.Clear();
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(6, 2), (ushort)CliActionId.Mount);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(8, 4), mountId);
            return 28;
        }

        /// <summary>
        /// C2S 0x01A: Requests dismounting from current mount or chocobo.
        /// </summary>
        public static int BuildDismountRequest(Span<byte> destination, ushort sequenceId)
        {
            PacketHeader.Write(destination, 0x01A, 7, sequenceId);
            var payload = destination.Slice(4, 24);
            payload.Clear();
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(6, 2), (ushort)CliActionId.Dismount);
            return 28;
        }

        /// <summary>
        /// C2S 0x05D: Sends an emote / motion request.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x05d_motion.h).
        /// </summary>
        public static int BuildEmoteRequest(Span<byte> destination, ushort sequenceId, uint targetId, ushort targetIndex, EmoteId emoteId, byte mode = 0, ushort param = 0)
        {
            PacketHeader.Write(destination, 0x05D, 4, sequenceId);
            var payload = destination.Slice(4, 12);
            payload.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), targetId);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(4, 2), targetIndex);
            payload[6] = (byte)emoteId;
            payload[7] = mode;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(8, 2), param);
            return 16;
        }

        /// <summary>
        /// C2S 0x0F1: Cancels an active status effect / buff.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x0f1_buffcancel.h).
        /// </summary>
        public static int BuildBuffCancelRequest(Span<byte> destination, ushort sequenceId, ushort buffId)
        {
            PacketHeader.Write(destination, 0x0F1, 2, sequenceId);
            var payload = destination.Slice(4, 4);
            payload.Clear();
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(0, 2), buffId);
            return 8;
        }

        /// <summary>
        /// C2S 0x11D: Sends a jump request.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x11d_jump.h).
        /// </summary>
        public static int BuildJumpRequest(Span<byte> destination, ushort sequenceId, uint playerId, ushort playerIndex)
        {
            PacketHeader.Write(destination, 0x11D, 3, sequenceId);
            var payload = destination.Slice(4, 8);
            payload.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), playerId);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(4, 2), playerIndex);
            return 12;
        }
    }

    #endregion
}
