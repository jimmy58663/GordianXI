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
        MpDrain = 22,
        /// <summary>Haste, e.g. a Haste Samba (XiPackets 0x0028 lists 23; LandSandBoat's enum stops at 22).</summary>
        Haste = 23
    }

    /// <summary>
    /// The skillchain effect an S2C 0x028 weapon skill (category 3) result carries in <c>proc_kind</c>, which picks the
    /// skillchain animation. For the other categories the same field is an <see cref="ActionProcAddEffect"/>.
    /// Values referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x0028).
    /// </summary>
    public enum ActionSkillchain : byte
    {
        None = 0,
        Light = 1,
        Darkness = 2,
        Gravitation = 3,
        Fragmentation = 4,
        Distortion = 5,
        Fusion = 6,
        Compression = 7,
        Liquefaction = 8,
        Induration = 9,
        Reverberation = 10,
        Transfixion = 11,
        Scission = 12,
        Detonation = 13,
        Impaction = 14,
        Radiance = 15,
        Umbra = 16
    }

    /// <summary>
    /// The extended message modifier flags of an S2C 0x028 result (<c>bit</c>), which add "Cover!", "Resist!" and similar
    /// to the log. Not every action sets them.
    /// Values referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x0028).
    /// </summary>
    [Flags]
    public enum ActionResultFlags : uint
    {
        None = 0,
        Cover = 0x01,
        Resist = 0x02,
        MagicBurst = 0x04,
        Immunobreak = 0x08,
        CriticalHit = 0x10
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
    /// ActionBuf[0] of a C2S 0x01A <see cref="CliActionId.Blockaid"/> request (<c>/blockaid [off|on]</c>, bare toggles).
    /// Values referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/client/0x001A) and
    /// LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x01a_action.h).
    /// </summary>
    public enum BlockaidMode : uint
    {
        Disable = 0,
        Enable = 1,
        Toggle = 2
    }

    /// <summary>
    /// ActionBuf[0] (<c>StatusId</c>) of a C2S 0x01A <see cref="CliActionId.HomepointMenu"/> answer: 0 returns the dead
    /// character to its home point; 1 and 2 are the Monstrosity death menu's Cancel and Retry. LandSandBoat rejects the
    /// request unless the character is dead (<c>c2s/0x01a_action.cpp</c>).
    /// Values referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/client/0x001A) and
    /// LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x01a_action.h).
    /// </summary>
    public enum HomepointMenuChoice : uint
    {
        ReturnToHomePoint = 0,
        MonstrosityCancel = 1,
        MonstrosityRetry = 2
    }

    /// <summary>
    /// ActionBuf[0] (<c>StatusId</c>) of a C2S 0x01A <see cref="CliActionId.RaiseMenu"/> or
    /// <see cref="CliActionId.TractorMenu"/> answer. LandSandBoat drops a declined Raise (the caster must cast again)
    /// and moves the corpse to the caster on an accepted Tractor.
    /// Values referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/client/0x001A) and
    /// LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x01a_action.h).
    /// </summary>
    public enum ReviveMenuAnswer : uint
    {
        Accept = 0,
        Decline = 1
    }

    /// <summary>
    /// The <c>type</c> of S2C 0x0F9 (GP_SERV_COMMAND_RES): what the dead character's menu offers. The client treats
    /// any other value as <see cref="HomePoint"/>.
    /// Values referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x00F9) and
    /// LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x0f9_res.h).
    /// </summary>
    public enum DeathMenuType : ushort
    {
        /// <summary>Only the home point menu (also: no Raise or Tractor offered any more).</summary>
        HomePoint = 0,

        /// <summary>A Raise was cast on the character, or its Reraise took effect: the Raise yes/no sub-menu.</summary>
        Raise = 1,

        /// <summary>A Tractor was cast on the character: the Tractor yes/no sub-menu.</summary>
        Tractor = 2
    }

    /// <summary>
    /// Emote ids sent as C2S 0x05D <c>Number</c> (and played by event opcode 0x6E). Point is 0; there is no "none" value.
    /// <c>/sit</c> is not an emote: it is its own packet (C2S 0x0EA). Ids referenced from XiPackets
    /// (https://github.com/atom0s/XiPackets/tree/main/world/client/0x005D) and LandSandBoat
    /// (https://github.com/LandSandBoat/server/blob/base/src/map/enums/emote.h).
    /// </summary>
    public enum EmoteId : byte
    {
        Point = 0,
        Bow = 1,
        Salute = 2,
        Kneel = 3,
        Laugh = 4,
        Cry = 5,
        No = 6,
        Yes = 7,
        Wave = 8,
        Goodbye = 9,
        Welcome = 10,
        Joy = 11,
        Cheer = 12,
        Clap = 13,
        Praise = 14,
        Smile = 15,
        Poke = 16,
        Slap = 17,
        Stagger = 18,
        Sigh = 19,
        Comfort = 20,
        Surprised = 21,
        Amazed = 22,
        Stare = 23,
        Blush = 24,
        Angry = 25,
        Disgusted = 26,
        Muted = 27,
        Doze = 28,
        Panic = 29,
        Grin = 30,
        Dance = 31,
        Think = 32,
        Fume = 33,
        Doubt = 34,
        Sulk = 35,
        Psych = 36,
        Huh = 37,
        Shocked = 38,
        /// <summary>HELM gathering motions; LandSandBoat sends them, the client never requests them.</summary>
        Logging = 40,
        Excavation = 41,
        Harvesting = 42,
        /// <summary>Sent with <c>Param</c> 1.</summary>
        Hurray = 43,
        Toss = 44,
        /// <summary><c>/dance1</c>-<c>/dance4</c>, sent with <c>Param</c> 2-5.</summary>
        Dance1 = 65,
        Dance2 = 66,
        Dance3 = 67,
        Dance4 = 68,
        /// <summary><c>/bell</c>, <c>Param</c> is the note.</summary>
        Bell = 73,
        /// <summary><c>/jobemote</c>, <c>Param</c> is the job id + 30.</summary>
        Job = 74,
        /// <summary><c>/aim</c>, sent with <c>Param</c> 53.</summary>
        Aim = 96
    }

    /// <summary>
    /// Crafting synthesis animation effect (S2C 0x030 <c>EffectNum</c>): the element of the crystal used.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/enums/synthesis_effect.h).
    /// </summary>
    public enum SynthesisEffect : short
    {
        None = 0x00,
        Water = 0x10,
        Wind = 0x11,
        Fire = 0x12,
        Earth = 0x13,
        Lightning = 0x14,
        Ice = 0x15,
        Light = 0x16,
        Dark = 0x17
    }

    /// <summary>
    /// Synthesis outcome LSB sends in S2C 0x030 <c>Type</c> with a synthesis effect.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/utils/synthutils.h, SYNTHESIS_RESULT).
    /// </summary>
    public enum SynthesisResult : byte
    {
        Fail = 0,
        Success = 1,
        HighQuality1 = 2,
        HighQuality2 = 3,
        HighQuality3 = 4
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

        /// <summary>The <see cref="Modifier"/> (<c>bit</c>) message flags: Cover, Resist, Magic Burst, Immunobreak, Critical Hit.</summary>
        public ActionResultFlags Flags => (ActionResultFlags)Modifier;

        /// <summary>
        /// Which of the four hit-distortion amounts (0, 0.25, 0.5, 1.0) the hit bends the target by: the low 2 bits of
        /// <see cref="Scale"/> (XiPackets 0x0028). Normal hits use 0-2, critical hits 2-3.
        /// </summary>
        public int HitDistortionIndex => Scale & 3;

        /// <summary>The hit distortion amount from the client's table (0, 0.25, 0.5, 1.0).</summary>
        public float HitDistortion => HitDistortionIndex switch { 1 => 0.25f, 2 => 0.5f, 3 => 1.0f, _ => 0f };

        /// <summary>
        /// Which row of the client's knockback table applies: <see cref="Scale"/> &gt;&gt; 2 (0-6; the rows are listed in
        /// XiPackets 0x0028), meaningful only for attacks and abilities that knock back.
        /// </summary>
        public int KnockbackIndex => Scale >> 2;

        /// <summary>
        /// For a weapon skill (category 3), the skillchain effect: <see cref="ProcKind"/> is the skillchain id there, not an
        /// <see cref="ActionProcAddEffect"/>. <see cref="ActionSkillchain.None"/> when there is no proc.
        /// </summary>
        public ActionSkillchain Skillchain => HasProc ? (ActionSkillchain)(byte)ProcKind : ActionSkillchain.None;

        /// <summary>The additional effect of a basic or ranged attack (categories 1 and 2) or any category but weapon skills; None for a skillchain.</summary>
        public ActionProcAddEffect GetAddEffect(ActionCategory category) =>
            HasProc && category != ActionCategory.SkillFinish ? ProcKind : ActionProcAddEffect.None;

        /// <summary>
        /// Info (<see cref="Info"/>) of a basic attack: critical hits are 2 and 3, normal hits 0 and 1 (XiPackets 0x0028);
        /// for Rune Fencer wards and effusions (category 15) it is the element (0 mixed, 1 Ignis ... 8 Tenebrae).
        /// </summary>
        public bool IsCriticalInfo => Info is 2 or 3;
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

        /// <summary>
        /// The action's <c>info</c> word. Only for a spell cast (<see cref="ActionCategory.MagicFinish"/>) is it a recast, in
        /// seconds; the other categories send 0 (XiPackets 0x0028). Use <see cref="RecastSeconds"/> for the recast.
        /// </summary>
        public uint Recast { get; init; }

        /// <summary>The spell's recast time in seconds; 0 for every category but a finished spell (category 4).</summary>
        public uint RecastSeconds => Category == ActionCategory.MagicFinish ? Recast : 0;

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
    /// S2C 0x0F9 (GP_SERV_COMMAND_RES): changes the dead character's menu. Payload (12-byte packet): 0 u32
    /// <c>UniqueNo</c>, 4 u16 <c>ActIndex</c> (both the character's own; the client does not use them), 6 u16
    /// <c>type</c> (<see cref="DeathMenuType"/>: 0 back to the plain home point menu, 1 the Raise sub-menu, 2 the
    /// Tractor sub-menu; any other value counts as 0). LandSandBoat sends type 1 when Raise is cast on a dead
    /// character (<c>CLuaBaseEntity::sendRaise</c>) and about 12 s after a death with Reraise active
    /// (<c>CDeathState::Update</c>), and type 2 for Tractor (<c>sendTractor</c>); it never sends type 0.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x00F9)
    /// and LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x0f9_res.h,
    /// ai/states/death_state.cpp, lua/lua_base_entity.cpp).
    /// </summary>
    public readonly ref struct S2C_0x0F9_Res
    {
        public const ushort PacketId = 0x0F9;
        public const int PayloadLength = 8;

        public bool IsValid { get; }
        public uint UniqueNo { get; }
        public ushort ActIndex { get; }

        /// <summary>The raw <c>type</c> as sent.</summary>
        public ushort RawType { get; }

        /// <summary>The menu the client shows: <see cref="RawType"/>, with unknown values read as <see cref="DeathMenuType.HomePoint"/> (XiPackets).</summary>
        public DeathMenuType Type => RawType is (ushort)DeathMenuType.Raise or (ushort)DeathMenuType.Tractor ? (DeathMenuType)RawType : DeathMenuType.HomePoint;

        public S2C_0x0F9_Res(ReadOnlySpan<byte> payload)
        {
            this = default;
            if (payload.Length < PayloadLength) return;
            UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload);
            ActIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            RawType = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2));
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x058 (GP_SERV_COMMAND_ASSIST): the server picks the character's target. Payload: 0 u32 <c>UniqueNo</c> (the
    /// local player), 4 u32 <c>AssistNo</c> (the entity to target), 8 u16 <c>ActIndex</c> (the player's index; the client
    /// ignores it), 10 padding; 12 bytes. LandSandBoat sends it in answer to <c>/assist</c> (C2S 0x01A kind 0x0C: the
    /// assisted character's battle target) and whenever the character's battle target changes (<c>OnChangeTarget</c>,
    /// <c>attack_state.cpp</c>, <c>player_controller.cpp</c>); <c>AssistNo</c> is 0 when there is none.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x0058)
    /// and LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x058_assist.cpp).
    /// </summary>
    public readonly ref struct S2C_0x058_Assist
    {
        public const ushort PacketId = 0x058;
        public const int PayloadLength = 10;

        public bool IsValid { get; }
        public uint PlayerId { get; }
        public uint TargetId { get; }
        public ushort PlayerIndex { get; }

        public S2C_0x058_Assist(ReadOnlySpan<byte> payload)
        {
            this = default;
            if (payload.Length < PayloadLength) return;
            PlayerId = BinaryPrimitives.ReadUInt32LittleEndian(payload);
            TargetId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            PlayerIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2));
            IsValid = true;
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
        /// <summary>The client's <c>CraftParam</c>; for a synthesis LSB sends the <see cref="SynthesisResult"/> here.</summary>
        public byte Type { get; }
        public byte Status { get; }
        public ushort Timer { get; }
        public bool IsValid { get; }

        public SynthesisResult Result => (SynthesisResult)Type;

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

    /// <summary>
    /// S2C 0x02F (GP_SERV_COMMAND_DIG): an entity (a player riding a chocobo) plays the chocobo digging animation.
    /// Payload: TarUniqueNo (u32 at +0), TarActIndex (u16 at +4), Flags (u8 at +6; the retail client checks
    /// <c>Flags &amp; 0x0F == 1</c> together with the rider's mount state to pick the animation), 1 byte of padding.
    /// LandSandBoat sends it once a dig's greens are spent, before the dig's result message.
    /// Packet layout referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x002F)
    /// and LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x01a_action.cpp).
    /// </summary>
    public readonly ref struct S2C_0x02F_Dig
    {
        public const ushort PacketId = 0x02F;

        public uint TargetId { get; }
        public ushort TargetIndex { get; }
        public byte Flags { get; }
        public bool IsValid { get; }

        public S2C_0x02F_Dig(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 7)
            {
                TargetId = 0;
                TargetIndex = 0;
                Flags = 0;
                IsValid = false;
                return;
            }

            TargetId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            TargetIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            Flags = payload[6];
            IsValid = true;
        }
    }

    #region Outbound C2S Packet Builders

    /// <summary>
    /// Zero-allocation packet builders for client-to-server combat and action requests.
    /// </summary>
    public static class CombatPacketBuilder
    {
        /// <summary>Size of a C2S 0x01A action packet: 4-byte header, then UniqueNo, ActIndex, ActionID and ActionBuf[4].</summary>
        public const int ActionPacketSize = 28;

        /// <summary>
        /// C2S 0x01A (GP_CLI_COMMAND_ACTION): the action request every <see cref="CliActionId"/> kind shares.
        /// 28 bytes: UniqueNo (u32 at +4), ActIndex (u16 at +8), ActionID (u16 at +10) and ActionBuf[4] (u32 x 4 at
        /// +12). Most kinds leave ActionBuf zeroed; <paramref name="param"/> is ActionBuf[0]: the spell, skill or
        /// ability id, the menu answer (home point, raise, tractor), the <see cref="BlockaidMode"/>, the mount id, or
        /// for Talk on a Trust the release index.
        /// Packet layout referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/client/0x001A)
        /// and LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x01a_action.h).
        /// </summary>
        public static int BuildAction(Span<byte> destination, ushort sequenceId, CliActionId action, uint targetId = 0, ushort targetIndex = 0, uint param = 0)
        {
            PacketHeader.Write(destination, 0x01A, 7, sequenceId);
            var payload = destination.Slice(4, ActionPacketSize - 4);
            payload.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), targetId);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(4, 2), targetIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(6, 2), (ushort)action);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(8, 4), param);
            return ActionPacketSize;
        }

        /// <summary>
        /// C2S 0x01A: Initiates basic melee auto-attack on target.
        /// </summary>
        public static int BuildAttackRequest(Span<byte> destination, ushort sequenceId, uint targetId, ushort targetIndex)
            => BuildAction(destination, sequenceId, CliActionId.Attack, targetId, targetIndex);

        /// <summary>
        /// C2S 0x01A: Talks to (triggers) an NPC or door: the interaction Confirm on a targeted NPC sends. The
        /// server answers with an event (0x032) or a message (0x036).
        /// </summary>
        public static int BuildTalkRequest(Span<byte> destination, ushort sequenceId, uint targetId, ushort targetIndex)
            => BuildAction(destination, sequenceId, CliActionId.Talk, targetId, targetIndex);

        /// <summary>
        /// C2S 0x0DD (GP_CLI_COMMAND_EQUIP_INSPECT): examines a target, the command menu's Check and <c>/check</c>.
        /// 16 bytes: UniqueNo (u32 at +4), ActIndex (u32 at +8), Kind (u8 at +12: 0 check, 1 checkname, 2 checkparam),
        /// 3 bytes of padding. The server answers a monster check with a battle message (0x029, "seems tough...")
        /// and a player check with 0x0C9 (equipment inspect), not decoded yet.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x0dd_equip_inspect.h)
        /// and XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/client/0x00DD).
        /// </summary>
        public static int BuildCheckRequest(Span<byte> destination, ushort sequenceId, uint targetId, ushort targetIndex, byte kind = 0)
        {
            PacketHeader.Write(destination, 0x0DD, 4, sequenceId);
            var payload = destination.Slice(4, 12);
            payload.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), targetId);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(4, 4), targetIndex);
            payload[8] = kind;
            return 16;
        }

        /// <summary>
        /// C2S 0x01A: Disengages from combat auto-attack.
        /// </summary>
        public static int BuildAttackOffRequest(Span<byte> destination, ushort sequenceId, uint targetId, ushort targetIndex)
            => BuildAction(destination, sequenceId, CliActionId.AttackOff, targetId, targetIndex);

        /// <summary>
        /// C2S 0x01A: Requests casting of a magic spell.
        /// Packet layout referenced from XiPackets (world/client) and LandSandBoat (c2s/0x01a_action.cpp): the
        /// position fields are laid out X, height, north (the same convention as 0x015), and LSB treats them as an
        /// offset from the target clamped to +/-19 (used by ground-targeted spells), not a world position.
        /// </summary>
        /// <param name="targetOffset">Ground-target offset from the target: X, Y = height, Z = north (internal axes).</param>
        public static int BuildCastMagicRequest(Span<byte> destination, ushort sequenceId, uint targetId, ushort targetIndex, ushort spellId, Vector3 targetOffset = default)
        {
            int length = BuildAction(destination, sequenceId, CliActionId.CastMagic, targetId, targetIndex, spellId);
            var payload = destination.Slice(4, length - 4);
            BinaryPrimitives.WriteSingleLittleEndian(payload.Slice(12, 4), targetOffset.X);
            BinaryPrimitives.WriteSingleLittleEndian(payload.Slice(16, 4), targetOffset.Y);
            BinaryPrimitives.WriteSingleLittleEndian(payload.Slice(20, 4), targetOffset.Z);
            return length;
        }

        /// <summary>
        /// C2S 0x01A: Requests execution of a weapon skill.
        /// </summary>
        public static int BuildWeaponskillRequest(Span<byte> destination, ushort sequenceId, uint targetId, ushort targetIndex, ushort wsId)
            => BuildAction(destination, sequenceId, CliActionId.Weaponskill, targetId, targetIndex, wsId);

        /// <summary>
        /// C2S 0x01A: Requests execution of a job ability.
        /// </summary>
        public static int BuildJobAbilityRequest(Span<byte> destination, ushort sequenceId, uint targetId, ushort targetIndex, ushort abilityId)
            => BuildAction(destination, sequenceId, CliActionId.JobAbility, targetId, targetIndex, abilityId);

        /// <summary>
        /// C2S 0x01A: Requests execution of ranged attack (shoot).
        /// </summary>
        public static int BuildShootRequest(Span<byte> destination, ushort sequenceId, uint targetId, ushort targetIndex)
            => BuildAction(destination, sequenceId, CliActionId.Shoot, targetId, targetIndex);

        /// <summary>
        /// C2S 0x01A: Requests targeting assist on a player.
        /// </summary>
        public static int BuildAssistRequest(Span<byte> destination, ushort sequenceId, uint targetId, ushort targetIndex)
            => BuildAction(destination, sequenceId, CliActionId.Assist, targetId, targetIndex);

        /// <summary>
        /// C2S 0x01A: Requests summoning/mounting a mount.
        /// </summary>
        public static int BuildMountRequest(Span<byte> destination, ushort sequenceId, uint mountId)
            => BuildAction(destination, sequenceId, CliActionId.Mount, param: mountId);

        /// <summary>
        /// C2S 0x01A <see cref="CliActionId.HomepointMenu"/> (0x0B): the dead character's home point menu answer,
        /// ActionBuf[0] = <paramref name="choice"/> (0 = return to the home point). UniqueNo / ActIndex are the
        /// character's own (LandSandBoat does not read them for this kind; what retail sends there is not captured).
        /// Packet layout referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/client/0x001A)
        /// and LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x01a_action.cpp).
        /// </summary>
        public static int BuildHomepointMenuRequest(Span<byte> destination, ushort sequenceId, uint playerId, ushort playerIndex,
            HomepointMenuChoice choice = HomepointMenuChoice.ReturnToHomePoint)
            => BuildAction(destination, sequenceId, CliActionId.HomepointMenu, playerId, playerIndex, (uint)choice);

        /// <summary>
        /// C2S 0x01A <see cref="CliActionId.RaiseMenu"/> (0x0D): accepts (ActionBuf[0] = 0) or declines (1) the Raise
        /// offered by S2C 0x0F9 type 1. Layout as <see cref="BuildHomepointMenuRequest"/>; values referenced from
        /// XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/client/0x001A).
        /// </summary>
        public static int BuildRaiseMenuRequest(Span<byte> destination, ushort sequenceId, uint playerId, ushort playerIndex, ReviveMenuAnswer answer)
            => BuildAction(destination, sequenceId, CliActionId.RaiseMenu, playerId, playerIndex, (uint)answer);

        /// <summary>
        /// C2S 0x01A <see cref="CliActionId.TractorMenu"/> (0x13): accepts (ActionBuf[0] = 0) or declines (1) the Tractor
        /// offered by S2C 0x0F9 type 2. Layout as <see cref="BuildHomepointMenuRequest"/>; values referenced from
        /// XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/client/0x001A).
        /// </summary>
        public static int BuildTractorMenuRequest(Span<byte> destination, ushort sequenceId, uint playerId, ushort playerIndex, ReviveMenuAnswer answer)
            => BuildAction(destination, sequenceId, CliActionId.TractorMenu, playerId, playerIndex, (uint)answer);

        /// <summary>
        /// C2S 0x01A: Requests dismounting from current mount or chocobo.
        /// </summary>
        public static int BuildDismountRequest(Span<byte> destination, ushort sequenceId)
            => BuildAction(destination, sequenceId, CliActionId.Dismount);

        /// <summary>
        /// C2S 0x063 (GP_CLI_COMMAND_DIG): tells the server the chocobo digging animation has finished and the client
        /// is ready for the result. 16 bytes: UniqueNo (u32 at +4, the local player), para (u32 at +8, always 0),
        /// ActIndex (u16 at +12, the local player), mode (u8 at +14, always 0x11, the ChocoboDig action id), 1 byte of
        /// padding. LandSandBoat ignores it (it settles the dig when the 0x01A ChocoboDig arrives), but the client
        /// sends it after a 0x02F dig on itself.
        /// Packet layout referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/client/0x0063)
        /// and LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x063_dig.h).
        /// </summary>
        public static int BuildDigFinishedRequest(Span<byte> destination, ushort sequenceId, uint playerId, ushort playerIndex)
        {
            PacketHeader.Write(destination, 0x063, 4, sequenceId);
            var payload = destination.Slice(4, 12);
            payload.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), playerId);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(8, 2), playerIndex);
            payload[10] = (byte)CliActionId.ChocoboDig;
            return 16;
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
