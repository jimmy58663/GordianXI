// src/Gordian.Core/World/WorldEntity.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Gordian.Core.Animation;
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
        /// Playback delay (seconds behind real time) a remote entity starts with before its update cadence is learned.
        /// Captured traffic needs ~1.4-2.0s: a ~1.4s server broadcast interval plus up to ~0.6s of sample staleness.
        /// </summary>
        public const float DefaultPlaybackDelaySeconds = 1.8f;

        /// <summary>
        /// Rate of the 0x00D movement counter (<see cref="LastMovTime"/>): ticks per second of continuous movement.
        /// Captured traffic confirms it: counter deltas / 60 × 5.0 yalms/sec match the reported displacements.
        /// </summary>
        public const double MovTimeTicksPerSecond = 60.0;

        /// <summary>
        /// Seconds of not-yet-played samples below which playback slows down while an entity is moving, rather than freezing.
        /// </summary>
        public const float StarvationBufferSeconds = 0.25f;

        /// <summary>
        /// Safety margin added to the worst observed sample staleness when choosing the playback delay. Small, because
        /// running short degrades to a brief slowdown rather than a stop.
        /// </summary>
        public const float DelayMarginSeconds = 0.1f;

        /// <summary>
        /// How much sooner than the steady-state delay a move from rest starts playing. The first stretch of a move does not
        /// need the full buffer; playback rebuilds it by running <see cref="DelayRecoveryRate"/> slower than real time.
        /// </summary>
        public const float StartLeadSeconds = 0.5f;

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
        /// Monotonic clock (seconds) shared by the network thread when timestamping position samples and the render thread
        /// when playing them back.
        /// </summary>
        public static double ClockSeconds => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        private readonly record struct MotionSample(double Time, Vector3 Position, byte Direction);

        private readonly object _motionLock = new();
        private readonly List<MotionSample> _samples = new(16);
        private readonly float[] _delayRequirements = new float[6];
        private int _delayRequirementCount;
        private int _delayRequirementNext;
        private bool _moveSessionActive;
        private double _moveSessionOrigin;
        private ushort _moveSessionLastMovTime;
        private float _targetPlaybackDelay = DefaultPlaybackDelaySeconds;
        private float _playbackDelay = DefaultPlaybackDelaySeconds;
        private volatile bool _hasMotionTimeline;
        private bool _expectMoreSamples;

        /// <summary>
        /// Current seconds this entity is displayed behind real time.
        /// </summary>
        public float PlaybackDelaySeconds { get { lock (_motionLock) return _playbackDelay; } }

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
        /// anchored at the last rest position. A stop is timed from the distance of its final step at the entity's speed.
        /// NPCs carry no counter and are timestamped on arrival.
        /// </para>
        /// The render thread plays the timeline back <see cref="PlaybackDelaySeconds"/> behind real time, which adapts to how
        /// stale the newest sample can get before the next one arrives.
        /// </summary>
        public void AddServerSample(Vector3 newPos, bool isMoving, ushort movTime, byte direction, double now)
        {
            lock (_motionLock)
            {
                double renderTime = now - _playbackDelay;
                bool hadSamples = _samples.Count > 0;
                Vector3 lastPos = hadSamples ? _samples[^1].Position : _position;
                byte lastDirection = hadSamples ? _samples[^1].Direction : Direction;
                double lastTime = hadSamples ? _samples[^1].Time : double.NegativeInfinity;
                float nominal = MovementSpeedYalms;

                float snapDistance = Math.Max(15.0f, nominal * MaxTrailSeconds * 2.0f);
                if (Vector3.Distance(newPos, lastPos) > snapDistance)
                {
                    // Warp / zone-line hop: restart the timeline at the new position.
                    _samples.Clear();
                    _samples.Add(new MotionSample(now, newPos, direction));
                    _moveSessionActive = isMoving && movTime > 0;
                    _moveSessionOrigin = now - (movTime / MovTimeTicksPerSecond);
                    _moveSessionLastMovTime = movTime;
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
                        // Every sample of one move is stamped from the same origin, so their spacing is exact. If the first
                        // packet was stale the whole move merely plays a constant amount later, which the adaptive
                        // delay absorbs.
                        measureDelay = true;
                    }
                    else
                    {
                        _moveSessionOrigin = now - sinceMoveStart;
                        if (!_moveSessionActive)
                        {
                            if (renderTime >= lastTime)
                            {
                                // Starting from rest (playback is idle): begin sooner than the steady-state delay. Re-seating
                                // the playback clock while it holds at the rest position is invisible, as long as it stays at or
                                // after the rest sample.
                                float startDelay = Math.Max(0.3f, _targetPlaybackDelay - StartLeadSeconds);
                                _playbackDelay = (float)Math.Min(startDelay, now - lastTime);
                                renderTime = now - _playbackDelay;
                            }

                            // Anchor the start of the move at the rest position so playback departs when the player did.
                            AppendSample(Math.Max(_moveSessionOrigin, lastTime), lastPos, lastDirection, renderTime, nominal);
                        }
                        _moveSessionActive = true;
                    }
                    _moveSessionLastMovTime = movTime;
                    sampleTime = _moveSessionOrigin + sinceMoveStart;
                }
                else if (isMoving)
                {
                    measureDelay = hadSamples && Vector3.DistanceSquared(newPos, lastPos) > 0.0025f;
                    sampleTime = now;
                }
                else
                {
                    // Stopped: the final step ended roughly its length (at movement speed) after the previous sample. An update
                    // while already at rest (a turn in place, a small correction) plays as soon as playback is idle.
                    sampleTime = _moveSessionActive && hadSamples
                        ? Math.Min(now, lastTime + (Vector3.Distance(newPos, lastPos) / nominal))
                        : (renderTime >= lastTime ? renderTime : now);
                    measureDelay = _moveSessionActive && hadSamples;
                    _moveSessionActive = false;
                    _moveSessionLastMovTime = 0;
                }

                if (measureDelay && hadSamples)
                {
                    // Playback must not have run past the newest sample we had when this one arrived.
                    RecordDelayRequirement((float)(now - lastTime));
                }

                LastSampleAgeSeconds = (float)(now - sampleTime);
                _expectMoreSamples = isMoving;
                AppendSample(sampleTime, newPos, direction, renderTime, nominal);
                _hasMotionTimeline = true;
                TargetPosition = newPos;
            }
        }

        private void AppendSample(double time, Vector3 pos, byte direction, double renderTime, float nominalSpeed)
        {
            if (_samples.Count > 0)
            {
                MotionSample last = _samples[^1];
                float stepSeconds = Math.Max(0.001f, Vector3.Distance(pos, last.Position) / nominalSpeed);
                if (last.Time < renderTime)
                {
                    // Playback is holding at the last sample: continue from there rather than interpolating from where
                    // the entity was long ago, which would pop it part-way along the new segment.
                    _samples.Add(new MotionSample(renderTime, last.Position, last.Direction));
                }
                if (time <= renderTime)
                {
                    // Arrived too late to play at its true time: play it from the held position at movement speed.
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
            if (requiredSeconds <= 0f || requiredSeconds > 3.0f) return;

            _delayRequirements[_delayRequirementNext] = requiredSeconds;
            _delayRequirementNext = (_delayRequirementNext + 1) % _delayRequirements.Length;
            _delayRequirementCount = Math.Min(_delayRequirementCount + 1, _delayRequirements.Length);

            float worst = 0f;
            for (int i = 0; i < _delayRequirementCount; i++)
            {
                worst = Math.Max(worst, _delayRequirements[i]);
            }
            _targetPlaybackDelay = Math.Clamp(worst + DelayMarginSeconds, 0.3f, 3.0f);
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
            if (_hasMotionTimeline)
            {
                lock (_motionLock)
                {
                    SnapToTargetPending = false;

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
                            _playbackDelay = Math.Min(3.0f, _playbackDelay + (dt * (1.0f - rate)));
                        }
                    }
                    else if (buffered > 0)
                    {
                        // Final stretch of a move whose stop has arrived: nothing left to wait for, so play it a little fast.
                        _playbackDelay = Math.Max(0.3f, _playbackDelay - (StopCatchUpRate * dt));
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

            // Smoothly rotate visual heading towards target heading
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

        public EntityAppearance Appearance { get; } = new EntityAppearance();
        public EntityAnimationState Animation { get; } = new EntityAnimationState();

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
        /// The server marked this entity as one the local player passes straight through.
        /// </summary>
        public bool IsNonBlocking { get; set; }
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
