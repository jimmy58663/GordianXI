// src/Gordian.Engine/Ui/Lobby/TitleFlythrough.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Events;
using Gordian.Engine.Events;

namespace Gordian.Engine.Ui.Lobby
{
    /// <summary>
    /// Plays the title fly-through (<see cref="TitleSceneResource"/>, #552) behind the lobby: one segment after another, each
    /// a zone flown by the segment routine's camera shots, fades, cross-dissolves and blur through its own
    /// <see cref="EventPresentation"/> (the cutscene presentation the zone renderer already draws), with the segment's weather,
    /// clock and environment.
    /// <para>
    /// The viewport loads <see cref="ZoneId"/> and reports each frame whether it is on screen; until it is, the clock stands
    /// still on black (every segment fades out to black at its end and in from black at its start, so a zone swap is never
    /// seen). <see cref="NextZoneId"/> is chosen when a segment starts, so the viewport can load it in the background
    /// meanwhile.
    /// </para>
    /// <para>
    /// Order: segment 12 first (<see cref="TitleSceneResource.OpeningSegmentNumber"/>), then the file's <c>loop</c>: a random
    /// segment of each of its choices in turn (the base game's, then each installed expansion's), over and over. A choice
    /// whose zones are not installed (<c>zoneAvailable</c>) is skipped, as an expansion choice is in retail. Without choices
    /// in the file the segments play in file order. <b>Unverified against retail:</b> that 0xAF tests the expansion, that the
    /// clock stands still between op 0x7D commands, and that op 0x7E's environment replaces the zone's for the rest of the
    /// segment.
    /// </para>
    /// </summary>
    public sealed class TitleFlythrough
    {
        private readonly TitleSceneResource _scene;
        private readonly Func<int, bool> _zoneAvailable;
        private readonly Random _random;
        private double _clock;
        private double _segmentStart = double.NaN;
        private int _task;
        private int _nextGroup;
        private int _nextInOrder;

        /// <summary>The fade to black the fly-through starts from (the first segment fades in from it).</summary>
        private static readonly SceneRoutine Black = new()
        {
            Name = "blak",
            TotalFrames = 0,
            Commands = new[] { new SceneCommand(0x0F, SceneCommandKind.SceneFade, 0, 0, string.Empty, 0x80000000) },
        };

        public TitleFlythrough(TitleSceneResource scene, Func<int, bool>? zoneAvailable = null, Random? random = null)
        {
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
            _zoneAvailable = zoneAvailable ?? (_ => true);
            _random = random ?? new Random();
            Presentation = new EventPresentation { Clock = () => _clock };
            Presentation.Play(++_task, scene.Scene, Black, Vector3.Zero);
            // The first segment starts just after the black, so its fade in starts from it.
            _clock = 1.0 / EventPresentation.FramesPerSecond;

            var opening = scene.Opening;
            if (opening != null && Available(opening))
            {
                Segment = opening;
                // The opening segment stands for its choice: the next pick is from the choice after it.
                for (int i = 0; i < scene.Groups.Count; i++)
                {
                    if (scene.Groups[i].Segments.Contains(opening))
                    {
                        _nextGroup = i + 1;
                        break;
                    }
                }
                _nextInOrder = opening.Number;
            }
            else
            {
                Segment = PickNext();
            }
            NextSegment = Segment;
        }

        /// <summary>The camera, fades and post effects of the segment playing (the clock is the fly-through's).</summary>
        public EventPresentation Presentation { get; }

        /// <summary>The segment playing, or waiting for its zone; null when no zone of the file is installed.</summary>
        public TitleSegment? Segment { get; private set; }

        /// <summary>The segment after this one (chosen when this one starts).</summary>
        public TitleSegment? NextSegment { get; private set; }

        /// <summary>The zone to show (0: none).</summary>
        public int ZoneId => Segment?.ZoneId ?? 0;

        /// <summary>The zone of the next segment, to load in the background (0: none).</summary>
        public int NextZoneId => NextSegment?.ZoneId ?? 0;

        /// <summary>Whether the segment waits for its zone (the screen is black).</summary>
        public bool IsWaitingForZone => double.IsNaN(_segmentStart);

        /// <summary>The segment's frame (60 a second) now.</summary>
        public double Frame => IsWaitingForZone ? 0 : (_clock - _segmentStart) * EventPresentation.FramesPerSecond;

        /// <summary>The weather now (op 0x7C; <c>fine</c> before the first).</summary>
        public string Weather { get; private set; } = "fine";

        /// <summary>The game hour now (op 0x7D; 12 before the first).</summary>
        public float Hour { get; private set; } = 12f;

        /// <summary>The file's environment lighting the scene (op 0x7E), or null for the zone's own.</summary>
        public string? Environment { get; private set; }

        /// <summary>The scene's resource (for the environments of op 0x7E).</summary>
        public TitleSceneResource Scene => _scene;

        /// <summary>
        /// Advances by <paramref name="deltaSeconds"/>. <paramref name="zoneShown"/> says whether <see cref="ZoneId"/> is the
        /// zone on screen: the segment starts once it is, and its clock stands still while it is not.
        /// </summary>
        public void Update(double deltaSeconds, bool zoneShown)
        {
            if (Segment == null) return;
            if (IsWaitingForZone)
            {
                if (!zoneShown) return;
                Begin();
                return;
            }
            if (!zoneShown) return;
            _clock += Math.Clamp(double.IsFinite(deltaSeconds) ? deltaSeconds : 0, 0, 0.25);
            ReadState();
            if (Frame >= Segment.Routine.TotalFrames) Advance();
        }

        /// <summary>Ends the segment playing and goes on to the next (a new visit to the title screen shows another zone).</summary>
        public void Skip()
        {
            if (Segment == null) return;
            Presentation.Stop(_task);
            Presentation.Play(++_task, _scene.Scene, Black, Vector3.Zero);
            _clock += 1.0 / EventPresentation.FramesPerSecond;
            Advance();
        }

        private void Begin()
        {
            _segmentStart = _clock;
            Presentation.Play(++_task, _scene.Scene, Segment!.Routine, Vector3.Zero);
            Environment = null;
            ReadState();
            NextSegment = PickNext();
        }

        private void Advance()
        {
            Segment = NextSegment ?? PickNext();
            NextSegment = Segment;
            _segmentStart = double.NaN;
        }

        /// <summary>The weather, clock and environment the segment's commands have set by now.</summary>
        private void ReadState()
        {
            double frame = Frame;
            foreach (var command in Segment!.Routine.Commands)
            {
                if (command.StartFrame > frame) break;
                switch (command.Kind)
                {
                    case SceneCommandKind.TitleWeather:
                        Weather = command.Reference;
                        break;
                    case SceneCommandKind.TitleClock:
                        Hour = TitleSceneResource.HourOf(command.Number);
                        break;
                    case SceneCommandKind.TitleEnvironment:
                        Environment = command.Reference;
                        break;
                }
            }
        }

        private bool Available(TitleSegment segment) => _zoneAvailable(segment.ZoneId);

        /// <summary>A random available segment of the next choice that has one, or the next in file order without choices.</summary>
        private TitleSegment? PickNext()
        {
            var groups = _scene.Groups;
            var candidates = new List<TitleSegment>();
            for (int tried = 0; tried < groups.Count; tried++)
            {
                var group = groups[_nextGroup % groups.Count];
                _nextGroup = (_nextGroup + 1) % groups.Count;
                candidates.Clear();
                foreach (var segment in group.Segments)
                {
                    if (Available(segment)) candidates.Add(segment);
                }
                if (candidates.Count > 0) return candidates[_random.Next(candidates.Count)];
            }

            var segments = _scene.Segments;
            for (int tried = 0; tried < segments.Count; tried++)
            {
                var segment = segments[_nextInOrder % segments.Count];
                _nextInOrder = (_nextInOrder + 1) % segments.Count;
                if (Available(segment)) return segment;
            }
            return null;
        }
    }
}
