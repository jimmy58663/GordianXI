// src/Gordian.Core/Audio/ZoneSoundTable.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using Gordian.Core.Resources.Containers;

namespace Gordian.Core.Audio
{
    /// <summary>One time-keyed ambient loop of a zone weather directory: plays from <see cref="StartMinute"/> until the next entry's start.</summary>
    /// <param name="StartMinute">Vana'diel minute of the day (0-1439) the loop takes over, from the pointer's <c>HHMM</c> name.</param>
    /// <param name="SoundId">The <c>.spw</c> sound effect id.</param>
    public readonly record struct AmbientLoopEntry(int StartMinute, int SoundId);

    /// <summary>
    /// The sound effect pointers (<c>0x3D</c>) a zone model DAT carries, grouped by what they are for:
    /// <list type="bullet">
    /// <item><c>weat/&lt;weather&gt;</c>: ambient loops keyed by the Vana'diel time they start, named <c>HHMM</c>
    /// (Bibiki Bay sunshine: <c>0600</c> → 1013, <c>1800</c> → 1015; Southern San d'Oria: <c>0000</c> → 1081 all day);
    /// <c>weat/&lt;weather&gt;/indo</c> holds an indoor set.</item>
    /// <item><c>fses</c> / <c>fser</c> (under the zone's own area directory): footstep pointers named <c>0&lt;terrain&gt;&lt;move&gt;&lt;shake&gt;</c>,
    /// walking and running.</item>
    /// <item><c>door/&lt;door&gt;</c>: a door's open and close sounds.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Found in the retail zone DATs (zone 230 Southern San d'Oria, zone 4 Bibiki Bay; <c>SoundPointerRetailTests</c>).
    /// The footstep naming is referenced from xi-tools <c>docs/sounds/footsteps.md</c> (https://github.com/vekien/xi-tools);
    /// **Beyond xi-tools:** the <c>fser</c> running set and the <c>HHMM</c> time keys of the weather ambient loops are our
    /// reading of the retail data (the time-key meaning is provisional: it matches the 06:00 / 18:00 day-night switch).
    /// </remarks>
    public sealed class ZoneSoundTable
    {
        private readonly Dictionary<string, List<AmbientLoopEntry>> _ambient = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<AmbientLoopEntry>> _indoorAmbient = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _walkSteps = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _runSteps = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int[]> _doors = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>An empty table (no zone sounds).</summary>
        public static ZoneSoundTable Empty { get; } = new();

        /// <summary>Weather directory names that carry ambient loops.</summary>
        public IReadOnlyCollection<string> AmbientWeathers => _ambient.Keys;

        /// <summary>Footstep pointers of the walking set by name.</summary>
        public IReadOnlyDictionary<string, int> WalkSteps => _walkSteps;

        /// <summary>Footstep pointers of the running set by name.</summary>
        public IReadOnlyDictionary<string, int> RunSteps => _runSteps;

        /// <summary>Door sound ids by door directory name.</summary>
        public IReadOnlyDictionary<string, int[]> Doors => _doors;

        /// <summary>Reads every sound pointer of a zone model DAT.</summary>
        public static ZoneSoundTable Read(DatDirectoryNode root)
        {
            var table = new ZoneSoundTable();
            foreach (DatResourceEntry entry in root.CollectByTypeRecursive(DatSectionType.SoundEffectPointer))
            {
                if (!SoundEffectPointer.TryDecode(entry.Payload.Span, out int soundId))
                {
                    continue;
                }

                DatDirectoryNode dir = entry.Parent;
                string dirName = dir.DatId;
                string? parentName = dir.Parent?.DatId;
                if (string.Equals(parentName, "weat", StringComparison.OrdinalIgnoreCase))
                {
                    Add(table._ambient, dirName, entry.DatId, soundId);
                }
                else if (string.Equals(dirName, "indo", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(dir.Parent?.Parent?.DatId, "weat", StringComparison.OrdinalIgnoreCase))
                {
                    Add(table._indoorAmbient, parentName!, entry.DatId, soundId);
                }
                else if (dirName == "fses")
                {
                    table._walkSteps.TryAdd(entry.DatId, soundId);
                }
                else if (dirName == "fser")
                {
                    table._runSteps.TryAdd(entry.DatId, soundId);
                }
                else if (string.Equals(parentName, "door", StringComparison.OrdinalIgnoreCase))
                {
                    table._doors[dirName] = table._doors.TryGetValue(dirName, out int[]? ids) ? [.. ids, soundId] : [soundId];
                }
            }

            foreach (List<AmbientLoopEntry> list in table._ambient.Values)
            {
                list.Sort((a, b) => a.StartMinute.CompareTo(b.StartMinute));
            }

            foreach (List<AmbientLoopEntry> list in table._indoorAmbient.Values)
            {
                list.Sort((a, b) => a.StartMinute.CompareTo(b.StartMinute));
            }

            return table;
        }

        /// <summary>
        /// The ambient loop for a weather at a Vana'diel minute of the day, or 0 when the zone has none. A weather the
        /// zone does not author falls back to its canonical sky category (<paramref name="fallbackWeathers"/>), then to
        /// <c>fine</c>, then to any authored weather.
        /// </summary>
        public int AmbientSound(string? weather, int minuteOfDay, bool indoor = false, params string[] fallbackWeathers)
        {
            Dictionary<string, List<AmbientLoopEntry>> source = indoor && _indoorAmbient.Count > 0 ? _indoorAmbient : _ambient;
            List<AmbientLoopEntry>? list = null;
            if (weather is not null)
            {
                source.TryGetValue(weather, out list);
            }

            foreach (string fallback in fallbackWeathers)
            {
                if (list is null)
                {
                    source.TryGetValue(fallback, out list);
                }
            }

            if (list is null)
            {
                source.TryGetValue("fine", out list);
            }

            if (list is null)
            {
                foreach (List<AmbientLoopEntry> any in source.Values)
                {
                    list = any;
                    break;
                }
            }

            if (list is null || list.Count == 0)
            {
                return 0;
            }

            // The latest entry at or before the minute; before the first entry the last one (from the day before) holds.
            AmbientLoopEntry chosen = list[^1];
            foreach (AmbientLoopEntry e in list)
            {
                if (e.StartMinute <= minuteOfDay)
                {
                    chosen = e;
                }
            }

            return chosen.SoundId;
        }

        /// <summary>
        /// The footstep sound for a terrain index and the actor's footwear movement digits, or 0 when the zone has none.
        /// The pointer name is <c>0&lt;terrain&gt;&lt;move&gt;&lt;shake + 1&gt;</c>.
        /// </summary>
        public int FootstepSound(int terrain, char movementChar, int shakeFactor, bool running)
        {
            if (terrain < 0 || terrain > 15)
            {
                return 0;
            }

            string name = string.Create(4, (terrain, movementChar, shakeFactor), static (span, s) =>
            {
                span[0] = '0';
                span[1] = s.terrain.ToString("x", CultureInfo.InvariantCulture)[0];
                span[2] = s.movementChar;
                span[3] = (char)('0' + Math.Clamp(s.shakeFactor + 1, 0, 9));
            });

            Dictionary<string, int> set = running && _runSteps.Count > 0 ? _runSteps : _walkSteps;
            return set.TryGetValue(name, out int id) ? id : _walkSteps.GetValueOrDefault(name);
        }

        private static void Add(Dictionary<string, List<AmbientLoopEntry>> target, string weather, string name, int soundId)
        {
            if (name.Length != 4 || !int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int hhmm))
            {
                return;
            }

            // Indoor sets are sometimes named by their sound id instead (Bibiki Bay "1064"): not a time, so all day.
            int minute = hhmm / 100 <= 23 && hhmm % 100 <= 59 ? hhmm / 100 * 60 + hhmm % 100 : 0;
            if (!target.TryGetValue(weather, out List<AmbientLoopEntry>? list))
            {
                target[weather] = list = new List<AmbientLoopEntry>();
            }

            if (!list.Exists(e => e.StartMinute == minute))
            {
                list.Add(new AmbientLoopEntry(minute, soundId));
            }
        }
    }
}
