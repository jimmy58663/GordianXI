// src/Gordian.Core/World/ExtendedJobData.cs
using System;
using Gordian.Core.Network.Packets;

namespace Gordian.Core.World
{
    /// <summary>
    /// The job-specific data of S2C 0x044 for the main or support job: Blue Mage set spells, the automaton, or the
    /// Monstrosity species and instincts. Immutable; <see cref="LocalPlayerState"/> replaces it per packet.
    /// </summary>
    public sealed class ExtendedJobData
    {
        /// <summary>The job number of the packet (a <see cref="JobId"/>, or 23 for Monstrosity).</summary>
        public byte JobNo { get; }
        public bool IsSubJob { get; }

        /// <summary>The 20 Blue Mage slots, each a spell id less 512 (0 = empty); empty for other jobs.</summary>
        public byte[] BlueSpells { get; }

        /// <summary>The automaton (Puppetmaster); null for other jobs.</summary>
        public AutomatonData? Automaton { get; }

        /// <summary>The Monstrosity species and instincts; null for other jobs.</summary>
        public MonstrosityData? Monstrosity { get; }

        /// <summary>The whole payload as received (unknown jobs keep their data here).</summary>
        public byte[] RawPayload { get; }

        public JobId Job => (JobId)JobNo;
        public bool IsBlueMage => JobNo == (byte)JobId.BlueMage;
        public bool IsPuppetmaster => JobNo == (byte)JobId.Puppetmaster;
        public bool IsMonstrosity => JobNo == S2C_0x044_ExtendedJob.MonstrosityJobNo;

        public ExtendedJobData(in S2C_0x044_ExtendedJob packet)
        {
            JobNo = packet.JobNo;
            IsSubJob = packet.IsSubJob;
            RawPayload = packet.Payload.ToArray();

            if (packet.IsBlueMage)
            {
                BlueSpells = new byte[S2C_0x044_ExtendedJob.BlueSpellSlots];
                for (int i = 0; i < BlueSpells.Length; i++) BlueSpells[i] = packet.GetBlueSpell(i);
            }
            else
            {
                BlueSpells = Array.Empty<byte>();
            }

            if (packet.IsPuppetmaster) Automaton = new AutomatonData(packet);
            if (packet.IsMonstrosity) Monstrosity = new MonstrosityData(packet);
        }

        /// <summary>The spell id (512 and up) set in Blue Mage slot <paramref name="slot"/>, or 0 when empty.</summary>
        public ushort GetBlueSpellId(int slot)
            => (uint)slot < (uint)BlueSpells.Length && BlueSpells[slot] != 0
                ? (ushort)(BlueSpells[slot] + S2C_0x044_ExtendedJob.BlueSpellIdBase)
                : (ushort)0;
    }

    /// <summary>
    /// The automaton of S2C 0x044 for Puppetmaster. Head and frame are item ids less 8192, attachments item ids less
    /// 8448 (0 = empty slot). Unlock bits: bit n of <see cref="UnlockedHeads"/> / <see cref="UnlockedFrames"/> is head /
    /// frame n, bit n of the eight <see cref="UnlockedAttachments"/> words is attachment n (LandSandBoat).
    /// </summary>
    public sealed class AutomatonData
    {
        public byte Head { get; }
        public byte Frame { get; }
        public byte[] Attachments { get; }
        public uint UnlockedHeads { get; }
        public uint UnlockedFrames { get; }
        public uint[] UnlockedAttachments { get; }
        public string Name { get; }
        public ushort Hp { get; }
        public ushort MaxHp { get; }
        public ushort Mp { get; }
        public ushort MaxMp { get; }
        public ushort MeleeSkill { get; }
        public ushort MeleeSkillCap { get; }
        public ushort RangedSkill { get; }
        public ushort RangedSkillCap { get; }
        public ushort MagicSkill { get; }
        public ushort MagicSkillCap { get; }

        /// <summary>STR, DEX, VIT, AGI, INT, MND, CHR.</summary>
        public ushort[] Stats { get; }

        /// <summary>The bonuses to <see cref="Stats"/>, same order.</summary>
        public short[] StatBonuses { get; }
        public byte ElementalCapacityBonus { get; }

        internal AutomatonData(in S2C_0x044_ExtendedJob packet)
        {
            Head = packet.AutomatonHead;
            Frame = packet.AutomatonFrame;
            Attachments = new byte[S2C_0x044_ExtendedJob.AutomatonAttachmentSlots];
            for (int i = 0; i < Attachments.Length; i++) Attachments[i] = packet.GetAutomatonAttachment(i);
            UnlockedHeads = packet.UnlockedHeads;
            UnlockedFrames = packet.UnlockedFrames;
            UnlockedAttachments = new uint[8];
            for (int i = 0; i < UnlockedAttachments.Length; i++) UnlockedAttachments[i] = packet.GetUnlockedAttachmentWord(i);
            Name = packet.AutomatonName;
            Hp = packet.AutomatonHp;
            MaxHp = packet.AutomatonMaxHp;
            Mp = packet.AutomatonMp;
            MaxMp = packet.AutomatonMaxMp;
            MeleeSkill = packet.AutomatonMeleeSkill;
            MeleeSkillCap = packet.AutomatonMeleeSkillCap;
            RangedSkill = packet.AutomatonRangedSkill;
            RangedSkillCap = packet.AutomatonRangedSkillCap;
            MagicSkill = packet.AutomatonMagicSkill;
            MagicSkillCap = packet.AutomatonMagicSkillCap;
            Stats = new ushort[7];
            StatBonuses = new short[7];
            for (int i = 0; i < 7; i++)
            {
                Stats[i] = packet.GetAutomatonStat(i);
                StatBonuses[i] = packet.GetAutomatonStatBonus(i);
            }
            ElementalCapacityBonus = packet.AutomatonElementalCapacityBonus;
        }

        /// <summary>True when attachment <paramref name="attachment"/> (item id less 8448) is unlocked.</summary>
        public bool IsAttachmentUnlocked(int attachment)
            => attachment >= 0 && attachment < 256 && (UnlockedAttachments[attachment >> 5] & (1u << (attachment & 31))) != 0;
    }

    /// <summary>The Monstrosity data of S2C 0x044 (job 23): the species and the 12 equipped instincts.</summary>
    public sealed class MonstrosityData
    {
        public ushort Species { get; }
        public ushort[] Instincts { get; }

        internal MonstrosityData(in S2C_0x044_ExtendedJob packet)
        {
            Species = packet.MonstrositySpecies;
            Instincts = new ushort[S2C_0x044_ExtendedJob.MonstrosityInstinctSlots];
            for (int i = 0; i < Instincts.Length; i++) Instincts[i] = packet.GetMonstrosityInstinct(i);
        }
    }
}
