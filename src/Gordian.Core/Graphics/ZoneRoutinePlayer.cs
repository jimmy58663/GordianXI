// src/Gordian.Core/Graphics/ZoneRoutinePlayer.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Events;
using Gordian.Core.Resources.Graphics;

namespace Gordian.Core.Graphics
{
    /// <summary>
    /// The two actors a zone routine plays on: the caster and the target a map scheduler (S2C 0x039) or an event's 0x2D
    /// names, or the door entity for a door's own routines (both the door). 0 = none (a routine the zone starts itself).
    /// </summary>
    public readonly record struct ZoneRoutineActors(uint CasterServerId, uint TargetServerId)
    {
        public static readonly ZoneRoutineActors None = default;

        /// <summary>
        /// The entity a generator of <paramref name="attachType"/> rides (#232), 0 for one placed in the zone: target-side
        /// types the target (else the caster), source-side types and the zone actor types 0xA-0xC the caster (else the
        /// target). LandSandBoat sends a map scheduler with one entity as both (<c>0x039_mapschedulor.cpp</c>), and a
        /// door's routines play on the door, so the side matters only for an event's 0x2D with two actors. Sides
        /// referenced from xi-model-viewer (ui/js/particle/runtime.js, TARGET_ATTACH); that the zone actor types ride the
        /// routine's caster is *inference* (Alzadaal's door <c>_20t</c> carries its effects as ZoneActor0xA a few yalms
        /// from the door).
        /// </summary>
        public uint ActorFor(ParticleAttachType attachType)
        {
            if (!Resources.ZoneDataLoader.IsActorAttached(attachType)) return 0;
            bool targetSide = attachType is ParticleAttachType.TargetActor or ParticleAttachType.TargetToSourceBasis;
            uint first = targetSide ? TargetServerId : CasterServerId;
            return first != 0 ? first : targetSide ? CasterServerId : TargetServerId;
        }
    }

    /// <summary>
    /// The Section 0x07 routines of a zone DAT that the client plays on demand, with the emitters of the generators they
    /// start, by directory path (<c>d_at/effe/pba1/1pba</c>). A routine or generator named by a command resolves in the
    /// routine's own directory first, then in each parent's, as the client resolves a routine's references (xi-tools
    /// docs/fx/effect_system.md "resolved through the routine's local directory then parents").
    /// </summary>
    public sealed class ZoneRoutineLibrary
    {
        private readonly Dictionary<string, Dictionary<string, SceneRoutine>> _routines = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, ZoneEmitterTemplate>> _generators = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<(string Directory, SceneRoutine Routine)>> _byName = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Number of routines a map scheduler can start by name.</summary>
        public int RoutineCount { get; private set; }

        /// <summary>Adds a routine a map scheduler or another routine can start (<see cref="Find"/>).</summary>
        public void AddRoutine(string directory, SceneRoutine routine) => AddRoutine(directory, routine, byName: true);

        /// <summary>
        /// Adds a routine; with <paramref name="byName"/> false only a command or <see cref="ZoneRoutinePlayer.PlayAt"/>
        /// reaches it by its directory, never a map scheduler's name (a door's <c>open</c>, #227).
        /// </summary>
        public void AddRoutine(string directory, SceneRoutine routine, bool byName)
        {
            ArgumentNullException.ThrowIfNull(routine);
            directory ??= string.Empty;
            if (!_routines.TryGetValue(directory, out var inDirectory)) _routines[directory] = inDirectory = new(StringComparer.OrdinalIgnoreCase);
            if (!inDirectory.TryAdd(routine.Name, routine)) return;
            RoutineCount++;
            if (!byName) return;
            if (!_byName.TryGetValue(routine.Name, out var named)) _byName[routine.Name] = named = new();
            named.Add((directory, routine));
        }

        /// <summary>Adds the emitter of a generator declared in <paramref name="directory"/>.</summary>
        public void AddGenerator(string directory, string id, ZoneEmitterTemplate template)
        {
            ArgumentNullException.ThrowIfNull(template);
            directory ??= string.Empty;
            if (!_generators.TryGetValue(directory, out var inDirectory)) _generators[directory] = inDirectory = new(StringComparer.OrdinalIgnoreCase);
            inDirectory.TryAdd(id, template);
        }

        /// <summary>
        /// The routines a map scheduler names: every routine of that name in the zone (the server names no directory;
        /// the portals' <c>1pa1</c> / <c>1pb1</c> / <c>2pb1</c> are each unique to their directory).
        /// </summary>
        public IReadOnlyList<(string Directory, SceneRoutine Routine)> Find(string name) =>
            name != null && _byName.TryGetValue(name, out var named) ? named : Array.Empty<(string, SceneRoutine)>();

        /// <summary>
        /// The routine <paramref name="name"/> as a command in <paramref name="directory"/> sees it: in the directory, then
        /// each parent, then a <c>door</c> directory under it. The last is a door's: Alzadaal's <c>d_at/door/_20t/open</c>
        /// starts <c>stat</c>, and the door's effect routines all sit in <c>d_at/door/_20t/door/</c> (retail DAT, read
        /// 2026-10-18); *inference* that the client looks there.
        /// </summary>
        public bool TryResolveRoutine(string directory, string name, out string foundIn, out SceneRoutine routine)
        {
            foreach (string dir in Ancestors(directory))
            {
                if (_routines.TryGetValue(dir, out var inDirectory) && inDirectory.TryGetValue(name, out routine!))
                {
                    foundIn = dir;
                    return true;
                }
            }
            string doorResources = (directory ?? string.Empty) + "/door";
            if (_routines.TryGetValue(doorResources, out var inDoor) && inDoor.TryGetValue(name, out routine!))
            {
                foundIn = doorResources;
                return true;
            }
            foundIn = string.Empty;
            routine = null!;
            return false;
        }

        /// <summary>The routine <paramref name="name"/> of exactly <paramref name="directory"/> (a door's <c>open</c>).</summary>
        public bool TryGetRoutine(string directory, string name, out SceneRoutine routine)
        {
            routine = null!;
            return _routines.TryGetValue(directory ?? string.Empty, out var inDirectory) && inDirectory.TryGetValue(name, out routine!);
        }

        /// <summary>The generator <paramref name="name"/> as a command in <paramref name="directory"/> sees it.</summary>
        public bool TryResolveGenerator(string directory, string name, out ZoneEmitterTemplate template)
        {
            foreach (string dir in Ancestors(directory))
            {
                if (_generators.TryGetValue(dir, out var inDirectory) && inDirectory.TryGetValue(name, out template!)) return true;
            }
            template = null!;
            return false;
        }

        private static IEnumerable<string> Ancestors(string directory)
        {
            string dir = directory ?? string.Empty;
            while (true)
            {
                yield return dir;
                int slash = dir.LastIndexOf('/');
                if (slash < 0) break;
                dir = dir.Substring(0, slash);
            }
        }
    }

    /// <summary>
    /// Plays a zone's on-demand routines (<see cref="ZoneRoutineLibrary"/>) on the zone's emitters when a trigger names
    /// them: a map scheduler from the server (S2C 0x039, <see cref="World.WorldState.PostMapScheduler"/>), e.g. the
    /// Alzadaal Runic Portals' idle glow <c>1pa1</c>, activation <c>1pa2</c> and end <c>1pak</c>, or an event's zone
    /// scheduler opcodes 0x2D / 0x60 sub 2 (start) and 0x51 (<see cref="Stop"/>) through the same queue (#226).
    /// <para>
    /// The commands play as in the cutscene scene files (<see cref="SceneEffectPlayer"/>, #192): op 0x02 starts a
    /// generator emitting for the command's duration, 0x1E kills one, 0x3F kills the first and starts the second, 0x03
    /// starts a routine, 0x73 starts one repeating and 0x5F stops one. A routine of 0 frames never repeats: started with
    /// 0x73 it runs once (Alzadaal's <c>s104</c> re-armed its pillars every frame when looped, #210).
    /// </para>
    /// </summary>
    public sealed class ZoneRoutinePlayer
    {
        private sealed class Running
        {
            public required string Directory;
            public required SceneRoutine Routine;
            public ZoneRoutineActors Actors;
            public bool Loop;
            public float Clock;
            public int Next;
        }

        private readonly ZoneRoutineLibrary _library;
        private readonly List<Running> _running = new();
        private Func<ZoneEmitterTemplate, ZoneParticleEmitter?>? _resolve;
        private Action<ZoneParticleEmitter, ZoneRoutineActors>? _bind;

        /// <summary>Routine nesting limit, so routines that start each other cannot grow without end.</summary>
        public const int MaxRunning = 64;

        public ZoneRoutinePlayer(ZoneRoutineLibrary library)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
        }

        /// <summary>Routines still running (looping ones until they are stopped).</summary>
        public int RunningCount => _running.Count;

        /// <summary>Starts every routine of the zone named <paramref name="name"/>; false when the zone has none.</summary>
        public bool Play(string name) => Play(name, ZoneRoutineActors.None);

        /// <summary>
        /// Starts every routine of the zone named <paramref name="name"/> on <paramref name="actors"/>: the generators it
        /// starts that ride an actor are bound to the one their attach type picks (<see cref="ZoneRoutineActors.ActorFor"/>,
        /// #232), and so are those of the routines it starts. False when the zone has none.
        /// </summary>
        public bool Play(string name, ZoneRoutineActors actors)
        {
            var found = _library.Find(name);
            foreach (var (directory, routine) in found)
            {
                if (_running.Count < MaxRunning) Add(directory, routine, loop: false, actors);
            }
            return found.Count > 0;
        }

        /// <summary>
        /// Starts the routine <paramref name="name"/> of exactly <paramref name="directory"/> on <paramref name="actors"/>
        /// (a door's <c>open</c> / <c>clos</c> / <c>into</c> / <c>intc</c> on the door entity, #227). False when there is none.
        /// </summary>
        public bool PlayAt(string directory, string name, ZoneRoutineActors actors)
        {
            if (!_library.TryGetRoutine(directory, name, out var routine)) return false;
            if (_running.Count < MaxRunning) Add(directory, routine, loop: false, actors);
            return true;
        }

        /// <summary>The length in frames of the routine <paramref name="name"/> of <paramref name="directory"/>, with the routines it starts; 0 when there is none.</summary>
        public int GetFrames(string directory, string name) =>
            _library.TryGetRoutine(directory, name, out var routine) ? FramesWithChildren(directory, routine, 0) : 0;

        private int FramesWithChildren(string directory, SceneRoutine routine, int depth)
        {
            int frames = routine.TotalFrames;
            if (depth >= 8) return frames;
            foreach (var command in routine.Commands)
            {
                if (command.Kind == SceneCommandKind.StartRoutine && _library.TryResolveRoutine(directory, command.Reference, out string dir, out var child) &&
                    !ReferenceEquals(child, routine))
                {
                    frames = Math.Max(frames, command.StartFrame + FramesWithChildren(dir, child, depth + 1));
                }
            }
            return frames;
        }

        /// <summary>
        /// Ends every running routine named <paramref name="name"/> (an event's 0x51, CodeENDMAPSCHEDULOR, or the server
        /// ending a map scheduler): nothing more of it runs; the routines it started and its particles go on, as a scene
        /// task's stop leaves them (<see cref="SceneEffectPlayer"/>). False when the zone has no routine of that name.
        /// </summary>
        public bool Stop(string name)
        {
            var found = _library.Find(name);
            foreach (var (_, routine) in found)
            {
                foreach (var run in _running)
                {
                    if (ReferenceEquals(run.Routine, routine)) End(run);
                }
            }
            return found.Count > 0;
        }

        /// <summary>Whether a routine named <paramref name="name"/> is still running (started by name or by another routine).</summary>
        public bool IsPlaying(string name)
        {
            foreach (var run in _running)
            {
                if (string.Equals(run.Routine.Name, name, StringComparison.OrdinalIgnoreCase) && (run.Loop || run.Next < run.Routine.Commands.Count)) return true;
            }
            return false;
        }

        /// <summary>
        /// Runs the commands due in the next <paramref name="frames"/> 60 Hz frames, starting and stopping emitters through
        /// <paramref name="resolve"/> (the zone's running emitter for a template).
        /// </summary>
        public void Update(float frames, Func<ZoneEmitterTemplate, ZoneParticleEmitter?> resolve) => Update(frames, resolve, null);

        /// <summary>
        /// Runs the commands due in the next <paramref name="frames"/> frames like <see cref="Update(float, Func{ZoneEmitterTemplate, ZoneParticleEmitter?})"/>;
        /// <paramref name="bind"/> hears of every emitter a command starts with the actors of its routine, so the caller
        /// can place actor-attached ones (#232).
        /// </summary>
        public void Update(float frames, Func<ZoneEmitterTemplate, ZoneParticleEmitter?> resolve, Action<ZoneParticleEmitter, ZoneRoutineActors>? bind)
        {
            _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
            _bind = bind;
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
                    float length = run.Routine.TotalFrames;
                    if (length <= 0f)
                    {
                        run.Loop = false;
                        break;
                    }
                    if (run.Clock < length) break;
                    run.Clock -= length;
                    run.Next = 0;
                }
            }
            _running.RemoveAll(r => !r.Loop && r.Next >= r.Routine.Commands.Count);
        }

        private ZoneParticleEmitter? Emitter(Running run, string generator) =>
            generator.Length > 0 && _library.TryResolveGenerator(run.Directory, generator, out var template) ? _resolve?.Invoke(template) : null;

        /// <summary>Starts a generator's emitter, binding it to the routine's actors first.</summary>
        private void Start(Running run, string generator, int duration)
        {
            if (Emitter(run, generator) is not { } emitter) return;
            _bind?.Invoke(emitter, run.Actors);
            emitter.Trigger(0, duration);
        }

        private void Execute(Running run, SceneCommand command)
        {
            switch (command.Kind)
            {
                case SceneCommandKind.SpawnGenerator:
                    Start(run, command.Reference, command.Duration);
                    break;
                case SceneCommandKind.KillGenerator:
                    Emitter(run, command.Reference)?.Kill();
                    break;
                case SceneCommandKind.ReplaceGenerator:
                    Emitter(run, command.Reference)?.Kill();
                    Start(run, command.Reference2, command.Duration);
                    break;
                case SceneCommandKind.StopGenerator:
                    // 0x2D: the generator stops emitting; its particles live out their lives (as in SceneEffectPlayer).
                    Emitter(run, command.Reference)?.StopEmitting();
                    break;
                case SceneCommandKind.StartRoutine:
                case SceneCommandKind.LoopRoutine:
                    if (_running.Count < MaxRunning && _library.TryResolveRoutine(run.Directory, command.Reference, out string dir, out var routine))
                    {
                        Add(dir, routine, command.Kind == SceneCommandKind.LoopRoutine && routine.TotalFrames > 0, run.Actors);
                    }
                    break;
                case SceneCommandKind.StopRoutine:
                    if (_library.TryResolveRoutine(run.Directory, command.Reference, out _, out var stopped))
                    {
                        foreach (var other in _running)
                        {
                            // A stopped routine ends with this pass: nothing more of it runs.
                            if (ReferenceEquals(other.Routine, stopped)) End(other);
                        }
                    }
                    break;
            }
        }

        private void Add(string directory, SceneRoutine routine, bool loop, ZoneRoutineActors actors)
        {
            // The same routine started again starts over (the old run is dropped after this pass).
            foreach (var run in _running)
            {
                if (ReferenceEquals(run.Routine, routine)) End(run);
            }
            _running.Add(new Running { Directory = directory, Routine = routine, Loop = loop, Actors = actors });
        }

        private static void End(Running run)
        {
            run.Loop = false;
            run.Next = run.Routine.Commands.Count;
        }
    }
}
