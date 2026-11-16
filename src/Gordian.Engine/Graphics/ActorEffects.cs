// src/Gordian.Engine/Graphics/ActorEffects.cs
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
        private readonly Dictionary<ZoneEmitterTemplate, ZoneEmitterTemplate> _parentOf = new(ReferenceEqualityComparer.Instance);

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
                foreach (var child in emitter.Template.Children.Values)
                {
                    if (!ReferenceEquals(child, emitter.Template)) _parentOf.TryAdd(child, emitter.Template);
                }
            }
            // A state-driven model appears in its idle state: the routines its ids0 links (the campfire's flames, #425).
            foreach (string link in effects.IdleStateLinks) Play(link);
            // A worn weapon's equip routines (the Afterglow, #8): the repeating ones replay at their own length.
            foreach (var start in effects.EquippedRoutines)
            {
                if (!Effects.Routines.TryGetValue(start.RoutineId, out var routine)) continue;
                PlayAt(routine, start.StartFrame);
                if (start.Repeats && routine.TotalFrames > 0) _loops.Add(new RoutineLoop(routine, start.StartFrame));
            }
        }

        private sealed class RoutineLoop(WeatherRoutineVariant routine, float firstStart)
        {
            public WeatherRoutineVariant Routine { get; } = routine;

            /// <summary>Frames until the routine starts over.</summary>
            public float UntilNext { get; set; } = firstStart + routine.TotalFrames;
        }

        private readonly List<RoutineLoop> _loops = new();

        /// <summary>Whether <see cref="Stop"/> has ended the instance's emission; it is dropped once its particles are gone.</summary>
        public bool IsStopping { get; private set; }

        /// <summary>Whether any emitter still has live particles.</summary>
        public bool HasParticles
        {
            get
            {
                foreach (var (_, emitter) in _emitters)
                {
                    if (emitter.Particles.Count > 0) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Ends every loop and every emitter's emission; live particles finish their lives. A weapon taken off plays its
        /// <c>!kl?</c>, which stops its generators with op 0x2D the same way.
        /// </summary>
        public void Stop()
        {
            IsStopping = true;
            _loops.Clear();
            foreach (var (_, emitter) in _emitters) emitter.StopEmitting();
        }

        public ActorEffectSet Effects { get; }

        public IReadOnlyList<(WeatherSkyLayer Layer, ZoneParticleEmitter Emitter)> Emitters => _emitters;

        /// <summary>
        /// The generator whose attachment places <paramref name="template"/>'s particles: its own for a generator a routine
        /// spawns, its parent's (up the chain) for a child-only one. A child particle is born at its parent particle's
        /// position (0x45) or its own base, in the space of the generator that emitted the parent, whatever attach its own
        /// header names. Retail level up (<c>ROM/13/35</c>): the text <c>g000</c> rides joint reference 1 and its trail
        /// child <c>g004</c> names no attach, so placing the child by its own (the model origin) drew a second "Level Up !!"
        /// at the feet (#581 round 2). Child placement referenced from xi-model-viewer
        /// (https://github.com/vekien/xi-model-viewer, ui/js/particle/runtime.js: a child takes its creator's associated
        /// position, <c>useParentAssociatedPositionOnly</c>).
        /// </summary>
        public ParticleGeneratorDefinition PlacementOf(ZoneEmitterTemplate template)
        {
            ArgumentNullException.ThrowIfNull(template);
            var current = template;
            for (int depth = 0; depth < 8 && current.ChildOnly && _parentOf.TryGetValue(current, out var parent); depth++) current = parent;
            return current.Definition;
        }

        /// <summary>
        /// Plays one of the model's routines on the actor (starts its generators at their routine times). Returns false
        /// when the model has no routine of that name.
        /// </summary>
        public bool Play(string routineId)
        {
            if (string.IsNullOrEmpty(routineId) || !Effects.Routines.TryGetValue(routineId, out var routine)) return false;
            PlayAt(routine, 0);
            return true;
        }

        private void PlayAt(WeatherRoutineVariant routine, int delayFrames)
        {
            foreach (var spawn in routine.Spawns)
            {
                if (_byTemplate.TryGetValue(spawn.Template, out var emitter)) emitter.Trigger(delayFrames + spawn.StartFrame, spawn.Duration);
            }
        }

        /// <summary>
        /// Advances every emitter by <paramref name="frames"/> 60 Hz effect frames. <paramref name="frame"/> carries the
        /// camera in the actor's model space.
        /// </summary>
        public void Update(float frames, in ZoneParticleFrame frame)
        {
            foreach (var loop in _loops)
            {
                loop.UntilNext -= frames;
                while (loop.UntilNext <= 0f)
                {
                    // Started where it fell in this step, so the cycle keeps its length.
                    PlayAt(loop.Routine, Math.Max(0, (int)MathF.Round(loop.UntilNext + frames)));
                    loop.UntilNext += loop.Routine.TotalFrames;
                }
            }
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
