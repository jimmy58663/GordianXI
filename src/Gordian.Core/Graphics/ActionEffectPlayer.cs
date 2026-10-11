// src/Gordian.Core/Graphics/ActionEffectPlayer.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Animation;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.World;

namespace Gordian.Core.Graphics
{
    /// <summary>
    /// Plays the particle effects of one action presentation (#132: a weapon skill, monster move, job ability or spell DAT)
    /// from its <c>main</c> routine: the same command walk as a cutscene scene file (<see cref="SceneEffectPlayer"/>:
    /// generator starts 0x02, routine starts 0x03 / 0x73, kills 0x1E / 0x3F / 0x5F), with each generator placed on the
    /// actor it attaches to: source-side ones on the actor (its attach bits 4-9 joint reference), target-side ones
    /// (<see cref="ParticleAttachType.TargetActor"/>, <see cref="ParticleAttachType.TargetActorSourceFacing"/>,
    /// <see cref="ParticleAttachType.TargetToSourceBasis"/>) on the action's first target (bits 10-15).
    /// Attach semantics referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer, ui/js/renderer.js
    /// getActorAttachPosition) and xi-tools (docs/fx/effect_system.md).
    /// </summary>
    public sealed class ActionEffectPlayer
    {
        public ActionEffectPlayer(ActionAsset asset, ActorEffectSet effects, uint actorId, uint targetId, int seed = 0)
        {
            ArgumentNullException.ThrowIfNull(asset);
            if (asset.Effects == null) throw new ArgumentException("The presentation has no effect routines.", nameof(asset));
            Asset = asset;
            Effects = effects ?? throw new ArgumentNullException(nameof(effects));
            ActorId = actorId;
            TargetId = targetId != 0 ? targetId : actorId;
            Player = new SceneEffectPlayer(asset.Effects, effects, seed);
        }

        public ActionAsset Asset { get; }

        public ActorEffectSet Effects { get; }

        /// <summary>The actor performing the action (source-side generators ride it).</summary>
        public uint ActorId { get; }

        /// <summary>The action's first target (target-side generators ride it); the actor itself when there is none.</summary>
        public uint TargetId { get; }

        public SceneEffectPlayer Player { get; }

        /// <summary>Frames played so far.</summary>
        public float Age { get; private set; }

        /// <summary>Starts the presentation's <c>main</c>; false when it has none.</summary>
        public bool Start() => Player.Start(0, ActionAsset.MainRoutine);

        /// <summary>Whether everything has played out (no routine running, no particle alive).</summary>
        public bool IsFinished => Player.IsIdle;

        /// <summary>Whether a generator rides the action's target rather than its actor.</summary>
        public static bool IsTargetSide(ParticleGeneratorDefinition generator) => generator.AttachType is
            ParticleAttachType.TargetActor or ParticleAttachType.TargetActorSourceFacing or ParticleAttachType.TargetToSourceBasis;

        /// <summary>
        /// Runs the commands due in the next <paramref name="frames"/> 60 Hz frames and advances the generators, each in its
        /// own actor's model space (<paramref name="actorFrame"/> / <paramref name="targetFrame"/> carry the camera there) or
        /// the camera's (<paramref name="cameraFrame"/>, for camera-following generators).
        /// </summary>
        public void Update(float frames, in ZoneParticleFrame actorFrame, in ZoneParticleFrame targetFrame, in ZoneParticleFrame cameraFrame)
        {
            Age += frames;
            Player.Advance(frames);
            foreach (var (_, emitter) in Player.Instance.Emitters)
            {
                var template = emitter.Template;
                if (SceneEffectPlayer.IsCameraSpace(template)) emitter.Update(frames, cameraFrame);
                else if (IsTargetSide(template.Definition)) emitter.Update(frames, targetFrame);
                else emitter.Update(frames, actorFrame);
            }
        }
    }

    /// <summary>
    /// Notices when an entity starts an action that carries a presentation with effects (#132), so the renderer starts
    /// its <see cref="ActionEffectPlayer"/> once per action: each new <see cref="EntityAnimationState.ActionSerial"/> whose
    /// <see cref="EntityAnimationState.ActiveRequest"/> has an <see cref="ActionAsset"/> with effect routines.
    /// </summary>
    public sealed class ActionEffectTracker
    {
        /// <summary>An action first seen this far into its routine (the entity came into view) shows no effects.</summary>
        public const float MaxLateStartTicks = 15f;

        private readonly Dictionary<uint, int> _serials = new();
        private readonly HashSet<uint> _seen = new();
        private readonly List<uint> _stale = new();

        /// <summary>
        /// The request whose effects start now, or null: the entity began a new action with a presentation since the last
        /// call (or, seen for the first time, began it less than <see cref="MaxLateStartTicks"/> ago).
        /// </summary>
        public ActionRequest? Observe(WorldEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            _seen.Add(entity.ServerId);
            var animation = entity.Animation;
            int serial = animation.ActionSerial;
            bool known = _serials.TryGetValue(entity.ServerId, out int last);
            if (known && last == serial) return null;
            _serials[entity.ServerId] = serial;
            if (animation.ActiveRequest is not { Asset.Effects: not null } request) return null;
            if (!known && animation.ActionTicks > MaxLateStartTicks) return null;
            return request;
        }

        /// <summary>Forgets the entities not observed since the last call (call once per frame).</summary>
        public void EndFrame()
        {
            _stale.Clear();
            foreach (uint id in _serials.Keys)
            {
                if (!_seen.Contains(id)) _stale.Add(id);
            }
            foreach (uint id in _stale) _serials.Remove(id);
            _seen.Clear();
        }

        /// <summary>Forgets every entity (a zone change).</summary>
        public void Clear()
        {
            _serials.Clear();
            _seen.Clear();
        }
    }
}
