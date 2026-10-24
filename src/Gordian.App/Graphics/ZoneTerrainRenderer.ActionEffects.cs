// src/Gordian.App/Graphics/ZoneTerrainRenderer.ActionEffects.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Animation;
using Gordian.Core.Diagnostics;
using Gordian.Engine.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.World;

namespace Gordian.App.Graphics
{
    public sealed partial class ZoneTerrainRenderer
    {
        /// <summary>At most this many presentation and spell effects play at once; the oldest give way.</summary>
        private const int MaxActionEffects = 48;

        /// <summary>A presentation's effects that have not finished after this many frames are dropped (a looping generator).</summary>
        private const float MaxActionEffectFrames = 60f * 20f;

        /// <summary>A shared effects player (chant circles, release flashes) left with nothing to do this long is dropped.</summary>
        private const float MaxSharedEffectFrames = 60f * 40f;

        /// <summary>One effect player with the places its actor and target were last drawn at.</summary>
        private sealed class ActionEffectSlot
        {
            public required ActionEffectPlayer Player;
            public Matrix4x4 ActorToWorld;
            public Matrix4x4 TargetToWorld;
        }

        /// <summary>Presentation DAT and op 0x19 spell effects, one slot per action.</summary>
        private readonly List<ActionEffectSlot> _actionEffects = new();

        /// <summary>The shared <c>ROM/0/0</c> routines each actor's motion routines linked, one player per actor.</summary>
        private readonly Dictionary<uint, ActionEffectSlot> _sharedActorEffects = new();

        private readonly ActionEffectTracker _actionEffectTracker = new();
        private readonly List<ActionEffectEvent> _actionEffectEvents = new();
        private readonly List<uint> _finishedSharedEffects = new();

        /// <summary>The world whose actors the action effects follow (render thread).</summary>
        private WorldState? _actionEffectsWorld;

        private int ActionEffectCount => _actionEffects.Count + _sharedActorEffects.Count;

        private void ClearActionEffects()
        {
            _actionEffects.Clear();
            _sharedActorEffects.Clear();
            _actionEffectTracker.Clear();
        }

        /// <summary>
        /// Plays the particle effects of actions (#132). <see cref="ActionEffectTracker"/> follows each drawn entity's motion
        /// routine and reports what it starts: a weapon skill / monster move / job ability / spell presentation DAT's own
        /// effects, the shared <c>ROM/0/0</c> routines the routines link (a chant's circle, a release's flash, a weapon
        /// skill's <c>eis1</c>), op 0x19 spell effects (a Trust appearing), and the end of an action (a chant's circle stops).
        /// Each player advances in its actor's model space, target-side generators in the target's.
        /// </summary>
        private void UpdateActionEffects(ViewportCamera camera, ZoneEnvironmentSettings environment, IEnumerable<WorldEntity>? entities,
            ResourceManager? resourceManager, float deltaSeconds)
        {
            if (!EnableZoneEffects || _entityRenderer == null)
            {
                ClearActionEffects();
                return;
            }

            // #377: another character's world is shown (a multi-box switch). Its actors are other objects whose action
            // serials the tracker never saw, so it would take every one's last action as new and replay its effects; the
            // effects playing belong to the world left. Start over: actors are then first seen, which starts only actions
            // just begun (MaxLateStartTicks) and appearances just spawned (IsFreshSpawn).
            if (!ReferenceEquals(World, _actionEffectsWorld))
            {
                _actionEffectsWorld = World;
                ClearActionEffects();
            }

            if (entities != null && resourceManager != null)
            {
                _actionEffectEvents.Clear();
                foreach (var entity in entities)
                {
                    if (entity.IsSpawned && entity.IsDrawn) _actionEffectTracker.Step(entity, _actionEffectEvents);
                }
                _actionEffectTracker.EndFrame();
                foreach (var effect in _actionEffectEvents) StartActionEffect(effect, resourceManager);
                PlayScheduledActorEffects(resourceManager);
            }
            if (ActionEffectCount == 0) return;

            float frames = Math.Clamp(deltaSeconds, 0.0f, 0.25f) * 60.0f;
            float dayFraction = environment.TimeOfDayHours / 24.0f;
            Vector3 daylight = StrongestLight(environment);
            int dayOfWeek = CurrentDayOfWeek();
            int moonPhase = CurrentMoonPhaseIndex();
            var cameraFrame = new ZoneParticleFrame(Vector3.Zero, dayFraction, daylight, Vector3.UnitZ, dayOfWeek, moonPhase);

            bool Advance(ActionEffectSlot slot)
            {
                PlaceActionEffect(slot);
                if (!Matrix4x4.Invert(slot.ActorToWorld, out var worldToActor) || !Matrix4x4.Invert(slot.TargetToWorld, out var worldToTarget)) return false;
                var actorFrame = new ZoneParticleFrame(Vector3.Transform(camera.Position, worldToActor), dayFraction, daylight,
                    Vector3.Normalize(Vector3.TransformNormal(camera.Forward, worldToActor)), dayOfWeek, moonPhase);
                var targetFrame = new ZoneParticleFrame(Vector3.Transform(camera.Position, worldToTarget), dayFraction, daylight,
                    Vector3.Normalize(Vector3.TransformNormal(camera.Forward, worldToTarget)), dayOfWeek, moonPhase);
                slot.Player.Update(frames, actorFrame, targetFrame, cameraFrame);
                return true;
            }

            for (int i = _actionEffects.Count - 1; i >= 0; i--)
            {
                var slot = _actionEffects[i];
                if (!Advance(slot) || slot.Player.IsFinished || slot.Player.Age > MaxActionEffectFrames) _actionEffects.RemoveAt(i);
            }

            _finishedSharedEffects.Clear();
            foreach (var (actor, slot) in _sharedActorEffects)
            {
                // An actor no longer drawn (despawned, left view) takes its cast effects with it: its action's end was
                // never seen, so a chant circle would otherwise run out its 1800 frames where it stood.
                if (!_entityRenderer.ActorAnchors.ContainsKey(actor))
                {
                    _finishedSharedEffects.Add(actor);
                    continue;
                }
                if (!Advance(slot) || slot.Player.IsFinished || slot.Player.Age > MaxSharedEffectFrames) _finishedSharedEffects.Add(actor);
            }
            foreach (uint actor in _finishedSharedEffects) _sharedActorEffects.Remove(actor);
        }

        private void StartActionEffect(ActionEffectEvent effect, ResourceManager resourceManager)
        {
            switch (effect.Kind)
            {
                case ActionEffectEventKind.Presentation when effect.Request?.Asset is { } asset:
                    StartFileEffect(asset, effect, resourceManager);
                    break;

                case ActionEffectEventKind.SpellEffect:
                    if (ActionMotionLibrary.Shared.ResolveSpellEffect(effect.SpellAnimation) is { Effects: not null } spell)
                    {
                        StartFileEffect(spell, effect, resourceManager);
                    }
                    else
                    {
                        GordianLog.Debug("Graphics", $"Spell effect {effect.SpellAnimation} on {effect.ActorId:X8}: no presentation file.");
                    }
                    break;

                case ActionEffectEventKind.SharedRoutine:
                    if (!_sharedActorEffects.TryGetValue(effect.ActorId, out var shared))
                    {
                        if (resourceManager.GetSharedRoutineEffects() is not { } files) return;
                        shared = new ActionEffectSlot { Player = new ActionEffectPlayer(files.Routines, files.Effects, effect.ActorId, effect.TargetId, unchecked((int)effect.ActorId)) };
                        UploadActorEffectMeshes(files.Effects);
                        PlaceActionEffect(shared);
                        _sharedActorEffects[effect.ActorId] = shared;
                    }
                    shared.Player.TargetId = effect.TargetId;
                    if (!shared.Player.Start(effect.Routine))
                    {
                        GordianLog.Debug("Graphics", $"Shared effect routine '{effect.Routine}' on {effect.ActorId:X8}: not in ROM/0/0.");
                    }
                    break;

                case ActionEffectEventKind.ActionEnded:
                    if (_sharedActorEffects.TryGetValue(effect.ActorId, out var running)) running.Player.StopIfSustained(effect.Routine);
                    break;
            }
        }

        private readonly List<ActorEffectRequest> _scheduledActorEffects = new();

        /// <summary>
        /// Plays the effects S2C 0x038 / 0x03A asked for (#311, <see cref="SchedulerPlayback"/>): a presentation file's on its
        /// actor toward its target; for an actor routine, the model's own generators of that routine, and the shared
        /// <c>ROM/0/0</c> routine of that name with its colour commands (<c>kesu</c> fades the actor out) and the actor
        /// routines it links (<c>hitl</c>: <c>eflg</c>, <c>selg</c>).
        /// </summary>
        private void PlayScheduledActorEffects(ResourceManager resourceManager)
        {
            if (World == null) return;
            _scheduledActorEffects.Clear();
            World.TakeActorEffects(World.CurrentZoneId, _scheduledActorEffects);
            foreach (var request in _scheduledActorEffects)
            {
                var effect = new ActionEffectEvent(ActionEffectEventKind.Presentation, request.ActorServerId, request.TargetServerId);
                if (request.Asset != null)
                {
                    StartFileEffect(request.Asset, effect, resourceManager);
                    continue;
                }
                bool modelPlayed = _actorEffects.TryGetValue(request.ActorServerId, out var instance) && instance.Play(request.Routine);
                bool sharedPlayed = false;
                if (resourceManager.GetSharedRoutineEffects() is { } files && files.Routines.TryGetRoutine(request.Routine, out var shared))
                {
                    if (World.TryGetByServerId(request.ActorServerId, out var actor)) actor.ActorColor.Schedule(shared, ActorColorTrack.Now);
                    foreach (var command in shared.Commands)
                    {
                        if (command.Kind == Gordian.Core.Resources.Events.SceneCommandKind.LinkActorRoutine && instance != null) instance.Play(command.Reference);
                    }
                    if (shared.HasEffects) StartActionEffect(effect with { Kind = ActionEffectEventKind.SharedRoutine, Routine = request.Routine }, resourceManager);
                    sharedPlayed = true;
                }
                GordianLog.Debug("Graphics", $"Actor scheduler '{request.Routine}' on {request.ActorServerId:X8}: model effects {(modelPlayed ? "played" : "none")}, ROM/0/0 {(sharedPlayed ? "played" : "none")}.");
            }
        }

        private void StartFileEffect(ActionAsset asset, ActionEffectEvent effect, ResourceManager resourceManager)
        {
            var effects = resourceManager.GetSceneEffects(asset.FileId);
            if (effects == null)
            {
                GordianLog.Debug("Graphics", $"{asset} on {effect.ActorId:X8}: no generator this client draws.");
                return;
            }
            var player = new ActionEffectPlayer(asset, effects, effect.ActorId, effect.TargetId,
                unchecked(asset.FileId * 31 + (int)effect.ActorId + _actionEffects.Count));
            if (!player.Start()) return;
            UploadActorEffectMeshes(effects);
            if (_actionEffects.Count >= MaxActionEffects) _actionEffects.RemoveAt(0);
            var slot = new ActionEffectSlot { Player = player };
            PlaceActionEffect(slot);
            _actionEffects.Add(slot);
            GordianLog.Debug("Graphics", $"Effects of {asset} on {effect.ActorId:X8} (target {player.TargetId:X8}, {(_entityRenderer?.ActorAnchors.ContainsKey(player.TargetId) == true ? "drawn" : "not drawn")}).");
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
            if (_entityRenderer == null || ActionEffectCount == 0) return;
            var cameraToWorld = CreateBillboardBasis(camera.Right, camera.Up, camera.Forward) * Matrix4x4.CreateTranslation(camera.Position);
            foreach (var slot in _actionEffects) AddActionEffectDraws(camera, slot, cameraToWorld);
            foreach (var slot in _sharedActorEffects.Values) AddActionEffectDraws(camera, slot, cameraToWorld);
        }

        private void AddActionEffectDraws(ViewportCamera camera, ActionEffectSlot slot, Matrix4x4 cameraToWorld)
        {
            var player = slot.Player;
            if (!_actorEffectMeshes.TryGetValue(player.Effects, out var byLayer)) return;
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
                    Vector3 joint = definition.AttachType == ParticleAttachType.None || !_entityRenderer!.ActorAnchors.TryGetValue(carrier, out var posed)
                        ? Vector3.Zero
                        : Skeleton.ResolveJointReference(posed.Skeleton, posed.Pose.Translations, posed.Pose.Rotations, posed.Pose.Scales,
                            ActorEffectInstance.GetJointReference(definition));
                    frame = DisplayFlip * Matrix4x4.CreateTranslation(joint) * placement;
                    distance = Vector3.Distance(camera.Position, placement.Translation);
                }
                foreach (var mesh in meshes) _effectDrawList.Add(new EffectDraw(mesh, distance, emitter, frame));
            }
        }
    }
}
