// src/Gordian.Core/Resources/Graphics/ZoneRoutineLibrary.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Events;
using Gordian.Core.Resources.Graphics;

namespace Gordian.Core.Resources.Graphics
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
}
