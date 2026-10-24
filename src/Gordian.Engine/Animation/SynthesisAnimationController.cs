// src/Gordian.Engine/Animation/SynthesisAnimationController.cs
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Gordian.Core.Animation;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Engine.Animation
{
    /// <summary>
    /// The synthesis animation (S2C 0x030, #315), as the maintainer's retail video <c>Synth_Wind_Fire_Lightning_Breaks.mp4</c>
    /// (2026-10-16/17) shows it: the crafter sits, waits about 2.4 s, raises the right hand with the crystal while the
    /// element effect plays for about 11 s, shows the result, stays seated about 1.4 s and stands up.
    /// <list type="bullet">
    /// <item>The seat: the crafter's server status 44 (<see cref="RestPosture.StatusSynthesis"/>) takes the Healing posture
    /// (<c>res0</c> sits, <c>res2</c> stands: Tarutaru's <c>rx2</c> is the video's roll onto the back). The packet's
    /// <c>Status</c> is put on the crafter at once, so it sits with the packet; the server's status update that ends the
    /// synthesis stands it up.</item>
    /// <item>The crystal and the effect: the element's file 4912 + <c>EffectNum</c>
    /// (<see cref="ActionMotionLibrary.ResolveSynthesis"/>) plays on the crafter as an action presentation
    /// <see cref="EffectDelaySeconds"/> after the packet: <c>frst</c> holds <c>ri0?</c> (the hand up) and starts the crystal
    /// and the element's loop, then the ending for <c>Type</c> (success, break, high quality) and <c>stcr</c>.</item>
    /// <item>The character's own 0x030 confirms the synthesis lock (<see cref="CraftingState.IsSynthesizing"/>); a zone change
    /// drops it.</item>
    /// </list>
    /// </summary>
    public sealed class SynthesisAnimationController
    {
        /// <summary>
        /// How long after S2C 0x030 the crystal effect starts: the video's crafter sits in about 0.4 s and the crystal appears
        /// about 2.4 s after it is seated, 20 ticks into <c>frst</c> (PROVISIONAL: what retail waits on is not known).
        /// </summary>
        public const double EffectDelaySeconds = 2.0;

        private WorldState? _world;
        private LocalPlayerState? _player;
        private CraftingState? _crafting;

        /// <summary>The library the effect files come from (the shared one by default; tests set their own).</summary>
        public ActionMotionLibrary Library { get; set; } = ActionMotionLibrary.Shared;

        /// <summary>How long to wait before the effect (tests set 0).</summary>
        public TimeSpan EffectDelay { get; set; } = TimeSpan.FromSeconds(EffectDelaySeconds);

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
            if (entity != null) _ = PlayEffectAsync(entity, (int)info.Effect, info.Param);
        }

        private async Task PlayEffectAsync(WorldEntity entity, int effectNum, int type)
        {
            try
            {
                if (EffectDelay > TimeSpan.Zero) await Task.Delay(EffectDelay).ConfigureAwait(false);
                var asset = Library.ResolveSynthesis(effectNum, type);
                if (asset == null)
                {
                    GordianLog.Debug("CRAFT", $"Synthesis effect 0x{effectNum:X2} on {entity.ServerId:X8}: no effect file.");
                    return;
                }
                GordianLog.Debug("CRAFT", $"Synthesis effect 0x{effectNum:X2} ({ActionMotionLibrary.SynthesisEnding(type)}) on {entity.ServerId:X8}: {asset}.");
                entity.Animation.EnqueueAction(new ActionRequest
                {
                    ActorId = entity.ServerId,
                    Category = ActionCategory.None,
                    Motion = ActionMotion.Routine,
                    Asset = asset,
                    ReceivedTimestamp = Stopwatch.GetTimestamp(),
                });
            }
            catch (Exception ex)
            {
                GordianLog.Warning("CRAFT", $"Synthesis effect on {entity.ServerId:X8} failed: {ex.Message}");
            }
        }
    }
}
