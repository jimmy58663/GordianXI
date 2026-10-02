// src/Gordian.Core/Events/EventPresentation.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Gordian.Core.Resources.Events;

namespace Gordian.Core.Events
{
    /// <summary>
    /// What a running event shows beyond its actors: the cutscene camera, the screen fades and flashes its scene routines
    /// play (<see cref="SceneRoutine"/>, started by the scheduler opcodes 0x45 / 0x9F and stopped by 0x52 / 0xA3), the log
    /// of tasks whose routines run particle generators (<see cref="CopySceneEffects"/>, played by the renderer with
    /// <see cref="Graphics.SceneEffectPlayer"/>), and whether the event holds the camera (0x46). The event VM feeds it on the game tick; the renderer reads it every frame, sampling the
    /// camera moves and fades at the frame's time so they run smoothly between ticks.
    /// <para>
    /// A camera command plays its route over its duration and then holds the route's last pose until another shot
    /// starts or the camera is released (a still, duration 0, holds its first key). The shot started last wins. A
    /// fade moves from the colour on screen when it starts to its target over its duration and stays there. When the
    /// camera is released (0x46 00, retail kills every camera task then) the viewport's own camera takes over again;
    /// when the event ends everything returns to normal.
    /// </para>
    /// </summary>
    /// <summary>What happened to a scene effect task (see <see cref="SceneEffectEvent"/>).</summary>
    public enum SceneEffectEventKind : byte
    {
        /// <summary>A task started a routine that runs generators or other routines.</summary>
        Start,

        /// <summary>The task was stopped (0x52 / 0xA3, or replaced by the same task started again).</summary>
        Stop,

        /// <summary>The event ended: every scene effect goes.</summary>
        Reset,
    }

    /// <summary>
    /// One entry of the scene effect log the renderer follows (<see cref="EventPresentation.CopySceneEffects"/>): a task
    /// that plays routine <see cref="Routine"/> of <see cref="Resource"/> (file <see cref="FileId"/>) on actor
    /// <see cref="CasterServerId"/>, which stood at <see cref="Origin"/> (internal axes) facing <see cref="Heading"/>
    /// when it started (for an actor that is not drawn, like the invisible marker of Port Jeuno 324's sky flash).
    /// </summary>
    public sealed record SceneEffectEvent(
        long Sequence,
        SceneEffectEventKind Kind,
        int TaskId,
        int FileId = 0,
        EventSceneResource? Resource = null,
        string Routine = "",
        uint CasterServerId = 0,
        uint TargetServerId = 0,
        Vector3 Origin = default,
        float Heading = 0f);

    public sealed class EventPresentation
    {
        private sealed class Shot
        {
            public int TaskId;
            public CameraRoute? Route;
            public CameraRoutePose FixedPose;
            public Vector3 Origin;
            public double Start;
            public double Duration;
        }

        private sealed class Fade
        {
            public int TaskId;
            public Vector3 Target;
            public Vector3 From;
            public double Start;
            public double Duration;
        }

        private readonly object _lock = new();
        private readonly List<Shot> _shots = new();
        private readonly List<Fade> _sceneFades = new();
        private readonly List<Fade> _interfaceFades = new();
        private readonly List<Fade> _flashFades = new();
        private readonly List<SceneEffectEvent> _effects = new();
        private readonly HashSet<int> _effectTasks = new();
        private long _effectSequence;
        private Vector3 _sceneBase = Vector3.One;
        private Vector3 _interfaceBase = Vector3.One;
        private Vector3 _flashBase = Vector3.Zero;
        private volatile bool _cameraHeld;

        /// <summary>The time in seconds commands are scheduled and sampled against (replaceable for tests).</summary>
        public Func<double> Clock { get; set; } = () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        /// <summary>Frames per second of the routines' timing.</summary>
        public const double FramesPerSecond = 60.0;

        /// <summary>Whether the event holds the camera (0x46 01 until 0x46 00): the player cannot turn it.</summary>
        public bool IsCameraHeld => _cameraHeld;

        /// <summary>The colour multiplier 1.0 of a fade command (its colour bytes are half-scale: 0x80 = unchanged).</summary>
        public static Vector3 ColorOf(uint bgra) =>
            new(((bgra >> 16) & 0xFF) / 128f, ((bgra >> 8) & 0xFF) / 128f, (bgra & 0xFF) / 128f);

        /// <summary>
        /// The colour a 0x72 command adds over the scene, 0-1 per channel: its B, G, R bytes are full scale (FF FF FF =
        /// white in <c>who?</c>, 00 = nothing in <c>whi?</c>).
        /// </summary>
        public static Vector3 FlashColorOf(uint bgra) =>
            new(((bgra >> 16) & 0xFF) / 255f, ((bgra >> 8) & 0xFF) / 255f, (bgra & 0xFF) / 255f);

        /// <summary>Entries the effect log keeps; the renderer reads it every frame, so it never falls this far behind.</summary>
        public const int MaxSceneEffectEvents = 256;

        /// <summary>
        /// Schedules a routine's camera shots and fades from now. <paramref name="origin"/> anchors actor-relative
        /// routes (the task's first actor, internal axes).
        /// </summary>
        public void Play(int taskId, EventSceneResource resource, SceneRoutine routine, Vector3 origin) =>
            Play(taskId, resource, routine, origin, 0, 0, 0, 0f);

        /// <summary>
        /// Schedules a routine's camera shots, fades and flashes from now, and logs its effects (generators and the routines
        /// it starts) for the renderer when it has any: they play on <paramref name="casterServerId"/>, which stands at
        /// <paramref name="origin"/> facing <paramref name="heading"/>.
        /// </summary>
        public void Play(int taskId, EventSceneResource resource, SceneRoutine routine, Vector3 origin, int fileId, uint casterServerId, uint targetServerId, float heading)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(routine);
            lock (_lock)
            {
                if (routine.HasEffects)
                {
                    AddEffect(new SceneEffectEvent(0, SceneEffectEventKind.Start, taskId, fileId, resource, routine.Name, casterServerId, targetServerId, origin, heading));
                    _effectTasks.Add(taskId);
                }
                double now = Clock();
                foreach (var command in routine.Commands)
                {
                    double start = now + command.StartFrame / FramesPerSecond;
                    double duration = command.Duration / FramesPerSecond;
                    switch (command.Kind)
                    {
                        case SceneCommandKind.Camera when resource.TryGetRoute(command.Reference, out var route):
                            _shots.Add(new Shot { TaskId = taskId, Route = route, Origin = origin, Start = start, Duration = duration });
                            break;
                        case SceneCommandKind.SceneFade:
                            Insert(_sceneFades, new Fade { TaskId = taskId, Target = ColorOf(command.Color), Start = start, Duration = duration }, _sceneBase);
                            break;
                        case SceneCommandKind.InterfaceFade:
                            Insert(_interfaceFades, new Fade { TaskId = taskId, Target = ColorOf(command.Color), Start = start, Duration = duration }, _interfaceBase);
                            break;
                        case SceneCommandKind.ScreenFlash:
                            Insert(_flashFades, new Fade { TaskId = taskId, Target = FlashColorOf(command.Color), Start = start, Duration = duration }, _flashBase);
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Stops a task (0x52): its commands that have not started are dropped and a running camera move stays on the
        /// pose it has reached.
        /// </summary>
        public void Stop(int taskId)
        {
            lock (_lock)
            {
                double now = Clock();
                for (int i = _shots.Count - 1; i >= 0; i--)
                {
                    var shot = _shots[i];
                    if (shot.TaskId != taskId) continue;
                    if (shot.Start > now)
                    {
                        _shots.RemoveAt(i);
                        continue;
                    }
                    shot.FixedPose = Sample(shot, now);
                    shot.Route = null;
                }
                _sceneFades.RemoveAll(f => f.TaskId == taskId && f.Start > now);
                _interfaceFades.RemoveAll(f => f.TaskId == taskId && f.Start > now);
                _flashFades.RemoveAll(f => f.TaskId == taskId && f.Start > now);
                Recompute(_sceneFades, _sceneBase);
                Recompute(_interfaceFades, _interfaceBase);
                Recompute(_flashFades, _flashBase);
                if (_effectTasks.Remove(taskId)) AddEffect(new SceneEffectEvent(0, SceneEffectEventKind.Stop, taskId));
            }
        }

        /// <summary>
        /// Copies the effect log entries after <paramref name="afterSequence"/> into <paramref name="into"/> (oldest
        /// first) and returns the newest sequence number, to pass next time.
        /// </summary>
        public long CopySceneEffects(long afterSequence, List<SceneEffectEvent> into)
        {
            ArgumentNullException.ThrowIfNull(into);
            lock (_lock)
            {
                foreach (var entry in _effects)
                {
                    if (entry.Sequence > afterSequence) into.Add(entry);
                }
                return _effectSequence;
            }
        }

        private void AddEffect(SceneEffectEvent entry)
        {
            _effects.Add(entry with { Sequence = ++_effectSequence });
            if (_effects.Count > MaxSceneEffectEvents) _effects.RemoveRange(0, _effects.Count - MaxSceneEffectEvents);
        }

        /// <summary>0x46: the event takes the camera (true) or gives it back, which ends every camera move.</summary>
        public void SetCameraHeld(bool held)
        {
            _cameraHeld = held;
            if (held) return;
            lock (_lock) _shots.Clear();
        }

        /// <summary>The event ended: the camera goes back to the player and the fades to normal.</summary>
        public void Reset()
        {
            _cameraHeld = false;
            lock (_lock)
            {
                _shots.Clear();
                _sceneFades.Clear();
                _interfaceFades.Clear();
                _flashFades.Clear();
                _sceneBase = Vector3.One;
                _interfaceBase = Vector3.One;
                _flashBase = Vector3.Zero;
                _effectTasks.Clear();
                AddEffect(new SceneEffectEvent(0, SceneEffectEventKind.Reset, -1));
            }
        }

        /// <summary>The event camera now (internal axes), or false when no shot has started (the viewport's camera applies).</summary>
        public bool TryGetCamera(out CameraRoutePose pose) => TryGetCamera(Clock(), out pose);

        public bool TryGetCamera(double now, out CameraRoutePose pose)
        {
            lock (_lock)
            {
                int current = -1;
                for (int i = 0; i < _shots.Count; i++)
                {
                    if (_shots[i].Start <= now && (current < 0 || _shots[i].Start >= _shots[current].Start)) current = i;
                }
                if (current < 0)
                {
                    pose = default;
                    return false;
                }
                var shot = _shots[current];
                // Shots the current one replaced are done with.
                _shots.RemoveAll(s => s != shot && s.Start <= shot.Start);
                pose = Sample(shot, now);
                return true;
            }
        }

        /// <summary>The colour the 3D scene is multiplied by now (1 = as drawn, 0 = black; the <c>fdo?</c> / <c>fdi?</c> fades).</summary>
        public Vector3 SceneColor => SceneColorAt(Clock());

        public Vector3 SceneColorAt(double now)
        {
            lock (_lock) return Evaluate(_sceneFades, ref _sceneBase, now);
        }

        /// <summary>The colour added over the 3D scene now, 0-1 per channel (0x72: the <c>who?</c> / <c>whi?</c> white fades and flashes).</summary>
        public Vector3 SceneFlash => SceneFlashAt(Clock());

        public Vector3 SceneFlashAt(double now)
        {
            lock (_lock) return Evaluate(_flashFades, ref _flashBase, now);
        }

        /// <summary>How visible the 2D interface is now, 0-1 (the <c>fao?</c> / <c>fai?</c> fades).</summary>
        public float InterfaceOpacity => InterfaceOpacityAt(Clock());

        public float InterfaceOpacityAt(double now)
        {
            lock (_lock)
            {
                var color = Evaluate(_interfaceFades, ref _interfaceBase, now);
                return Math.Clamp((color.X + color.Y + color.Z) / 3f, 0f, 1f);
            }
        }

        private static CameraRoutePose Sample(Shot shot, double now)
        {
            if (shot.Route == null) return shot.FixedPose;
            float progress = shot.Duration > 0 ? (float)((now - shot.Start) / shot.Duration) : 0f;
            return shot.Route.Evaluate(progress, shot.Origin);
        }

        /// <summary>Adds a fade in start order and works out where every fade starts from.</summary>
        private static void Insert(List<Fade> fades, Fade fade, Vector3 baseColor)
        {
            int at = fades.Count;
            while (at > 0 && fades[at - 1].Start > fade.Start) at--;
            fades.Insert(at, fade);
            Recompute(fades, baseColor);
        }

        private static void Recompute(List<Fade> fades, Vector3 baseColor)
        {
            for (int i = 0; i < fades.Count; i++)
            {
                fades[i].From = i == 0 ? baseColor : ValueAt(fades[i - 1], fades[i].Start);
            }
        }

        private static Vector3 ValueAt(Fade fade, double time)
        {
            if (time <= fade.Start) return fade.From;
            if (fade.Duration <= 0 || time >= fade.Start + fade.Duration) return fade.Target;
            return Vector3.Lerp(fade.From, fade.Target, (float)((time - fade.Start) / fade.Duration));
        }

        private static Vector3 Evaluate(List<Fade> fades, ref Vector3 baseColor, double now)
        {
            int current = -1;
            for (int i = 0; i < fades.Count && fades[i].Start <= now; i++) current = i;
            if (current < 0) return baseColor;
            if (current > 0)
            {
                // Fades before the running one are done: it starts from their result.
                baseColor = fades[current].From;
                fades.RemoveRange(0, current);
            }
            return ValueAt(fades[0], now);
        }
    }
}
