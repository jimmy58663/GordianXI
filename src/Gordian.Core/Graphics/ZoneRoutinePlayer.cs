// src/Gordian.Core/Graphics/ZoneRoutinePlayer.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Events;

namespace Gordian.Core.Graphics
{
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
        public void AddRoutine(string directory, SceneRoutine routine)
        {
            ArgumentNullException.ThrowIfNull(routine);
            directory ??= string.Empty;
            if (!_routines.TryGetValue(directory, out var inDirectory)) _routines[directory] = inDirectory = new(StringComparer.OrdinalIgnoreCase);
            if (!inDirectory.TryAdd(routine.Name, routine)) return;
            if (!_byName.TryGetValue(routine.Name, out var named)) _byName[routine.Name] = named = new();
            named.Add((directory, routine));
            RoutineCount++;
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

        /// <summary>The routine <paramref name="name"/> as a command in <paramref name="directory"/> sees it.</summary>
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
            foundIn = string.Empty;
            routine = null!;
            return false;
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
    /// Alzadaal Runic Portals' idle glow <c>1pa1</c>, activation <c>1pa2</c> and end <c>1pak</c>.
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
            public bool Loop;
            public float Clock;
            public int Next;
        }

        private readonly ZoneRoutineLibrary _library;
        private readonly List<Running> _running = new();
        private Func<ZoneEmitterTemplate, ZoneParticleEmitter?>? _resolve;

        /// <summary>Routine nesting limit, so routines that start each other cannot grow without end.</summary>
        public const int MaxRunning = 64;

        public ZoneRoutinePlayer(ZoneRoutineLibrary library)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
        }

        /// <summary>Routines still running (looping ones until they are stopped).</summary>
        public int RunningCount => _running.Count;

        /// <summary>Starts every routine of the zone named <paramref name="name"/>; false when the zone has none.</summary>
        public bool Play(string name)
        {
            var found = _library.Find(name);
            foreach (var (directory, routine) in found)
            {
                if (_running.Count < MaxRunning) Add(directory, routine, loop: false);
            }
            return found.Count > 0;
        }

        /// <summary>
        /// Runs the commands due in the next <paramref name="frames"/> 60 Hz frames, starting and stopping emitters through
        /// <paramref name="resolve"/> (the zone's running emitter for a template).
        /// </summary>
        public void Update(float frames, Func<ZoneEmitterTemplate, ZoneParticleEmitter?> resolve)
        {
            _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
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

        private void Execute(Running run, SceneCommand command)
        {
            switch (command.Kind)
            {
                case SceneCommandKind.SpawnGenerator:
                    Emitter(run, command.Reference)?.Trigger(0, command.Duration);
                    break;
                case SceneCommandKind.KillGenerator:
                    Emitter(run, command.Reference)?.Kill();
                    break;
                case SceneCommandKind.ReplaceGenerator:
                    Emitter(run, command.Reference)?.Kill();
                    Emitter(run, command.Reference2)?.Trigger(0, command.Duration);
                    break;
                case SceneCommandKind.StartRoutine:
                case SceneCommandKind.LoopRoutine:
                    if (_running.Count < MaxRunning && _library.TryResolveRoutine(run.Directory, command.Reference, out string dir, out var routine))
                    {
                        Add(dir, routine, command.Kind == SceneCommandKind.LoopRoutine && routine.TotalFrames > 0);
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

        private void Add(string directory, SceneRoutine routine, bool loop)
        {
            // The same routine started again starts over (the old run is dropped after this pass).
            foreach (var run in _running)
            {
                if (ReferenceEquals(run.Routine, routine)) End(run);
            }
            _running.Add(new Running { Directory = directory, Routine = routine, Loop = loop });
        }

        private static void End(Running run)
        {
            run.Loop = false;
            run.Next = run.Routine.Commands.Count;
        }
    }
}
