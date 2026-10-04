// src/Gordian.Core/World/CombatState.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;

namespace Gordian.Core.World
{
    /// <summary>
    /// An entity's crafting animation state (S2C 0x030): the synthesis effect, its parameter (the client's CraftParam),
    /// the entity's server status, and the synthesis timer.
    /// </summary>
    public sealed record CraftEffectInfo(uint ServerId, ushort TargetIndex, SynthesisEffect Effect, byte Param, byte ServerStatus, ushort Timer);

    /// <summary>
    /// Thread-safe active session combat state, targeting, casting timers, ability recasts,
    /// and combat log history for HUD and automation consumption.
    /// </summary>
    public sealed class CombatState
    {
        private readonly object _lock = new();
        private const int MaxLogHistory = 100;

        private readonly List<CombatActionRecord> _actionHistory = new(MaxLogHistory);
        private readonly List<CombatMessageRecord> _messageHistory = new(MaxLogHistory);
        private readonly ushort[] _abilityRecasts = new ushort[32];

        #region Engagement & Target

        public bool IsEngaged { get; private set; }
        public bool IsLockedOn { get; set; }
        public uint TargetServerId { get; private set; }
        public ushort TargetIndex { get; private set; }

        #endregion

        #region Spell Casting

        public bool IsCasting { get; private set; }
        public ushort CastingSpellId { get; private set; }
        public DateTime CastStartTime { get; private set; }

        #endregion

        #region Recasts

        public uint MountRecastSeconds { get; private set; }

        #endregion

        #region Events

        public event Action<CombatActionRecord>? ActionExecuted;
        public event Action<CombatMessageRecord>? BattleMessageReceived;
        public event Action? RecastsUpdated;
        public event Action? EngagementChanged;
        public event Action? CastingStateChanged;
        public event Action<CraftEffectInfo>? CraftEffectChanged;

        #endregion

        public void SetTarget(uint serverId, ushort targetIndex)
        {
            lock (_lock)
            {
                TargetServerId = serverId;
                TargetIndex = targetIndex;
            }
        }

        public void Engage(uint serverId, ushort targetIndex)
        {
            bool changed;
            lock (_lock)
            {
                changed = !IsEngaged || TargetServerId != serverId || TargetIndex != targetIndex;
                IsEngaged = true;
                TargetServerId = serverId;
                TargetIndex = targetIndex;
            }

            if (changed)
            {
                EngagementChanged?.Invoke();
            }
        }

        private readonly Dictionary<uint, CraftEffectInfo> _craftEffects = new();

        /// <summary>Stores an entity's crafting animation state (S2C 0x030), keyed by its server id.</summary>
        public void SetCraftEffect(CraftEffectInfo effect)
        {
            lock (_lock) _craftEffects[effect.ServerId] = effect;
            CraftEffectChanged?.Invoke(effect);
        }

        /// <summary>The last crafting animation state the server sent for an entity, if any.</summary>
        public bool TryGetCraftEffect(uint serverId, out CraftEffectInfo effect)
        {
            lock (_lock) return _craftEffects.TryGetValue(serverId, out effect!);
        }

        public void Disengage()
        {
            bool changed;
            lock (_lock)
            {
                changed = IsEngaged;
                IsEngaged = false;
            }

            if (changed)
            {
                EngagementChanged?.Invoke();
            }
        }

        public void StartCasting(ushort spellId)
        {
            lock (_lock)
            {
                IsCasting = true;
                CastingSpellId = spellId;
                CastStartTime = DateTime.UtcNow;
            }

            CastingStateChanged?.Invoke();
        }

        public void FinishCasting()
        {
            lock (_lock)
            {
                IsCasting = false;
                CastingSpellId = 0;
            }

            CastingStateChanged?.Invoke();
        }

        public void RecordAction(CombatActionRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);

            lock (_lock)
            {
                if (_actionHistory.Count >= MaxLogHistory)
                {
                    _actionHistory.RemoveAt(0);
                }
                _actionHistory.Add(record);
            }

            ActionExecuted?.Invoke(record);
        }

        public void RecordBattleMessage(CombatMessageRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);

            lock (_lock)
            {
                if (_messageHistory.Count >= MaxLogHistory)
                {
                    _messageHistory.RemoveAt(0);
                }
                _messageHistory.Add(record);
            }

            BattleMessageReceived?.Invoke(record);
        }

        public void UpdateRecasts(in S2C_0x119_AbilRecast recastPacket)
        {
            if (!recastPacket.IsValid) return;

            lock (_lock)
            {
                Array.Clear(_abilityRecasts, 0, _abilityRecasts.Length);
                for (int i = 0; i < 31; i++)
                {
                    var timer = recastPacket.GetTimer(i);
                    if (timer.TimerId < _abilityRecasts.Length)
                    {
                        _abilityRecasts[timer.TimerId] = timer.TimerSeconds;
                    }
                }
                MountRecastSeconds = recastPacket.MountRecast;
            }

            RecastsUpdated?.Invoke();
        }

        public ushort GetAbilityRecast(byte timerId)
        {
            lock (_lock)
            {
                return timerId < _abilityRecasts.Length ? _abilityRecasts[timerId] : (ushort)0;
            }
        }

        public IReadOnlyList<CombatActionRecord> GetRecentActions()
        {
            lock (_lock)
            {
                return _actionHistory.ToArray();
            }
        }

        public IReadOnlyList<CombatMessageRecord> GetRecentMessages()
        {
            lock (_lock)
            {
                return _messageHistory.ToArray();
            }
        }
    }
}
