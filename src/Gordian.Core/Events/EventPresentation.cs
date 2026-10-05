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

    /// <summary>
    /// The motion blur of the cutscene scene routines (op 0x0E): <see cref="Amount"/> of the previous frame kept each
    /// frame (0 = off), tinted by <see cref="Tint"/> and zoomed by <see cref="Zoom"/>.
    /// </summary>
    public readonly record struct SceneBlur(float Amount, Vector3 Tint, float Zoom)
    {
        public bool IsActive => Amount > 0.001f;
    }

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
        private readonly List<Fade> _blurTints = new();
        private readonly List<Fade> _blurAmounts = new();
        private readonly List<Dissolve> _dissolves = new();
        private int _dissolveSequence;
        private readonly List<SceneEffectEvent> _effects = new();
        private readonly HashSet<int> _effectTasks = new();
        private long _effectSequence;
        private Vector3 _sceneBase = Vector3.One;
        private Vector3 _interfaceBase = Vector3.One;
        private Vector3 _flashBase = Vector3.Zero;
        private Vector3 _blurTintBase = Vector3.One;
        private Vector3 _blurAmountBase = NoBlur;

        /// <summary>The blur channel's resting value: nothing of the previous frame kept, no zoom.</summary>
        private static readonly Vector3 NoBlur = new(0f, 1f, 0f);

        private sealed class Dissolve
        {
            public int TaskId;
            public int Sequence;
            public double Start;
            public double Duration;
        }
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
                AddSounds(taskId, fileId, resource, routine, now, origin, casterServerId, 0);
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
                        case SceneCommandKind.Blur:
                            Insert(_blurTints, new Fade { TaskId = taskId, Target = ColorOf(command.Color), Start = start, Duration = duration }, _blurTintBase);
                            Insert(_blurAmounts, new Fade { TaskId = taskId, Target = BlurAmountOf(command.Color, command.Factor), Start = start, Duration = duration }, _blurAmountBase);
                            break;
                        case SceneCommandKind.CrossDissolve when duration > 0:
                            _dissolves.Add(new Dissolve { TaskId = taskId, Sequence = ++_dissolveSequence, Start = start, Duration = duration });
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
                _blurTints.RemoveAll(f => f.TaskId == taskId && f.Start > now);
                _blurAmounts.RemoveAll(f => f.TaskId == taskId && f.Start > now);
                _dissolves.RemoveAll(d => d.TaskId == taskId && d.Start > now);
                _sounds.RemoveAll(s => s.TaskId == taskId && s.Start > now);
                Recompute(_sceneFades, _sceneBase);
                Recompute(_interfaceFades, _interfaceBase);
                Recompute(_flashFades, _flashBase);
                Recompute(_blurTints, _blurTintBase);
                Recompute(_blurAmounts, _blurAmountBase);
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

        /// <summary>A cutscene sound effect due at <see cref="Start"/> (presentation clock seconds).</summary>
        /// <param name="TaskId">The scheduler task.</param>
        /// <param name="Start">When it plays.</param>
        /// <param name="SoundId">The <c>.spw</c> id (0 for a kill).</param>
        /// <param name="Opcode">The routine command: 0x0A at the source actor, 0x0B at the target, 0x60 global; 0x02 a sound generator starts, 0x1E one is killed.</param>
        /// <param name="Origin">Where the task's actor stands (internal axes).</param>
        /// <param name="CasterServerId">The task's actor.</param>
        /// <param name="Duration">For a sound generator, how long it emits (seconds; 0 = until killed or the event ends when the file loops, else one pass).</param>
        /// <param name="FileId">The scene file.</param>
        /// <param name="Generator">The sound generator's name, for 0x02 / 0x1E.</param>
        /// <param name="Far">The generator's audible range (0x4C), 0 when not a generator.</param>
        /// <param name="Near">The generator's full-volume radius.</param>
        public readonly record struct SceneSound(int TaskId, double Start, int SoundId, byte Opcode, Vector3 Origin, uint CasterServerId,
            double Duration = 0, int FileId = 0, string Generator = "", float Far = 0f, float Near = 0f);

        private readonly List<SceneSound> _sounds = new();
        private const int MaxSoundLinkDepth = 4;
        private int _soundResets;

        /// <summary>Increments when the event ends (<see cref="Reset"/>): cutscene sounds still playing should stop.</summary>
        public int SoundResets => Volatile.Read(ref _soundResets);

        /// <summary>
        /// Schedules the sounds of a routine and of the routines it starts (0x03, one pass of 0x73 loops): the sound
        /// commands (<see cref="SceneCommandKind.Sound"/>), and the generators it spawns, replaces or kills whose linked
        /// data is a sound (Port Jeuno 324: 57129 <c>se00</c> plays <c>1060</c> → 41060, 30905 <c>who1</c> 8158, <c>0pro</c>
        /// 34125 and 34126; the lightning <c>0rak</c> spawns <c>6041</c> and the looping <c>2088</c>, which <c>krak</c>
        /// kills). The renderer's effect player only exists for files with drawn generators, so sounds are scheduled here.
        /// </summary>
        private void AddSounds(int taskId, int fileId, EventSceneResource resource, SceneRoutine routine, double start, Vector3 origin, uint caster, int depth)
        {
            foreach (var command in routine.Commands)
            {
                double at = start + command.StartFrame / FramesPerSecond;
                double duration = command.Duration / FramesPerSecond;
                switch (command.Kind)
                {
                    case SceneCommandKind.Sound when resource.TryGetSound(command.Reference, out int soundId):
                        _sounds.Add(new SceneSound(taskId, at, soundId, command.Opcode, origin, caster, FileId: fileId));
                        break;
                    case SceneCommandKind.SpawnGenerator when resource.TryGetGeneratorSound(command.Reference, out var spawned):
                        _sounds.Add(new SceneSound(taskId, at, spawned.SoundId, 0x02, origin, caster, duration, fileId, command.Reference, spawned.Far, spawned.Near));
                        break;
                    case SceneCommandKind.KillGenerator when resource.TryGetGeneratorSound(command.Reference, out _):
                        _sounds.Add(new SceneSound(taskId, at, 0, 0x1E, origin, caster, FileId: fileId, Generator: command.Reference));
                        break;
                    case SceneCommandKind.ReplaceGenerator:
                        if (resource.TryGetGeneratorSound(command.Reference, out _))
                        {
                            _sounds.Add(new SceneSound(taskId, at, 0, 0x1E, origin, caster, FileId: fileId, Generator: command.Reference));
                        }

                        if (resource.TryGetGeneratorSound(command.Reference2, out var replacement))
                        {
                            _sounds.Add(new SceneSound(taskId, at, replacement.SoundId, 0x02, origin, caster, duration, fileId, command.Reference2, replacement.Far, replacement.Near));
                        }

                        break;
                    case SceneCommandKind.StartRoutine or SceneCommandKind.LoopRoutine when depth < MaxSoundLinkDepth
                        && command.Reference != routine.Name && resource.TryGetRoutine(command.Reference, out var child):
                        AddSounds(taskId, fileId, resource, child, at, origin, caster, depth + 1);
                        break;
                }
            }

            if (_sounds.Count > MaxSceneEffectEvents)
            {
                _sounds.RemoveRange(0, _sounds.Count - MaxSceneEffectEvents);
            }
        }

        /// <summary>Moves the sounds due by now into <paramref name="into"/> (each is handed out once).</summary>
        public void TakeDueSounds(List<SceneSound> into)
        {
            ArgumentNullException.ThrowIfNull(into);
            lock (_lock)
            {
                double now = Clock();
                for (int i = 0; i < _sounds.Count; i++)
                {
                    if (_sounds[i].Start <= now)
                    {
                        into.Add(_sounds[i]);
                        _sounds.RemoveAt(i--);
                    }
                }
            }
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
                _blurTints.Clear();
                _blurAmounts.Clear();
                _dissolves.Clear();
                _sounds.Clear();
                Interlocked.Increment(ref _soundResets);
                _sceneBase = Vector3.One;
                _interfaceBase = Vector3.One;
                _flashBase = Vector3.Zero;
                _blurTintBase = Vector3.One;
                _blurAmountBase = NoBlur;
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

        /// <summary>
        /// The motion blur now (op 0x0E, <c>blon</c> / <c>blof</c>): how much of the previous frame each frame keeps (0 =
        /// off, 1 = all), its tint (1 = as drawn) and its zoom (1 = none, below 1 the trail spreads outward).
        /// </summary>
        public SceneBlur Blur => BlurAt(Clock());

        public SceneBlur BlurAt(double now)
        {
            lock (_lock)
            {
                var tint = Evaluate(_blurTints, ref _blurTintBase, now);
                var amount = Evaluate(_blurAmounts, ref _blurAmountBase, now);
                return new SceneBlur(Math.Clamp(amount.X, 0f, 1f), tint, amount.Y);
            }
        }

        /// <summary>
        /// The cross-dissolve now (op 0x10, <c>ovl1</c> / <c>ovl2</c>): which one (a new number when one starts, so the
        /// renderer keeps the frame before it) and how much of that frame still shows, 1 at its start to 0 at its end;
        /// (0, 0) when none runs.
        /// </summary>
        public (int Sequence, float Opacity) CrossDissolve => CrossDissolveAt(Clock());

        public (int Sequence, float Opacity) CrossDissolveAt(double now)
        {
            lock (_lock)
            {
                _dissolves.RemoveAll(d => now >= d.Start + d.Duration);
                Dissolve? running = null;
                foreach (var dissolve in _dissolves)
                {
                    if (dissolve.Start <= now && (running == null || dissolve.Start >= running.Start)) running = dissolve;
                }
                if (running == null) return (0, 0f);
                return (running.Sequence, 1f - (float)((now - running.Start) / running.Duration));
            }
        }

        /// <summary>
        /// The blur channel of an op 0x0E command: X the share of the previous frame kept (its A byte, 0x80 = all, as the
        /// fade colours' 0x80 = full), Y the zoom (its float).
        /// </summary>
        public static Vector3 BlurAmountOf(uint bgra, float factor) =>
            new(((bgra >> 24) & 0xFF) / 128f, float.IsFinite(factor) && factor > 0f ? factor : 1f, 0f);

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
