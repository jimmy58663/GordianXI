// src/Gordian.Core/Animation/SynthesisAnimationController.cs
using System;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Core.Animation
{
    /// <summary>
    /// The hand-off of the synthesis animation (S2C 0x030, #315). The crafter's body plays the seated posture of its server
    /// status 44 (<see cref="RestPosture.StatusSynthesis"/>, the Healing routines <c>res0</c> / <c>res2</c>): the packet's
    /// <c>Status</c> is put on the crafter's entity at once, so it sits down with the effect rather than with the next entity
    /// update, and the status update that ends the synthesis stands it up. The character's own 0x030 also confirms the
    /// synthesis lock (<see cref="CraftingState.IsSynthesizing"/>), and a zone change drops it. NOT done: the crystal in the
    /// raised hand and the element effect (wind ribbons, fire flame, lightning sphere; success flash and rays, break shatter):
    /// no routine for them was found in the DATs (2026-10-18: every routine and clip name of the install, the shared effects
    /// DAT ROM/0/0, the race bases and motion packs, files and directories named after synthesis); the element
    /// (<c>EffectNum</c> 0x10-0x17: water, wind, fire, earth, lightning, ice, light, dark, LandSandBoat
    /// <c>SynthesisEffect</c>) and result (<c>Type</c>) stay in <see cref="CombatState"/> for when it is found.
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
            WorldEntity? entity = null;
            if (_world != null && (info.ServerId == 0 || !_world.TryGetByServerId(info.ServerId, out entity) || entity == null))
            {
                _world.TryGetByTargetIndex(info.TargetIndex, out entity);
            }
            bool own = info.ServerId == _player.ServerId || (entity != null && entity.ServerId == _player.ServerId);
            // The crafter sits down now (status 44); the server's later status update stands it up.
            if (entity != null && info.ServerStatus == RestPosture.StatusSynthesis) entity.AnimationState = info.ServerStatus;
            if (own) _crafting?.ConfirmSynthesis();
        }
    }
}
