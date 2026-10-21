// src/Gordian.Core/World/ActorColorTrack.cs
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gordian.Core.World
{
    /// <summary>
    /// The colour an entity's routines draw it with (#206, #311): the routine command 0x29 moves it toward a B, G, R, A
    /// colour over a duration (<see cref="Resources.Events.SceneCommandKind.ActorColor"/>), 0x2E holds the change for a
    /// while and then lets the entity be drawn as is (<see cref="Resources.Events.SceneCommandKind.ActorHold"/>). Values are
    /// multipliers, 1 = as drawn (the routines' half-scale 0x80). Thread-safe: the network thread and the event schedule,
    /// the renderer samples.
    /// <para>
    /// A fade starts from the colour on the entity when it starts and goes linearly to its target; a later fade takes over
    /// from wherever the earlier one had got to (the easing is not known; *inference*, as for the scene fades).
    /// </para>
    /// </summary>
    public sealed class ActorColorTrack
    {
        private readonly record struct Fade(double Start, double Duration, Vector4 Target);

        private readonly object _gate = new();
        private readonly List<Fade> _fades = new();
        private double _holdUntil = double.NaN;

        /// <summary>The colour of an entity drawn as is.</summary>
        public static readonly Vector4 AsDrawn = Vector4.One;

        /// <summary>Whether anything is scheduled (the renderer skips the sample otherwise).</summary>
        public bool IsActive { get { lock (_gate) return _fades.Count > 0; } }

        /// <summary>The multiplier of a routine colour <c>B G R A</c> from the low byte (0x80 = 1).</summary>
        public static Vector4 ColorOf(uint bgra) =>
            new(((bgra >> 16) & 0xFF) / 128f, ((bgra >> 8) & 0xFF) / 128f, (bgra & 0xFF) / 128f, ((bgra >> 24) & 0xFF) / 128f);

        /// <summary>Schedules a fade to <paramref name="target"/> (R, G, B, A) from <paramref name="start"/> over <paramref name="duration"/> seconds.</summary>
        public void FadeTo(double start, double duration, Vector4 target)
        {
            lock (_gate)
            {
                int at = _fades.Count;
                while (at > 0 && _fades[at - 1].Start > start) at--;
                _fades.Insert(at, new Fade(start, Math.Max(0.0, duration), target));
            }
        }

        /// <summary>
        /// Holds the colour changes until <paramref name="until"/> (seconds): then every change is dropped and the entity is
        /// drawn as is. A later hold extends it.
        /// </summary>
        public void HoldUntil(double until)
        {
            lock (_gate)
            {
                if (double.IsNaN(_holdUntil) || until > _holdUntil) _holdUntil = until;
            }
        }

        /// <summary>
        /// Schedules a routine's colour commands from <paramref name="startSeconds"/>: each 0x29 a fade, each 0x2E a hold
        /// to its end. Returns whether the routine had any.
        /// </summary>
        public bool Schedule(Resources.Events.SceneRoutine routine, double startSeconds)
        {
            ArgumentNullException.ThrowIfNull(routine);
            bool any = false;
            foreach (var command in routine.Commands)
            {
                double start = startSeconds + command.StartFrame / FramesPerSecond;
                switch (command.Kind)
                {
                    case Resources.Events.SceneCommandKind.ActorColor:
                        FadeTo(start, command.Duration / FramesPerSecond, ColorOf(command.Color));
                        any = true;
                        break;
                    case Resources.Events.SceneCommandKind.ActorHold:
                        HoldUntil(start + command.Duration / FramesPerSecond);
                        any = true;
                        break;
                }
            }
            return any;
        }

        /// <summary>Routine frames per second.</summary>
        public const double FramesPerSecond = 60.0;

        /// <summary>The clock the tracks are scheduled and sampled on, in seconds.</summary>
        public static double Now => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

        /// <summary>Drops every change (a new spawn, the end of an event).</summary>
        public void Reset()
        {
            lock (_gate)
            {
                _fades.Clear();
                _holdUntil = double.NaN;
            }
        }

        /// <summary>The colour multiplier (R, G, B, A) at <paramref name="now"/> seconds.</summary>
        public Vector4 Sample(double now)
        {
            lock (_gate)
            {
                if (!double.IsNaN(_holdUntil) && now >= _holdUntil)
                {
                    _fades.Clear();
                    _holdUntil = double.NaN;
                }
                var color = AsDrawn;
                for (int i = 0; i < _fades.Count; i++)
                {
                    var fade = _fades[i];
                    if (fade.Start > now) break;
                    // The fade runs until the next one starts (or now).
                    double end = i + 1 < _fades.Count && _fades[i + 1].Start <= now ? _fades[i + 1].Start : now;
                    float t = fade.Duration <= 0 ? 1f : (float)Math.Clamp((end - fade.Start) / fade.Duration, 0.0, 1.0);
                    color = Vector4.Lerp(color, fade.Target, t);
                }
                return color;
            }
        }
    }
}
