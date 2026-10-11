// src/Gordian.App/Graphics/ZoneTerrainRenderer.ActionEffects.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Graphics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.World;

namespace Gordian.App.Graphics
{
    public sealed partial class ZoneTerrainRenderer
    {
        /// <summary>At most this many action presentations play their effects at once; the oldest give way.</summary>
        private const int MaxActionEffects = 48;

        /// <summary>An action's effects that have not finished after this many frames are dropped (a looping generator).</summary>
        private const float MaxActionEffectFrames = 60f * 20f;

        /// <summary>One action presentation's effects with the places it was last drawn at.</summary>
        private sealed class ActionEffectSlot
        {
            public required ActionEffectPlayer Player;
            public Matrix4x4 ActorToWorld;
            public Matrix4x4 TargetToWorld;
        }

        private readonly List<ActionEffectSlot> _actionEffects = new();
        private readonly ActionEffectTracker _actionEffectTracker = new();

        private void ClearActionEffects()
        {
            _actionEffects.Clear();
            _actionEffectTracker.Clear();
        }

        /// <summary>
        /// Plays the particle effects of weapon skills, monster moves, job abilities and spells (#132): starts one
        /// <see cref="ActionEffectPlayer"/> when a drawn entity begins an action whose presentation DAT has effect routines,
        /// and advances each in its actor's model space (target-side generators in the target's).
        /// </summary>
        private void UpdateActionEffects(ViewportCamera camera, ZoneEnvironmentSettings environment, IEnumerable<WorldEntity>? entities,
            ResourceManager? resourceManager, float deltaSeconds)
        {
            if (!EnableZoneEffects || _entityRenderer == null)
            {
                ClearActionEffects();
                return;
            }

            if (entities != null && resourceManager != null)
            {
                foreach (var entity in entities)
                {
                    if (!entity.IsSpawned || !entity.IsDrawn) continue;
                    if (_actionEffectTracker.Observe(entity) is not { Asset: { } asset } request) continue;
                    var effects = resourceManager.GetSceneEffects(asset.FileId);
                    if (effects == null) continue;
                    var player = new ActionEffectPlayer(asset, effects, entity.ServerId, request.PrimaryTargetId,
                        unchecked(asset.FileId * 31 + (int)entity.ServerId + entity.Animation.ActionSerial));
                    if (!player.Start()) continue;
                    UploadActorEffectMeshes(effects);
                    if (_actionEffects.Count >= MaxActionEffects) _actionEffects.RemoveAt(0);
                    var slot = new ActionEffectSlot { Player = player };
                    PlaceActionEffect(slot);
                    _actionEffects.Add(slot);
                }
                _actionEffectTracker.EndFrame();
            }
            if (_actionEffects.Count == 0) return;

            float frames = Math.Clamp(deltaSeconds, 0.0f, 0.25f) * 60.0f;
            float dayFraction = environment.TimeOfDayHours / 24.0f;
            Vector3 daylight = StrongestLight(environment);
            int dayOfWeek = CurrentDayOfWeek();
            int moonPhase = CurrentMoonPhaseIndex();
            var cameraFrame = new ZoneParticleFrame(Vector3.Zero, dayFraction, daylight, Vector3.UnitZ, dayOfWeek, moonPhase);
            for (int i = _actionEffects.Count - 1; i >= 0; i--)
            {
                var slot = _actionEffects[i];
                PlaceActionEffect(slot);
                if (!Matrix4x4.Invert(slot.ActorToWorld, out var worldToActor) || !Matrix4x4.Invert(slot.TargetToWorld, out var worldToTarget))
                {
                    _actionEffects.RemoveAt(i);
                    continue;
                }
                var actorFrame = new ZoneParticleFrame(Vector3.Transform(camera.Position, worldToActor), dayFraction, daylight,
                    Vector3.Normalize(Vector3.TransformNormal(camera.Forward, worldToActor)), dayOfWeek, moonPhase);
                var targetFrame = new ZoneParticleFrame(Vector3.Transform(camera.Position, worldToTarget), dayFraction, daylight,
                    Vector3.Normalize(Vector3.TransformNormal(camera.Forward, worldToTarget)), dayOfWeek, moonPhase);
                slot.Player.Update(frames, actorFrame, targetFrame, cameraFrame);
                if (slot.Player.IsFinished || slot.Player.Age > MaxActionEffectFrames) _actionEffects.RemoveAt(i);
            }
        }

        /// <summary>Takes the actor's and target's current placement when they are drawn; otherwise keeps the last one.</summary>
        private void PlaceActionEffect(ActionEffectSlot slot)
        {
            if (_entityRenderer == null) return;
            var player = slot.Player;
            if (_entityRenderer.ActorAnchors.TryGetValue(player.ActorId, out var actor)) slot.ActorToWorld = actor.ModelToWorld;
            if (_entityRenderer.ActorAnchors.TryGetValue(player.TargetId, out var target)) slot.TargetToWorld = target.ModelToWorld;
            else if (slot.TargetToWorld == default) slot.TargetToWorld = slot.ActorToWorld;
        }

        /// <summary>
        /// Queues the action effects' live particles: each generator in its actor's frame (the action's actor, or its target
        /// for target-side ones) at the joint reference it attaches to, camera-following ones in the camera's frame.
        /// </summary>
        private void AddActionEffectDraws(ViewportCamera camera)
        {
            if (_entityRenderer == null || _actionEffects.Count == 0) return;
            var cameraToWorld = CreateBillboardBasis(camera.Right, camera.Up, camera.Forward) * Matrix4x4.CreateTranslation(camera.Position);
            foreach (var slot in _actionEffects)
            {
                var player = slot.Player;
                if (!_actorEffectMeshes.TryGetValue(player.Effects, out var byLayer)) continue;
                foreach (var (layer, emitter) in player.Player.Instance.Emitters)
                {
                    if (emitter.Particles.Count == 0 || !byLayer.TryGetValue(layer, out var meshes)) continue;
                    var definition = emitter.Template.Definition;
                    Matrix4x4 frame;
                    float distance;
                    if (SceneEffectPlayer.IsCameraSpace(emitter.Template))
                    {
                        frame = cameraToWorld;
                        distance = 0.0f;
                    }
                    else
                    {
                        bool onTarget = ActionEffectPlayer.IsTargetSide(definition);
                        uint carrier = onTarget ? player.TargetId : player.ActorId;
                        Matrix4x4 placement = onTarget ? slot.TargetToWorld : slot.ActorToWorld;
                        Vector3 joint = definition.AttachType == ParticleAttachType.None || !_entityRenderer.ActorAnchors.TryGetValue(carrier, out var posed)
                            ? Vector3.Zero
                            : ActorEffectInstance.ResolveJointReference(posed.Skeleton, posed.Pose.Translations, posed.Pose.Rotations, posed.Pose.Scales,
                                ActorEffectInstance.GetJointReference(definition));
                        frame = DisplayFlip * Matrix4x4.CreateTranslation(joint) * placement;
                        distance = Vector3.Distance(camera.Position, placement.Translation);
                    }
                    foreach (var mesh in meshes) _effectDrawList.Add(new EffectDraw(mesh, distance, emitter, frame));
                }
            }
        }
    }
}
