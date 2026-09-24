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
            int scheduleLoopFrames = 0)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Curves = curves ?? throw new ArgumentNullException(nameof(curves));
            Schedule = schedule;
            ScheduleLoopFrames = scheduleLoopFrames;
        }

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
    public readonly record struct ZoneParticleFrame(Vector3 CameraRawPosition, float DayFraction, Vector3 DaylightColor);

    /// <summary>
    /// One live particle, in raw DAT space relative to its generator's base position.
    /// </summary>
    public sealed class ZoneParticle
    {
        internal readonly Dictionary<ushort, Vector3> Velocities = new();
        internal readonly Dictionary<ushort, Vector3> RotationVelocities = new();
        internal readonly Dictionary<ushort, Vector3> ScaleVelocities = new();
        internal readonly Dictionary<ushort, float> InitialValues = new();
        internal bool DaylightColored;

        public float Age { get; internal set; }
        public float MaxAge { get; internal set; }
        public Vector3 InitialPosition { get; internal set; }
        public Vector3 Position { get; internal set; }
        public Vector3 Rotation { get; internal set; }
        public Vector3 Scale { get; internal set; }

        /// <summary>
        /// Particle color, half-range (0.5 = 0x80 neutral).
        /// </summary>
        public Vector4 Color { get; internal set; } = Vector4.One;

        /// <summary>
        /// Per-frame multiplier from distance fades, clock alpha and daylight tint; reset every update.
        /// </summary>
        public Vector4 ColorMultiplier { get; internal set; } = Vector4.One;

        public Vector2 TexCoordTranslate { get; internal set; }

        public bool IsExpired => Age >= MaxAge;

        public float Progress => float.IsPositiveInfinity(MaxAge) || MaxAge <= 0f ? 0f : Math.Clamp(Age / MaxAge, 0f, 1f);

        /// <summary>
        /// Offset from the generator base in raw DAT space.
        /// </summary>
        public Vector3 LocalOffset => InitialPosition + Position;

        /// <summary>
        /// The texture factor handed to the particle shader: color times the frame's multiplier.
        /// </summary>
        public Vector4 TextureFactor => Color * ColorMultiplier;
    }

    /// <summary>
    /// Clean-room runtime for one zone-anchored particle generator (shoreline surf, wave crests), auto-running or started
    /// by a looping ambient routine:
    /// emits particles at the authored cadence and advances each through its life with the generator's
    /// Section 2 initializers and Section 3 updaters, driven by the 60 Hz effect clock.
    /// Supported opcodes cover what zone water effects use: velocity, acceleration, rotation and scale velocity,
    /// progress curves for position/rotation/scale/color/UV, constant UV scroll, clock color and alpha, draw-distance
    /// fade, daylight tint and the repeat expiration handler. Other opcodes are ignored.
    /// Generator semantics referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer,
    /// ui/js/particle/runtime.js, ops/initializers.js, ops/updaters.js and ops/generator.js, after xim).
    /// </summary>
    public sealed class ZoneParticleEmitter
    {
        private const float MaxStepFrames = 4.0f;
        private readonly Random _random;
        private float _framesUntilNextParticle;
        private int _totalEmitted;
        private float _routineClock = -1.0f;
        private bool _armed;
        private float _emitLifeTime;
        private float _maxEmitTime;
        private int _emittedSinceArm;

        public ZoneParticleEmitter(ZoneEmitterTemplate template, int seed = 0)
        {
            Template = template ?? throw new ArgumentNullException(nameof(template));
            _random = new Random(seed);
        }

        public ZoneEmitterTemplate Template { get; }

        public List<ZoneParticle> Particles { get; } = new();

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
            for (int i = 0; i < Particles.Count; i++)
            {
                UpdateParticle(Particles[i], frames, frame);
            }
            Particles.RemoveAll(p => p.IsExpired);

            AdvanceSchedule(frames);
            _emitLifeTime += frames;
            if (IsDoneEmitting()) return;

            bool culled = Def.MaxEmitDistance > 0.0f &&
                          Vector3.Distance(frame.CameraRawPosition, Template.RawBasePosition) > Def.MaxEmitDistance;
            if (culled) return;

            _framesUntilNextParticle -= frames;
            while (_framesUntilNextParticle <= 0.0f)
            {
                // A continuous singleton keeps exactly one particle alive.
                if (Def.ContinuousSingleton && Particles.Count > 0) break;

                _framesUntilNextParticle += Def.FramesPerEmission + PosRand(Def.EmissionVariance);
                int count = Def.ContinuousSingleton ? 1 : Def.ParticlesPerEmission + 1;
                for (int i = 0; i < count; i++)
                {
                    Particles.Add(CreateParticle());
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

        private void Arm(int duration)
        {
            _armed = true;
            _emitLifeTime = 0.0f;
            _maxEmitTime = duration;
            _emittedSinceArm = 0;
            _framesUntilNextParticle = 0.0f;
        }

        private ZoneParticle CreateParticle()
        {
            var particle = new ZoneParticle { Scale = Vector3.Zero };
            foreach (var op in Def.Initializers)
            {
                switch (op.OpCode)
                {
                    case 0x01: // StandardParticleSetup: life span (0 = forever) plus variance
                    {
                        var setup = Def.Setup;
                        int life = setup?.MaxLifeSpan ?? 0;
                        particle.MaxAge = life == 0 ? float.PositiveInfinity : life + PosRand(setup?.LifeSpanVariance ?? 0);
                        break;
                    }
                    case 0x02: // TranslationVelocitySetup
                        particle.Velocities[op.Allocation] = op.Vector(0);
                        break;
                    case 0x09: // RotationInitializer
                        particle.Rotation = op.Vector(0);
                        break;
                    case 0x0B: // RotationVelocitySetup
                        particle.RotationVelocities[op.Allocation] = op.Vector(0);
                        break;
                    case 0x0F: // ScaleInitializer
                        particle.Scale = op.Vector(0);
                        break;
                    case 0x10: // ScaleVarianceInitializer
                    {
                        var v = op.Vector(0);
                        particle.Scale += new Vector3(v.X * PosRand(1f), v.Y * PosRand(1f), v.Z * PosRand(1f));
                        break;
                    }
                    case 0x12: // ScaleVelocitySetup
                        particle.ScaleVelocities[op.Allocation] = op.Vector(0);
                        break;
                    case 0x16: // ColorSetup: RGBA bytes
                    {
                        uint rgba = op.Args.Length > 0 ? op.Args[0] : 0;
                        particle.Color = new Vector4(
                            (rgba & 0xFF) / 255f, ((rgba >> 8) & 0xFF) / 255f, ((rgba >> 16) & 0xFF) / 255f, (rgba >> 24) / 255f);
                        break;
                    }
                    case 0x91: // DaylightBasedColorSetup
                        particle.DaylightColored = true;
                        break;
                }
            }
            return particle;
        }

        private void UpdateParticle(ZoneParticle particle, float frames, in ZoneParticleFrame frame)
        {
            if (particle.IsExpired) return;

            particle.Age += frames;
            if (particle.IsExpired)
            {
                // 0x05 repeat: the particle loops instead of dying.
                if (Def.ExpirationHandlers.Contains(0x05)) particle.Age = 1e-7f;
                else return;
            }

            particle.ColorMultiplier = Vector4.One;
            foreach (var op in Def.Updaters)
            {
                ApplyUpdater(particle, op, frames, frame);
            }
        }

        private void ApplyUpdater(ZoneParticle p, ParticleOpcode op, float frames, in ZoneParticleFrame frame)
        {
            ushort slot = op.Allocation;
            switch (op.OpCode)
            {
                case 0x02: // PositionUpdater
                    if (p.Velocities.TryGetValue(slot, out var velocity)) p.Position += velocity * frames;
                    break;
                case 0x03: // VelocityAccelerator
                    if (p.Velocities.TryGetValue(slot, out var v)) p.Velocities[slot] = v + op.Vector(0) * frames;
                    break;
                case 0x05: // RotationUpdater
                    if (p.RotationVelocities.TryGetValue(slot, out var rv)) p.Rotation += rv * frames;
                    break;
                case 0x08: // ScaleUpdater
                    if (p.ScaleVelocities.TryGetValue(slot, out var sv)) p.Scale += sv * frames;
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

                case 0x27: // TextureCoordinateUpdater (U)
                    p.TexCoordTranslate += new Vector2(op.Float(0) * frames, 0f);
                    break;
                case 0x28: // TextureCoordinateUpdater (V)
                    p.TexCoordTranslate += new Vector2(0f, op.Float(0) * frames);
                    break;

                case 0x2E: // DrawDistanceUpdater: fade by distance to the particle
                {
                    float distance = Vector3.Distance(frame.CameraRawPosition, Template.RawBasePosition + p.LocalOffset);
                    p.ColorMultiplier = p.ColorMultiplier with { W = p.ColorMultiplier.W * FallOff(distance, op.Float(0), op.Float(1)) };
                    break;
                }

                case 0x3C: Clock(slot, frame.DayFraction, r => p.Color = p.Color with { X = r }); break;
                case 0x3D: Clock(slot, frame.DayFraction, g => p.Color = p.Color with { Y = g }); break;
                case 0x3E: Clock(slot, frame.DayFraction, b => p.Color = p.Color with { Z = b }); break;
                case 0x3F: Clock(slot, frame.DayFraction, a => p.ColorMultiplier = p.ColorMultiplier with { W = p.ColorMultiplier.W * a }); break;

                case 0x69: // DaylightBasedColorApplier
                    if (p.DaylightColored)
                    {
                        var d = frame.DaylightColor;
                        p.ColorMultiplier = new Vector4(p.ColorMultiplier.X * d.X, p.ColorMultiplier.Y * d.Y, p.ColorMultiplier.Z * d.Z, p.ColorMultiplier.W);
                    }
                    break;
            }
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

        private float PosRand(float max) => max <= 0f ? 0f : (float)_random.NextDouble() * max;
    }
}
