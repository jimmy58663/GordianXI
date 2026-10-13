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
        private readonly ushort[] _abilityRecasts = new ushort[256];
        private long _abilityRecastStamp;

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

        /// <summary>The server picked the character's target (S2C 0x058): its server id, 0 for none.</summary>
        public event Action<uint>? AssistTargetReceived;

        #endregion

        /// <summary>The target the server last picked for the character (S2C 0x058), 0 before any.</summary>
        public uint AssistTargetServerId { get; private set; }

        /// <summary>Records the target the server picked (S2C 0x058) and raises <see cref="AssistTargetReceived"/>.</summary>
        public void ApplyAssistTarget(uint serverId)
        {
            lock (_lock) AssistTargetServerId = serverId;
            AssistTargetReceived?.Invoke(serverId);
        }

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

        /// <summary>
        /// Stores the ability recasts of S2C 0x119 (seconds left per recast id, counted from now). Timer 0 is the
        /// two-hour ability's (LandSandBoat <c>0x119_abil_recast.cpp</c> writes <c>Recast::Special</c> there); the other
        /// slots carry their recast id, and an unused slot (id 0, 0 s) must not clear the two-hour timer.
        /// </summary>
        public void UpdateRecasts(in S2C_0x119_AbilRecast recastPacket)
        {
            if (!recastPacket.IsValid) return;

            lock (_lock)
            {
                Array.Clear(_abilityRecasts, 0, _abilityRecasts.Length);
                for (int i = 0; i < 31; i++)
                {
                    var timer = recastPacket.GetTimer(i);
                    if (i > 0 && timer.TimerId == 0 && timer.TimerSeconds == 0) continue;
                    if (timer.TimerId < _abilityRecasts.Length)
                    {
                        _abilityRecasts[timer.TimerId] = timer.TimerSeconds;
                    }
                }
                MountRecastSeconds = recastPacket.MountRecast;
                _abilityRecastStamp = Clock();
            }

            RecastsUpdated?.Invoke();
        }

        /// <summary>The seconds S2C 0x119 last reported for a recast id (not counted down).</summary>
        public ushort GetAbilityRecast(byte timerId)
        {
            lock (_lock)
            {
                return timerId < _abilityRecasts.Length ? _abilityRecasts[timerId] : (ushort)0;
            }
        }

        /// <summary>
        /// The time left on an ability's recast (<see cref="Gordian.Core.Resources.Tables.CommandRecord.RecastId"/>):
        /// the last S2C 0x119 value counted down since it arrived; zero when ready.
        /// </summary>
        public TimeSpan GetAbilityRecastRemaining(int recastId)
        {
            lock (_lock)
            {
                if (recastId < 0 || recastId >= _abilityRecasts.Length || _abilityRecasts[recastId] == 0) return TimeSpan.Zero;
                var left = TimeSpan.FromSeconds(_abilityRecasts[recastId]) - Elapsed(_abilityRecastStamp);
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }

        private readonly Dictionary<ushort, (long Stamp, uint Seconds)> _spellRecasts = new();

        /// <summary>The clock recasts count down by (<see cref="System.Diagnostics.Stopwatch"/> ticks); replaceable for tests.</summary>
        public Func<long> Clock { get; set; } = System.Diagnostics.Stopwatch.GetTimestamp;

        private TimeSpan Elapsed(long stamp) =>
            TimeSpan.FromSeconds((Clock() - stamp) / (double)System.Diagnostics.Stopwatch.Frequency);

        /// <summary>
        /// A spell's recast starts (the local player's S2C 0x028 category 4, whose header carries the recast in seconds;
        /// LandSandBoat <c>battle_entity.cpp</c> sets it from <c>CMagicState::GetRecast</c>). The server sends no separate
        /// spell recast list, so the client keeps it from these.
        /// </summary>
        public void SetSpellRecast(ushort spellId, uint seconds)
        {
            lock (_lock)
            {
                if (seconds == 0) _spellRecasts.Remove(spellId);
                else _spellRecasts[spellId] = (Clock(), seconds);
            }
            RecastsUpdated?.Invoke();
        }

        /// <summary>The time left on a spell's recast; zero when ready.</summary>
        public TimeSpan GetSpellRecastRemaining(ushort spellId)
        {
            lock (_lock)
            {
                if (!_spellRecasts.TryGetValue(spellId, out var recast)) return TimeSpan.Zero;
                var left = TimeSpan.FromSeconds(recast.Seconds) - Elapsed(recast.Stamp);
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
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
