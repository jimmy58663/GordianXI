// src/Gordian.Engine/Animation/ChairTransitions.cs
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Gordian.Core.Animation;
using Gordian.Core.World;

namespace Gordian.Engine.Animation
{
    /// <summary>The chair drawn under a player this frame (#379): which chair (0-10, -1 for none) and how opaque it is.</summary>
    public readonly record struct ChairFrame(int Chair, float Alpha)
    {
        /// <summary>No chair.</summary>
        public static readonly ChairFrame None = new(-1, 0f);

        /// <summary>Whether a chair is drawn.</summary>
        public bool IsShown => Chair >= 0 && Alpha > 0f;
    }

    /// <summary>
    /// The chair <c>/sitchair</c> draws under a player (#379): shown while the server status is a chair status (63-73,
    /// <see cref="RestPosture.ChairFor"/>), fading in as the character sits and out as it stands. Each chair model's own
    /// routines time it: <c>init</c> fades it in over 86 ticks and <c>chi2</c> fades it out over 78 (op 0x29, the op a mount's
    /// <c>coff</c> fades it with, operands 0x56 and 0x4E; read from the retail chair DATs 100507-100517, 2026-11-13).
    /// *Inference*: the operands are read as fade lengths in 60 Hz ticks, as for the mounts; no reference names the op.
    /// A player first seen already seated shows its chair at once. Timed in wall time and shared by every viewport.
    /// </summary>
    public static class ChairTransitions
    {
        /// <summary>The chair's <c>init</c> fade-in, in 60 Hz ticks.</summary>
        public const int FadeInTicks = 86;

        /// <summary>The chair's <c>chi2</c> fade-out, in 60 Hz ticks.</summary>
        public const int FadeOutTicks = 78;

        private sealed class State
        {
            public int Chair = -1;
            public long Start;
            public bool Leaving;
        }

        private static readonly ConditionalWeakTable<WorldEntity, State> States = new();

        /// <summary>The chair to draw for <paramref name="entity"/> this frame.</summary>
        public static ChairFrame Observe(WorldEntity entity) => Observe(entity, Stopwatch.GetTimestamp());

        internal static ChairFrame Observe(WorldEntity entity, long now)
        {
            ArgumentNullException.ThrowIfNull(entity);
            int chair = RestPosture.ChairFor(entity.AnimationState);
            if (!States.TryGetValue(entity, out var state))
            {
                // First seen seated: the chair is already there.
                States.AddOrUpdate(entity, new State { Chair = chair, Start = now - TicksToTimestamp(FadeInTicks) });
                return chair >= 0 ? new ChairFrame(chair, 1f) : ChairFrame.None;
            }

            lock (state)
            {
                if (chair >= 0 && (chair != state.Chair || state.Leaving))
                {
                    state.Chair = chair;
                    state.Leaving = false;
                    state.Start = now;
                }
                else if (chair < 0 && state.Chair >= 0 && !state.Leaving)
                {
                    state.Leaving = true;
                    state.Start = now;
                }

                if (state.Chair < 0) return ChairFrame.None;
                float ticks = (float)((now - state.Start) / (double)Stopwatch.Frequency * EntityAnimationState.RoutineTicksPerSecond);
                if (!state.Leaving) return new ChairFrame(state.Chair, Math.Clamp(ticks / FadeInTicks, 0f, 1f));
                float alpha = 1f - Math.Clamp(ticks / FadeOutTicks, 0f, 1f);
                if (alpha > 0f) return new ChairFrame(state.Chair, alpha);
                state.Chair = -1;
                state.Leaving = false;
                return ChairFrame.None;
            }
        }

        private static long TicksToTimestamp(int ticks) => (long)(ticks / EntityAnimationState.RoutineTicksPerSecond * Stopwatch.Frequency);
    }
}
