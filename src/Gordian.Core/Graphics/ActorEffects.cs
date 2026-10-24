// src/Gordian.Core/Graphics/ActorEffects.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;

namespace Gordian.Engine.Graphics
{
    /// <summary>
    /// One actor's running copy of its model's effects: an emitter per layer, the auto-running (idle) generators emitting
    /// from spawn, the rest waiting for <see cref="Play"/>. Particles are simulated in the actor's model space (raw DAT
    /// axes, Y down, around the actor's feet); the renderer places them with the actor's transform, offset to the joint
    /// reference each generator attaches to (<see cref="GetJointReference"/>).
    /// Attachment semantics referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer,
    /// ui/js/particle/runtime.js attachment source and ui/js/renderer.js getActorAttachPosition).
    /// </summary>
    public sealed class ActorEffectInstance
    {
        private readonly Dictionary<ZoneEmitterTemplate, ZoneParticleEmitter> _byTemplate = new(ReferenceEqualityComparer.Instance);
        private readonly List<(WeatherSkyLayer Layer, ZoneParticleEmitter Emitter)> _emitters = new();

        public ActorEffectInstance(ActorEffectSet effects, int seed = 0)
        {
            Effects = effects ?? throw new ArgumentNullException(nameof(effects));
            for (int i = 0; i < effects.Layers.Count; i++)
            {
                var layer = effects.Layers[i];
                var emitter = new ZoneParticleEmitter(layer.Emitter!, seed + i);
                // A state-driven model's state effects wait for their routine (#257).
                if (effects.StateTriggered.Contains(emitter.Template)) emitter.StartsOnTrigger = true;
                _emitters.Add((layer, emitter));
                _byTemplate[emitter.Template] = emitter;
            }
            foreach (var (_, emitter) in _emitters)
            {
                emitter.ChildResolver = template => _byTemplate.TryGetValue(template, out var child) ? child : null;
            }
        }

        public ActorEffectSet Effects { get; }

        public IReadOnlyList<(WeatherSkyLayer Layer, ZoneParticleEmitter Emitter)> Emitters => _emitters;

        /// <summary>
        /// Plays one of the model's routines on the actor (starts its generators at their routine times). Returns false
        /// when the model has no routine of that name.
        /// </summary>
        public bool Play(string routineId)
        {
            if (string.IsNullOrEmpty(routineId) || !Effects.Routines.TryGetValue(routineId, out var routine)) return false;
            foreach (var spawn in routine.Spawns)
            {
                if (_byTemplate.TryGetValue(spawn.Template, out var emitter)) emitter.Trigger(spawn.StartFrame, spawn.Duration);
            }
            return true;
        }

        /// <summary>
        /// Advances every emitter by <paramref name="frames"/> 60 Hz effect frames. <paramref name="frame"/> carries the
        /// camera in the actor's model space.
        /// </summary>
        public void Update(float frames, in ZoneParticleFrame frame)
        {
            foreach (var (_, emitter) in _emitters) emitter.Update(frames, frame);
        }

        /// <summary>
        /// The skeleton joint reference a generator attaches to: the source reference (attach bits 4-9) for source-side
        /// attach types, the target reference (bits 10-15) for target-side ones. Until actions bind a separate target,
        /// target-side generators ride this actor too.
        /// </summary>
        public static int GetJointReference(ParticleGeneratorDefinition generator) => generator.AttachType switch
        {
            ParticleAttachType.TargetActor or ParticleAttachType.TargetActorSourceFacing or ParticleAttachType.TargetToSourceBasis
                => generator.AttachedJoint1,
            _ => generator.AttachedJoint0
        };
    }
}
