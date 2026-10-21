// src/Gordian.App/Graphics/ZoneTerrainRenderer.AttachedEffects.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Diagnostics;
using Gordian.Core.Graphics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.World;
using Gordian.Core.World.Collision;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Zone generators that ride an actor (#232) and doors' own effect routines (#227): a map scheduler, an event's 0x2D
    /// or a door's routine binds each actor-attached emitter it starts to an entity (<see cref="ZoneRoutineActors"/>);
    /// the emitter is simulated in that entity's model space and drawn at it, like a model's own effects
    /// (<see cref="ActorEffectInstance"/>). An actor that is not drawn (a door, an invisible marker) is placed by its
    /// server position and heading.
    /// </summary>
    public sealed partial class ZoneTerrainRenderer
    {
        /// <summary>Where an actor-attached zone emitter is: its entity and, once found this frame, its model frame.</summary>
        private sealed class AttachedBinding
        {
            public uint ServerId;
            public bool Placed;
            public Matrix4x4 ModelToWorld;
            public ActorAnchor? Anchor;
        }

        private Dictionary<ZoneParticleEmitter, WeatherSkyLayer> _attachedLayers = new(ReferenceEqualityComparer.Instance);
        private Dictionary<ZoneParticleEmitter, AttachedBinding> _attachedBindings = new(ReferenceEqualityComparer.Instance);
        private DoorEffectTrigger? _doorEffects;
        private Action<ZoneParticleEmitter, ZoneRoutineActors>? _bindAttached;

        /// <summary>Actor-attached zone emitters bound to an entity now (tests and the performance overlay).</summary>
        internal int BoundAttachedEmitters => _attachedBindings.Count;

        /// <summary>Binds an emitter a routine started (and its children) to the entity its attach type picks.</summary>
        private void BindAttached(ZoneParticleEmitter emitter, ZoneRoutineActors actors)
        {
            if (!_attachedLayers.TryGetValue(emitter, out var layer)) return;
            Bind(emitter, actors.ActorFor(layer.AttachType), 0);

            void Bind(ZoneParticleEmitter target, uint serverId, int depth)
            {
                if (serverId == 0) _attachedBindings.Remove(target);
                else if (!_attachedBindings.TryGetValue(target, out var binding) || binding.ServerId != serverId)
                {
                    // A new actor: what the emitter left at the old one goes.
                    target.Particles.Clear();
                    _attachedBindings[target] = new AttachedBinding { ServerId = serverId };
                }
                if (depth >= 4) return;
                foreach (var child in target.Template.Children.Values)
                {
                    if (_emittersByTemplate.TryGetValue(child, out var childEmitter) && _attachedLayers.ContainsKey(childEmitter)) Bind(childEmitter, serverId, depth + 1);
                }
            }
        }

        /// <summary>
        /// Plays a map scheduler (S2C 0x039) or an event's zone scheduler (0x2D / 0x60 sub 2): on a door entity, a door
        /// routine of that door's directory (and <c>open</c> / <c>clos</c> move its leaves, #233); else every routine of
        /// that name in the zone, on the request's actors. False when nothing of that name exists.
        /// </summary>
        private bool PlayMapScheduler(MapSchedulerRequest request)
        {
            if (_mapRoutines == null) return false;
            if (request.Stop) return _mapRoutines.Stop(request.Routine);
            if (World != null && _doorEffects != null && DoorEffectTrigger.IsDoorRoutine(request.Routine) &&
                World.TryGetByServerId(request.CasterServerId, out var caster) && caster.Type == EntityType.Door && caster.TransportId.Length > 0)
            {
                bool played = _doorEffects.Play(caster.TransportId, caster.ServerId, request.Routine, _mapRoutines);
                if (request.Routine is "open" or "clos")
                {
                    double now = System.Diagnostics.Stopwatch.GetTimestamp() * TimestampSeconds;
                    _doorAnimator?.SetOpen(caster.TransportId, request.Routine == "open", now);
                    played |= _doorAnimator != null;
                }
                return played;
            }
            return _mapRoutines.Play(request.Routine, new ZoneRoutineActors(request.CasterServerId, request.TargetServerId));
        }

        /// <summary>
        /// Before the zone's routines run: plays doors' routines on a status change, and binds the auto-running
        /// actor-attached generators of a door directory (the door's own glow) to that door's entity.
        /// </summary>
        private void UpdateDoorEffects()
        {
            if (World == null || _mapRoutines == null) return;
            _doorEffects?.Update(World, _mapRoutines);
            if (_attachedLayers.Count == 0) return;
            Dictionary<string, WorldEntity>? doors = null;
            foreach (var (emitter, layer) in _attachedLayers)
            {
                if (layer.OwnerDoorId == null || !emitter.Template.Definition.AutoRun || _attachedBindings.ContainsKey(emitter)) continue;
                doors ??= ZoneDoors.DoorEntities(World);
                if (doors.TryGetValue(layer.OwnerDoorId, out var door)) BindAttached(emitter, new ZoneRoutineActors(door.ServerId, door.ServerId));
            }
        }

        /// <summary>
        /// Advances the actor-attached zone emitters in their entity's model space (the camera taken into it); an unbound
        /// one, or one whose entity is gone, does not emit. Bindings whose emitter has nothing more to do are dropped.
        /// </summary>
        private void UpdateAttachedEmitters(ViewportCamera camera, ZoneEnvironmentSettings environment, float frames)
        {
            if (_attachedLayers.Count == 0) return;
            float dayFraction = environment.TimeOfDayHours / 24.0f;
            Vector3 daylight = StrongestLight(environment);
            int dayOfWeek = CurrentDayOfWeek();
            int moonPhase = CurrentMoonPhaseIndex();
            foreach (var (emitter, _) in _attachedLayers)
            {
                if (!_attachedBindings.TryGetValue(emitter, out var binding) || !TryPlaceActor(binding))
                {
                    if (emitter.Particles.Count > 0) emitter.Particles.Clear();
                    continue;
                }
                if (!Matrix4x4.Invert(binding.ModelToWorld, out var worldToModel)) continue;
                var frame = new ZoneParticleFrame(Vector3.Transform(camera.Position, worldToModel), dayFraction, daylight,
                    Vector3.Normalize(Vector3.TransformNormal(camera.Forward, worldToModel)), dayOfWeek, moonPhase);
                emitter.Update(frames, frame);
            }
        }

        /// <summary>The binding's entity frame this frame: where the entity renderer drew it, else its server place.</summary>
        private bool TryPlaceActor(AttachedBinding binding)
        {
            if (_entityRenderer != null && _entityRenderer.ActorAnchors.TryGetValue(binding.ServerId, out var anchor))
            {
                binding.ModelToWorld = anchor.ModelToWorld;
                binding.Anchor = anchor;
                binding.Placed = true;
                return true;
            }
            binding.Anchor = null;
            if (World != null && World.TryGetByServerId(binding.ServerId, out var entity) && entity.IsSpawned)
            {
                var position = new Vector3(-entity.Position.X, -entity.Position.Y, entity.Position.Z);
                binding.ModelToWorld = EntityModelFrame * Matrix4x4.CreateRotationY(-entity.HeadingRadians - MathF.PI) * Matrix4x4.CreateTranslation(position);
                binding.Placed = true;
                return true;
            }
            binding.Placed = false;
            return false;
        }

        /// <summary>A routine started a generator with no emitter (a sound): the audio side plays it (round 4 E2).</summary>
        private void PostRoutineSound(string directory, string generator, ZoneRoutineActors actors) =>
            World?.PostZoneSound(new ZoneSoundRequest(directory, generator, actors.CasterServerId, actors.TargetServerId));

        /// <summary>Queues the live particles of the actor-attached zone emitters, each at its entity's joint reference.</summary>
        private void AddAttachedEffectDraws(ViewportCamera camera)
        {
            if (_attachedBindings.Count == 0) return;
            foreach (var effectMesh in _effectSubmeshes)
            {
                if (!effectMesh.Layer.IsActorAttached || !_emitters.TryGetValue(effectMesh.Layer, out var emitter) || emitter.Particles.Count == 0 ||
                    !_attachedBindings.TryGetValue(emitter, out var binding) || !binding.Placed) continue;
                Vector3 joint = binding.Anchor is { } posed
                    ? ActorEffectInstance.ResolveJointReference(posed.Skeleton, posed.Pose.Translations, posed.Pose.Rotations, posed.Pose.Scales,
                        ActorEffectInstance.GetJointReference(emitter.Template.Definition))
                    : Vector3.Zero;
                Matrix4x4 frame = DisplayFlip * Matrix4x4.CreateTranslation(joint) * binding.ModelToWorld;
                _effectDrawList.Add(new EffectDraw(effectMesh, Vector3.Distance(camera.Position, binding.ModelToWorld.Translation), emitter, frame));
            }
        }

        private static void LogDoorEffects(int doors, int attached, ushort zoneId)
        {
            if (doors > 0 || attached > 0) GordianLog.Debug("Graphics", $"Zone {zoneId}: {doors} doors with effect routines, {attached} actor-attached emitters.");
        }
    }
}
