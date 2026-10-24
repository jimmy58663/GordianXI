// src/Gordian.Core/Resources/Graphics/WeatherRoutines.cs
using System;
using System.Collections.Generic;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// One generator start in a routine: the generator, when in the routine it starts, and how long it emits.
    /// </summary>
    public readonly record struct WeatherRoutineSpawn(ZoneEmitterTemplate Template, int StartFrame, int Duration);

    /// <summary>
    /// One routine (Section 0x07) with the emitters it starts: a weather routine group's variant (e.g. a single
    /// lightning strike), or a routine the client plays on an actor (<see cref="ActorEffectSet"/>).
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
}
