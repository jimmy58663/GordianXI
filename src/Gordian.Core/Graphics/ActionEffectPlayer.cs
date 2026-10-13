// src/Gordian.Core/Graphics/ActionEffectPlayer.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Animation;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
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
        /// <summary>A player for one presentation DAT (<see cref="Start()"/> plays its <c>main</c>).</summary>
        public ActionEffectPlayer(ActionAsset asset, ActorEffectSet effects, uint actorId, uint targetId, int seed = 0)
            : this((asset ?? throw new ArgumentNullException(nameof(asset))).Effects
                   ?? throw new ArgumentException("The presentation has no effect routines.", nameof(asset)), effects, actorId, targetId, seed)
        {
            Asset = asset;
        }

        /// <summary>
        /// A player for any effect file's routines on one actor: a presentation DAT, or the shared effects DAT <c>ROM/0/0</c>
        /// whose routines the actors' motion routines link (one per actor, so a release's <c>stwh</c> stops the circle the
        /// chant's <c>ner2</c> started).
        /// </summary>
        public ActionEffectPlayer(Resources.Events.EventSceneResource routines, ActorEffectSet effects, uint actorId, uint targetId, int seed = 0)
        {
            Routines = routines ?? throw new ArgumentNullException(nameof(routines));
            Effects = effects ?? throw new ArgumentNullException(nameof(effects));
            ActorId = actorId;
            TargetId = targetId != 0 ? targetId : actorId;
            Player = new SceneEffectPlayer(routines, effects, seed);
        }

        /// <summary>The presentation this player was made for, or null for a shared effects player.</summary>
        public ActionAsset? Asset { get; }

        /// <summary>The routines the player runs.</summary>
        public Resources.Events.EventSceneResource Routines { get; }

        /// <summary>
        /// Routines at least this long (60 Hz frames) run until something stops them: the chant circles (1800). They are
        /// stopped when the action that started them ends (<see cref="ActionEffectEventKind.ActionEnded"/>).
        /// </summary>
        public const int SustainedRoutineFrames = 600;

        /// <summary>Starts routine <paramref name="routine"/> of the file (again, if it runs); false when the file has none.</summary>
        public bool Start(string routine)
        {
            Age = 0f;
            return Player.Start(0, routine);
        }

        /// <summary>
        /// Stops routine <paramref name="routine"/> if it runs on until stopped (<see cref="SustainedRoutineFrames"/>); its
        /// particles live out their life. Returns whether it was one.
        /// </summary>
        public bool StopIfSustained(string routine)
        {
            if (!Routines.TryGetRoutine(routine, out var found) || found.TotalFrames < SustainedRoutineFrames) return false;
            Player.StopRoutineAndGenerators(routine);
            return true;
        }

        public ActorEffectSet Effects { get; }

        /// <summary>The actor performing the action (source-side generators ride it).</summary>
        public uint ActorId { get; }

        /// <summary>The action's first target (target-side generators ride it); the actor itself when there is none.</summary>
        public uint TargetId { get; set; }

        public SceneEffectPlayer Player { get; }

        /// <summary>Frames played so far.</summary>
        public float Age { get; private set; }

        /// <summary>Starts the presentation's <c>main</c>; false when it has none.</summary>
        public bool Start() => Start(ActionAsset.MainRoutine);

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

    /// <summary>What an <see cref="ActionEffectTracker"/> asks the renderer to start or stop.</summary>
    public enum ActionEffectEventKind : byte
    {
        /// <summary>Play the effect routines of <see cref="ActionEffectEvent.Request"/>'s presentation DAT.</summary>
        Presentation,

        /// <summary>Start the shared effect routine <see cref="ActionEffectEvent.Routine"/> (<c>ROM/0/0</c>) on the actor.</summary>
        SharedRoutine,

        /// <summary>Play spell animation <see cref="ActionEffectEvent.SpellAnimation"/>'s effects on the actor (op 0x19).</summary>
        SpellEffect,

        /// <summary>
        /// The action that started shared routine <see cref="ActionEffectEvent.Routine"/> is over: a routine that would run on
        /// (a chant's 1800-frame circle) stops, as the release's <c>stwh</c> does; an interrupted or replaced chant has none.
        /// </summary>
        ActionEnded
    }

    /// <summary>One start or stop for the renderer (see <see cref="ActionEffectTracker"/>).</summary>
    public readonly record struct ActionEffectEvent(ActionEffectEventKind Kind, uint ActorId, uint TargetId, string Routine = "",
        int SpellAnimation = -1, ActionRequest? Request = null);

    /// <summary>
    /// Follows each drawn entity's motion routine on its clock (as <c>ActionSoundTracker</c> does for the sounds) and reports
    /// the effects it starts (#132): a presentation DAT's own effect routines when its action begins, the shared effect
    /// routines and spell effects the playing routine links as its clock passes them (<see cref="RoutineEffectCollector"/>:
    /// the chant's circle, the release's flash, a weapon skill's <c>eis1</c>), the end of the action (so a chant's circle
    /// stops), and a summoned pet's or Trust's appearance (its model's <c>spop</c> when first seen with the PetNewFlag).
    /// </summary>
    public sealed class ActionEffectTracker
    {
        /// <summary>An action first seen this far into its routine (the entity came into view) shows no effects.</summary>
        public const float MaxLateStartTicks = 15f;

        /// <summary>The routine a summoned Trust or pet runs as it appears (Trust model 3000: op 0x19, spell animation 921).</summary>
        public const string SummonedRoutine = "spop";

        private sealed class Tracked
        {
            public int Serial;
            public IReadOnlyList<RoutineEffectCue> Cues = Array.Empty<RoutineEffectCue>();
            public int Next;
            public uint TargetId;
            public readonly List<string> Started = new();
        }

        private readonly Dictionary<uint, Tracked> _entities = new();
        private readonly HashSet<uint> _seen = new();
        private readonly List<uint> _stale = new();

        /// <summary>Advances one entity (render thread, once per frame) and adds what starts or stops to <paramref name="into"/>.</summary>
        public void Step(WorldEntity entity, List<ActionEffectEvent> into)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(into);
            uint id = entity.ServerId;
            _seen.Add(id);
            var animation = entity.Animation;
            var model = animation.Model;
            bool known = _entities.TryGetValue(id, out var found);
            var tracked = found ?? new Tracked { Serial = animation.ActionSerial };
            if (!known)
            {
                _entities[id] = tracked;
                if (entity.IsPetSpawning && model != null)
                {
                    foreach (var cue in model.GetRoutineEffects(SummonedRoutine)) Emit(id, id, cue, into, null);
                }
            }

            var routine = animation.ActiveRoutine;
            bool started = known ? animation.ActionSerial != tracked.Serial : routine != null && animation.ActionTicks <= MaxLateStartTicks;
            if ((started || routine == null) && tracked.Started.Count > 0)
            {
                foreach (string name in tracked.Started) into.Add(new ActionEffectEvent(ActionEffectEventKind.ActionEnded, id, tracked.TargetId, name));
                tracked.Started.Clear();
            }

            if (started && routine != null && model != null)
            {
                var request = animation.ActiveRequest;
                uint target = request?.PrimaryTargetId ?? 0;
                tracked.TargetId = target != 0 ? target : id;
                tracked.Cues = CuesOf(model, routine, request);
                tracked.Next = 0;
                if (request?.Asset?.Effects != null) into.Add(new ActionEffectEvent(ActionEffectEventKind.Presentation, id, tracked.TargetId, Request: request));
            }
            else if (routine == null)
            {
                tracked.Cues = Array.Empty<RoutineEffectCue>();
                tracked.Next = 0;
            }

            tracked.Serial = animation.ActionSerial;
            if (routine != null)
            {
                float ticks = animation.ActionTicks;
                while (tracked.Next < tracked.Cues.Count && tracked.Cues[tracked.Next].Tick <= ticks)
                {
                    Emit(id, tracked.TargetId, tracked.Cues[tracked.Next++], into, tracked);
                }
            }
        }

        private static void Emit(uint actor, uint target, RoutineEffectCue cue, List<ActionEffectEvent> into, Tracked? tracked)
        {
            if (cue.IsSpellEffect)
            {
                into.Add(new ActionEffectEvent(ActionEffectEventKind.SpellEffect, actor, target, SpellAnimation: cue.SpellAnimation));
                return;
            }
            into.Add(new ActionEffectEvent(ActionEffectEventKind.SharedRoutine, actor, target, cue.Routine));
            tracked?.Started.Add(cue.Routine);
        }

        /// <summary>
        /// The effects the playing routine starts: a presentation's <c>main</c> through its DAT and the actor
        /// (<see cref="EventMotionBank.GetEffectsOnModel"/>), else the model's own routine of that name; none for an event
        /// gesture from a motion bank.
        /// </summary>
        internal static IReadOnlyList<RoutineEffectCue> CuesOf(EntityModel model, MotionRoutine routine, ActionRequest? request)
        {
            if (request?.Asset?.Motion is { } bank && ReferenceEquals(bank.BuildOnModel(model, ActionAsset.MainRoutine), routine))
            {
                return bank.GetEffectsOnModel(model, ActionAsset.MainRoutine);
            }
            return model.MotionRoutines.TryGetValue(routine.Name, out var own) && ReferenceEquals(own, routine)
                ? model.GetRoutineEffects(routine.Name)
                : Array.Empty<RoutineEffectCue>();
        }

        /// <summary>Forgets the entities not stepped since the last call (call once per frame).</summary>
        public void EndFrame()
        {
            _stale.Clear();
            foreach (uint id in _entities.Keys)
            {
                if (!_seen.Contains(id)) _stale.Add(id);
            }
            foreach (uint id in _stale) _entities.Remove(id);
            _seen.Clear();
        }

        /// <summary>Forgets every entity (a zone change).</summary>
        public void Clear()
        {
            _entities.Clear();
            _seen.Clear();
        }
    }
}
