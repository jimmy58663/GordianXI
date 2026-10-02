// src/Gordian.Core/Graphics/SceneEffectPlayer.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Events;

namespace Gordian.Core.Graphics
{
    /// <summary>
    /// Plays the effect commands of a cutscene's scene routines (<see cref="SceneRoutine"/>) on one actor: the particle
    /// generators of the scene resource DAT (an <see cref="ActorEffectSet"/> of that file, run as an
    /// <see cref="ActorEffectInstance"/>) and the routines that start and stop each other. One player serves every task of
    /// the same file on the same actor, since one task's routine kills what another's spawned (Port Jeuno event 324:
    /// <c>bl00</c> holds the black card <c>bk00</c>, <c>open</c> replaces it, <c>kill</c> removes it).
    /// <para>
    /// The command meanings were read from the retail scene DATs (2026-10-02, #192: files 51402, 51327 and 51328 of Port
    /// Jeuno event 324): op 0x02 starts a generator emitting for the command's duration (the effect routine command,
    /// <see cref="Resources.Graphics.EffectRoutineDecoder"/>); 0x1E names one generator and appears only in the
    /// <c>kill</c> routine after <c>bl00</c> started that generator, so it is read as killing it; 0x3F names two, the
    /// first always one an earlier routine left running (the black card, the open eyelid) and the second the next stage of
    /// it, so it is read as replacing the first with the second; 0x03 names another routine of the file (<c>mai2</c>
    /// runs <c>cas1</c>, <c>kie0</c> and <c>edxx</c> in order), 0x73 one the file's <c>stop</c> routine later ends with
    /// 0x5F (<c>loop</c>, <c>tama</c>), so 0x03 starts a routine, 0x73 starts one repeating and 0x5F stops one. A stopped
    /// routine starts nothing more; its particles live out their life.
    /// </para>
    /// </summary>
    public sealed class SceneEffectPlayer
    {
        private sealed class Running
        {
            public required SceneRoutine Routine;
            /// <summary>The task that started it, or -1 for a routine another routine started.</summary>
            public int TaskId;
            public bool Loop;
            public float Clock;
            public int Next;
        }

        private readonly EventSceneResource _resource;
        private readonly Dictionary<string, ZoneParticleEmitter> _byName = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Running> _running = new();

        /// <summary>Routine nesting limit, so routines that start each other cannot grow without end.</summary>
        public const int MaxRunning = 64;

        public SceneEffectPlayer(EventSceneResource resource, ActorEffectSet effects, int seed = 0)
        {
            _resource = resource ?? throw new ArgumentNullException(nameof(resource));
            Instance = new ActorEffectInstance(effects ?? throw new ArgumentNullException(nameof(effects)), seed);
            foreach (var (layer, emitter) in Instance.Emitters)
            {
                if (!emitter.Template.ChildOnly) _byName.TryAdd(layer.Name, emitter);
            }
        }

        public ActorEffectInstance Instance { get; }

        /// <summary>Routines still running (looping ones until they are stopped).</summary>
        public int RunningCount => _running.Count;

        /// <summary>Whether nothing runs and no particle is alive: the player can be dropped.</summary>
        public bool IsIdle
        {
            get
            {
                if (_running.Count > 0) return false;
                foreach (var (_, emitter) in Instance.Emitters)
                {
                    if (emitter.Particles.Count > 0) return false;
                }
                return true;
            }
        }

        /// <summary>Whether the generator draws in front of the camera (setup flag 0x04: the blink's black cards and masks).</summary>
        public static bool IsCameraSpace(ZoneEmitterTemplate template) => template.Definition.Setup?.FollowCamera == true;

        /// <summary>Starts a routine of the file as task <paramref name="taskId"/>; false when the file has no such routine.</summary>
        public bool Start(int taskId, string routine)
        {
            if (!_resource.TryGetRoutine(routine, out var scene)) return false;
            Add(taskId, scene, loop: false);
            return true;
        }

        /// <summary>
        /// Stops the routine task <paramref name="taskId"/> runs (0x52 / 0xA3). Routines it started go on: the scene files
        /// stop those by name (51327's <c>stop</c> ends <c>loop</c> and <c>tama</c>).
        /// </summary>
        public void Stop(int taskId)
        {
            foreach (var run in _running)
            {
                if (run.TaskId == taskId) End(run);
            }
            _running.RemoveAll(r => r.Next >= r.Routine.Commands.Count && !r.Loop);
        }

        /// <summary>Stops every routine and removes every particle.</summary>
        public void Kill()
        {
            _running.Clear();
            foreach (var (_, emitter) in Instance.Emitters) emitter.Kill();
        }

        /// <summary>
        /// Runs the commands due in the next <paramref name="frames"/> 60 Hz frames, then advances the generators:
        /// <paramref name="actorFrame"/> carries the camera in the actor's model space, <paramref name="cameraFrame"/> in
        /// the camera's own space (for <see cref="IsCameraSpace"/> generators).
        /// </summary>
        public void Update(float frames, in ZoneParticleFrame actorFrame, in ZoneParticleFrame cameraFrame)
        {
            Advance(frames);
            foreach (var (_, emitter) in Instance.Emitters)
            {
                emitter.Update(frames, IsCameraSpace(emitter.Template) ? cameraFrame : actorFrame);
            }
        }

        /// <summary>Runs the commands due in the next <paramref name="frames"/> frames (without moving particles).</summary>
        public void Advance(float frames)
        {
            // Routines a command starts run from this frame on, in the same pass.
            for (int i = 0; i < _running.Count; i++)
            {
                var run = _running[i];
                run.Clock += frames;
                var commands = run.Routine.Commands;
                while (true)
                {
                    while (run.Next < commands.Count && commands[run.Next].StartFrame <= run.Clock)
                    {
                        Execute(run, commands[run.Next++]);
                    }
                    if (run.Next < commands.Count || !run.Loop) break;
                    float length = Math.Max(1, run.Routine.TotalFrames);
                    if (run.Clock < length) break;
                    run.Clock -= length;
                    run.Next = 0;
                }
            }
            _running.RemoveAll(r => !r.Loop && r.Next >= r.Routine.Commands.Count);
        }

        private void Execute(Running run, SceneCommand command)
        {
            switch (command.Kind)
            {
                case SceneCommandKind.SpawnGenerator:
                    if (_byName.TryGetValue(command.Reference, out var spawned)) spawned.Trigger(0, command.Duration);
                    break;
                case SceneCommandKind.KillGenerator:
                    if (_byName.TryGetValue(command.Reference, out var killed)) killed.Kill();
                    break;
                case SceneCommandKind.ReplaceGenerator:
                    if (_byName.TryGetValue(command.Reference, out var replaced)) replaced.Kill();
                    if (_byName.TryGetValue(command.Reference2, out var next)) next.Trigger(0, command.Duration);
                    break;
                case SceneCommandKind.StartRoutine:
                case SceneCommandKind.LoopRoutine:
                    if (_running.Count < MaxRunning && _resource.TryGetRoutine(command.Reference, out var routine))
                    {
                        Add(-1, routine, command.Kind == SceneCommandKind.LoopRoutine);
                    }
                    break;
                case SceneCommandKind.StopRoutine:
                    foreach (var other in _running)
                    {
                        // A stopped routine ends with this pass: nothing more of it runs.
                        if (string.Equals(other.Routine.Name, command.Reference, StringComparison.Ordinal)) End(other);
                    }
                    break;
            }
        }

        private void Add(int taskId, SceneRoutine routine, bool loop)
        {
            // The same routine started again on this actor starts over (the old run is dropped after this pass).
            foreach (var run in _running)
            {
                if (ReferenceEquals(run.Routine, routine)) End(run);
            }
            _running.Add(new Running { Routine = routine, TaskId = taskId, Loop = loop });
        }

        private static void End(Running run)
        {
            run.Loop = false;
            run.Next = run.Routine.Commands.Count;
        }
    }
}
