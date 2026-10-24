// src/Gordian.Engine/Animation/FootfallDetector.cs
using System.Collections.Generic;
using Gordian.Core.Animation;
using Gordian.Core.World;

namespace Gordian.Engine.Animation
{
    /// <summary>
    /// The footfall hook (#40): finds the frames a moving actor's foot lands. The step sounds (<c>FootstepTracker</c>) and
    /// the footprints and foot effects (<c>ZoneTerrainRenderer</c>) each keep one and ask it per actor and frame, so a
    /// change to when a foot lands (a mount's gait, a new landing rule) is made here once for both.
    /// </summary>
    /// <remarks>
    /// Provisional: retail fires on the model's foot-landing state (xi-tools <c>docs/sounds/footsteps.md</c> §3, after
    /// xim), which GordianXI does not detect yet, so a foot lands at the start and at the middle of each gait clip cycle.
    /// Footfall gating referenced from xi-tools (https://github.com/vekien/xi-tools).
    /// </remarks>
    public sealed class FootfallDetector
    {
        private readonly Dictionary<uint, float> _phases = new();
        private readonly HashSet<uint> _seen = new();
        private readonly List<uint> _stale = new();

        /// <summary>Whether an animation category is a stepping gait.</summary>
        public static bool IsStepping(AnimationCategory category) => category is AnimationCategory.Walk or AnimationCategory.Run
            or AnimationCategory.CombatWalk or AnimationCategory.CombatRun or AnimationCategory.MoveBackward
            or AnimationCategory.MoveLeft or AnimationCategory.MoveRight or AnimationCategory.CombatMoveBackward
            or AnimationCategory.CombatMoveLeft or AnimationCategory.CombatMoveRight;

        /// <summary>Whether a gait is a run (diagnostics; the step sound and effects do not depend on it).</summary>
        public static bool IsRunning(AnimationCategory category) => category is AnimationCategory.Run or AnimationCategory.CombatRun;

        /// <summary>
        /// Whether a cycle phase (0-1) moving from <paramref name="previous"/> to <paramref name="current"/> crossed a
        /// foot landing (phase 0 or 0.5).
        /// </summary>
        public static bool CrossedLanding(float previous, float current)
        {
            if (previous < 0f)
            {
                return false;
            }

            if (current < previous)
            {
                // Wrapped through 0 (or restarted).
                return true;
            }

            return previous < 0.5f && current >= 0.5f;
        }

        /// <summary>
        /// Advances one actor (once per frame) and returns whether a foot landed this frame: the actor is in a stepping
        /// gait, not playing an action, and its clip's phase crossed a landing. The first frame an actor is seen never lands.
        /// </summary>
        public bool Step(WorldEntity entity)
        {
            EntityAnimationState animation = entity.Animation;
            var clip = animation.CurrentClip;
            if (!IsStepping(animation.Current) || clip is null || clip.DurationSeconds <= 0f || animation.IsPlayingAction)
            {
                return false;
            }

            uint id = entity.ServerId;
            _seen.Add(id);
            float phase = animation.ElapsedSeconds % clip.DurationSeconds / clip.DurationSeconds;
            float previous = _phases.TryGetValue(id, out float p) ? p : -1f;
            _phases[id] = phase;
            return CrossedLanding(previous, phase);
        }

        /// <summary>Forgets the actors not stepped through <see cref="Step"/> since the last call (call once per frame).</summary>
        public void EndFrame()
        {
            if (_phases.Count > _seen.Count)
            {
                _stale.Clear();
                foreach (uint id in _phases.Keys)
                {
                    if (!_seen.Contains(id))
                    {
                        _stale.Add(id);
                    }
                }

                foreach (uint id in _stale)
                {
                    _phases.Remove(id);
                }
            }

            _seen.Clear();
        }

        /// <summary>Forgets every actor (another world is shown: its actors are other objects).</summary>
        public void Clear()
        {
            _phases.Clear();
            _seen.Clear();
        }
    }
}
