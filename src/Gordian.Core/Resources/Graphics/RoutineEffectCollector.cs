// src/Gordian.Core/Resources/Graphics/RoutineEffectCollector.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// The particle effects an actor's motion routines start (#132). The actors' own DATs carry no generators for their
    /// casts and releases: their routines link routines of the shared effects DAT <c>ROM/0/0</c>, which do. Read from the
    /// retail data (Hume male base motion <c>ROM/27/82</c> and <c>ROM/0/0</c>, 2026-10-12):
    /// <list type="bullet">
    /// <item>the chants link a circle that runs for 1800 frames: <c>cawh</c> (white magic) → <c>ner2</c> (<c>gn20</c>,
    /// <c>gn21</c>), <c>cabk</c> (black) → <c>ner1</c> (<c>gn10</c>-<c>gn13</c>), <c>cafa</c> (Trust) → <c>nefa</c>
    /// (<c>fa01</c>, <c>fa02</c>);</item>
    /// <item>the releases stop it and flash: <c>shwh</c> → <c>stwh</c> (op 0x5F stops <c>ner2</c>, op 0x1E kills its
    /// <c>gn2s</c>) and <c>eis4</c>; <c>shbk</c> → <c>stbk</c> and <c>eis3</c>; <c>shfa</c> → <c>stfa</c> and <c>eifa</c>;</item>
    /// <item>a weapon skill's <c>main</c> links <c>eis1</c>, a monster move's <c>stnm</c>;</item>
    /// <item>op 0x19 plays a spell presentation's effects: a Trust model's <c>spop</c> (summoned) plays spell animation 921.</item>
    /// </list>
    /// Links are followed into <paramref name="routines"/> as for the clips (a blocking link, 0x3B / 0x3C, pushes the rest
    /// back by the child's length); a link that leaves them becomes a cue naming the shared routine. Op table from xi-tools
    /// (docs/fx/effect_system.md, docs/ability/inspect.md).
    /// </summary>
    public static class RoutineEffectCollector
    {
        private const int MaxLinkDepth = 4;

        /// <summary>Links that name no effect: the result display and the weapon show / hide routines.</summary>
        private static bool IsNotAnEffect(string name) => name is "dada" or "mdam" or "ldad" or "proc"
            || SharedWeaponRoutines.ByName.ContainsKey(name);

        /// <summary>The effects routine <paramref name="name"/> starts, sorted by tick; empty when it starts none.</summary>
        public static IReadOnlyList<RoutineEffectCue> Collect(IReadOnlyDictionary<string, RawMotionRoutine> routines, string name)
        {
            ArgumentNullException.ThrowIfNull(routines);
            if (string.IsNullOrEmpty(name) || !routines.TryGetValue(name, out RawMotionRoutine? root)) return Array.Empty<RoutineEffectCue>();

            var cues = new List<RoutineEffectCue>();
            Walk(root, 0, 0);
            if (cues.Count == 0) return Array.Empty<RoutineEffectCue>();
            cues.Sort((a, b) => a.Tick.CompareTo(b.Tick));
            return cues;

            void Walk(RawMotionRoutine routine, int baseTick, int depth)
            {
                int shift = 0;
                foreach (var effect in routine.SpellEffects) cues.Add(new RoutineEffectCue(baseTick + effect.StartTick, string.Empty, effect.Animation));
                foreach (var command in routine.SoundCommands)
                {
                    if (command.ChoiceGroup >= 0 || command.Op is not (0x03 or 0x3B or 0x57 or 0x3C) || command.Name == routine.Name) continue;
                    int at = baseTick + command.StartTick + shift;
                    if (routines.TryGetValue(command.Name, out var child))
                    {
                        if (depth < MaxLinkDepth) Walk(child, at, depth + 1);
                        if (command.Op is 0x3B or 0x3C) shift += child.TotalTicks;
                    }
                    else if (!IsNotAnEffect(command.Name))
                    {
                        cues.Add(new RoutineEffectCue(at, command.Name));
                    }
                }
            }
        }
    }
}
