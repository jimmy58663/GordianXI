// src/Gordian.Core/Resources/Graphics/ZoneEmitterTemplate.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Graphics;

namespace Gordian.Core.Resources.Graphics
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

        /// <summary>
        /// The Section 0x3E point lists the generator's initializers 0x54 name, by allocation slot: updater 0x34 moves the
        /// particle along one over its life (<see cref="Resources.Graphics.PointListDecoder"/>).
        /// </summary>
        public Dictionary<ushort, Vector3[]> PointLists { get; } = new();

        /// <summary>
        /// The curve of <see cref="ParticleGeneratorDefinition.EmissionClockKeyFrameId"/> (section 1 op 0x17), resolved like
        /// the slot curves: each emission makes the authored count times its value at the time of day. Null: no such op.
        /// </summary>
        public KeyFrameCurve? EmissionClockCurve { get; set; }
    }
}
