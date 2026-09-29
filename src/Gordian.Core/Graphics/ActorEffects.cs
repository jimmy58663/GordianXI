// src/Gordian.Core/Graphics/ActorEffects.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Graphics
{
    /// <summary>
    /// The particle effects a model DAT carries for its actor: every Section 0x05 generator that draws a particle mesh or
    /// sprite sheet (as an emitter layer), the textures they sample, and the Section 0x07 routines the client plays on the
    /// actor. Effect-only models such as the Home Point crystal (model 51, <c>ROM/3/25.DAT</c>) are drawn entirely by
    /// their auto-running generators; their skeleton mesh is a placeholder.
    /// Effect-only entities referenced from xi-tools (docs/dats/ROM_3_25.md, docs/entity/npc-look.md).
    /// </summary>
    public sealed class ActorEffectSet
    {
        public ActorEffectSet(
            IReadOnlyList<WeatherSkyLayer> layers,
            IReadOnlyList<WeatherRoutineVariant> routines,
            IReadOnlyDictionary<string, DecodedTexture> textures)
        {
            Layers = layers ?? throw new ArgumentNullException(nameof(layers));
            Textures = textures ?? throw new ArgumentNullException(nameof(textures));
            var byName = new Dictionary<string, WeatherRoutineVariant>(StringComparer.OrdinalIgnoreCase);
            foreach (var routine in routines ?? throw new ArgumentNullException(nameof(routines))) byName.TryAdd(routine.DatId, routine);
            Routines = byName;
        }

        /// <summary>
        /// Emitter layers (child-only layers included), each with its <see cref="WeatherSkyLayer.Emitter"/> template and
        /// display-space mesh groups.
        /// </summary>
        public IReadOnlyList<WeatherSkyLayer> Layers { get; }

        /// <summary>
        /// The model's routines by FourCC (e.g. <c>aper</c> idle, <c>bind</c> Home Point activation).
        /// </summary>
        public IReadOnlyDictionary<string, WeatherRoutineVariant> Routines { get; }

        public IReadOnlyDictionary<string, DecodedTexture> Textures { get; }
    }

    /// <summary>
    /// Builds an <see cref="ActorEffectSet"/> from a model DAT with the zone effect parser in actor mode.
    /// </summary>
    public static class ActorEffectLoader
    {
        /// <summary>
        /// Returns the model's effects, or null when it has no generator that draws anything.
        /// </summary>
        public static ActorEffectSet? Load(ReadOnlySpan<byte> datBytes, SharedEffectResources? sharedEffects = null)
        {
            if (datBytes.IsEmpty) return null;
            // Most models carry no generators: skip the full parse (and a second decode of their body textures).
            bool hasGenerator = false;
            foreach (var header in DatSectionWalker.ReadHeaders(datBytes))
            {
                if (header.TypeCode == DatSectionType.ParticleGenerator) { hasGenerator = true; break; }
            }
            if (!hasGenerator) return null;

            var decoded = new Dictionary<string, DecodedTexture>(StringComparer.OrdinalIgnoreCase);
            var parsed = ZoneDataLoader.ParseZoneContainer(datBytes, zoneId: -1, outTextures: decoded, sharedEffects: sharedEffects, actorEffects: true);

            var layers = new List<WeatherSkyLayer>();
            var textures = new Dictionary<string, DecodedTexture>(StringComparer.OrdinalIgnoreCase);
            foreach (var layer in parsed.EffectLayers)
            {
                if (layer.Emitter == null || layer.MeshGroups.Count == 0) continue;
                layers.Add(layer);
                // Keep only the textures the effect meshes sample, under the trimmed name the texture cache looks up.
                foreach (var group in layer.MeshGroups)
                {
                    string name = group.TextureName.Trim();
                    if (name.Length > 0 && !textures.ContainsKey(name) && FindTexture(decoded, name) is { } texture) textures[name] = texture;
                }
            }
            return layers.Count == 0 ? null : new ActorEffectSet(layers, parsed.ActorRoutines, textures);
        }

        private static DecodedTexture? FindTexture(Dictionary<string, DecodedTexture> decoded, string name)
        {
            if (decoded.TryGetValue(name, out var exact)) return exact;
            foreach (var (key, texture) in decoded)
            {
                if (key.EndsWith(name, StringComparison.OrdinalIgnoreCase) || name.EndsWith(key, StringComparison.OrdinalIgnoreCase)) return texture;
            }
            return null;
        }
    }

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

        /// <summary>
        /// Position of skeleton joint reference <paramref name="referenceIndex"/> in model space for a posed skeleton:
        /// the reference's joint plus its offset (scaled and turned by the joint). Returns the model origin when the
        /// skeleton has no such reference or the pose lacks the joint.
        /// </summary>
        public static Vector3 ResolveJointReference(
            Skeleton? skeleton,
            ReadOnlySpan<Vector3> translations,
            ReadOnlySpan<Quaternion> rotations,
            ReadOnlySpan<Vector3> scales,
            int referenceIndex)
        {
            if (skeleton == null || referenceIndex < 0 || referenceIndex >= skeleton.References.Count) return Vector3.Zero;
            var reference = skeleton.References[referenceIndex];
            int joint = reference.Index;
            if (joint >= translations.Length) return Vector3.Zero;
            Vector3 offset = reference.Offset * (joint < scales.Length ? scales[joint] : Vector3.One);
            if (joint < rotations.Length) offset = Vector3.Transform(offset, rotations[joint]);
            return translations[joint] + offset;
        }
    }
}
