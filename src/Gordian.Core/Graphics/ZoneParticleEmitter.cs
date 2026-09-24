// src/Gordian.Core/Graphics/ZoneParticleEmitter.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Graphics;

namespace Gordian.Core.Graphics
{
    /// <summary>
    /// A zone-anchored Section 0x05 generator prepared for simulation: its definition, the keyframe curves its links
    /// resolve to, and its base position in raw DAT space.
    /// </summary>
    public sealed class ZoneEmitterTemplate
    {
        public ZoneEmitterTemplate(
            ParticleGeneratorDefinition definition,
            IReadOnlyDictionary<ushort, KeyFrameCurve> curves,
            IReadOnlyList<EffectRoutineSpawn>? schedule = null,
            int scheduleLoopFrames = 0,
            int spriteFrameCount = 0,
            bool childOnly = false,
            bool isWeather = false)
        {
            ChildOnly = childOnly;
            IsWeather = isWeather;
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Curves = curves ?? throw new ArgumentNullException(nameof(curves));
            Schedule = schedule;
            ScheduleLoopFrames = scheduleLoopFrames;
            SpriteFrameCount = spriteFrameCount;
        }

        /// <summary>
        /// Number of cards in the generator's Section 0x21 sprite sheet (0 for mesh generators).
        /// </summary>
        public int SpriteFrameCount { get; }

        /// <summary>
        /// True for a generator that only runs as another particle's child: it never emits on its own.
        /// </summary>
        public bool ChildOnly { get; }

        /// <summary>
        /// True for a generator declared in a weather directory (rain, snow, lightning): it runs only while its weather
        /// is active and emits a third of its authored particle count, as the client does for weather effects.
        /// </summary>
        public bool IsWeather { get; }

        /// <summary>
        /// Child generators this generator's particles spawn (opcodes 0x3C, 0x44, 0x53, 0x6A and expiration 0x01), by DatId.
        /// </summary>
        public Dictionary<string, ZoneEmitterTemplate> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// For a generator started by a looping zone effect routine (Section 0x07), when in the routine it starts and
        /// how long it emits each time; null for auto-running generators.
        /// </summary>
        public IReadOnlyList<EffectRoutineSpawn>? Schedule { get; }

        /// <summary>
        /// Length of the looping routine in frames.
        /// </summary>
        public int ScheduleLoopFrames { get; }

        public ParticleGeneratorDefinition Definition { get; }

        /// <summary>
        /// Resolved keyframe curves by the allocation slot of the Section 2 link that names them.
        /// </summary>
        public IReadOnlyDictionary<ushort, KeyFrameCurve> Curves { get; }

        public Vector3 RawBasePosition => Definition.Setup?.BasePosition ?? Vector3.Zero;
    }

    /// <summary>
    /// Per-frame inputs shared by every zone emitter.
    /// </summary>
    /// <param name="CameraRawPosition">Camera position in raw DAT space.</param>
    /// <param name="DayFraction">Vana'diel time of day in [0, 1).</param>
    /// <param name="DaylightColor">The strongest of the model sun and moon light colors (daylight-based color).</param>
    /// <param name="CameraRawForward">Camera view direction in raw DAT space (angular-distance rotation).</param>
    /// <param name="DayOfWeek">Vana'diel weekday index (0-7) for day-of-week tints.</param>
    /// <param name="MoonPhase">Moon phase index (0-11) for moon-phase tints.</param>
    /// <param name="ViewerInSubEnvironment">True while the floor under the camera links a sub-environment (a cave or
    /// interior, e.g. <c>ev01</c>): camera-following and camera-anchored weather effects stop emitting there.</param>
    public readonly record struct ZoneParticleFrame(
        Vector3 CameraRawPosition,
        float DayFraction,
        Vector3 DaylightColor,
        Vector3 CameraRawForward = default,
        int DayOfWeek = 0,
        int MoonPhase = 0,
        bool ViewerInSubEnvironment = false);

    /// <summary>
    /// A particle's velocity state for one allocation slot (position, rotation or scale transform).
    /// </summary>
    internal sealed class ParticleTransformState
    {
        public Vector3 Velocity;
        public Vector3 RelativeVelocity;
        public Vector3 VelocityRotation;
        public float? DampeningFactor;
    }

    internal enum ParticleTransformKind
    {
        Position,
        Rotation,
        Scale
    }

    /// <summary>
    /// One live particle, in raw DAT space relative to its generator's base position.
    /// </summary>
    public sealed class ZoneParticle
    {
        internal readonly Dictionary<ushort, (ParticleTransformKind Kind, ParticleTransformState State)> Transforms = new();
        internal readonly Dictionary<ushort, (Vector3 Acceleration, Vector3 PreviousAmplitude)> Oscillations = new();
        internal readonly Dictionary<ushort, int[]> ColorTransforms = new();
        internal readonly Dictionary<ushort, float> InitialValues = new();
        internal readonly Dictionary<ushort, ChildStream> ChildStreams = new();
        internal bool DaylightColored;
        internal bool FollowsCamera;
        internal Vector3[]? SubRelativeVelocities;

        /// <summary>
        /// For a child spawned by a transform-following stream (0x33 / 0x46): the parent it follows, how, and the parent's
        /// orientation (rotation and scale, raw DAT axes) that its base and local offset are expressed in.
        /// </summary>
        internal ZoneParticle? FollowedParent;
        internal ChildFollow FollowMode;
        internal Matrix4x4 ParentLinear = Matrix4x4.Identity;
        internal Vector3 ParentBase;
        internal ParticleGeneratorDefinition? Definition;

        /// <summary>
        /// Raw DAT-space origin the particle's local offset is measured from: its generator's base position, the camera
        /// plus that base for camera-following and camera-anchored generators, or for a child particle the
        /// parent-derived position it was spawned at.
        /// </summary>
        public Vector3 Origin { get; internal set; }

        /// <summary>
        /// For a batched generator (rain, snow), the raw DAT-space offsets of the sub-particles this particle stands for:
        /// the particle draws once per offset, translated in world space. Null for an ordinary particle.
        /// </summary>
        public Vector3[]? SubOffsets { get; internal set; }

        /// <summary>
        /// The particle's local movement over its last update, raw DAT axes (Movement billboards face along it).
        /// </summary>
        public Vector3 LastMovement { get; internal set; }

        /// <summary>
        /// World position in raw DAT space.
        /// </summary>
        public Vector3 WorldPosition => FollowedParent == null
            ? Origin + LocalOffset
            : Origin + Vector3.TransformNormal(ParentBase + LocalOffset, ParentLinear);

        public float Age { get; internal set; }
        public float MaxAge { get; internal set; }
        public Vector3 InitialPosition { get; internal set; }
        public Vector3 Position { get; internal set; }
        public Vector3 Rotation { get; internal set; }
        public Vector3 Scale { get; internal set; }

        /// <summary>
        /// True when the particle's Y rotation is negated in its orientation (incremental rotation, opcode 0x3B).
        /// </summary>
        public bool NegateRotationY { get; internal set; }

        /// <summary>
        /// True for occlusion-probe particles (opcode 0x53): they feed a visibility query and are never drawn.
        /// </summary>
        public bool IsOcclusionProbe { get; internal set; }

        /// <summary>
        /// Particle color, half-range (0.5 = 0x80 neutral).
        /// </summary>
        public Vector4 Color { get; internal set; } = Vector4.One;

        /// <summary>
        /// Per-frame multiplier from distance fades, clock alpha and daylight tint; reset every update.
        /// </summary>
        public Vector4 ColorMultiplier { get; internal set; } = Vector4.One;

        /// <summary>
        /// Day-of-week and moon-phase tints, applied modulate-2x (null when the generator has none).
        /// </summary>
        public Vector4? DayOfWeekTint { get; internal set; }

        /// <inheritdoc cref="DayOfWeekTint"/>
        public Vector4? MoonPhaseTint { get; internal set; }

        public Vector2 TexCoordTranslate { get; internal set; }

        /// <summary>
        /// The sprite-sheet card this particle draws (sprite-sheet generators only).
        /// </summary>
        public int SpriteIndex { get; internal set; }

        /// <summary>
        /// Point-light parameters (opcode 0x58, animated by 0x49 / 0x5B-0x5E): range in yalms, theta (the light's power),
        /// and their multipliers.
        /// </summary>
        public float LightRange { get; internal set; }

        /// <inheritdoc cref="LightRange"/>
        public float LightTheta { get; internal set; } = 1f;

        /// <inheritdoc cref="LightRange"/>
        public float LightRangeMultiplier { get; internal set; } = 1f;

        /// <inheritdoc cref="LightRange"/>
        public float LightThetaMultiplier { get; internal set; } = 1f;

        public bool IsExpired => Age >= MaxAge;

        public float Progress => float.IsPositiveInfinity(MaxAge) || MaxAge <= 0f ? 0f : Math.Clamp(Age / MaxAge, 0f, 1f);

        /// <summary>
        /// Offset from the generator base in raw DAT space.
        /// </summary>
        public Vector3 LocalOffset => InitialPosition + Position;

        /// <summary>
        /// The texture factor handed to the particle shader: color times the frame's multiplier, with any day-of-week
        /// and moon-phase tints applied modulate-2x.
        /// </summary>
        public Vector4 TextureFactor
        {
            get
            {
                var factor = Color * ColorMultiplier;
                if (DayOfWeekTint is { } day) factor *= day * 2.0f;
                if (MoonPhaseTint is { } moon) factor *= moon * 2.0f;
                return factor;
            }
        }
    }

    /// <summary>
    /// How a child particle takes its placement from its parent.
    /// </summary>
    internal enum ChildFollow
    {
        /// <summary>Own base position (or the parent's position with 0x45), fixed at birth.</summary>
        None,

        /// <summary>0x33: base and offset expressed in the parent's transform, tracked while both follow.</summary>
        Transform,

        /// <summary>0x46: as <see cref="Transform"/>, with the parent's orientation billboarded toward the camera.</summary>
        TransformBillboard
    }

    /// <summary>
    /// A child generator running for the life of its parent particle (opcodes 0x44 / 0x53 / 0x6A): it emits at the child's
    /// own cadence while the parent lives, from the parent's current position.
    /// </summary>
    internal sealed class ChildStream
    {
        public ChildStream(ZoneParticleEmitter child, float maxEmitTime)
        {
            Child = child;
            MaxEmitTime = maxEmitTime;
        }

        public ZoneParticleEmitter Child { get; }
        public float MaxEmitTime { get; }
        public float LifeTime;
        public float FramesUntilNext;
        public int Emitted;
    }

    /// <summary>
    /// Clean-room runtime for one zone-anchored particle generator (shoreline surf, wave crests, drifting leaves, sparks),
    /// auto-running or started by a looping ambient routine: emits particles at the authored cadence and advances each
    /// through its life with the generator's Section 2 initializers and Section 3 updaters, driven by the 60 Hz effect
    /// clock. Covers the initializers and updaters zone effect meshes use (velocity, relative velocity and their variance,
    /// spherical spawn scatter, rotation/scale velocity and variance, incremental rotation, oscillation, dampening,
    /// velocity rotation, progress and clock curves for position/rotation/scale/color/UV/velocity, color transforms,
    /// constant and integrated UV scroll, single/double-range distance fades, birth and per-frame daylight tints, day-of-week and moon-phase tints,
    /// occlusion probes, the repeat expiration handler and child generators). Weather generators follow or anchor to the
    /// camera, scatter camera-oriented spawn shells, and batch their particles as sub-particle offsets drawn with one
    /// particle's state. Specular and point-light opcodes are ignored.
    /// Generator semantics referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer,
    /// ui/js/particle/runtime.js, types.js, ops/initializers.js, ops/updaters.js and ops/generator.js, after xim).
    /// </summary>
    public sealed class ZoneParticleEmitter
    {
        private const float MaxStepFrames = 4.0f;
        private readonly Random _random;
        private Vector3 _cameraRawPosition;
        private Vector3 _cameraRawForward;
        private Vector3 _daylightColor = Vector3.One;
        private float _framesUntilNextParticle;
        private int _totalEmitted;
        private float _routineClock = -1.0f;
        private bool _armed;
        private float _emitLifeTime;
        private float _maxEmitTime;
        private int _emittedSinceArm;
        private readonly List<(float Delay, int Duration)> _pendingTriggers = new();

        public ZoneParticleEmitter(ZoneEmitterTemplate template, int seed = 0)
        {
            Template = template ?? throw new ArgumentNullException(nameof(template));
            _random = new Random(seed);
        }

        public ZoneEmitterTemplate Template { get; }

        public List<ZoneParticle> Particles { get; } = new();

        /// <summary>
        /// Finds the running emitter for a child generator's template (wired by the owner of all emitters).
        /// </summary>
        public Func<ZoneEmitterTemplate, ZoneParticleEmitter?>? ChildResolver { get; set; }

        private ParticleGeneratorDefinition Def => Template.Definition;

        /// <summary>
        /// Advances the emitter by <paramref name="frames"/> 60 Hz effect frames (split into small steps).
        /// </summary>
        public void Update(float frames, in ZoneParticleFrame frame)
        {
            while (frames > 0.0f)
            {
                float step = MathF.Min(frames, MaxStepFrames);
                Step(step, frame);
                frames -= step;
            }
        }

        private void Step(float frames, in ZoneParticleFrame frame)
        {
            _cameraRawPosition = frame.CameraRawPosition;
            _cameraRawForward = frame.CameraRawForward;
            _daylightColor = frame.DaylightColor;
            for (int i = 0; i < Particles.Count; i++)
            {
                UpdateParticle(Particles[i], frames, frame);
            }
            Particles.RemoveAll(p => p.IsExpired);

            if (Template.ChildOnly) return;
            AdvanceSchedule(frames);
            AdvanceTriggers(frames);
            _emitLifeTime += frames;
            if (IsDoneEmitting()) return;

            bool culled = Def.MaxEmitDistance > 0.0f &&
                          Vector3.Distance(frame.CameraRawPosition, Template.RawBasePosition) > Def.MaxEmitDistance;
            if (culled) return;

            // Camera-attached weather (rain, snow) only emits while the viewer is in the main environment, which keeps
            // it out of caves and interiors; particles already falling finish their lives.
            if (frame.ViewerInSubEnvironment && IsCameraAttachedWeather) return;

            _framesUntilNextParticle -= frames;
            while (_framesUntilNextParticle <= 0.0f)
            {
                // A continuous singleton keeps exactly one particle alive.
                if (Def.ContinuousSingleton && Particles.Count > 0) break;
                // A generator whose particle lives forever emits exactly once (per start, for a routine-started one).
                if (LivesForever && (Def.AutoRun ? _totalEmitted : _emittedSinceArm) > 0) break;

                _framesUntilNextParticle += Def.FramesPerEmission + PosRand(Def.EmissionVariance);
                int count = ParticlesPerEmission;
                for (int i = 0; i < count; i++)
                {
                    Particles.Add(CreateParticle(null, ChildFollow.None));
                    _totalEmitted++;
                    _emittedSinceArm++;
                }
                if (IsDoneEmitting()) break;
            }
        }

        /// <summary>
        /// A non-auto-running generator emits only inside the window its routine armed; once that window has passed and it
        /// has emitted, it stops until the routine starts it again.
        /// </summary>
        private bool IsDoneEmitting()
        {
            if (Def.AutoRun) return false;
            if (!_armed) return true;
            return _emitLifeTime >= _maxEmitTime && _emittedSinceArm > 0;
        }

        /// <summary>
        /// Advances the looping routine clock and (re)arms the generator at each scheduled start.
        /// </summary>
        private void AdvanceSchedule(float frames)
        {
            var schedule = Template.Schedule;
            if (schedule == null || schedule.Count == 0) return;

            float loop = Math.Max(1, Template.ScheduleLoopFrames);
            float previous = _routineClock;
            _routineClock += frames;
            for (int cycle = (int)MathF.Floor(Math.Max(previous, 0f) / loop); cycle <= (int)MathF.Floor(_routineClock / loop); cycle++)
            {
                foreach (var spawn in schedule)
                {
                    float at = cycle * loop + spawn.StartFrame;
                    if (at > previous && at <= _routineClock) Arm(spawn.Duration);
                }
            }
        }

        /// <summary>
        /// Starts a non-auto-running generator from outside (a weather routine the client plays at random): after
        /// <paramref name="delayFrames"/> it emits for <paramref name="durationFrames"/> (at least once).
        /// </summary>
        public void Trigger(int delayFrames, int durationFrames) => _pendingTriggers.Add((delayFrames, durationFrames));

        private void AdvanceTriggers(float frames)
        {
            for (int i = _pendingTriggers.Count - 1; i >= 0; i--)
            {
                var (delay, duration) = _pendingTriggers[i];
                delay -= frames;
                if (delay > 0f)
                {
                    _pendingTriggers[i] = (delay, duration);
                    continue;
                }
                _pendingTriggers.RemoveAt(i);
                Arm(duration);
            }
        }

        private void Arm(int duration)
        {
            _armed = true;
            _emitLifeTime = 0.0f;
            _maxEmitTime = duration;
            _emittedSinceArm = 0;
            _framesUntilNextParticle = 0.0f;
        }

        // ── initializers ─────────────────────────────────────────────────────────

        /// <summary>
        /// Spawns one emission's worth of particles as children of <paramref name="parent"/> (a birth or expiry burst, or
        /// one step of a continuous child stream).
        /// </summary>
        internal void EmitChildren(ZoneParticle parent, ChildFollow follow)
        {
            int count = ParticlesPerEmission;
            for (int i = 0; i < count; i++)
            {
                Particles.Add(CreateParticle(parent, follow));
                _totalEmitted++;
            }
        }

        /// <summary>
        /// A life span of 0 means a particle that never expires (a sea plane, a fixed glow, a lamp's light); a point light
        /// authored with a 1-frame life is treated the same way, as the client does, instead of flickering.
        /// Semantics referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer,
        /// ui/js/particle/ops/initializers.js StandardParticleSetup, after xim).
        /// </summary>
        private bool LivesForever => Def.Setup is { } lifeSetup &&
            (lifeSetup.MaxLifeSpan == 0 || (lifeSetup.MaxLifeSpan == 1 && lifeSetup.LinkedDataType == ParticleLinkedDataType.PointLight));

        private bool IsCameraAttachedWeather =>
            Template.IsWeather && Def.Setup is { } setup && (setup.FollowCamera || setup.CameraAttachedBasePosition);

        /// <summary>
        /// Particles created per emission: one for a continuous singleton or a batched generator (whose single particle
        /// carries <see cref="AuthoredCount"/> sub-particles), otherwise the authored count.
        /// </summary>
        private int ParticlesPerEmission => Def.ContinuousSingleton || Def.Batched ? 1 : AuthoredCount;

        /// <summary>
        /// The authored particles per emission (+1). Weather generators emit a third of it, doubled first when batched,
        /// which keeps rain and snow from becoming a solid wall.
        /// </summary>
        private int AuthoredCount
        {
            get
            {
                if (Def.ContinuousSingleton) return 1;
                if (!Template.IsWeather) return Def.ParticlesPerEmission + 1;
                return Def.ParticlesPerEmission * (Def.Batched ? 2 : 1) / 3 + 1;
            }
        }

        /// <summary>
        /// Creates a particle. A child of a transform-following stream sits in its parent's transform; a child with opcode
        /// 0x45 / 0x9B copies its parent's position; otherwise a particle starts at its own generator's base position.
        /// </summary>
        private ZoneParticle CreateParticle(ZoneParticle? parent, ChildFollow follow)
        {
            var p = new ZoneParticle { Scale = Vector3.Zero, Origin = Template.RawBasePosition, Definition = Def };
            var config = Def.Setup;
            if (parent != null && follow != ChildFollow.None)
            {
                p.FollowedParent = parent;
                p.FollowMode = follow;
                p.ParentBase = Template.RawBasePosition;
                FollowParent(p);
            }
            else if (parent != null && (HasInitializer(0x45) || HasInitializer(0x9B)))
            {
                // A batched parent stands in for its sub-particles; children copy the first one's position.
                var batchOffset = parent.SubOffsets is { Length: > 0 } parentSubs ? parentSubs[0] : Vector3.Zero;
                p.Origin = parent.WorldPosition + batchOffset + Template.RawBasePosition;
            }
            else if (config != null && (config.FollowCamera || config.CameraAttachedBasePosition))
            {
                // Weather effects sit at the camera plus their base: camera-anchored ones (0x0400) where the camera was
                // at birth, camera-following ones tracking it for as long as they follow their generator.
                p.Origin = _cameraRawPosition + Template.RawBasePosition;
                p.FollowsCamera = !config.CameraAttachedBasePosition && config.FollowGenerator;
            }
            if (Def.Batched)
            {
                int subCount = AuthoredCount;
                p.SubOffsets = new Vector3[subCount];
                p.SubRelativeVelocities = new Vector3[subCount];
            }

            foreach (var op in Def.Initializers)
            {
                ushort slot = op.Allocation;
                switch (op.OpCode)
                {
                    case 0x01: // StandardParticleSetup: life span (0 = forever) plus variance
                    {
                        var setup = Def.Setup;
                        int life = setup?.MaxLifeSpan ?? 0;
                        p.MaxAge = LivesForever ? float.PositiveInfinity : life + PosRand(setup?.LifeSpanVariance ?? 0);
                        break;
                    }

                    case 0x02: Allocate(p, slot, ParticleTransformKind.Position).Velocity = op.Vector(0); break;
                    case 0x0B: Allocate(p, slot, ParticleTransformKind.Rotation).Velocity = op.Vector(0); break;
                    case 0x12: Allocate(p, slot, ParticleTransformKind.Scale).Velocity = op.Vector(0); break;

                    case 0x03: // velocity variance (position / rotation / scale transform at the slot)
                    case 0x0C:
                    case 0x13:
                        if (p.Transforms.TryGetValue(slot, out var varied))
                        {
                            var v = op.Vector(0);
                            varied.State.Velocity += new Vector3(v.X * Rand(), v.Y * Rand(), v.Z * Rand());
                        }
                        break;

                    // Spawn scatter: a batched particle scatters each of its sub-particles instead of itself.
                    case 0x06: // SphericalPositionVarianceSimple
                    case 0x07: // SphericalPositionVarianceMedium
                    case 0x1F: // SphericalPositionVarianceFull
                        if (p.SubOffsets is { } scattered)
                        {
                            for (int i = 0; i < scattered.Length; i++) scattered[i] += SpawnOffset(op);
                        }
                        else
                        {
                            p.InitialPosition += SpawnOffset(op);
                        }
                        break;

                    case 0x08: // RelativeVelocitySetup: velocity along the spawn offset direction
                        if (p.Transforms.TryGetValue(slot, out var relative) && p.InitialPosition.LengthSquared() > 0f)
                        {
                            relative.State.RelativeVelocity = Vector3.Normalize(p.InitialPosition) * op.Float(0);
                        }
                        if (p.SubOffsets is { } subs && p.SubRelativeVelocities is { } subVelocities)
                        {
                            for (int i = 0; i < subs.Length; i++)
                            {
                                subVelocities[i] = subs[i].LengthSquared() > 0f ? Vector3.Normalize(subs[i]) * op.Float(0) : Vector3.Zero;
                            }
                        }
                        break;
                    case 0x41: // RelativeVelocityVarianceSetup
                        if (p.Transforms.TryGetValue(slot, out var relVar) && p.InitialPosition.LengthSquared() > 0f)
                        {
                            relVar.State.RelativeVelocity += Vector3.Normalize(p.InitialPosition) * (op.Float(0) * Rand());
                        }
                        break;
                    case 0x31: // RandomVelocitySetup: one random value on all three axes
                        if (p.Transforms.TryGetValue(slot, out var random))
                        {
                            float r = op.Float(0) * Rand();
                            random.State.Velocity = new Vector3(r, r, r);
                        }
                        break;
                    case 0x67: // ReverseDisplacementSetup: start at the end of the trajectory and run it backwards
                        if (p.Transforms.TryGetValue(slot, out var reverse) && reverse.Kind == ParticleTransformKind.Position &&
                            !float.IsPositiveInfinity(p.MaxAge))
                        {
                            p.Position += TotalVelocity(p, reverse.State) * p.MaxAge;
                            reverse.State.Velocity = -reverse.State.Velocity;
                            reverse.State.RelativeVelocity = -reverse.State.RelativeVelocity;
                        }
                        break;

                    case 0x09: p.Rotation = op.Vector(0); break; // RotationInitializer
                    case 0x0A: // RotationVarianceInitializer
                    {
                        var v = op.Vector(0);
                        p.Rotation += new Vector3(v.X * Rand(), v.Y * Rand(), v.Z * Rand());
                        break;
                    }
                    case 0x3B: // IncrementalRotationApplier: each successive particle turns one more step
                        p.Rotation += op.Vector(0) * (1 + _totalEmitted);
                        p.NegateRotationY = true;
                        break;

                    case 0x0F: p.Scale = op.Vector(0); break; // ScaleInitializer
                    case 0x10: // ScaleVarianceInitializer
                    {
                        var v = op.Vector(0);
                        p.Scale += new Vector3(v.X * PosRand(1f), v.Y * PosRand(1f), v.Z * PosRand(1f));
                        break;
                    }
                    case 0x11: // SingleScaleVarianceInitializer
                    {
                        float v = PosRand(op.Float(0));
                        p.Scale += new Vector3(v);
                        break;
                    }

                    case 0x16: p.Color = Rgba(op.Args.Length > 0 ? op.Args[0] : 0); break; // ColorSetup
                    case 0x17: // ColorVarianceSetup
                    {
                        var v = Rgba(op.Args.Length > 0 ? op.Args[0] : 0);
                        p.Color += new Vector4(v.X * PosRand(1f), v.Y * PosRand(1f), v.Z * PosRand(1f), v.W * PosRand(1f));
                        break;
                    }
                    case 0x18: // UniformColorVarianceSetup
                    {
                        float f = ((op.Args.Length > 0 ? op.Args[0] : 0) & 0xFF) / 255f * PosRand(1f);
                        p.Color += new Vector4(f);
                        break;
                    }
                    case 0x19: // ColorTransformSetup: four signed 16-bit rates
                        p.ColorTransforms[slot] = SignedShorts(op);
                        break;
                    case 0x1A: // ColorTransformVariance
                        if (p.ColorTransforms.TryGetValue(slot, out var transform))
                        {
                            var variance = SignedShorts(op);
                            for (int i = 0; i < 4; i++) transform[i] += (int)MathF.Round(PosRand(1f) * variance[i]);
                        }
                        break;

                    case 0x3D: // OscillationSetup
                        p.Oscillations[slot] = (Vector3.Zero, Vector3.Zero);
                        break;
                    case 0x3E: // OscillationAccelerationSetup (X, Y, Z)
                    case 0x3F:
                    case 0x40:
                        if (p.Oscillations.TryGetValue(slot, out var oscillation))
                        {
                            int axis = op.OpCode - 0x3E;
                            float value = op.Float(0) + op.Float(1) * Rand();
                            p.Oscillations[slot] = (WithAxis(oscillation.Acceleration, axis, value), oscillation.PreviousAmplitude);
                        }
                        break;

                    case 0x90: // DaylightBasedColorAdjuster: the birth color takes the strongest model light's tint
                        p.Color = new Vector4(p.Color.X * _daylightColor.X, p.Color.Y * _daylightColor.Y, p.Color.Z * _daylightColor.Z, p.Color.W);
                        break;
                    case 0x91: p.DaylightColored = true; break; // DaylightBasedColorSetup

                    case 0x58: // PointLightParamsInitializer: range, theta, range multiplier, theta multiplier
                        p.LightRange = op.Float(0);
                        p.LightTheta = op.Float(1);
                        p.LightRangeMultiplier = LightMultiplier(op.Float(2));
                        p.LightThetaMultiplier = LightMultiplier(op.Float(3));
                        break;
                    case 0x7E when parent != null: p.LightTheta = parent.LightTheta; break; // ParentThetaConfig
                    case 0x7F when parent != null: p.LightRange = parent.LightRange; break; // ParentRangeConfig

                    // Parent-copy initializers: a child takes its parent's state at birth.
                    case 0x46 when parent != null: // ParentVelocityConfig: the parent's travel velocity, scaled
                        if (p.Transforms.TryGetValue(slot, out var inherited))
                        {
                            inherited.State.Velocity = ParentVelocity(parent) * op.Float(0);
                        }
                        break;
                    case 0x47 when parent != null: // ParentRotateConfig
                    case 0x79 when parent != null:
                        p.Rotation = parent.Rotation;
                        break;
                    case 0x48 when parent != null: p.Color = parent.TextureFactor; break; // ParentColorConfig
                    case 0x49 when parent != null: p.Scale = parent.Scale; break; // ParentScaleConfig
                    case 0x4A when parent != null: p.TexCoordTranslate = parent.TexCoordTranslate; break; // ParentTexCoordConfig

                    case 0x44: // ChildGeneratorSetup: a child generator that lives as long as this particle
                    case 0x53:
                    case 0x6A:
                        if (ResolveChild(op.Id(1)) is { } streamChild)
                        {
                            float lifeSpan = Def.ContinuousSingleton ? float.PositiveInfinity : p.MaxAge;
                            p.ChildStreams[slot] = new ChildStream(streamChild, lifeSpan);
                        }
                        break;
                }
            }

            // 0x3C OnceChildGeneratorSetup: emit the child exactly once, at birth, from the finished particle.
            foreach (var op in Def.Initializers)
            {
                if (op.OpCode == 0x3C) ResolveChild(op.Id(1))?.EmitChildren(p, ChildFollow.None);
            }
            return p;
        }

        /// <summary>
        /// Places a following child in its parent's transform: the parent's world position, with the child's base and
        /// local offset turned and scaled by the parent's orientation (rotation forced to X-Y-Z order, scale before or
        /// after rotation as the parent authors it; 0x46 billboards it toward the camera, 0x33 applies the parent's
        /// movement / camera billboard).
        /// Semantics referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer,
        /// ui/js/particle/ops/updaters.js ChildGeneratorUpdater and runtime.js computeWorldSpaceTransform, after xim).
        /// </summary>
        private void FollowParent(ZoneParticle p)
        {
            var parent = p.FollowedParent!;
            p.Origin = parent.WorldPosition;
            p.ParentLinear = ParentOrientation(parent, p.FollowMode == ChildFollow.TransformBillboard);
        }

        private Matrix4x4 ParentOrientation(ZoneParticle parent, bool billboard)
        {
            var setup = parent.Definition?.Setup;
            float yMul = parent.NegateRotationY ? -1f : 1f;
            // Column-form Rx * Ry * Rz (X-Y-Z order) as a row-vector matrix.
            var rotation = Matrix4x4.CreateRotationZ(parent.Rotation.Z) * Matrix4x4.CreateRotationY(yMul * parent.Rotation.Y) *
                           Matrix4x4.CreateRotationX(parent.Rotation.X);
            var scale = Matrix4x4.CreateScale(parent.Scale);
            var oriented = setup?.ScaleBeforeRotate == true ? rotation * scale : scale * rotation;

            if (billboard)
            {
                oriented *= AxisBillboardMatrix(_cameraRawForward);
            }
            else if (setup != null)
            {
                Vector3? direction = setup.BillBoardType switch
                {
                    ParticleBillBoardType.Movement when parent.SubOffsets == null => parent.LastMovement,
                    ParticleBillBoardType.MovementHorizontal when parent.SubOffsets == null => parent.LastMovement with { Y = 0f },
                    ParticleBillBoardType.Camera => _cameraRawPosition - parent.WorldPosition,
                    _ => null
                };
                if (direction is { } towards) oriented *= CreateDirectionOrientation(towards);
            }
            return oriented;
        }

        /// <summary>
        /// The parent particle's travel velocity (its first position transform, turned by its velocity rotation).
        /// </summary>
        private static Vector3 ParentVelocity(ZoneParticle parent)
        {
            foreach (var t in parent.Transforms.Values)
            {
                if (t.Kind == ParticleTransformKind.Position) return TotalVelocity(parent, t.State);
            }
            return Vector3.Zero;
        }

        /// <summary>
        /// Row-vector form of <see cref="AxisBillboard"/>.
        /// </summary>
        internal static Matrix4x4 AxisBillboardMatrix(Vector3 direction)
        {
            var x = AxisBillboard(Vector3.UnitX, direction);
            var y = AxisBillboard(Vector3.UnitY, direction);
            var z = AxisBillboard(Vector3.UnitZ, direction);
            return new Matrix4x4(x.X, x.Y, x.Z, 0f, y.X, y.Y, y.Z, 0f, z.X, z.Y, z.Z, 0f, 0f, 0f, 0f, 1f);
        }

        /// <summary>
        /// The client's movement/camera billboard orientation, raw DAT axes: pitches the particle about its left axis
        /// toward <paramref name="direction"/>, then yaws it about Y to face the direction's heading (straight up or down
        /// turns it a quarter about Z). Row-vector form of xim's axis-angle then Y rotation.
        /// Semantics referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer,
        /// ui/js/particle/runtime.js applyMovementOrientation, after xim).
        /// </summary>
        public static Matrix4x4 CreateDirectionOrientation(Vector3 direction)
        {
            if (direction.LengthSquared() < 1e-12f) return Matrix4x4.Identity;
            var movement = Vector3.Normalize(direction);
            if (MathF.Abs(movement.Y) >= 0.999f)
            {
                return Matrix4x4.CreateRotationZ(MathF.Sign(movement.Y) * MathF.PI / 2.0f);
            }

            var left = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, movement));
            var up = Vector3.Normalize(Vector3.Cross(movement, left));
            float angle = -MathF.Acos(Math.Clamp(Vector3.Dot(up, Vector3.UnitY), -1.0f, 1.0f)) * MathF.Sign(movement.Y);
            return Matrix4x4.CreateRotationY(-MathF.Atan2(movement.Z, movement.X)) * Matrix4x4.CreateFromAxisAngle(left, angle);
        }

        private bool HasInitializer(byte opCode)
        {
            foreach (var op in Def.Initializers)
            {
                if (op.OpCode == opCode) return true;
            }
            return false;
        }

        private ZoneParticleEmitter? ResolveChild(string generatorId)
        {
            if (string.IsNullOrEmpty(generatorId) || ChildResolver == null) return null;
            return Template.Children.TryGetValue(generatorId, out var child) ? ChildResolver(child) : null;
        }

        /// <summary>
        /// Advances a continuous child stream: while the parent lives (or the child auto-runs), emit at the child's cadence.
        /// </summary>
        private static void AdvanceChildStream(ZoneParticle parent, ChildStream stream, float frames, ChildFollow follow)
        {
            var childDef = stream.Child.Template.Definition;
            stream.LifeTime += frames;
            if (!childDef.AutoRun && stream.LifeTime >= stream.MaxEmitTime && stream.Emitted > 0) return;

            stream.FramesUntilNext -= frames;
            while (stream.FramesUntilNext <= 0.0f)
            {
                stream.FramesUntilNext += childDef.FramesPerEmission + stream.Child.PosRand(childDef.EmissionVariance);
                stream.Child.EmitChildren(parent, follow);
                stream.Emitted++;
                if (childDef.ContinuousSingleton) break;
            }
        }

        private static ParticleTransformState Allocate(ZoneParticle p, ushort slot, ParticleTransformKind kind)
        {
            var state = new ParticleTransformState();
            p.Transforms[slot] = (kind, state);
            return state;
        }

        /// <summary>
        /// One spawn offset for a spherical position-variance initializer (0x06 simple, 0x07 medium with its radius
        /// variance doubled for batched generators, 0x1F full). A camera-oriented 0x1F shell is turned to face along the
        /// camera's view unless the particle's local position is already in camera space.
        /// </summary>
        private Vector3 SpawnOffset(ParticleOpcode op)
        {
            switch (op.OpCode)
            {
                case 0x06:
                    return SphericalOffset(op.Float(0), op.Float(1), Vector3.One, 0f, 0f, 0f, MathF.PI, 1);
                case 0x07:
                    return SphericalOffset(op.Float(0) * (Def.Batched ? 2f : 1f), op.Float(1),
                        new Vector3(op.Float(2), op.Float(3), op.Float(4)), 0f, op.Float(6), 0f, MathF.PI, 1);
                default:
                {
                    var offset = SphericalOffset(op.Float(0), op.Float(1),
                        new Vector3(op.Float(2), op.Float(3), op.Float(4)), op.Float(5), op.Float(6), op.Float(7), op.Float(8),
                        1 + (op.Args.Length > 10 ? (int)op.Args[10] : 0));
                    bool cameraOriented = op.Args.Length > 9 && op.Args[9] == 1;
                    return cameraOriented && Def.Setup?.LocalPositionInCameraSpace != true
                        ? AxisBillboard(offset, _cameraRawForward)
                        : offset;
                }
            }
        }

        /// <summary>
        /// Re-expresses <paramref name="v"/> in the basis whose forward (+Z) is <paramref name="direction"/> with world up:
        /// x along the left axis (up x forward), y along the derived up, z along the direction.
        /// </summary>
        internal static Vector3 AxisBillboard(Vector3 v, Vector3 direction)
        {
            if (direction.LengthSquared() < 1e-12f) return v;
            var forward = Vector3.Normalize(direction);
            var left = Vector3.Cross(Vector3.UnitY, forward);
            if (left.LengthSquared() < 1e-12f) return v;
            left = Vector3.Normalize(left);
            var up = Vector3.Normalize(Vector3.Cross(forward, left));
            return left * v.X + up * v.Y + forward * v.Z;
        }

        /// <summary>
        /// A spawn offset on a (scaled, tilted) sphere shell: radius base + variance * cbrt(u) along +X, tilted about Z,
        /// spun about Y by a random (or evenly divided) angle, scaled, then turned by the authored Z and Y axis rotations.
        /// </summary>
        private Vector3 SphericalOffset(float radiusVariance, float baseRadius, Vector3 radiusScale,
            float rotationZ, float rotationY, float tilt, float tiltVariance, int rotationDivisor)
        {
            float phi = rotationDivisor <= 1
                ? PosRand(MathF.Tau)
                : MathF.PI + MathF.Tau / rotationDivisor * (_totalEmitted % rotationDivisor);
            float random = radiusVariance == 0f ? 0f : MathF.Cbrt(PosRand(1f));
            var v = new Vector3(baseRadius + radiusVariance * random, 0f, 0f);
            v = RotateZ(v, tilt + tiltVariance * Rand());
            v = RotateY(v, phi);
            v *= radiusScale;
            v = RotateZ(v, rotationZ);
            return RotateY(v, rotationY);
        }

        // ── updaters ─────────────────────────────────────────────────────────────

        private void UpdateParticle(ZoneParticle particle, float frames, in ZoneParticleFrame frame)
        {
            if (particle.IsExpired) return;

            particle.Age += frames;
            if (particle.IsExpired)
            {
                // 0x05 repeat: the particle loops instead of dying; 0x01 emits a child generator where it died.
                foreach (var handler in Def.ExpirationOpcodes)
                {
                    if (handler.OpCode == 0x01) ResolveChild(handler.Id(1))?.EmitChildren(particle, ChildFollow.None);
                }
                if (Def.ExpirationHandlers.Contains(0x05)) particle.Age = 1e-7f;
                else return;
            }

            if (particle.FollowsCamera) particle.Origin = frame.CameraRawPosition + Template.RawBasePosition;
            // A following child tracks its live parent when it follows its generator.
            if (particle.FollowedParent is { IsExpired: false } && Def.Setup?.FollowGenerator != false) FollowParent(particle);

            var previousPosition = particle.Position;
            particle.ColorMultiplier = Vector4.One;
            foreach (var op in Def.Updaters)
            {
                ApplyUpdater(particle, op, frames, frame);
            }
            if (frames > 0f) particle.LastMovement = particle.Position - previousPosition;
        }

        private void ApplyUpdater(ZoneParticle p, ParticleOpcode op, float frames, in ZoneParticleFrame frame)
        {
            ushort slot = op.Allocation;
            switch (op.OpCode)
            {
                case 0x02: // PositionUpdater (sub-particles drift along their own relative velocity)
                    if (p.Transforms.TryGetValue(slot, out var moving)) p.Position += TotalVelocity(p, moving.State) * frames;
                    if (p.SubOffsets is { } drifting && p.SubRelativeVelocities is { } driftVelocities)
                    {
                        for (int i = 0; i < drifting.Length; i++) drifting[i] += driftVelocities[i] * frames;
                    }
                    break;
                case 0x03: // VelocityAccelerator
                case 0x06:
                case 0x09:
                    if (p.Transforms.TryGetValue(slot, out var accelerating)) accelerating.State.Velocity += op.Vector(0) * frames;
                    break;
                case 0x05: // RotationUpdater
                    if (p.Transforms.TryGetValue(slot, out var spinning)) p.Rotation += spinning.State.Velocity * frames;
                    break;
                case 0x08: // ScaleUpdater
                    if (p.Transforms.TryGetValue(slot, out var growing)) p.Scale += growing.State.Velocity * frames;
                    break;

                case 0x0B: // ColorTransformApplier
                    if (p.ColorTransforms.TryGetValue(slot, out var ct))
                    {
                        var delta = new Vector4(ct[0] >> 7, ct[1] >> 7, ct[2] >> 7, ct[3] >> 7) * (0.5f * frames);
                        p.Color += delta;
                    }
                    break;
                case 0x0C: // ColorTransformModifier
                    if (p.ColorTransforms.TryGetValue(slot, out var modified))
                    {
                        var modifier = SignedShorts(op);
                        float rate = frames / 30f;
                        for (int i = 0; i < 4; i++) modified[i] += (int)MathF.Floor(modifier[i] * rate);
                    }
                    break;

                case 0x0D: // SpriteSheetFrameUpdater: step through the cards over the particle's life
                {
                    int cards = Template.SpriteFrameCount;
                    if (cards > 0) p.SpriteIndex = Math.Min(cards - 1, (int)MathF.Floor((cards + 1) * p.Progress));
                    break;
                }
                case 0x45: // MoonPhaseSpriteSheetUpdater
                    if (Template.SpriteFrameCount > 0) p.SpriteIndex = Math.Min(Template.SpriteFrameCount - 1, frame.MoonPhase);
                    break;

                case 0x0F: Progress(p, slot, null, x => p.Position = p.Position with { X = x }); break;
                case 0x10: Progress(p, slot, null, y => p.Position = p.Position with { Y = y }); break;
                case 0x11: Progress(p, slot, null, z => p.Position = p.Position with { Z = z }); break;

                // Rotation curves are authored in half-turns.
                case 0x12: Progress(p, slot, p.Rotation.X / MathF.PI, x => p.Rotation = p.Rotation with { X = x * MathF.PI }); break;
                case 0x13: Progress(p, slot, p.Rotation.Y / MathF.PI, y => p.Rotation = p.Rotation with { Y = y * MathF.PI }); break;
                case 0x14: Progress(p, slot, p.Rotation.Z / MathF.PI, z => p.Rotation = p.Rotation with { Z = z * MathF.PI }); break;

                case 0x15: Progress(p, slot, p.Scale.X, x => p.Scale = p.Scale with { X = x }); break;
                case 0x16: Progress(p, slot, p.Scale.Y, y => p.Scale = p.Scale with { Y = y }); break;
                case 0x17: Progress(p, slot, p.Scale.Z, z => p.Scale = p.Scale with { Z = z }); break;

                case 0x18: Progress(p, slot, p.Color.X, r => p.Color = p.Color with { X = r }); break;
                case 0x19: Progress(p, slot, p.Color.Y, g => p.Color = p.Color with { Y = g }); break;
                case 0x1A: Progress(p, slot, p.Color.Z, b => p.Color = p.Color with { Z = b }); break;
                case 0x1B: Progress(p, slot, p.Color.W, a => p.Color = p.Color with { W = a }); break;

                case 0x1C: Progress(p, slot, null, u => p.TexCoordTranslate = p.TexCoordTranslate with { X = u }); break;
                case 0x1D: Progress(p, slot, null, w => p.TexCoordTranslate = p.TexCoordTranslate with { Y = w }); break;

                case 0x25: // ChildGeneratorBasicUpdater: the child emits from its own base (or the parent via 0x45)
                    if (p.ChildStreams.TryGetValue(slot, out var basicStream)) AdvanceChildStream(p, basicStream, frames, ChildFollow.None);
                    break;
                case 0x33: // ChildGeneratorUpdater: the child sits in the parent particle's transform
                    if (p.ChildStreams.TryGetValue(slot, out var followStream)) AdvanceChildStream(p, followStream, frames, ChildFollow.Transform);
                    break;
                case 0x46: // ChildGeneratorUpdater, camera-billboarded parent orientation
                    if (p.ChildStreams.TryGetValue(slot, out var billboardStream)) AdvanceChildStream(p, billboardStream, frames, ChildFollow.TransformBillboard);
                    break;

                case 0x26: // VelocityRotator
                    if (p.Transforms.TryGetValue(slot, out var turning)) turning.State.VelocityRotation += op.Vector(0) * (0.5f * frames);
                    break;

                case 0x27: // TextureCoordinateUpdater (U)
                    p.TexCoordTranslate += new Vector2(op.Float(0) * frames, 0f);
                    break;
                case 0x28: // TextureCoordinateUpdater (V)
                    p.TexCoordTranslate += new Vector2(0f, op.Float(0) * frames);
                    break;

                case 0x29: // OscillationApplier (X, Y, Z)
                case 0x2A:
                case 0x2B:
                    Oscillate(p, slot, op.OpCode - 0x29, op);
                    break;

                case 0x2C: // VelocityDampener
                    if (p.Transforms.TryGetValue(slot, out var damped))
                    {
                        float f = MathF.Pow(damped.State.DampeningFactor ?? op.Float(0), frames);
                        damped.State.Velocity *= f;
                        damped.State.RelativeVelocity *= f;
                        if (p.SubRelativeVelocities is { } dampedSubs)
                        {
                            for (int i = 0; i < dampedSubs.Length; i++) dampedSubs[i] *= f;
                        }
                    }
                    break;

                case 0x2E: // DrawDistanceUpdater: fade by distance to the particle
                {
                    float distance = Vector3.Distance(frame.CameraRawPosition, p.WorldPosition);
                    MultiplyAlpha(p, FallOff(distance, op.Float(0), op.Float(1)));
                    break;
                }
                case 0x2F: // VelocityRotationUpdater: velocity collapses onto +X, turned by the particle's rotation
                    if (p.Transforms.TryGetValue(slot, out var collapsing))
                    {
                        float magnitude = collapsing.State.Velocity.Length() + collapsing.State.RelativeVelocity.Length();
                        collapsing.State.Velocity = new Vector3(magnitude, 0f, 0f);
                        collapsing.State.RelativeVelocity = Vector3.Zero;
                        collapsing.State.VelocityRotation = p.Rotation;
                    }
                    break;

                case 0x30: ProgressVelocity(p, slot, 0); break;
                case 0x31: ProgressVelocity(p, slot, 1); break;
                case 0x32: ProgressVelocity(p, slot, 2); break;

                case 0x3C: Clock(slot, frame.DayFraction, r => p.Color = p.Color with { X = r }); break;
                case 0x3D: Clock(slot, frame.DayFraction, g => p.Color = p.Color with { Y = g }); break;
                case 0x3E: Clock(slot, frame.DayFraction, b => p.Color = p.Color with { Z = b }); break;
                case 0x3F: Clock(slot, frame.DayFraction, a => MultiplyAlpha(p, a)); break;
                case 0x40: Clock(slot, frame.DayFraction, x => p.Scale = p.Scale with { X = x }); break;
                case 0x41: Clock(slot, frame.DayFraction, y => p.Scale = p.Scale with { Y = y }); break;
                case 0x42: Clock(slot, frame.DayFraction, z => p.Scale = p.Scale with { Z = z }); break;

                case 0x44: // progress-driven dampening factor
                    Progress(p, slot, null, d =>
                    {
                        foreach (var t in p.Transforms.Values)
                        {
                            if (t.Kind == ParticleTransformKind.Position) { t.State.DampeningFactor = d; break; }
                        }
                    });
                    break;

                case 0x48: // DoubleRangeDrawDistanceUpdater: visible only inside a near..far band
                {
                    float distance = Vector3.Distance(frame.CameraRawPosition, p.WorldPosition) +
                                     1.15f * MathF.Abs(p.Scale.X);
                    MultiplyAlpha(p, DoubleRangeWeight(distance, op.Float(0), op.Float(1), op.Float(2), op.Float(3)));
                    break;
                }

                case 0x49: Clock(slot, frame.DayFraction, t => p.LightTheta = t); break; // point-light theta by time of day
                case 0x5B: Progress(p, slot, p.LightTheta, t => p.LightTheta = t); break;
                case 0x5C: Progress(p, slot, p.LightRange, r => p.LightRange = r); break;
                case 0x5D: Progress(p, slot, p.LightThetaMultiplier, m => p.LightThetaMultiplier = m); break;
                case 0x5E: Progress(p, slot, p.LightRangeMultiplier, m => p.LightRangeMultiplier = m); break;

                case 0x4E: // DayOfWeekColorUpdater
                    p.DayOfWeekTint = TintAt(op, 8, frame.DayOfWeek);
                    break;
                case 0x4F: // MoonPhaseColorUpdater
                    p.MoonPhaseTint = TintAt(op, 12, frame.MoonPhase);
                    break;

                case 0x53: // OcclusionUpdater: occlusion probe only
                    p.IsOcclusionProbe = true;
                    break;

                case 0x54: Progress(p, slot, null, u => p.TexCoordTranslate += new Vector2(u * frames, 0f)); break;
                case 0x55: Progress(p, slot, null, v => p.TexCoordTranslate += new Vector2(0f, v * frames)); break;
                case 0x56: Progress(p, slot, null, x => p.Rotation += new Vector3(x * frames * MathF.PI, 0f, 0f)); break;
                case 0x57: Progress(p, slot, null, y => p.Rotation += new Vector3(0f, y * frames * MathF.PI, 0f)); break;
                case 0x58: Progress(p, slot, null, z => p.Rotation += new Vector3(0f, 0f, z * frames * MathF.PI)); break;

                case 0x59: // AngularDistanceRotationUpdater: spin relative to the camera
                {
                    var particlePos = p.WorldPosition;
                    var toParticle = particlePos - frame.CameraRawPosition;
                    float distance = toParticle.Length();
                    if (distance > 1e-5f && frame.CameraRawForward.LengthSquared() > 0f)
                    {
                        float cos = Math.Clamp(Vector3.Dot(Vector3.Normalize(frame.CameraRawForward), toParticle / distance), -1f, 1f);
                        float angle = 16f * MathF.Acos(cos);
                        p.Rotation = p.Rotation with { Z = -(op.Float(1) + op.Float(0) * (angle + distance)) };
                    }
                    break;
                }

                case 0x61: Clock(slot, frame.DayFraction, x => p.Rotation += new Vector3(x * MathF.PI, 0f, 0f)); break;
                case 0x62: Clock(slot, frame.DayFraction, y => p.Rotation += new Vector3(0f, y * MathF.PI, 0f)); break;
                case 0x63: Clock(slot, frame.DayFraction, z => p.Rotation += new Vector3(0f, 0f, z * MathF.PI)); break;
                case 0x66: Clock(slot, frame.DayFraction, x => p.Rotation = p.Rotation with { X = x * MathF.PI }); break;
                case 0x67: Clock(slot, frame.DayFraction, y => p.Rotation = p.Rotation with { Y = y * MathF.PI }); break;
                case 0x68: Clock(slot, frame.DayFraction, z => p.Rotation = p.Rotation with { Z = z * MathF.PI }); break;

                case 0x69: // DaylightBasedColorApplier
                    if (p.DaylightColored)
                    {
                        var d = frame.DaylightColor;
                        p.ColorMultiplier = new Vector4(p.ColorMultiplier.X * d.X, p.ColorMultiplier.Y * d.Y, p.ColorMultiplier.Z * d.Z, p.ColorMultiplier.W);
                    }
                    break;

                case 0x6B: Clock(slot, frame.DayFraction, x => p.Position = p.Position with { X = x }); break;
                case 0x6C: Clock(slot, frame.DayFraction, y => p.Position = p.Position with { Y = y }); break;
                case 0x6D: Clock(slot, frame.DayFraction, z => p.Position = p.Position with { Z = z }); break;
            }
        }

        /// <summary>
        /// Total translation velocity: (velocity + relative velocity) turned by the transform's velocity rotation (Z-Y-X).
        /// </summary>
        private static Vector3 TotalVelocity(ZoneParticle p, ParticleTransformState t)
        {
            var v = t.Velocity + t.RelativeVelocity;
            var r = t.VelocityRotation;
            if (r == Vector3.Zero) return v;
            float yMul = p.NegateRotationY ? -1f : 1f;
            return Vector3.Transform(v, RotationZyx(r.X, r.Y * yMul, r.Z));
        }

        /// <summary>
        /// Z-Y-X Euler rotation matching the client's convention for velocity rotation.
        /// </summary>
        private static Matrix4x4 RotationZyx(float x, float y, float z) =>
            Matrix4x4.CreateRotationX(x) * Matrix4x4.CreateRotationY(y) * Matrix4x4.CreateRotationZ(z);

        /// <summary>
        /// Sinusoidal sway along one axis (or relative to the particle's direction of travel): applies the change in
        /// amplitude since the last frame to the position.
        /// </summary>
        private void Oscillate(ZoneParticle p, ushort slot, int axis, ParticleOpcode op)
        {
            if (!p.Oscillations.TryGetValue(slot, out var osc)) return;
            float period = op.Float(0);
            if (period == 0f) return;
            float rate = 180f / period;
            float frequency = MathF.PI * (p.Age / rate);
            float baseOffset = op.Float(1);
            float baseAmplitude = 0.5f * (MathF.Sin(baseOffset + frequency - MathF.PI / 2f) + MathF.Cos(baseOffset));
            float amplitude = 0.5f * Axis(osc.Acceleration, axis) * baseAmplitude * rate;
            float delta = amplitude - Axis(osc.PreviousAmplitude, axis);

            Vector3 direction = WithAxis(Vector3.Zero, axis, 1f);
            foreach (var t in p.Transforms.Values)
            {
                if (t.State.RelativeVelocity.LengthSquared() < 1e-14f) continue;
                var forward = Vector3.Normalize(t.State.RelativeVelocity);
                direction = axis switch
                {
                    0 => forward,
                    1 => Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ)),
                    _ => Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY))
                };
                break;
            }

            p.Position += direction * delta;
            p.Oscillations[slot] = (osc.Acceleration, WithAxis(osc.PreviousAmplitude, axis, amplitude));
        }

        private void ProgressVelocity(ZoneParticle p, ushort slot, int axis)
        {
            Progress(p, slot, null, value =>
            {
                foreach (var t in p.Transforms.Values)
                {
                    if (t.Kind != ParticleTransformKind.Position) continue;
                    t.State.Velocity = WithAxis(t.State.Velocity, axis, value);
                    break;
                }
            });
        }

        /// <summary>
        /// Samples the slot's curve at the particle's progress, looped by the link's cycle count. With an initial value,
        /// the curve's first keyframe is replaced by the particle's value when the updater first runs.
        /// </summary>
        private void Progress(ZoneParticle p, ushort slot, float? initial, Action<float> set)
        {
            if (!Template.Curves.TryGetValue(slot, out var curve)) return;
            int cycles = Def.KeyFrameLinks.TryGetValue(slot, out var link) ? link.Cycles : 1;

            float? seed = null;
            if (initial.HasValue)
            {
                if (!p.InitialValues.TryGetValue(slot, out float stored))
                {
                    stored = initial.Value;
                    p.InitialValues[slot] = stored;
                }
                seed = stored;
            }

            float progress = cycles * p.Progress;
            progress -= MathF.Floor(progress);
            set(curve.Evaluate(progress, seed));
        }

        private void Clock(ushort slot, float dayFraction, Action<float> set)
        {
            if (Template.Curves.TryGetValue(slot, out var curve)) set(curve.Evaluate(Math.Clamp(dayFraction, 0f, 1f)));
        }

        private static void MultiplyAlpha(ZoneParticle p, float factor) =>
            p.ColorMultiplier = p.ColorMultiplier with { W = p.ColorMultiplier.W * factor };

        /// <summary>
        /// Distance falloff: 1 within <paramref name="near"/>, 0 beyond <paramref name="far"/>, linear between.
        /// </summary>
        public static float FallOff(float distance, float near, float far)
        {
            if (far <= near) return distance <= near ? 1.0f : 0.0f;
            if (distance <= near) return 1.0f;
            if (distance >= far) return 0.0f;
            return (far - distance) / (far - near);
        }

        /// <summary>
        /// Band visibility: fades in across [nearStart, nearEnd], stays visible to farStart, fades out by farEnd.
        /// </summary>
        public static float DoubleRangeWeight(float distance, float nearStart, float nearEnd, float farStart, float farEnd)
        {
            if (distance < nearStart) return 0f;
            if (distance < nearEnd) return 1f - (nearEnd - distance) / (nearEnd - nearStart);
            if (distance < farStart) return 1f;
            if (distance < farEnd) return 1f - (distance - farStart) / (farEnd - farStart);
            return 0f;
        }

        /// <summary>
        /// Point-light multiplier encoding: 2^x for x >= 0, 1 + x for -1 <= x < 0, otherwise 0.
        /// </summary>
        internal static float LightMultiplier(float x) => x >= 0f ? MathF.Pow(2f, x) : x >= -1f ? 1f + x : 0f;

        private static Vector4? TintAt(ParticleOpcode op, int count, int index)
        {
            // One reserved dword, then `count` RGBA byte quads.
            int arg = 1 + Math.Clamp(index, 0, count - 1);
            return arg < op.Args.Length ? Rgba(op.Args[arg]) : null;
        }

        private static Vector4 Rgba(uint rgba) =>
            new((rgba & 0xFF) / 255f, ((rgba >> 8) & 0xFF) / 255f, ((rgba >> 16) & 0xFF) / 255f, (rgba >> 24) / 255f);

        private static int[] SignedShorts(ParticleOpcode op)
        {
            uint lo = op.Args.Length > 0 ? op.Args[0] : 0;
            uint hi = op.Args.Length > 1 ? op.Args[1] : 0;
            return new[] { (int)(short)(lo & 0xFFFF), (int)(short)(lo >> 16), (int)(short)(hi & 0xFFFF), (int)(short)(hi >> 16) };
        }

        private static Vector3 RotateZ(Vector3 v, float a)
        {
            float c = MathF.Cos(a), s = MathF.Sin(a);
            return new Vector3(v.X * c - v.Y * s, v.X * s + v.Y * c, v.Z);
        }

        private static Vector3 RotateY(Vector3 v, float a)
        {
            float c = MathF.Cos(a), s = MathF.Sin(a);
            return new Vector3(v.X * c + v.Z * s, v.Y, -v.X * s + v.Z * c);
        }

        private static float Axis(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

        private static Vector3 WithAxis(Vector3 v, int axis, float value) => axis switch
        {
            0 => v with { X = value },
            1 => v with { Y = value },
            _ => v with { Z = value }
        };

        internal float PosRand(float max) => max <= 0f ? 0f : (float)_random.NextDouble() * max;

        private float Rand() => (float)_random.NextDouble() * 2f - 1f;
    }
}
