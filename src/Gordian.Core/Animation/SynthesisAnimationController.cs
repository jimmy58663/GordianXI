// src/Gordian.Core/Animation/SynthesisAnimationController.cs
using System;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Core.Animation
{
    /// <summary>
    /// The hand-off of the synthesis animation (S2C 0x030). It does <b>not</b> play an animation yet: the routine retail
    /// plays has not been identified. The lc0n / ls0n routines of the race motion DATs are the ranged attack start and
    /// finish (calg / shlg reach them through ops 0x76 / 0x77, n the ranged weapon's RangeType, #158), not a synthesis,
    /// and no routine of those DATs or of ROM/0/0 is named or shaped like a crafting pose. Better no animation than a
    /// wrong one; <c>/playroutine</c> remains for trying routines in game. What it does: the character's own 0x030
    /// confirms the synthesis lock (<see cref="CraftingState.IsSynthesizing"/>), and a zone change drops it. The
    /// element (<c>EffectNum</c>) and result (<c>Type</c>) stay in <see cref="CombatState"/> for the day the routine is found.
    /// </summary>
    public sealed class SynthesisAnimationController
    {
        private WorldState? _world;
        private LocalPlayerState? _player;
        private CraftingState? _crafting;

        /// <summary>Subscribes to a session's crafting events.</summary>
        public void Attach(CombatState combat, CraftingState crafting, WorldState world, LocalPlayerState player)
        {
            ArgumentNullException.ThrowIfNull(combat);
            _crafting = crafting ?? throw new ArgumentNullException(nameof(crafting));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _player = player ?? throw new ArgumentNullException(nameof(player));
            combat.CraftEffectChanged += OnCraftEffect;
            world.ZoneChanged += _ => crafting.EndSynthesis();
        }

        private void OnCraftEffect(CraftEffectInfo info)
        {
            if (info.Effect == SynthesisEffect.None || _player == null) return;
            bool own = info.ServerId == _player.ServerId
                || (info.ServerId == 0 && _world != null && _world.TryGetByTargetIndex(info.TargetIndex, out var e) && e != null && e.ServerId == _player.ServerId);
            if (own) _crafting?.ConfirmSynthesis();
        }
    }
}
