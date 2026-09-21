// src/Gordian.Core/World/LocalPlayerState.cs
using System;
using Gordian.Core.Network.Packets;

namespace Gordian.Core.World
{
    /// <summary>
    /// Thread-safe active player character vitals, attributes, progression, and state cache.
    /// </summary>
    public sealed class LocalPlayerState
    {
        private readonly object _lock = new object();

        #region Vitals
        public int CurrentHp { get; private set; }
        public int MaxHp { get; private set; }
        public int CurrentMp { get; private set; }
        public int MaxMp { get; private set; }
        public short CurrentTp { get; private set; }
        public byte Hpp { get; private set; }
        #endregion

        #region Identity & Progression
        public uint ServerId { get; set; }
        public ushort ZoneId { get; set; }
        public JobId MainJob { get; private set; } = JobId.None;
        public byte MainJobLevel { get; private set; }
        public JobId SubJob { get; private set; } = JobId.None;
        public byte SubJobLevel { get; private set; }
        public short ExpNow { get; private set; }
        public short ExpNext { get; private set; }
        public ushort TitleId { get; private set; }
        public ushort Rank { get; private set; }
        public ushort RankPoints { get; private set; }
        public ushort HomePointZone { get; private set; }
        public byte Nation { get; private set; }
        public byte SuperiorLevel { get; private set; }
        public byte ItemLevel { get; private set; }
        public byte HighestItemLevel { get; private set; }
        public byte UnityFaction { get; private set; }
        public uint UnityPoints { get; private set; }
        public byte GmLevel { get; set; }
        public bool IsGm => GmLevel > 0;
        #endregion

        #region Attributes & Combat Stats
        // 0:STR, 1:DEX, 2:VIT, 3:AGI, 4:INT, 5:MND, 6:CHR
        public ushort[] BaseStats { get; } = new ushort[7];
        public short[] StatModifiers { get; } = new short[7];
        public short Attack { get; private set; }
        public short Defense { get; private set; }
        // 0:Fire, 1:Ice, 2:Wind, 3:Earth, 4:Thunder, 5:Water, 6:Light, 7:Dark
        public short[] ElementalResistances { get; } = new short[8];
        #endregion

        #region Skills & Recasts
        public ushort[] SkillBase { get; } = new ushort[64];
        public uint[] CommandRecast { get; } = new uint[31];
        public ushort[] AbilityRecasts { get; } = new ushort[32];
        public uint MountRecastSeconds { get; private set; }
        #endregion

        #region Learned Magic & Commands
        public byte[] LearnedSpells { get; } = new byte[128];
        public byte[] LearnedWeaponSkills { get; } = new byte[64];
        public byte[] LearnedJobAbilities { get; } = new byte[64];
        public byte[] LearnedPetAbilities { get; } = new byte[64];
        public byte[] LearnedTraits { get; } = new byte[32];
        #endregion

        #region Status Effects & Buffs
        public byte[] BuffIcons { get; } = new byte[32];
        public byte[] BuffStatusBits { get; } = new byte[8];
        public ushort PetActorIndex { get; private set; }
        public byte MountId { get; private set; }
        public byte WardrobeMask { get; private set; }
        public ushort CostumeId { get; private set; }
        public uint DeadCounterSeconds { get; private set; }
        #endregion

        #region Locomotion & Speed
        /// <summary>
        /// Authoritative player movement speed transmitted by server (S2C 0x037 / 0x00A).
        /// 0 when uninitialized (falling back to profile/entity run speed).
        /// Standard run speed is 50 (5.0 yalms/sec). Mount / Flee speed is 80 (8.0 yalms/sec).
        /// </summary>
        public ushort Speed { get; private set; }

        /// <summary>
        /// Animation playback rate divisor transmitted by server (nominally 50).
        /// </summary>
        public byte SpeedBase { get; private set; } = 50;
        #endregion

        #region Events
        public event Action? VitalsUpdated;
        public event Action? StatsUpdated;
        public event Action? SkillsUpdated;
        public event Action? BuffsUpdated;
        public event Action? SpeedUpdated;
        public event Action? MagicLearnedUpdated;
        public event Action? CommandsUpdated;
        public event Action? AbilityRecastsUpdated;
        #endregion

        public void UpdateFromJobInfo(in S2C_0x01B_JobInfo jobInfo)
        {
            lock (_lock)
            {
                if (jobInfo.MainJob != JobId.None)
                {
                    MainJob = jobInfo.MainJob;
                    MainJobLevel = jobInfo.MainJobLevel;
                }
                if (jobInfo.SubJob != JobId.None)
                {
                    SubJob = jobInfo.SubJob;
                    SubJobLevel = jobInfo.SubJobLevel;
                }
                if (jobInfo.HpMax > 0)
                {
                    MaxHp = jobInfo.HpMax;
                }
                if (jobInfo.MpMax > 0)
                {
                    MaxMp = jobInfo.MpMax;
                }
                for (int i = 0; i < 7; i++)
                {
                    ushort b = jobInfo.GetBaseStat(i);
                    if (b > 0)
                    {
                        BaseStats[i] = b;
                    }
                    StatModifiers[i] = jobInfo.GetStatModifier(i);
                }

                if (CurrentHp == 0 && MaxHp > 0 && Hpp > 0)
                {
                    CurrentHp = (MaxHp * Hpp) / 100;
                }
            }

            StatsUpdated?.Invoke();
            VitalsUpdated?.Invoke();
        }

        public void UpdateFromGroupAttr(in S2C_0x0DF_GroupAttr attr)
        {
            lock (_lock)
            {
                CurrentHp = (int)attr.Hp;
                CurrentMp = (int)attr.Mp;
                CurrentTp = (short)attr.Tp;
                Hpp = attr.Hpp;

                if (attr.MainJob != JobId.None && (MainJob == JobId.None || attr.MainJobLevel > 0))
                {
                    MainJob = attr.MainJob;
                    MainJobLevel = attr.MainJobLevel;
                }
                if (attr.SubJob != JobId.None && (SubJob == JobId.None || attr.SubJobLevel > 0))
                {
                    SubJob = attr.SubJob;
                    SubJobLevel = attr.SubJobLevel;
                }
            }

            VitalsUpdated?.Invoke();
            StatsUpdated?.Invoke();
        }

        public void UpdateFromCharStatus(in S2C_0x037_CharStatus status)
        {
            bool speedChanged = false;
            lock (_lock)
            {
                Hpp = status.Hpp;
                PetActorIndex = status.PetActorIndex;
                MountId = status.MountId;
                WardrobeMask = status.WardrobeMask;
                CostumeId = status.CostumeId;
                DeadCounterSeconds = status.DeadCounterSeconds;

                if (status.Speed > 0 && Speed != status.Speed)
                {
                    Speed = status.Speed;
                    speedChanged = true;
                }
                if (status.SpeedBase > 0 && SpeedBase != status.SpeedBase)
                {
                    SpeedBase = status.SpeedBase;
                    speedChanged = true;
                }

                if (!status.BuffStatus.IsEmpty)
                {
                    int copyLen = Math.Min(status.BuffStatus.Length, BuffIcons.Length);
                    status.BuffStatus.Slice(0, copyLen).CopyTo(BuffIcons);
                }

                if (!status.BuffStatusBits.IsEmpty)
                {
                    int copyLen = Math.Min(status.BuffStatusBits.Length, BuffStatusBits.Length);
                    status.BuffStatusBits.Slice(0, copyLen).CopyTo(BuffStatusBits);
                }

                if (CurrentHp == 0 && MaxHp > 0 && Hpp > 0)
                {
                    CurrentHp = (MaxHp * Hpp) / 100;
                }
            }

            if (speedChanged)
            {
                SpeedUpdated?.Invoke();
            }
            BuffsUpdated?.Invoke();
            VitalsUpdated?.Invoke();
        }

        public void SetSpeed(ushort speed, byte speedBase = 50)
        {
            bool speedChanged = false;
            lock (_lock)
            {
                ushort effectiveSpeed = speed > 0 ? speed : (ushort)50;
                if (Speed != effectiveSpeed)
                {
                    Speed = effectiveSpeed;
                    speedChanged = true;
                }
                if (speedBase > 0 && SpeedBase != speedBase)
                {
                    SpeedBase = speedBase;
                    speedChanged = true;
                }
            }

            if (speedChanged)
            {
                SpeedUpdated?.Invoke();
            }
        }

        public void UpdateFromCliStatus(in S2C_0x061_CliStatus cliStatus)
        {
            lock (_lock)
            {
                MaxHp = cliStatus.HpMax;
                MaxMp = cliStatus.MpMax;
                MainJob = cliStatus.MainJob;
                MainJobLevel = cliStatus.MainJobLevel;
                SubJob = cliStatus.SubJob;
                SubJobLevel = cliStatus.SubJobLevel;
                ExpNow = cliStatus.ExpNow;
                ExpNext = cliStatus.ExpNext;
                Attack = cliStatus.Attack;
                Defense = cliStatus.Defense;
                TitleId = cliStatus.TitleId;
                Rank = cliStatus.Rank;
                RankPoints = cliStatus.RankPoints;
                HomePointZone = cliStatus.HomePointZone;
                Nation = cliStatus.Nation;
                SuperiorLevel = cliStatus.SuperiorLevel;
                ItemLevel = cliStatus.ItemLevel;
                HighestItemLevel = cliStatus.HighestItemLevel;
                UnityFaction = cliStatus.UnityFaction;
                UnityPoints = cliStatus.UnityPoints;

                for (int i = 0; i < 7; i++)
                {
                    BaseStats[i] = cliStatus.GetBaseStat(i);
                    StatModifiers[i] = cliStatus.GetStatModifier(i);
                }

                for (int i = 0; i < 8; i++)
                {
                    ElementalResistances[i] = cliStatus.GetElementalResistance(i);
                }

                if (CurrentHp == 0 && MaxHp > 0 && Hpp > 0)
                {
                    CurrentHp = (MaxHp * Hpp) / 100;
                }
            }

            StatsUpdated?.Invoke();
            VitalsUpdated?.Invoke();
        }

        public void UpdateFromCliStatus2(in S2C_0x062_CliStatus2 cliStatus2)
        {
            lock (_lock)
            {
                for (int i = 0; i < 31; i++)
                {
                    CommandRecast[i] = cliStatus2.GetCommandRecast(i);
                }

                for (int i = 0; i < 64; i++)
                {
                    SkillBase[i] = cliStatus2.GetSkillBase(i);
                }
            }

            SkillsUpdated?.Invoke();
        }

        public void UpdateVitals(int currentHp, int currentMp, short currentTp)
        {
            lock (_lock)
            {
                CurrentHp = currentHp;
                CurrentMp = currentMp;
                CurrentTp = currentTp;
                if (MaxHp > 0)
                {
                    Hpp = (byte)Math.Clamp((CurrentHp * 100) / MaxHp, 0, 100);
                }
            }

            VitalsUpdated?.Invoke();
        }

        public bool HasStatusEffect(byte effectId)
        {
            lock (_lock)
            {
                for (int i = 0; i < BuffIcons.Length; i++)
                {
                    if (BuffIcons[i] == effectId) return true;
                }
                return false;
            }
        }

        public void UpdateFromMagicData(in S2C_0x0AA_MagicData magicData)
        {
            if (!magicData.IsValid) return;

            lock (_lock)
            {
                magicData.MagicDataTbl.CopyTo(LearnedSpells);
            }

            MagicLearnedUpdated?.Invoke();
        }

        public void UpdateFromCommandData(in S2C_0x0AC_CommandData commandData)
        {
            if (!commandData.IsValid) return;

            lock (_lock)
            {
                commandData.WeaponSkills.CopyTo(LearnedWeaponSkills);
                commandData.JobAbilities.CopyTo(LearnedJobAbilities);
                commandData.PetAbilities.CopyTo(LearnedPetAbilities);
                commandData.Traits.CopyTo(LearnedTraits);
            }

            CommandsUpdated?.Invoke();
        }

        public void UpdateFromAbilRecast(in S2C_0x119_AbilRecast abilRecast)
        {
            if (!abilRecast.IsValid) return;

            lock (_lock)
            {
                Array.Clear(AbilityRecasts, 0, AbilityRecasts.Length);
                for (int i = 0; i < 31; i++)
                {
                    var timer = abilRecast.GetTimer(i);
                    if (timer.TimerId < AbilityRecasts.Length)
                    {
                        AbilityRecasts[timer.TimerId] = timer.TimerSeconds;
                    }
                }
                MountRecastSeconds = abilRecast.MountRecast;
            }

            AbilityRecastsUpdated?.Invoke();
        }

        public bool HasSpell(ushort spellId)
        {
            lock (_lock)
            {
                int byteIdx = spellId >> 3;
                int bitIdx = spellId & 7;
                return byteIdx >= 0 && byteIdx < LearnedSpells.Length && (LearnedSpells[byteIdx] & (1 << bitIdx)) != 0;
            }
        }

        public bool HasWeaponSkill(ushort wsId)
        {
            lock (_lock)
            {
                int byteIdx = wsId >> 3;
                int bitIdx = wsId & 7;
                return byteIdx >= 0 && byteIdx < LearnedWeaponSkills.Length && (LearnedWeaponSkills[byteIdx] & (1 << bitIdx)) != 0;
            }
        }

        public bool HasJobAbility(ushort abilityId)
        {
            lock (_lock)
            {
                int byteIdx = abilityId >> 3;
                int bitIdx = abilityId & 7;
                return byteIdx >= 0 && byteIdx < LearnedJobAbilities.Length && (LearnedJobAbilities[byteIdx] & (1 << bitIdx)) != 0;
            }
        }

        public bool HasPetAbility(ushort petAbilId)
        {
            lock (_lock)
            {
                int byteIdx = petAbilId >> 3;
                int bitIdx = petAbilId & 7;
                return byteIdx >= 0 && byteIdx < LearnedPetAbilities.Length && (LearnedPetAbilities[byteIdx] & (1 << bitIdx)) != 0;
            }
        }

        public bool HasTrait(ushort traitId)
        {
            lock (_lock)
            {
                int byteIdx = traitId >> 3;
                int bitIdx = traitId & 7;
                return byteIdx >= 0 && byteIdx < LearnedTraits.Length && (LearnedTraits[byteIdx] & (1 << bitIdx)) != 0;
            }
        }
    }
}
