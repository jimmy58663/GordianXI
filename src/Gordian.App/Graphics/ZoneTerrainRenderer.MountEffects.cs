// src/Gordian.App/Graphics/ZoneTerrainRenderer.MountEffects.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources;
using Gordian.Core.World;
using Gordian.Engine.Graphics;

namespace Gordian.App.Graphics
{
    public sealed partial class ZoneTerrainRenderer
    {
        /// <summary>The effects each ridden mount's own routines started (the chocobo's dig, #437), by mount actor key.</summary>
        private readonly Dictionary<uint, ActorEffectInstance> _mountEffects = new();

        private readonly MountEffectTracker _mountEffectTracker = new();
        private readonly List<uint> _finishedMountEffects = new();

        private void ClearMountEffects()
        {
            _mountEffects.Clear();
            _mountEffectTracker.Clear();
        }

        /// <summary>
        /// Plays the particle effects a mount's own routine starts (#437): when a ridden mount starts a routine its DAT has
        /// effects for (<see cref="ResourceManager.GetMountEffects"/>: the chocobo's <c>chor</c> kicks up <c>chov</c> /
        /// <c>cho0</c> / <c>cho1</c>), an instance plays it on the mount's anchor (<see cref="MountModels.ActorKey"/>),
        /// simulated in the mount's model space as a model's own effects are. A mount no longer drawn takes them with it.
        /// </summary>
        private void UpdateMountEffects(ViewportCamera camera, ZoneEnvironmentSettings environment, IEnumerable<WorldEntity>? entities,
            ResourceManager? resourceManager, float deltaSeconds)
        {
            if (_entityRenderer == null) return;
            if (entities != null && resourceManager != null)
            {
                foreach (var entity in entities)
                {
                    if (!entity.IsSpawned || !_entityRenderer.Draws(entity) || _mountEffectTracker.Step(entity) is not { } routine) continue;
                    if (resourceManager.GetMountEffects(entity.Mount) is not { } effects || !effects.Routines.ContainsKey(routine)) continue;
                    uint key = MountModels.ActorKey(entity.ServerId);
                    if (!_mountEffects.TryGetValue(key, out var instance) || !ReferenceEquals(instance.Effects, effects))
                    {
                        instance = new ActorEffectInstance(effects, unchecked((int)key));
                        _mountEffects[key] = instance;
                        UploadActorEffectMeshes(effects);
                    }
                    instance.Play(routine);
                }
                _mountEffectTracker.EndFrame();
            }
            if (_mountEffects.Count == 0) return;

            float frames = Math.Clamp(deltaSeconds, 0.0f, 0.25f) * 60.0f;
            float dayFraction = environment.TimeOfDayHours / 24.0f;
            Vector3 daylight = StrongestLight(environment);
            int dayOfWeek = CurrentDayOfWeek();
            int moonPhase = CurrentMoonPhaseIndex();
            _finishedMountEffects.Clear();
            foreach (var (key, instance) in _mountEffects)
            {
                if (!_entityRenderer.ActorAnchors.TryGetValue(key, out var anchor) || !Matrix4x4.Invert(anchor.ModelToWorld, out var worldToModel))
                {
                    _finishedMountEffects.Add(key);
                    continue;
                }
                var frame = new ZoneParticleFrame(Vector3.Transform(camera.Position, worldToModel), dayFraction, daylight,
                    Vector3.Normalize(Vector3.TransformNormal(camera.Forward, worldToModel)), dayOfWeek, moonPhase);
                instance.Update(frames, frame);
            }
            foreach (uint key in _finishedMountEffects) _mountEffects.Remove(key);
        }

        /// <summary>Queues the live particles of the mounts' effects, each on its mount's joints.</summary>
        private void AddMountEffectDraws(ViewportCamera camera)
        {
            foreach (var (key, instance) in _mountEffects) AddActorInstanceDraws(camera, key, instance);
        }
    }
}
