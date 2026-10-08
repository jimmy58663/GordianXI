// src/Gordian.Core/Animation/SynthesisAnimationController.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Core.Animation
{
    /// <summary>
    /// The routine names of the synthesis motion in the player races' base motion DAT (Hume male <c>ROM/32/58</c>).
    /// <para>
    /// <b>Provisional.</b> The base DAT holds nine pairs <c>lc0n</c> / <c>ls0n</c> (n 0 to 6, plus 10 and 11). The
    /// <c>lc</c> routines are long (800 ticks, 13.3 s: a short start clip, then a clip looped ten times) and hide the weapons
    /// (<c>hwso</c>); the <c>ls</c> routines are one short end clip (about 100 ticks). Each uses its own clip family
    /// (<c>sf</c>, <c>sh</c>, <c>gu</c>, <c>na</c>, <c>yu</c>), and S2C 0x030 carries the result LandSandBoat computed when
    /// the synthesis started (<c>Type</c>: 0 fail, 1 success, 2-4 high quality). The mapping used here, routine
    /// <c>lc0(Type + 1)</c> to loop and <c>ls0(Type + 1)</c> to finish, is read from that structure and has not been
    /// compared with retail; <c>/playroutine</c> exists to compare the nine pairs in game.
    /// </para>
    /// </summary>
    public static class SynthesisMotion
    {
        /// <summary>The highest <c>lc0n</c> / <c>ls0n</c> index a result maps to.</summary>
        public const int MaxIndex = 6;

        private static int Index(byte type) => Math.Clamp(type + 1, 1, MaxIndex);

        /// <summary>The looping synthesis routine for an S2C 0x030 <c>Type</c> (the result the server computed).</summary>
        public static string StartRoutine(byte type) => $"lc{Index(type):D2}";

        /// <summary>The finishing routine for an S2C 0x030 <c>Type</c>.</summary>
        public static string EndRoutine(byte type) => $"ls{Index(type):D2}";
    }

    /// <summary>
    /// Plays the synthesis animation on the crafters: S2C 0x030 (the server sends it to the crafter and to everyone in
    /// range) starts <see cref="SynthesisMotion.StartRoutine"/> on the entity it names, and the synthesis result (S2C 0x06F
    /// for the character, 0x070 for others) plays <see cref="SynthesisMotion.EndRoutine"/>. The crystal's elemental effect
    /// (<c>EffectNum</c>) is not drawn yet. For the character it also confirms the synthesis lock
    /// (<see cref="CraftingState.IsSynthesizing"/>).
    /// </summary>
    public sealed class SynthesisAnimationController
    {
        private readonly Dictionary<uint, byte> _types = new();
        private readonly object _sync = new();
        private WorldState? _world;
        private LocalPlayerState? _player;
        private CraftingState? _crafting;

        /// <summary>The routines played, newest last (tests and the debug log read it).</summary>
        public string LastRoutine { get; private set; } = string.Empty;

        /// <summary>Subscribes to a session's crafting events.</summary>
        public void Attach(CombatState combat, CraftingState crafting, WorldState world, LocalPlayerState player)
        {
            ArgumentNullException.ThrowIfNull(combat);
            _crafting = crafting ?? throw new ArgumentNullException(nameof(crafting));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _player = player ?? throw new ArgumentNullException(nameof(player));
            combat.CraftEffectChanged += OnCraftEffect;
            crafting.SynthesisCompleted += OnOwnCompleted;
            crafting.OtherSynthesisCompleted += OnOtherCompleted;
            world.ZoneChanged += _ =>
            {
                lock (_sync) _types.Clear();
                crafting.EndSynthesis();
            };
        }

        private void OnCraftEffect(CraftEffectInfo info)
        {
            if (info.Effect == SynthesisEffect.None) return;
            var entity = Find(info.ServerId, info.TargetIndex);
            if (entity == null) return;
            lock (_sync) _types[entity.ServerId] = info.Param;
            if (_player != null && entity.ServerId == _player.ServerId) _crafting?.ConfirmSynthesis();
            Play(entity, SynthesisMotion.StartRoutine(info.Param));
        }

        private void OnOwnCompleted(SynthesisOutcome outcome)
        {
            var entity = _player == null ? null : Find(_player.ServerId, 0);
            if (entity != null) PlayEnd(entity);
        }

        private void OnOtherCompleted(OtherSynthesisOutcome outcome)
        {
            // S2C 0x070 names the crafter by index (and the low 16 bits of the id).
            var entity = Find(0, outcome.TargetIndex);
            if (entity != null) PlayEnd(entity);
        }

        private void PlayEnd(WorldEntity entity)
        {
            byte type;
            lock (_sync)
            {
                if (!_types.Remove(entity.ServerId, out type)) return; // no synthesis animation was started on it
            }
            Play(entity, SynthesisMotion.EndRoutine(type));
        }

        /// <summary>Plays a named routine of the entity's model on it, replacing its current gesture.</summary>
        public void Play(WorldEntity entity, string routine)
        {
            ArgumentNullException.ThrowIfNull(entity);
            LastRoutine = routine;
            entity.Animation.EnqueueAction(new ActionRequest
            {
                ActorId = entity.ServerId,
                Motion = ActionMotion.EventMotion,
                Routine = routine,
                ReceivedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
            });
        }

        private WorldEntity? Find(uint serverId, ushort index)
        {
            var world = _world;
            if (world == null) return null;
            if (serverId != 0 && world.TryGetByServerId(serverId, out var entity) && entity != null) return entity;
            return index != 0 && world.TryGetByTargetIndex(index, out entity) ? entity : null;
        }
    }
}
