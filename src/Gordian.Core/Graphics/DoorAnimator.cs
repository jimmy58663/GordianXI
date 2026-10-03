// src/Gordian.Core/Graphics/DoorAnimator.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;
using Gordian.Core.World.Collision;

namespace Gordian.Core.Graphics
{
    /// <summary>The pose of one door leaf: its turn (radians) and slide about its placement origin, local axes.</summary>
    public readonly record struct DoorLeafPose(Vector3 Rotation, Vector3 Slide)
    {
        public static readonly DoorLeafPose Shut = default;
    }

    /// <summary>
    /// Plays a zone's door routines from its door entities' status, as retail's door actor does: a door starts in its
    /// <c>intc</c> pose (closed at load), takes its <c>into</c> pose at once when its entity is first seen open, plays
    /// <c>open</c> when the status turns to open (8) and <c>clos</c> when it leaves it. A routine started mid-way takes
    /// each leaf from where it is. The status is <see cref="ZoneDoors.StatusOf"/> (the event's while one set it). A door
    /// whose entity goes out of range keeps its last pose.
    /// Each move goes linearly from the leaf's pose at the move's start to its target over the move's duration (the
    /// easing is not known; *inference*). Door behaviour referenced from xi-tools (https://github.com/vekien/xi-tools,
    /// docs/zone/doors.md, after the PS2 client's <c>XiDoorActor</c>: OpenDoor / CloseDoor / InitOpenDoor / InitCloseDoor).
    /// </summary>
    public sealed class DoorAnimator
    {
        private const double FramesPerSecond = 60.0;

        private sealed class DoorState
        {
            public required ZoneDoorRoutines Routines { get; init; }
            public bool? Open;
            public DoorRoutine? Playing;
            public double StartSeconds;

            /// <summary>Leaf poses when <see cref="Playing"/> started (or the settled poses when nothing plays).</summary>
            public Dictionary<int, DoorLeafPose> From { get; } = new();
        }

        private readonly Dictionary<string, DoorState> _doors = new(StringComparer.Ordinal);

        public DoorAnimator(IReadOnlyDictionary<string, ZoneDoorRoutines> routines)
        {
            foreach (var (id, doorRoutines) in routines)
            {
                var state = new DoorState { Routines = doorRoutines };
                if (doorRoutines.InitClose != null) Settle(state, doorRoutines.InitClose);
                _doors[id] = state;
            }
        }

        /// <summary>The number of doors with routines.</summary>
        public int Count => _doors.Count;

        /// <summary>Starts the routines of doors whose entity status changed since the last update.</summary>
        public void Update(WorldState world, double nowSeconds)
        {
            if (_doors.Count == 0) return;
            foreach (var (id, entity) in ZoneDoors.DoorEntities(world))
            {
                if (_doors.TryGetValue(id, out var state)) Observe(state, ZoneDoors.StatusOf(entity) == ZoneDoors.StatusOpen, nowSeconds);
            }
        }

        /// <summary>Sets a door's state as its entity would (for tests and tools): open or closed at <paramref name="nowSeconds"/>.</summary>
        public void SetOpen(string doorId, bool open, double nowSeconds)
        {
            if (_doors.TryGetValue(doorId, out var state)) Observe(state, open, nowSeconds);
        }

        /// <summary>Whether a door's routine is still playing (its leaves are moving).</summary>
        public bool IsMoving(string doorId, double nowSeconds) =>
            _doors.TryGetValue(doorId, out var state) && state.Playing != null && Frame(state, nowSeconds) < state.Playing.TotalFrames;

        /// <summary>The pose of a door leaf now; <see cref="DoorLeafPose.Shut"/> for a door without routines.</summary>
        public DoorLeafPose GetPose(string doorId, int part, double nowSeconds)
        {
            if (!_doors.TryGetValue(doorId, out var state)) return DoorLeafPose.Shut;
            var from = state.From.TryGetValue(part, out var pose) ? pose : DoorLeafPose.Shut;
            return state.Playing == null ? from : Evaluate(state.Playing, part, from, Frame(state, nowSeconds));
        }

        /// <summary>
        /// The internal-space transform that takes a leaf's world-space vertices (baked at its authored pose) to its
        /// <paramref name="pose"/>: the turn and slide happen in the placement's own frame, about its origin (a hinged
        /// leaf's hinge), as retail multiplies them into the part's base matrix.
        /// </summary>
        public static Matrix4x4 LeafTransform(in ZonePlacement placement, DoorLeafPose pose)
        {
            if (pose == DoorLeafPose.Shut) return Matrix4x4.Identity;
            var placed = ZoneDefDecoder.CreateTrsMatrix(placement.Position, placement.Rotation, placement.Scale);
            if (!Matrix4x4.Invert(placed, out var unplaced)) return Matrix4x4.Identity;
            var local = ZoneDefDecoder.CreateTrsMatrix(pose.Slide, pose.Rotation, Vector3.One);
            return unplaced * local * placed;
        }

        private static double Frame(DoorState state, double nowSeconds) => Math.Max(0.0, (nowSeconds - state.StartSeconds) * FramesPerSecond);

        private void Observe(DoorState state, bool open, double nowSeconds)
        {
            if (state.Open == open) return;
            bool first = state.Open == null;
            state.Open = open;
            if (first)
            {
                // Retail's InitOpenDoor: a door already open when it is sent takes its open pose without moving.
                if (open && (state.Routines.InitOpen ?? state.Routines.Open) is { } initial) Settle(state, initial);
                return;
            }

            var routine = open ? state.Routines.Open : state.Routines.Close;
            if (routine == null) return;
            CaptureFrom(state, routine, nowSeconds);
            state.Playing = routine;
            state.StartSeconds = nowSeconds;
        }

        /// <summary>Takes every leaf the routine moves from its current pose.</summary>
        private void CaptureFrom(DoorState state, DoorRoutine routine, double nowSeconds)
        {
            var current = new Dictionary<int, DoorLeafPose>();
            foreach (var part in Parts(state, routine)) current[part] = GetPoseOf(state, part, nowSeconds);
            foreach (var (part, pose) in current) state.From[part] = pose;
        }

        private static DoorLeafPose GetPoseOf(DoorState state, int part, double nowSeconds)
        {
            var from = state.From.TryGetValue(part, out var pose) ? pose : DoorLeafPose.Shut;
            return state.Playing == null ? from : Evaluate(state.Playing, part, from, Frame(state, nowSeconds));
        }

        private static IEnumerable<int> Parts(DoorState state, DoorRoutine routine)
        {
            var parts = new HashSet<int>(state.From.Keys);
            foreach (var move in routine.Moves) parts.Add(move.Part);
            if (state.Playing != null) foreach (var move in state.Playing.Moves) parts.Add(move.Part);
            return parts;
        }

        /// <summary>Puts every leaf at the routine's end pose at once.</summary>
        private static void Settle(DoorState state, DoorRoutine routine)
        {
            var parts = new HashSet<int>(state.From.Keys);
            foreach (var move in routine.Moves) parts.Add(move.Part);
            foreach (int part in parts)
            {
                var from = state.From.TryGetValue(part, out var pose) ? pose : DoorLeafPose.Shut;
                state.From[part] = Evaluate(routine, part, from, double.PositiveInfinity);
            }
            state.Playing = null;
        }

        /// <summary>A leaf's pose <paramref name="frame"/> frames into a routine that started with the leaf at <paramref name="start"/>.</summary>
        internal static DoorLeafPose Evaluate(DoorRoutine routine, int part, DoorLeafPose start, double frame)
        {
            Vector3 rotation = start.Rotation, slide = start.Slide;
            DoorMove? lastRotate = null, lastSlide = null;
            Vector3 rotateFrom = rotation, slideFrom = slide;
            foreach (var move in routine.Moves)
            {
                if (move.Part != part || move.StartFrame > frame) continue;
                if (move.Kind == DoorMoveKind.Rotate)
                {
                    // A later move on the same leaf starts from where the earlier one had the leaf at that moment.
                    if (lastRotate is { } earlier) rotateFrom = Vector3.Lerp(rotateFrom, earlier.Target, Progress(earlier, move.StartFrame));
                    lastRotate = move;
                }
                else
                {
                    if (lastSlide is { } earlier) slideFrom = Vector3.Lerp(slideFrom, earlier.Target, Progress(earlier, move.StartFrame));
                    lastSlide = move;
                }
            }
            if (lastRotate is { } rotate) rotation = Vector3.Lerp(rotateFrom, rotate.Target, Progress(rotate, frame));
            if (lastSlide is { } slideMove) slide = Vector3.Lerp(slideFrom, slideMove.Target, Progress(slideMove, frame));
            return new DoorLeafPose(rotation, slide);
        }

        private static float Progress(DoorMove move, double frame) =>
            move.Duration <= 0 ? 1.0f : (float)Math.Clamp((frame - move.StartFrame) / move.Duration, 0.0, 1.0);
    }
}
