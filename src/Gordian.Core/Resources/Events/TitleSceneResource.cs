// src/Gordian.Core/Resources/Events/TitleSceneResource.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;

namespace Gordian.Core.Resources.Events
{
    /// <summary>
    /// One segment of the title fly-through: a routine of <see cref="TitleSceneResource"/> that flies one zone.
    /// <see cref="Number"/> counts the segments in file order from 1 (xi-tools' section numbers: 12 is North Gustaberg).
    /// </summary>
    public sealed record TitleSegment(int Number, SceneRoutine Routine, int ZoneId)
    {
        public string Name => Routine.Name;
    }

    /// <summary>
    /// One random choice of the title's <c>loop</c> routine: one of <see cref="Segments"/> plays. <see cref="Expansion"/> is
    /// 0 for the base game's choice (op 0x3D), else the expansion op 0xAF names.
    /// </summary>
    public sealed record TitleSegmentGroup(int Expansion, IReadOnlyList<TitleSegment> Segments);

    /// <summary>
    /// The title screen fly-through: <c>ROM/0/23.DAT</c> (file 23, magic <c>titl</c>), the live 3D background of the lobby.
    /// An ordinary section file: 317 camera Routes (0x06, <see cref="CameraRoute"/>), 24 routines (0x07,
    /// <see cref="SceneRoutine"/>) and three environments (0x2F). It holds no geometry: each segment routine flies a real zone.
    /// <para>
    /// Read from the retail file (2026-11-13, #552). A segment routine starts with op 0x7B (the zone id, u32), sets the weather
    /// with 0x7C (a FourCC) and the clock with 0x7D (u32, 1/60 s of the game day: 2,592,000 = 12:00, so hour =
    /// value / 216,000), then plays its shots with the cutscene ops (0x04 camera, 0x0F fade, 0x10 cross-dissolve, 0x0E blur),
    /// fading in from black at its start and out to black at its end (8,900-14,461 frames: 2.5-4 minutes at 60 a second). One segment (<c>mov1</c>, La Theine
    /// Plateau) lights its scene with the file's own environment <c>s101</c> through op 0x7E. <c>main</c> repeats
    /// <c>loop</c> (op 0x73), and <c>loop</c> is the order: a random choice (0x3D ... 0x3E) of one of <c>mov1</c>-<c>mov8</c>,
    /// played and waited for (0x3B), then the same for <c>ex1a</c>-<c>ex1c</c>, <c>ex2a</c>/<c>d</c>/<c>b</c>/<c>c</c> and
    /// <c>ex3a</c>/<c>d</c>/<c>e</c>/<c>c</c>, each choice opened by 0xAF 1, 2 or 3 instead of 0x3D (read as the expansion
    /// that must be installed). <c>ex3b</c>, <c>ex2e</c> and <c>ex2f</c> (segments 4, 21 and 22) are in no choice.
    /// </para>
    /// <para>
    /// <b>Differs from xi-tools</b> (https://github.com/vekien/xi-tools, docs/dats/ROM_0_23.md and docs/title/): it reads
    /// the file as a list of tagged nodes with "zone sections" behind an 8-byte marker <c>0x67b</c> and "weather records" with
    /// fog colours and ranges; the marker is op 0x7B's command header (op 0x7B, 6 dwords), and a "weather record" is an
    /// op 0x7C command followed by an op 0x7D whose clock value was read as a colour. There is no fog in the file: the zone's
    /// own environment at the segment's hour and weather applies. <b>Beyond xi-tools</b>, which says the rule that picks the
    /// segments after the first is not in the file: <c>loop</c> above. That segment 12 always plays first is xi-tools'
    /// experiment (its docs/title/README.md "Play order").
    /// </para>
    /// </summary>
    public sealed class TitleSceneResource
    {
        /// <summary>The file id of <c>ROM/0/23.DAT</c>.</summary>
        public const int FileId = 23;

        /// <summary>The segment the client always plays first (North Gustaberg, <c>mov5</c>; xi-tools docs/title/README.md).</summary>
        public const int OpeningSegmentNumber = 12;

        /// <summary>The routine whose random choices order the segments.</summary>
        public const string OrderRoutine = "loop";

        /// <summary>Op 0x7D clock units per game hour (60 per second).</summary>
        public const double ClockUnitsPerHour = 216000.0;

        private readonly Dictionary<string, EnvironmentKeyframe> _environments;

        private TitleSceneResource(EventSceneResource scene, List<TitleSegment> segments, List<TitleSegmentGroup> groups, Dictionary<string, EnvironmentKeyframe> environments)
        {
            Scene = scene;
            Segments = segments;
            Groups = groups;
            _environments = environments;
        }

        /// <summary>The routes and routines (played through the cutscene presentation).</summary>
        public EventSceneResource Scene { get; }

        /// <summary>The segments in file order (every routine with a zone).</summary>
        public IReadOnlyList<TitleSegment> Segments { get; }

        /// <summary>The random choices of <c>loop</c> in order (empty when the file has none).</summary>
        public IReadOnlyList<TitleSegmentGroup> Groups { get; }

        /// <summary>The segment played first, or null.</summary>
        public TitleSegment? Opening
        {
            get
            {
                foreach (var segment in Segments)
                {
                    if (segment.Number == OpeningSegmentNumber) return segment;
                }
                return Segments.Count > 0 ? Segments[0] : null;
            }
        }

        /// <summary>The file's own 0x2F environment <paramref name="name"/> (op 0x7E).</summary>
        public bool TryGetEnvironment(string name, out EnvironmentKeyframe keyframe) => _environments.TryGetValue(name, out keyframe!);

        /// <summary>The game hour (0-24) of an op 0x7D clock value.</summary>
        public static float HourOf(uint clock) => (float)(clock / ClockUnitsPerHour % 24.0);

        public static TitleSceneResource? Parse(ReadOnlySpan<byte> file)
        {
            var scene = EventSceneResource.Parse(file);
            var segments = new List<TitleSegment>();
            var environments = new Dictionary<string, EnvironmentKeyframe>(StringComparer.Ordinal);
            foreach (var header in DatSectionWalker.ReadHeaders(file))
            {
                if (header.DataOffset + header.DataSizeBytes > file.Length) continue;
                if (header.RawTypeCode == 0x07 && scene.TryGetRoutine(header.DatId, out var routine) && ZoneOf(routine) is int zone and > 0)
                {
                    segments.Add(new TitleSegment(segments.Count + 1, routine, zone));
                }
                else if (header.RawTypeCode == 0x2F
                    && EnvironmentDecoder.DecodeEnvironmentKeyframe(file.Slice(header.DataOffset, header.DataSizeBytes), header.DatId) is { } keyframe)
                {
                    environments[header.DatId] = keyframe;
                }
            }
            if (segments.Count == 0) return null;
            return new TitleSceneResource(scene, segments, ReadGroups(scene, segments), environments);
        }

        private static int? ZoneOf(SceneRoutine routine)
        {
            foreach (var command in routine.Commands)
            {
                if (command.Kind == SceneCommandKind.TitleZone) return (int)command.Number;
            }
            return null;
        }

        private static List<TitleSegmentGroup> ReadGroups(EventSceneResource scene, List<TitleSegment> segments)
        {
            var groups = new List<TitleSegmentGroup>();
            if (!scene.TryGetRoutine(OrderRoutine, out var loop)) return groups;
            List<TitleSegment>? open = null;
            int expansion = 0;
            foreach (var command in loop.Commands)
            {
                switch (command.Kind)
                {
                    case SceneCommandKind.RandomOpen:
                    case SceneCommandKind.ExpansionChoice:
                        Close();
                        open = new List<TitleSegment>();
                        expansion = command.Kind == SceneCommandKind.ExpansionChoice ? (int)command.Number : 0;
                        break;
                    case SceneCommandKind.RandomClose:
                        Close();
                        break;
                    case SceneCommandKind.LinkWait when open != null:
                        var segment = segments.Find(s => s.Name == command.Reference);
                        if (segment != null) open.Add(segment);
                        break;
                }
            }
            Close();
            return groups;

            void Close()
            {
                if (open is { Count: > 0 }) groups.Add(new TitleSegmentGroup(expansion, open));
                open = null;
            }
        }
    }
}
