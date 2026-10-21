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
    /// A door's open and close sounds, as its <c>open</c> / <c>clos</c> routines play them (op 0x0B: a 0x3D section name
    /// at +8, the audible range at +0x14 and the full-volume radius at +0x18), 0 for none. Read from the retail zone DATs
    /// (round 5 E2, 2026-10-20): Alzadaal's gate <c>_20d</c> plays <c>9229</c> opening and <c>9230</c> closing, heard to 30
    /// yalms, full within 3; Metalworks' <c>_6l0</c> <c>9021</c> / <c>9022</c>, 10 / 3. Op 0x0B referenced from xi-tools
    /// (docs/fx/effect_system.md: the sound at the target); the two floats are read as 0x4C's far / near (*inference*).
    /// </summary>
    public readonly record struct DoorSounds(int OpenSound, int CloseSound, float Far, float Near);

    /// <summary>
    /// The sound effect pointers (<c>0x3D</c>) a zone model DAT carries, grouped by what they are for:
    /// <list type="bullet">
    /// <item><c>weat/&lt;weather&gt;</c>: ambient loops keyed by the Vana'diel time they start, named <c>HHMM</c>
    /// (Bibiki Bay sunshine: <c>0600</c> → 1013, <c>1800</c> → 1015; Southern San d'Oria: <c>0000</c> → 1081 all day);
    /// <c>weat/&lt;weather&gt;/indo</c> holds an indoor set.</item>
    /// <item><c>fses</c> / <c>fser</c> (under the zone's own area directory): footstep pointers named <c>0&lt;terrain&gt;&lt;move&gt;&lt;shake&gt;</c>,
    /// on dry ground and in the rain (<see cref="IsWetWeather"/>).</item>
    /// <item><c>door/&lt;door&gt;</c>: a door's open and close sounds.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Found in the retail zone DATs (zone 230 Southern San d'Oria, zone 4 Bibiki Bay; <c>SoundPointerRetailTests</c>).
    /// The footstep naming is referenced from xi-tools <c>docs/sounds/footsteps.md</c> (https://github.com/vekien/xi-tools);
    /// **Beyond xi-tools:** the <c>fser</c> rain set (#342) and the <c>HHMM</c> time keys of the weather ambient loops are our
    /// reading of the retail data (the time-key meaning is provisional: it matches the 06:00 / 18:00 day-night switch).
    /// <c>fser</c> is not a running set: its foot effects (<c>fser/fefr</c>) are wet ground, a flat ripple mesh <c>asi2</c>
    /// with a water splash child on a path (<c>0100</c>) where <c>fses/fefs</c> has a dust puff, and a wet-grass <c>0200</c>
    /// in 281 zones where <c>fefs</c> has one in 11 (retail zone DATs 0-299, 2026-10-20). Read as running, it played wet
    /// steps on South Gustaberg's dry dirt.
    /// </remarks>
    public sealed class ZoneSoundTable
    {
        private readonly Dictionary<string, List<AmbientLoopEntry>> _ambient = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<AmbientLoopEntry>> _indoorAmbient = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _drySteps = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _wetSteps = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int[]> _doors = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DoorSounds> _doorSounds = new(StringComparer.Ordinal);

        /// <summary>Each door's open and close sounds by door id (<c>_20d</c>), from its own routines.</summary>
        public IReadOnlyDictionary<string, DoorSounds> DoorRoutineSounds => _doorSounds;

        /// <summary>An empty table (no zone sounds).</summary>
        public static ZoneSoundTable Empty { get; } = new();

        /// <summary>Weather directory names that carry ambient loops.</summary>
        public IReadOnlyCollection<string> AmbientWeathers => _ambient.Keys;

        /// <summary>Footstep pointers of the dry-ground set (<c>fses</c>) by name.</summary>
        public IReadOnlyDictionary<string, int> DrySteps => _drySteps;

        /// <summary>Footstep pointers of the rain set (<c>fser</c>) by name.</summary>
        public IReadOnlyDictionary<string, int> WetSteps => _wetSteps;

        /// <summary>
        /// Whether a weather wets the ground, so the zone's rain sets (<c>fser</c> steps, <c>fser/fefr</c> foot effects) apply:
        /// rain and squall. Provisional: thunder and thunderstorms are read as dry (Carpenters' Landing's <c>weat/thdr</c>
        /// carries lightning and fireflies, no rain generator), not checked against retail.
        /// </summary>
        public static bool IsWetWeather(string? weatherId) =>
            string.Equals(weatherId, "rain", StringComparison.OrdinalIgnoreCase) || string.Equals(weatherId, "squl", StringComparison.OrdinalIgnoreCase);

        /// <summary>Door sound ids by door directory name.</summary>
        public IReadOnlyDictionary<string, int[]> Doors => _doors;

        /// <summary>Reads every sound pointer of a zone model DAT.</summary>
        /// <summary>The first sound command (op 0x0B) of a routine's timeline: its 0x3D section name and range.</summary>
        internal static bool TryReadRoutineSound(ReadOnlySpan<byte> payload, out string name, out float far, out float near)
        {
            name = string.Empty;
            far = near = 0f;
            if (payload.Length < 0x20) return false;
            int p = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0x14)) - 16;
            while (p >= 0 && p + 8 <= payload.Length)
            {
                byte op = payload[p];
                if (op == 0) break;
                int size = Math.Max(1, System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(p + 1)) & 0x1F) * 4;
                if (op == 0x0B && size >= 0x1C && p + 0x1C <= payload.Length)
                {
                    var id = payload.Slice(p + 8, 4);
                    int length = id.IndexOf((byte)0);
                    if (length < 0) length = 4;
                    if (length == 0) return false;
                    name = System.Text.Encoding.ASCII.GetString(id.Slice(0, length));
                    far = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(p + 0x14));
                    near = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(p + 0x18));
                    return true;
                }
                p += size;
            }
            return false;
        }

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
                    table._drySteps.TryAdd(entry.DatId, soundId);
                }
                else if (dirName == "fser")
                {
                    table._wetSteps.TryAdd(entry.DatId, soundId);
                }
                else if (string.Equals(parentName, "door", StringComparison.OrdinalIgnoreCase))
                {
                    table._doors[dirName] = table._doors.TryGetValue(dirName, out int[]? ids) ? [.. ids, soundId] : [soundId];
                }
            }

            foreach (DatResourceEntry routine in root.CollectByTypeRecursive(DatSectionType.EffectRoutine))
            {
                DatDirectoryNode door = routine.Parent;
                bool open = routine.DatId == "open", close = routine.DatId == "clos";
                if (!(open || close) || door.DatId.Length == 0 || door.DatId[0] != '_' ||
                    !string.Equals(door.Parent?.DatId, "door", StringComparison.OrdinalIgnoreCase) ||
                    !TryReadRoutineSound(routine.Payload.Span, out string name, out float far, out float near) ||
                    door.SearchLocalAndParents(name, DatSectionType.SoundEffectPointer) is not { } pointer ||
                    !SoundEffectPointer.TryDecode(pointer.Payload.Span, out int id))
                {
                    continue;
                }

                table._doorSounds.TryGetValue(door.DatId, out DoorSounds sounds);
                sounds = open ? sounds with { OpenSound = id } : sounds with { CloseSound = id };
                if (float.IsFinite(far) && far > 0f) sounds = sounds with { Far = far, Near = float.IsFinite(near) ? Math.Clamp(near, 0f, far) : 0f };
                table._doorSounds[door.DatId] = sounds;
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
        /// The pointer name is <c>0&lt;terrain&gt;&lt;move&gt;&lt;shake + 1&gt;</c>, from the rain set when
        /// <paramref name="wet"/> (falling back to the dry set), else the dry set. Walking and running use the same pointer.
        /// </summary>
        public int FootstepSound(int terrain, char movementChar, int shakeFactor, bool wet)
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

            Dictionary<string, int> set = wet && _wetSteps.Count > 0 ? _wetSteps : _drySteps;
            return set.TryGetValue(name, out int id) ? id : _drySteps.GetValueOrDefault(name);
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
