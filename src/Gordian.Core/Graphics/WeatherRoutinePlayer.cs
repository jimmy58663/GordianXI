// src/Gordian.Core/Graphics/WeatherRoutinePlayer.cs
using System;
using System.Collections.Generic;

namespace Gordian.Core.Graphics
{
    /// <summary>
    /// One generator start in a weather routine: the generator, when in the routine it starts, and how long it emits.
    /// </summary>
    public readonly record struct WeatherRoutineSpawn(ZoneEmitterTemplate Template, int StartFrame, int Duration);

    /// <summary>
    /// One routine (Section 0x07) of a weather routine group, e.g. a single lightning-strike variant.
    /// </summary>
    public sealed record WeatherRoutineVariant(string DatId, int TotalFrames, IReadOnlyList<WeatherRoutineSpawn> Spawns);

    /// <summary>
    /// The short routines of one weather directory (e.g. <c>thdr/kmi1</c>: strikes <c>s000</c>..<c>s003</c>), of which
    /// the client plays one at a time.
    /// </summary>
    public sealed class WeatherRoutineGroup
    {
        public WeatherRoutineGroup(string weatherId, string directory)
        {
            WeatherId = weatherId;
            Directory = directory;
        }

        public string WeatherId { get; }

        public string Directory { get; }

        public List<WeatherRoutineVariant> Routines { get; } = new();
    }

    /// <summary>
    /// Plays short weather routines (lightning strikes) while their weather is active: each group plays one randomly
    /// chosen routine, waits for it to finish plus a random gap, then plays another. The client's strike selection and
    /// timing are not documented in any available reference, so the gap is an estimate (<see cref="MinGapFrames"/>,
    /// <see cref="MaxGapFrames"/>) to be calibrated against retail.
    /// </summary>
    public sealed class WeatherRoutinePlayer
    {
        private readonly IReadOnlyList<WeatherRoutineGroup> _groups;
        private readonly float[] _framesUntilNext;
        private readonly Random _random;
        private string? _weatherId;

        public WeatherRoutinePlayer(IReadOnlyList<WeatherRoutineGroup> groups, int seed = 0)
        {
            _groups = groups ?? throw new ArgumentNullException(nameof(groups));
            _framesUntilNext = new float[groups.Count];
            _random = new Random(seed);
        }

        /// <summary>
        /// Shortest pause between the end of one routine in a group and the start of the next, in 60 Hz frames.
        /// </summary>
        public int MinGapFrames { get; set; } = 240;

        /// <summary>
        /// Longest pause between routines in a group, in 60 Hz frames.
        /// </summary>
        public int MaxGapFrames { get; set; } = 900;

        /// <summary>
        /// Advances every group of the active weather, triggering the chosen routine's generators through
        /// <paramref name="resolve"/>. A weather change restarts the wait before the first routine.
        /// </summary>
        public void Update(float frames, string weatherId, Func<ZoneEmitterTemplate, ZoneParticleEmitter?> resolve)
        {
            if (!string.Equals(weatherId, _weatherId, StringComparison.OrdinalIgnoreCase))
            {
                _weatherId = weatherId;
                for (int i = 0; i < _framesUntilNext.Length; i++) _framesUntilNext[i] = Gap();
            }

            for (int i = 0; i < _groups.Count; i++)
            {
                var group = _groups[i];
                if (group.Routines.Count == 0 || !string.Equals(group.WeatherId, weatherId, StringComparison.OrdinalIgnoreCase)) continue;

                _framesUntilNext[i] -= frames;
                if (_framesUntilNext[i] > 0f) continue;

                var routine = group.Routines[_random.Next(group.Routines.Count)];
                foreach (var spawn in routine.Spawns)
                {
                    resolve(spawn.Template)?.Trigger(spawn.StartFrame, spawn.Duration);
                }
                _framesUntilNext[i] = routine.TotalFrames + Gap();
            }
        }

        private float Gap() => MinGapFrames + (float)_random.NextDouble() * Math.Max(0, MaxGapFrames - MinGapFrames);
    }
}
