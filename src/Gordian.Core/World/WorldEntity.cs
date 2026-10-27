// src/Gordian.Core/World/WorldEntity.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using Gordian.Core.Animation;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Packets;

namespace Gordian.Core.World
{
    /// <summary>
    /// High-level categorization of world entities.
    /// </summary>
    public enum EntityType : byte
    {
        Player = 0,
        Npc = 1,
        Monster = 2,
        Pet = 3,
        Trust = 4,
        Elevator = 5,
        Ship = 6,
        Door = 7
    }

    /// <summary>
    /// Visual model and equipment appearance for an entity.
    /// </summary>
    public sealed class EntityAppearance
    {
        public uint ModelId { get; set; }
        public ushort CostumeId { get; set; }
        public ushort[] GrapIdTable { get; set; } = new ushort[9];

        public ushort FaceModel => GrapIdTable[0];
        public ushort Head => GrapIdTable[1];
        public ushort Body => GrapIdTable[2];
        public ushort Hands => GrapIdTable[3];
        public ushort Legs => GrapIdTable[4];
        public ushort Feet => GrapIdTable[5];
        public ushort MainWeapon => GrapIdTable[6];
        public ushort SubWeapon => GrapIdTable[7];
        public ushort RangedWeapon => GrapIdTable[8];

        public void CopyFrom(ReadOnlySpan<ushort> grapTable)
        {
            int limit = Math.Min(grapTable.Length, GrapIdTable.Length);
            for (int i = 0; i < limit; i++)
            {
                GrapIdTable[i] = grapTable[i];
            }
        }
    }

    public enum LocomotionDirection : byte
    {
        Forward = 0,
        Backward = 1,
        Left = 2,
        Right = 3
    }

    /// <summary>
    /// Base class representing any dynamic or static entity in the game world.
    /// </summary>
    public class WorldEntity
    {
        public uint ServerId { get; }
        public ushort TargetIndex { get; set; }
        public string Name { get; set; } = string.Empty;
        public EntityType Type { get; set; }

        private Vector3 _position;
        private bool _hasTarget;
        public Vector3 Position
        {
            get => _position;
            set
            {
                _position = value;
                if (!_hasTarget)
                {
                    _targetPosition = value;
                    _hasTarget = true;
                }
            }
        }

        private Vector3 _targetPosition;

        /// <summary>
        /// Latest server-reported position. Remote entities chase it each frame in <see cref="InterpolatePosition"/>.
        /// </summary>
        public Vector3 TargetPosition
        {
            get => _targetPosition;
            set
            {
                _targetPosition = value;
                _hasTarget = true;
            }
        }

        /// <summary>
        /// Set by the network thread to request that the next render tick jump straight to <see cref="TargetPosition"/>
        /// (a discontinuity such as a warp), keeping all <see cref="Position"/> writes on the render thread.
        /// </summary>
        public volatile bool SnapToTargetPending;

        /// <summary>
        /// Set when the server reports the entity has stopped while it is still visually approaching the final position;
        /// <see cref="Speed"/> is cleared once it arrives so the run/walk animation carries through to the stop.
        /// </summary>
        public volatile bool StopOnArrival;

        public float RenderHeadingRadians { get; set; }
        public LocomotionDirection LocomotionDirection { get; set; } = LocomotionDirection.Forward;

        /// <summary>
        /// Seconds of travel an entity may trail its latest server position before a new position is treated as a
        /// discontinuity (warp) and snapped rather than run.
        /// </summary>
        public const float MaxTrailSeconds = 1.5f;

        /// <summary>
        /// Seconds between position updates of a moving entity, assumed until its own cadence is learned
        /// (<see cref="UpdateIntervalSeconds"/>). LandSandBoat moves entities on its 400 ms logic tick
        /// (<c>kLogicUpdateInterval</c>, map_constants.h) and a Release build sends 0x00D / 0x00E for a moving entity on
        /// every tick: median 0.40 s (10th-90th percentile 0.31-0.49 s) over 57,000 mob and 1,600 player updates in the
        /// maintainer's session logs of 2026-10-23 to 2026-10-26 (#359). A Debug build batched them about 1.4 s apart.
        /// </summary>
        public const float DefaultUpdateIntervalSeconds = 0.4f;

        /// <summary>
        /// Playback delay (seconds behind real time) a remote entity starts with before its update cadence is learned: one
        /// update interval plus the arrival jitter (90th percentile 0.49 s against the 0.40 s median, #359) and
        /// <see cref="DelayMarginSeconds"/>.
        /// </summary>
        public const float DefaultPlaybackDelaySeconds = 0.6f;

        /// <summary>
        /// Shortest playback delay: the timeline never plays closer to real time than this.
        /// </summary>
        public const float MinPlaybackDelaySeconds = 0.3f;

        /// <summary>
        /// Longest playback delay: covers a Debug server's 1.4 s batches plus their staleness.
        /// </summary>
        public const float MaxPlaybackDelaySeconds = 3.0f;

        /// <summary>
        /// Update intervals of silence after which a moving entity counts as stopped (plus <see cref="DelayMarginSeconds"/>).
        /// LandSandBoat sends no update when a monster or NPC stops, it just stops sending: 99.9% of the gaps between a moving
        /// mob's updates are under 0.6 s and almost none fall between 0.6 and 1.2 s, while a pause lasts seconds (#359).
        /// Without this, playback took the silence for a late update and grew its delay to the 3 s cap, so the mob's next
        /// move started 3 s late (every one of about 1,900 moves after a pause in the 2026-10-23/25/26 logs).
        /// </summary>
        public const float StopTimeoutIntervals = 2.0f;

        /// <summary>
        /// Gaps longer than this between two moving updates are pauses, not the update cadence, and are not learned.
        /// </summary>
        public const float MaxLearnedIntervalSeconds = 3.0f;

        /// <summary>
        /// Rate of the 0x00D movement counter (<see cref="LastMovTime"/>): ticks per second of continuous movement.
        /// Captured traffic confirms it: counter deltas / 60 × 5.0 yalms/sec match the reported displacements.
        /// </summary>
        public const double MovTimeTicksPerSecond = 60.0;

        /// <summary>
        /// Safety margin added to the worst observed sample staleness when choosing the playback delay. Small, because
        /// running short degrades to a brief slowdown rather than a stop.
        /// </summary>
        public const float DelayMarginSeconds = 0.1f;

        /// <summary>
        /// Seconds of not-yet-played samples below which playback slows down while an entity is moving, rather than freezing.
        /// No more than <see cref="DelayMarginSeconds"/>: the delay leaves that much in hand when the update arrives as late as
        /// the worst recent one, so a normal update never triggers the slowdown. At 0.25 s (tuned for 1.4 s batches) every
        /// 400 ms update cycle dipped below it and the entity sped up and slowed down on each one.
        /// </summary>
        public const float StarvationBufferSeconds = DelayMarginSeconds;

        /// <summary>
        /// How much sooner than the steady-state delay a move from rest starts playing, in update intervals. The first
        /// stretch of a move does not need the full buffer; playback rebuilds it by running <see cref="DelayRecoveryRate"/>
        /// slower than real time. 0.35 intervals is the 0.5 s tuned against the Debug server's 1.4 s batches; at the Release
        /// server's 0.4 s it is 0.14 s.
        /// </summary>
        public const float StartLeadIntervals = 0.35f;

        /// <summary>
        /// Number of recent delay requirements (gaps between updates) whose worst sets the playback delay: about 5 s of
        /// movement at 400 ms, so the occasional 0.5-0.6 s gap (2-10% of them, #359) is in the window.
        /// </summary>
        public const int DelayRequirementWindow = 12;

        /// <summary>
        /// Fraction of real time by which playback may run slow (to rebuild the delay) or fast (to shrink it).
        /// </summary>
        public const float DelayRecoveryRate = 0.15f;

        /// <summary>
        /// Once the stop has been received nothing more needs buffering, so the remaining stretch plays this fraction
        /// faster to reach the stop point sooner.
        /// </summary>
        public const float StopCatchUpRate = 0.2f;

        /// <summary>
        /// How fast (fraction of real time) a move's clock origin may shift between samples, either way. The move counter only
        /// bounds when a sample was taken (never after it arrived), so the origin moves toward the freshest bound at most this
        /// fast, which follows a counter running up to this far off real time without speeding playback up or slowing it down
        /// by more. Captured counters ran up to about 2% off real time (#59).
        /// </summary>
        public const double MoveClockDriftAllowance = 0.05;

        /// <summary>
        /// Monotonic clock (seconds) shared by the network thread when timestamping position samples and the render thread
        /// when playing them back.
        /// </summary>
        public static double ClockSeconds => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        private readonly record struct MotionSample(double Time, Vector3 Position, byte Direction);

        private readonly object _motionLock = new();
        private readonly List<MotionSample> _samples = new(16);
        private readonly float[] _delayRequirements = new float[DelayRequirementWindow];
        private int _delayRequirementCount;
        private int _delayRequirementNext;
        private readonly float[] _intervals = new float[8];
        private int _intervalCount;
        private int _intervalNext;
        private float _updateInterval = DefaultUpdateIntervalSeconds;
        private float _groundSpeed;
        private double _lastArrival = double.NegativeInfinity;
        private bool _lastSampleMoving;
        private bool _moveSessionActive;
        private double _moveSessionOrigin;
        private ushort _moveSessionLastMovTime;
        private double _moveSessionLastArrival;
        private float _targetPlaybackDelay = DefaultPlaybackDelaySeconds;
        private float _playbackDelay = DefaultPlaybackDelaySeconds;
        private volatile bool _hasMotionTimeline;
        private bool _expectMoreSamples;

        /// <summary>
        /// Current seconds this entity is displayed behind real time.
        /// </summary>
        public float PlaybackDelaySeconds { get { lock (_motionLock) return _playbackDelay; } }

        /// <summary>
        /// Seconds between this entity's position updates while it moves: the median of its recent gaps, or
        /// <see cref="DefaultUpdateIntervalSeconds"/> until one is seen. The other playback timings scale with it, so a
        /// server with a different cadence (a Debug LandSandBoat build batches about 1.4 s apart) is followed after one gap.
        /// </summary>
        public float UpdateIntervalSeconds { get { lock (_motionLock) return _updateInterval; } }

        /// <summary>
        /// Silence after which a moving entity counts as stopped: <see cref="StopTimeoutIntervals"/> update intervals plus
        /// <see cref="DelayMarginSeconds"/> (0.9 s at 400 ms updates).
        /// </summary>
        public float StopTimeoutSeconds { get { lock (_motionLock) return StopTimeoutFor(_updateInterval); } }

        /// <summary>
        /// Ground speed (yalms per second) seen between this entity's recent moving samples, falling back to
        /// <see cref="MovementSpeedYalms"/> until one is seen. It is not always the speed field / 10: LandSandBoat moves
        /// monsters and NPCs at speed / 17 yalms per second (<c>kYalmsPerSecondPerSpeed</c>, pathfind.cpp; the logs agree:
        /// 0.94 yalm per 400 ms at speed 40, 0.49 at speed 21), and a mounted player covers twice its speed field (#359).
        /// Used to time a step the server gave no time for: a stop's final step, and a sample that arrived too late.
        /// </summary>
        public float GroundSpeedYalms { get { lock (_motionLock) return GroundSpeedLocked; } }

        private float GroundSpeedLocked => _groundSpeed > 0f ? _groundSpeed : MovementSpeedYalms;

        private static float StopTimeoutFor(float interval) => (StopTimeoutIntervals * interval) + DelayMarginSeconds;

        /// <summary>
        /// Whether server position samples drive this entity's render position (remote entities once they first move).
        /// </summary>
        public bool HasMotionTimeline => _hasMotionTimeline;

        /// <summary>
        /// Whether the rendered position is travelling this frame (the played-back segment has displacement).
        /// </summary>
        public bool IsTranslating { get; private set; }

        /// <summary>
        /// When the rendered position last stopped travelling, or <see cref="DateTime.MinValue"/>.
        /// </summary>
        public DateTime ArrivedUtc { get; private set; } = DateTime.MinValue;

        /// <summary>
        /// Age, in seconds, of the most recent position sample at the moment it arrived (diagnostics).
        /// </summary>
        public float LastSampleAgeSeconds { get; private set; }

        /// <summary>
        /// Records a server position on this entity's motion timeline. Called from the network thread.
        /// <para>
        /// For players, <paramref name="movTime"/> (the 0x00D movement counter, 60 ticks/sec since the move began) places each
        /// sample at the moment it was actually taken, rather than when the packet happened to arrive; the start of a move is
        /// anchored at the last rest position. A stop is timed from the distance of its final step at the entity's ground speed.
        /// NPCs carry no counter and are timestamped on arrival; a move from rest is anchored one update interval earlier,
        /// since the server moved them for a tick before reporting it.
        /// </para>
        /// The render thread plays the timeline back <see cref="PlaybackDelaySeconds"/> behind real time, which adapts to how
        /// stale the newest sample can get before the next one arrives.
        /// </summary>
        public void AddServerSample(Vector3 newPos, bool isMoving, ushort movTime, byte direction, double now)
        {
            lock (_motionLock)
            {
                double renderTime = now - _playbackDelay;

                // Samples already played are dropped here too: an entity of a session not on screen is not played back,
                // and its timeline would otherwise grow for as long as it keeps moving.
                while (_samples.Count > 2 && _samples[1].Time <= renderTime)
                {
                    _samples.RemoveAt(0);
                }

                bool hadSamples = _samples.Count > 0;
                Vector3 lastPos = hadSamples ? _samples[^1].Position : _position;
                byte lastDirection = hadSamples ? _samples[^1].Direction : Direction;
                double lastTime = hadSamples ? _samples[^1].Time : double.NegativeInfinity;
                float nominal = MovementSpeedYalms;

                // The gap since the previous update of a move is the server's cadence; a gap past the stop timeout means the
                // entity stopped in between (the render thread may not have noticed: a session not on screen is not played).
                double sinceLast = now - _lastArrival;
                if (_lastSampleMoving && isMoving && sinceLast <= MaxLearnedIntervalSeconds)
                {
                    LearnInterval((float)sinceLast);
                }
                bool withinMove = sinceLast <= StopTimeoutFor(_updateInterval);
                bool continuingMove = _expectMoreSamples && withinMove;
                _lastArrival = now;
                _lastSampleMoving = isMoving;

                float snapDistance = Math.Max(15.0f, Math.Max(nominal, GroundSpeedLocked) * MaxTrailSeconds * 2.0f);
                if (Vector3.Distance(newPos, lastPos) > snapDistance)
                {
                    // Warp / zone-line hop: restart the timeline at the new position.
                    _samples.Clear();
                    _samples.Add(new MotionSample(now, newPos, direction));
                    _moveSessionActive = isMoving && movTime > 0;
                    _moveSessionOrigin = now - (movTime / MovTimeTicksPerSecond);
                    _moveSessionLastMovTime = movTime;
                    _moveSessionLastArrival = now;
                    _expectMoreSamples = isMoving;
                    _hasMotionTimeline = true;
                    SnapToTargetPending = true;
                    TargetPosition = newPos;
                    return;
                }

                double sampleTime;
                bool measureDelay = false;
                if (isMoving && movTime > 0)
                {
                    double sinceMoveStart = movTime / MovTimeTicksPerSecond;
                    bool continuing = _moveSessionActive && movTime > _moveSessionLastMovTime;
                    if (continuing)
                    {
                        // Every sample of one move is stamped from one origin, so their spacing follows the counter. A sample
                        // was taken no later than it arrived, so each one bounds the origin from above: the origin moves
                        // toward the tightest bound (the freshest packet), but by at most the drift allowance of real time
                        // either way, so one fresh packet after stale ones does not squeeze a segment into a sprint.
                        // A fixed origin from the first packet let a counter running fast push the stamps further into the
                        // future the longer the move lasted: a follower running for minutes fell ever further behind (#59).
                        double bound = now - sinceMoveStart;
                        double allowance = MoveClockDriftAllowance * Math.Max(0.0, now - _moveSessionLastArrival);
                        double target = Math.Min(bound, _moveSessionOrigin + allowance);
                        _moveSessionOrigin = Math.Max(target, _moveSessionOrigin - allowance);
                        measureDelay = withinMove;
                        renderTime = ResumeFromHold(now, lastTime, renderTime);
                    }
                    else
                    {
                        _moveSessionOrigin = now - sinceMoveStart;
                        if (!_moveSessionActive)
                        {
                            renderTime = SeatMoveStart(now, lastTime, renderTime);

                            // Anchor the start of the move at the rest position so playback departs when the player did.
                            AppendSample(Math.Max(_moveSessionOrigin, lastTime), lastPos, lastDirection, renderTime);
                        }
                        _moveSessionActive = true;
                    }
                    _moveSessionLastMovTime = movTime;
                    _moveSessionLastArrival = now;
                    sampleTime = _moveSessionOrigin + sinceMoveStart;
                }
                else if (isMoving)
                {
                    if (!continuingMove)
                    {
                        // From rest. Monsters and NPCs carry no move counter, but the server moved them for one update
                        // interval before sending this, so the move began about that long ago: anchor the rest position
                        // there. Without the anchor the first step either snapped (no earlier sample) or was stretched over
                        // the whole playback delay (3 s after a pause, #359).
                        renderTime = SeatMoveStart(now, lastTime, renderTime);
                        AppendSample(Math.Max(now - _updateInterval, lastTime), lastPos, lastDirection, renderTime);
                    }
                    else
                    {
                        renderTime = ResumeFromHold(now, lastTime, renderTime);
                    }
                    measureDelay = continuingMove && hadSamples && Vector3.DistanceSquared(newPos, lastPos) > 0.0025f;
                    sampleTime = now;
                }
                else
                {
                    // Stopped: the final step ended roughly its length (at ground speed) after the previous sample. An update
                    // while already at rest (a turn in place, a small correction) plays as soon as playback is idle.
                    sampleTime = _moveSessionActive && hadSamples
                        ? Math.Min(now, lastTime + (Vector3.Distance(newPos, lastPos) / GroundSpeedLocked))
                        : (renderTime >= lastTime ? renderTime : now);
                    measureDelay = _moveSessionActive && hadSamples && withinMove;
                    _moveSessionActive = false;
                    _moveSessionLastMovTime = 0;
                }

                if (measureDelay && hadSamples)
                {
                    // Playback must not have run past the newest sample we had when this one arrived.
                    RecordDelayRequirement((float)(now - lastTime));
                }

                if (isMoving && continuingMove && hadSamples)
                {
                    LearnGroundSpeed(Vector3.Distance(newPos, lastPos), sampleTime - lastTime);
                }

                LastSampleAgeSeconds = (float)(now - sampleTime);
                _expectMoreSamples = isMoving;
                AppendSample(sampleTime, newPos, direction, renderTime);
                _hasMotionTimeline = true;
                TargetPosition = newPos;
            }
        }

        /// <summary>
        /// A move from rest while playback is idle at the rest position begins sooner than the steady-state delay (by
        /// <see cref="StartLeadIntervals"/> update intervals). Re-seating the playback clock while it holds there is invisible,
        /// as long as it stays at or after the rest sample. Returns the new render time.
        /// </summary>
        private double SeatMoveStart(double now, double lastTime, double renderTime)
        {
            if (renderTime < lastTime) return renderTime;

            float startDelay = Math.Max(MinPlaybackDelaySeconds, _targetPlaybackDelay - (StartLeadIntervals * _updateInterval));
            _playbackDelay = (float)Math.Min(startDelay, now - lastTime);
            return now - _playbackDelay;
        }

        /// <summary>
        /// The next sample of a move arrived after playback ran out and stopped at the newest one: the playback clock is set
        /// back to that sample, which is invisible while it holds there, so the new segment plays at its true pace instead of
        /// being squeezed into what is left of the delay (a sprint). The delay grows to what this update needed; it eases back
        /// toward the target at <see cref="DelayRecoveryRate"/>. Returns the new render time.
        /// </summary>
        private double ResumeFromHold(double now, double lastTime, double renderTime)
        {
            if (renderTime < lastTime || now - lastTime > MaxPlaybackDelaySeconds) return renderTime;

            _playbackDelay = (float)(now - lastTime);
            return lastTime;
        }

        private void AppendSample(double time, Vector3 pos, byte direction, double renderTime)
        {
            if (_samples.Count > 0)
            {
                MotionSample last = _samples[^1];
                float stepSeconds = Math.Max(0.001f, Vector3.Distance(pos, last.Position) / GroundSpeedLocked);
                if (last.Time < renderTime)
                {
                    // Playback is holding at the last sample: continue from there rather than interpolating from where
                    // the entity was long ago, which would pop it part-way along the new segment.
                    _samples.Add(new MotionSample(renderTime, last.Position, last.Direction));
                }
                if (time <= renderTime)
                {
                    // Arrived too late to play at its true time: play it from the held position at ground speed.
                    time = renderTime + stepSeconds;
                }
                if (time <= _samples[^1].Time)
                {
                    time = _samples[^1].Time + stepSeconds;
                }
            }
            _samples.Add(new MotionSample(time, pos, direction));
        }

        private void RecordDelayRequirement(float requiredSeconds)
        {
            if (requiredSeconds <= 0f || requiredSeconds > MaxPlaybackDelaySeconds) return;

            _delayRequirements[_delayRequirementNext] = requiredSeconds;
            _delayRequirementNext = (_delayRequirementNext + 1) % _delayRequirements.Length;
            _delayRequirementCount = Math.Min(_delayRequirementCount + 1, _delayRequirements.Length);

            float worst = 0f;
            for (int i = 0; i < _delayRequirementCount; i++)
            {
                worst = Math.Max(worst, _delayRequirements[i]);
            }
            _targetPlaybackDelay = Math.Clamp(Math.Max(worst, _updateInterval) + DelayMarginSeconds, MinPlaybackDelaySeconds, MaxPlaybackDelaySeconds);
        }

        private void LearnInterval(float seconds)
        {
            _intervals[_intervalNext] = seconds;
            _intervalNext = (_intervalNext + 1) % _intervals.Length;
            _intervalCount = Math.Min(_intervalCount + 1, _intervals.Length);

            // The median: a clumped pair of updates or a short pause does not move it.
            Span<float> sorted = stackalloc float[_intervals.Length];
            _intervals.AsSpan(0, _intervalCount).CopyTo(sorted);
            sorted = sorted[.._intervalCount];
            sorted.Sort();
            _updateInterval = sorted[_intervalCount / 2];

            // The next update is due an interval after the newest one, so playback needs at least that much in hand: a
            // slower server is followed from its first gap, without waiting for late updates to grow the delay.
            _targetPlaybackDelay = Math.Clamp(Math.Max(_targetPlaybackDelay, _updateInterval + DelayMarginSeconds), MinPlaybackDelaySeconds, MaxPlaybackDelaySeconds);
        }

        private void LearnGroundSpeed(float distance, double seconds)
        {
            // Too short a gap (two updates clumped together) says little about speed.
            if (seconds < 0.15 || distance < 0.05f) return;

            float observed = Math.Clamp((float)(distance / seconds), 0.2f, 40.0f);
            _groundSpeed = _groundSpeed > 0f ? _groundSpeed + (0.3f * (observed - _groundSpeed)) : observed;
        }

        /// <summary>
        /// Advances the render position using the shared <see cref="ClockSeconds"/>.
        /// </summary>
        public void InterpolatePosition(float deltaSeconds) => InterpolatePosition(deltaSeconds, ClockSeconds);

        /// <summary>
        /// Places the entity at a server-set position (WPOS), discarding any motion in flight: the render thread jumps
        /// there on its next tick instead of walking over.
        /// </summary>
        public void Warp(Vector3 position, byte direction, double now)
        {
            lock (_motionLock)
            {
                if (_hasMotionTimeline)
                {
                    _samples.Clear();
                    _samples.Add(new MotionSample(now, position, direction));
                    _moveSessionActive = false;
                }
                Direction = direction;
                TargetPosition = position;
                SnapToTargetPending = true;
            }
        }

        /// <summary>
        /// Plays the motion timeline back at <paramref name="now"/> minus the playback delay, interpolating between server
        /// samples so the entity travels at its true speed along its true path, never past the newest sample. Entities
        /// without a timeline move toward <see cref="TargetPosition"/> at their movement speed. Aligns the visual heading
        /// with the direction of travel.
        /// </summary>
        public void InterpolatePosition(float deltaSeconds, double now)
        {
            if (SnapToTargetPending && !_hasMotionTimeline)
            {
                SnapToTargetPending = false;
                _position = _targetPosition;
            }

            Vector3 travel;
            bool translating;
            float silentFor = 0f;
            if (_hasMotionTimeline)
            {
                lock (_motionLock)
                {
                    SnapToTargetPending = false;

                    if (_expectMoreSamples && now - _lastArrival > StopTimeoutFor(_updateInterval))
                    {
                        // No update for longer than a moving entity ever goes without one: it stopped. LandSandBoat sends no
                        // stop for monsters and NPCs, it just stops sending (#359). Finish the move and idle once there,
                        // instead of growing the delay as if the next update were only late.
                        // A player's move session is kept: if its counter carries on, the move never stopped.
                        _expectMoreSamples = false;
                        StopOnArrival = true;
                        silentFor = (float)(now - _lastArrival);
                    }

                    float dt = Math.Max(0f, deltaSeconds);
                    double buffered = _samples[^1].Time - (now - _playbackDelay);
                    if (_expectMoreSamples)
                    {
                        // Ease toward the target delay by a bounded fraction of real time, so playback never runs backwards.
                        // Not while still waiting at the rest position for a move to begin: that would spend the start lead.
                        float maxStep = DelayRecoveryRate * dt;
                        bool awaitingMoveStart = (now - _playbackDelay) < _samples[0].Time;
                        float maxGrow = awaitingMoveStart ? 0f : maxStep;
                        _playbackDelay += Math.Clamp(_targetPlaybackDelay - _playbackDelay, -maxStep, maxGrow);

                        // About to run out of samples: slow playback (down to a quarter speed) rather than freezing until the
                        // next update lands. This also grows the delay to what the connection needs.
                        buffered = _samples[^1].Time - (now - _playbackDelay);
                        if (buffered < StarvationBufferSeconds)
                        {
                            float rate = Math.Clamp((float)(buffered / StarvationBufferSeconds), 0.25f, 1.0f);
                            _playbackDelay = Math.Min(MaxPlaybackDelaySeconds, _playbackDelay + (dt * (1.0f - rate)));
                        }
                    }
                    else if (buffered > 0)
                    {
                        // Final stretch of a move whose stop has arrived: nothing left to wait for, so play it a little fast.
                        _playbackDelay = Math.Max(MinPlaybackDelaySeconds, _playbackDelay - (StopCatchUpRate * dt));
                    }
                    double renderTime = now - _playbackDelay;

                    while (_samples.Count > 2 && _samples[1].Time <= renderTime)
                    {
                        _samples.RemoveAt(0);
                    }

                    MotionSample a = _samples[0];
                    if (_samples.Count == 1 || renderTime <= a.Time)
                    {
                        _position = a.Position;
                        Direction = a.Direction;
                        travel = Vector3.Zero;
                        translating = false;
                    }
                    else
                    {
                        MotionSample b = _samples[1];
                        if (renderTime >= b.Time)
                        {
                            _position = b.Position;
                            travel = Vector3.Zero;
                            translating = false;
                        }
                        else
                        {
                            float t = (float)((renderTime - a.Time) / Math.Max(1e-6, b.Time - a.Time));
                            _position = Vector3.Lerp(a.Position, b.Position, t);
                            travel = b.Position - a.Position;
                            translating = travel.LengthSquared() > 0.0001f;
                        }

                        // Facing follows the timeline too, so the entity does not turn toward a move before playing it.
                        Direction = b.Direction;
                    }

                    if (StopOnArrival && renderTime >= _samples[^1].Time)
                    {
                        StopOnArrival = false;
                        Speed = 0;
                        LocomotionDirection = LocomotionDirection.Forward;
                    }
                }
            }
            else
            {
                travel = _targetPosition - _position;
                float remaining = travel.Length();
                translating = remaining > 0.001f;
                if (translating)
                {
                    float step = MovementSpeedYalms * Math.Max(0f, deltaSeconds);
                    _position = step >= remaining ? _targetPosition : _position + (travel * (step / remaining));
                }
                else
                {
                    _position = _targetPosition;
                }

                if (StopOnArrival && Vector3.DistanceSquared(_position, _targetPosition) <= 0.0025f)
                {
                    StopOnArrival = false;
                    Speed = 0;
                    LocomotionDirection = LocomotionDirection.Forward;
                }
            }

            if (silentFor > 0f)
            {
                GordianLog.Info("Locomotion", $"[Entity 0x{ServerId:X8}:{Name}] MOVE STOP (silence): no update for {silentFor:F2}s, " +
                    $"interval={UpdateIntervalSeconds:F2}s, ground={GroundSpeedYalms:F2}, delay={PlaybackDelaySeconds:F2}s");
            }

            if (IsTranslating && !translating)
            {
                ArrivedUtc = DateTime.UtcNow;
            }
            IsTranslating = translating;

            UpdateHeading(deltaSeconds, travel, translating ? 1f : 0f);
        }

        /// <summary>
        /// Ground travel speed in yalms per second (Speed 50 => 5.0), falling back to the base speed then the standard run speed.
        /// </summary>
        public float MovementSpeedYalms
        {
            get
            {
                byte speed = Speed > 0 ? Speed : (SpeedBase > 0 ? SpeedBase : (byte)50);
                return Math.Max(1.0f, speed / 10.0f);
            }
        }

        private void UpdateHeading(float deltaSeconds, Vector3 travel, float distToTarget)
        {
            // For remote players moving towards the latest server position, orient facing along the remaining travel vector.
            // The chase never passes the target, so this vector always points the way the player is really going.
            // For NPCs and monsters, the server is the absolute authority on facing rotation in every 0x00E packet;
            // preserving wire Direction prevents backward flips and ensures custom poses (e.g. Uragnite in-shell) face correctly.
            if (Type == EntityType.Player)
            {
                float flatDistSq = (travel.X * travel.X) + (travel.Z * travel.Z);
                if (flatDistSq > 0.0025f && distToTarget > 0.05f)
                {
                    float moveAngleRad = HeadingOf(travel.X, travel.Z);
                    if (AnimationState != 1 && ClaimServerId == 0)
                    {
                        Direction = DirectionFromRadians(moveAngleRad);
                        LocomotionDirection = LocomotionDirection.Forward;
                    }
                    else
                    {
                        // Positive = travel is to the character's right, since increasing heading turns right.
                        float diffRad = moveAngleRad - HeadingRadians;
                        while (diffRad > MathF.PI) diffRad -= MathF.PI * 2.0f;
                        while (diffRad < -MathF.PI) diffRad += MathF.PI * 2.0f;

                        if (MathF.Abs(diffRad) >= (3.0f * MathF.PI / 4.0f))
                        {
                            LocomotionDirection = LocomotionDirection.Backward;
                        }
                        else if (diffRad > (MathF.PI / 4.0f))
                        {
                            LocomotionDirection = LocomotionDirection.Right;
                        }
                        else if (diffRad < -(MathF.PI / 4.0f))
                        {
                            LocomotionDirection = LocomotionDirection.Left;
                        }
                        else
                        {
                            LocomotionDirection = LocomotionDirection.Forward;
                        }
                    }
                }
                else if (distToTarget <= 0.05f && !S2C_0x00D_CharPc.IsMovingMovTime(LastMovTime))
                {
                    LocomotionDirection = LocomotionDirection.Forward;
                }
            }

            // Smoothly rotate visual heading towards target heading. The server's TurnFlag (EasesHeading) is not consulted:
            // snapping standing turns when it is clear was tried and rejected in the in-game test (2026-10-10, #334).
            float targetHeadingRad = HeadingRadians;
            float diff = targetHeadingRad - RenderHeadingRadians;
            while (diff > MathF.PI) diff -= MathF.PI * 2.0f;
            while (diff < -MathF.PI) diff += 2.0f * MathF.PI;
            RenderHeadingRadians += diff * Math.Min(1.0f, deltaSeconds * 15.0f);
        }

        /// <summary>
        /// Facing rotation exactly as carried on the wire (0x00D/0x00E inbound, 0x015 outbound): 256 steps per turn on the
        /// (X, Z) ground plane — 0 = East (+X), 64 = South (-Z), 128 = West (-X), 192 = North (+Z), i.e. clockwise on a
        /// north-up map, so increasing the heading turns the character to its right.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server) position_t rotation.
        /// </summary>
        public byte Direction { get; set; }
        public float HeadingRadians => (Direction / 256.0f) * MathF.PI * 2.0f;

        /// <summary>
        /// Heading in radians (wire convention, [0, 2π)) of a ground-plane vector.
        /// </summary>
        public static float HeadingOf(float dx, float dz)
        {
            float rad = MathF.Atan2(-dz, dx);
            return rad < 0f ? rad + (MathF.PI * 2.0f) : rad;
        }

        /// <summary>
        /// Unit ground-plane (X, Z) forward vector for a wire-convention heading in radians.
        /// </summary>
        public static Vector2 ForwardOf(float headingRad) => new(MathF.Cos(headingRad), -MathF.Sin(headingRad));

        /// <summary>
        /// Unit ground-plane (X, Z) vector pointing to the character's right for a wire-convention heading in radians,
        /// i.e. the forward vector of the heading a quarter turn further on.
        /// </summary>
        public static Vector2 RightOf(float headingRad) => ForwardOf(headingRad + (MathF.PI / 2.0f));

        /// <summary>
        /// Quantizes a wire-convention heading in radians to a <see cref="Direction"/> byte.
        /// </summary>
        public static byte DirectionFromRadians(float headingRad) =>
            (byte)((int)MathF.Round(headingRad / (MathF.PI * 2.0f) * 256.0f) & 0xFF);

        /// <summary>
        /// Quantizes a wire-convention heading in degrees to a <see cref="Direction"/> byte.
        /// </summary>
        public static byte DirectionFromDegrees(float headingDeg) =>
            (byte)((int)MathF.Round(headingDeg / 360.0f * 256.0f) & 0xFF);

        public byte Speed { get; set; }
        public byte SpeedBase { get; set; }
        public ushort LastMovTime { get; set; }
        public byte AnimationState { get; set; }
        public byte AnimationSub { get; set; }
        public byte Hpp { get; set; }
        public uint ClaimServerId { get; set; }

        /// <summary>
        /// The target index of the entity's owner: a pet's master or a Trust's summoner, from S2C 0x067 / 0x068
        /// (<see cref="EntitySyncPacket"/>); 0 when it has none or none was reported.
        /// </summary>
        public ushort OwnerTargetIndex { get; set; }

        /// <summary>The raw <c>NameFlags</c> of the last S2C 0x067 / 0x068 for this entity (Campaign, Pankration, Level Sync icon bits); 0 until one arrives.</summary>
        public uint SyncNameFlags { get; set; }

        private int _jumpCount;

        /// <summary>
        /// How many times the entity has jumped (S2C 0x11E for other players): the renderer can watch the count change
        /// to play a jump; nothing plays it yet.
        /// </summary>
        public int JumpCount => Volatile.Read(ref _jumpCount);

        /// <summary>Counts a jump of the entity.</summary>
        public void NotifyJump() => Interlocked.Increment(ref _jumpCount);

        public EntityAppearance Appearance { get; } = new EntityAppearance();
        public EntityAnimationState Animation { get; } = new EntityAnimationState();

        /// <summary>What the player rides (#87; S2C 0x00D, or 0x037 for the local player); <see cref="MountAppearance.None"/> on foot.</summary>
        public MountAppearance Mount { get; set; }

        /// <summary>The last mount number S2C 0x00D's Flags6 carried for the player (#87), for updates without it.</summary>
        public byte MountWire { get; set; }

        /// <summary>The ridden mount's own motion (idle, walk, run), played beside the rider's <see cref="Animation"/> (#87).</summary>
        public EntityAnimationState MountAnimation { get; } = new EntityAnimationState();

        public bool IsSpawned { get; set; } = true;

        /// <summary>
        /// Invisible and untargetable (InvisFlag).
        /// </summary>
        public bool IsInvisible { get; set; }

        /// <summary>
        /// Body size class from the entity update (0 = small, 1 = medium, 2 = large); sizes its bump collision.
        /// </summary>
        public byte GraphSize { get; set; }

        /// <summary>
        /// Fully hidden and untargetable (HideFlag).
        /// </summary>
        public bool IsHidden { get; set; }

        /// <summary>
        /// The server's SleepFlag (0x00D / 0x00E Flags1 bit 2): the entity's scheduler is suspended, so the client neither draws
        /// nor targets it (XiPackets). An event still draws its own participants, as for <see cref="IsHidden"/>.
        /// </summary>
        public bool IsSleeping { get; set; }

        /// <summary>The server's TargetOffFlag (Flags1 bit 19): drawn, but not targetable by normal means (XiPackets).</summary>
        public bool IsTargetOff { get; set; }

        /// <summary>
        /// 0x00E AutoPartyFlag (Flags2 bit 31): invisible and not targetable by normal means; only auto-targeting picks it once
        /// it attacks the player (XiPackets). GordianXI has no auto-target, so it is simply not drawn or targeted.
        /// </summary>
        public bool IsAutoTargetOnly { get; set; }

        /// <summary>
        /// The server's TurnFlag (Flags1 bit 30): a new heading is eased over time (XiPackets). Decoded, not used: GordianXI
        /// always eases (<see cref="UpdateHeading"/>); snapping standing turns while it is clear (LandSandBoat never sets it)
        /// looked wrong in the in-game test (2026-10-10).
        /// </summary>
        public bool EasesHeading { get; set; }

        /// <summary>
        /// The server's <c>facetarget</c> (Flags0 bits 17-31): the target index of the entity whose way this entity's head turns,
        /// or 0 for none. The renderer turns the head as for an event look while no event look is set.
        /// </summary>
        public ushort FaceTargetIndex { get; set; }

        /// <summary>The 0x00E model hitbox size (Flags2.g x 0.1 yalms); 0 when not sent. Nothing reads it yet.</summary>
        public float ModelHitboxRadius { get; set; }

        /// <summary>The server's ShadowFlag (Flags2 bit 25): the shadow is hidden (<c>EntityRenderer</c> draws none for it).</summary>
        public bool HidesShadow { get; set; }

        /// <summary>The server's NamedFlag (0x00E Flags2 bit 29): a proper name, which takes no "The" (XiPackets).</summary>
        public bool HasProperName { get; set; }

        /// <summary>The server's SingleFlag (0x00E Flags2 bit 30): referred to in the plural (XiPackets). Nothing reads it yet.</summary>
        public bool IsPlural { get; set; }

        /// <summary>
        /// The server's PetNewFlag (0x00E Flags3 bit 2): a pet or Trust being spawned, whose model then plays its appearance
        /// (<c>spop</c>, <c>ActionEffectTracker</c>). The flag stays as last sent: see <see cref="PetSpawnTimestamp"/>.
        /// </summary>
        public bool IsPetSpawning
        {
            get => _isPetSpawning;
            set
            {
                if (value && !_isPetSpawning) PetSpawnTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                _isPetSpawning = value;
            }
        }
        private bool _isPetSpawning;

        /// <summary>
        /// When <see cref="IsPetSpawning"/> was last raised (<see cref="System.Diagnostics.Stopwatch"/> ticks; 0 never). The
        /// server stops sending the flag only with the entity's next general update, which a Trust standing still may not
        /// get for a long time, so the appearance is played only while the spawn is recent (#377: shown later, it replayed).
        /// </summary>
        public long PetSpawnTimestamp { get; set; }

        /// <summary>
        /// The server's MotStopFlag (Flags3 bit 4): the motion is paused, so the entity freezes in its current pose (petrify,
        /// terror; XiPackets). The renderer stops advancing its animation and its head look.
        /// </summary>
        public bool IsMotionStopped { get; set; }

        /// <summary>
        /// The server's CliPriorityFlag (Flags3 bit 5): drawn past the client's entity limit. GordianXI has no entity limit, so
        /// every entity is drawn already.
        /// </summary>
        public bool IsPriorityDrawn { get; set; }

        /// <summary>The server's OcclusionoffFlag (0x00E Flags3 bit 7). GordianXI runs no entity occlusion tests.</summary>
        public bool IsOcclusionExempt { get; set; }

        /// <summary>
        /// 0x00E Flags3 bit 31: drawn half-transparent (retail overrides the distance alpha to 0.5; LandSandBoat's name
        /// visibility <c>ghost_phase</c>).
        /// </summary>
        public bool IsHalfTransparent { get; set; }

        /// <summary>The server animation status of a dead entity (LandSandBoat <c>xi.animation.DEATH</c>).</summary>
        public const byte StatusDead = 3;

        /// <summary>
        /// A monster, pet or Trust in its death status (<see cref="AnimationState"/> 3). LandSandBoat sends nothing else at
        /// death that the client could read (no flag; HP 0 and the hitbox byte 0, the status byte keeps MonsterFlag until the
        /// fade), so the death status is what greys the name and makes it untargetable (#327). Players are left out: a dead
        /// player stays targetable (Raise).
        /// </summary>
        public bool IsDeadBattleEntity => AnimationState == StatusDead && Type is EntityType.Monster or EntityType.Pet or EntityType.Trust;

        /// <summary>
        /// Whether the server's state lets the entity be targeted: not hidden, invisible, asleep (SleepFlag), TargetOff,
        /// auto-target only, or a dead monster. Targeting also needs a name and a spawned entity (<c>TargetCycling</c>).
        /// </summary>
        public bool IsServerTargetable =>
            !IsHidden && !IsInvisible && !IsSleeping && !IsTargetOff && !IsAutoTargetOnly && !IsDeadBattleEntity;

        /// <summary>
        /// Whether a message names the entity with "the": a monster without NamedFlag (<see cref="HasProperName"/>).
        /// PROVISIONAL: retail's rule for NPCs is not checked, so NPCs take none.
        /// </summary>
        public bool TakesArticle => Type == EntityType.Monster && !HasProperName;

        private volatile bool _isInEvent;

        /// <summary>
        /// Taking part in the running event (its actor block runs a VM). An event draws its entities even when the
        /// server hides them: LandSandBoat keeps cutscene-only NPCs at status Disappear (2) or CutsceneOnly (6), which
        /// is the low byte of the entity update's Flags1 and so sets <see cref="IsHidden"/>. Cleared when the event ends,
        /// when the server's state applies again.
        /// </summary>
        public bool IsInEvent
        {
            get => _isInEvent;
            set => _isInEvent = value;
        }

        /// <summary>
        /// Whether the renderer draws the entity: not while the server hides it (HideFlag), suspends it (SleepFlag) or makes
        /// it auto-target only (0x00E AutoPartyFlag), unless it takes part in the running event (retail draws event entities
        /// by their event state, then restores the entity's own state when the event object is destroyed).
        /// </summary>
        public bool IsDrawn => !IsEventHidden && ((!IsHidden && !IsSleeping && !IsAutoTargetOnly) || IsInEvent);

        private volatile EventPose? _eventPose;
        private volatile EventLook? _eventLook;
        private volatile bool _isEventHidden;

        /// <summary>Whom a running event has the entity look at (its head turns), or null. Cleared when the event ends.</summary>
        public EventLook? EventLook
        {
            get => _eventLook;
            set => _eventLook = value;
        }

        private volatile int _eventHeadTurnSpeed;

        /// <summary>
        /// The head turn speed a running event set (opcode 0x59 sub 2 / 3, retail <c>TurnSpeedHead</c>), or 0 for the
        /// default. Read by the renderer for a fixed look axis (<c>HeadLook.AxisStep</c>). Cleared when the
        /// event ends.
        /// </summary>
        public int EventHeadTurnSpeed
        {
            get => _eventHeadTurnSpeed;
            set => _eventHeadTurnSpeed = value;
        }

        private volatile int _eventTurnSpeed;

        /// <summary>
        /// The body turn speed a running event set (opcode 0x59 sub 0 / 1, retail <c>TurnSpeed</c>), in 4096ths of a turn per
        /// 60 Hz frame (provisional, #197), or 0 for the default turn ease. Cleared when the event ends.
        /// </summary>
        public int EventTurnSpeed
        {
            get => _eventTurnSpeed;
            set => _eventTurnSpeed = value;
        }

        /// <summary>The colour alpha of an entity drawn as is (retail colours are half scale: 0x80 = 1.0).</summary>
        public const int OpaqueEventAlpha = 0x80;

        private volatile int _eventAlpha = OpaqueEventAlpha;

        /// <summary>
        /// The alpha a running event fades the entity to (opcode 0x6C, CodeTRANSPAR: retail's actor colour alpha, 0 =
        /// invisible, <see cref="OpaqueEventAlpha"/> and above = opaque). Back to opaque when the event ends.
        /// </summary>
        public int EventAlpha
        {
            get => _eventAlpha;
            set => _eventAlpha = value;
        }

        /// <summary>
        /// The colour the entity's routines draw it with: op 0x29 fades and 0x2E holds from S2C 0x038 actor schedulers
        /// (<c>kesu</c> fades a despawning entity out) and from event scene routines (the battlefield warps), #206 / #311.
        /// </summary>
        public ActorColorTrack ActorColor { get; } = new ActorColorTrack();

        private int _spokenLines;

        /// <summary>
        /// How many event message lines the entity has spoken: each one moves its mouth once (the renderer watches the count
        /// change; see <c>FaceMotion</c>).
        /// </summary>
        public int SpokenLines => Volatile.Read(ref _spokenLines);

        /// <summary>The mouth flaps in one play of the mouth clip (<c>mou4</c>), the flaps a line gets when no count is given.</summary>
        public const int MouthFlapsPerClip = 3;

        /// <summary>
        /// Counts a line the entity speaks (an event message with it as the speaker) and how many times its mouth flaps for
        /// it (the engine's <c>FaceMotion.FlapsFor</c>).
        /// </summary>
        public void Speak(int flaps = MouthFlapsPerClip)
        {
            Volatile.Write(ref _lineFlaps, flaps);
            Interlocked.Increment(ref _spokenLines);
        }

        private int _lineFlaps = MouthFlapsPerClip;

        /// <summary>How many times the mouth flaps for the entity's last line.</summary>
        public int LineFlaps => Volatile.Read(ref _lineFlaps);

        private int _speechStops;

        /// <summary>How often the player has closed one of the entity's lines (each stops its mouth at once).</summary>
        public int SpeechStops => Volatile.Read(ref _speechStops);

        /// <summary>
        /// The player closed the entity's line: its mouth stops (retail 0x23 sets the speaker's <c>MouthCounter</c> to 0 and
        /// calls <c>XiSkeletonActor::SpeakStop</c>, XiEvents OpCodes/0x0023).
        /// </summary>
        public void StopSpeaking() => Interlocked.Increment(ref _speechStops);

        /// <summary>
        /// Where a running event (cutscene) has placed the entity, or null when the event leaves it alone. While set,
        /// the renderer draws the entity here instead of its server position (retail copies an event entity's
        /// <c>EventPos</c> / <c>EventDir</c> over its position: XiAtelBuff::CopyAllPosEvent). Cleared when the event ends.
        /// </summary>
        public EventPose? EventPose
        {
            get => _eventPose;
            set => _eventPose = value;
        }

        /// <summary>
        /// A running event keeps the height it places the entity at instead of putting it on the floor below (retail
        /// <c>Render.Flags0</c> bit 21, set by opcodes 0x33 / 0x59 sub 5: Port Jeuno 324 holds its invisible marker in
        /// the sky where the explosion plays). Cleared when the event ends.
        /// </summary>
        public bool KeepsEventHeight
        {
            get => _keepsEventHeight;
            set => _keepsEventHeight = value;
        }

        private volatile bool _keepsEventHeight;

        /// <summary>
        /// A running event hides the entity's name plate (retail <c>Render.Flags3</c> bit 16, set by opcode 0x92: Port
        /// Jeuno 324 sets it on every NPC it places, #191). Cleared when the event ends.
        /// </summary>
        public bool HidesEventName
        {
            get => _hidesEventName;
            set => _hidesEventName = value;
        }

        private volatile bool _hidesEventName;

        /// <summary>
        /// The other render-flag bits a running event set on the entity, and its blink switch (opcodes 0x81, 0x94, 0xAB,
        /// 0xC0; see <see cref="World.EventRenderFlags"/>). Cleared when the event ends.
        /// </summary>
        public EventRenderFlags EventRenderFlags
        {
            get => _eventRenderFlags;
            set => _eventRenderFlags = value;
        }

        private volatile EventRenderFlags _eventRenderFlags;

        /// <summary>
        /// The status a running event gave the entity in place of its server status (<see cref="AnimationState"/>), or 0
        /// for none: 8 opens a door, 9 closes it (opcodes 0x4C / 0x4D, retail <c>StatusEvent</c>). Cleared when the event
        /// ends. Event VM opcodes referenced from XiEvents (https://github.com/atom0s/XiEvents, OpCodes/0x004C, 0x004D).
        /// </summary>
        public byte EventStatus
        {
            get => _eventStatus;
            set => _eventStatus = value;
        }

        private volatile byte _eventStatus;

        /// <summary>
        /// Hidden by a running event: the scripts' hide flag (opcodes 0x22 / 0x4E, retail <c>Render.Flags0</c> bit 17) or
        /// the cutscene's NO_PCS / NO_NPCS flags for entities outside the event. Cleared when the event ends.
        /// </summary>
        public bool IsEventHidden
        {
            get => _isEventHidden;
            set => _isEventHidden = value;
        }

        /// <summary>
        /// Flags from the last entity update that change the name plate (colour, icon, stars, or hiding the name).
        /// </summary>
        public NamePlateFlags NamePlate { get; set; }

        /// <summary>
        /// The server marked this entity as one the local player passes straight through.
        /// </summary>
        public bool IsNonBlocking { get; set; }

        /// <summary>
        /// The server's GroundFlag: this entity ignores world collision, so it is drawn at its reported height instead of
        /// on the floor.
        /// </summary>
        public bool IgnoresWorldCollision { get; set; }

        /// <summary>
        /// For an elevator, ship or door: the FourCC of the zone object it moves (its door id, e.g. <c>_6l0</c> for a door),
        /// empty otherwise.
        /// </summary>
        public string TransportId { get; set; } = string.Empty;

        /// <summary>
        /// For a ship (0x00E SubKind 4): its ship number, which names the vessel model (<see cref="ShipModels"/>); 0 otherwise.
        /// </summary>
        public uint ShipNumber { get; set; }

        /// <summary>
        /// For an elevator or ship: the Earth second since the Vana'diel epoch its current leg started.
        /// </summary>
        public uint TransportStartSeconds { get; set; }

        /// <summary>
        /// For an elevator: how many seconds a leg takes.
        /// </summary>
        public uint TransportTravelSeconds { get; set; }

        /// <summary>
        /// For an elevator or ship: when (Earth seconds since the Vana'diel epoch, on this client's synced clock) the
        /// current leg's update arrived; 0 when not seen arriving.
        /// </summary>
        public double TransportObservedSeconds { get; set; }

        /// <summary>
        /// The moving platform this character was last drawn standing on, so it rides the platform as it moves even
        /// though its own reported position stays put (the legacy client carries riders itself).
        /// </summary>
        public string RidingPlatformId { get; set; } = string.Empty;
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastPositionChangeUtc { get; set; } = DateTime.MinValue;

        public WorldEntity(uint serverId, ushort targetIndex, EntityType type)
        {
            ServerId = serverId;
            TargetIndex = targetIndex;
            Type = type;
        }

        public override string ToString()
        {
            return $"[{Type}] {Name} (ID: {ServerId}, Index: {TargetIndex}) at {Position:F1}";
        }
    }

    /// <summary>
    /// Represents another player character (PC) in the zone.
    /// </summary>
    public sealed class PlayerEntity : WorldEntity
    {
        public JobId MainJob { get; set; } = JobId.None;
        public byte MainJobLevel { get; set; }
        public JobId SubJob { get; set; } = JobId.None;
        public byte SubJobLevel { get; set; }

        public byte LsColorR { get; set; }
        public byte LsColorG { get; set; }
        public byte LsColorB { get; set; }

        public byte GmLevel { get; set; }
        public bool IsSeekingParty { get; set; }
        public bool IsAnonymous { get; set; }
        public bool IsAway { get; set; }
        public bool HasBazaar { get; set; }
        public bool IsCharmed { get; set; }
        public bool IsMentor { get; set; }
        public bool IsNewPlayer { get; set; }

        public ushort PetActorIndex { get; set; }

        public PlayerEntity(uint serverId, ushort targetIndex)
            : base(serverId, targetIndex, EntityType.Player)
        {
        }
    }
}
