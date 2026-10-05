// src/Gordian.App/Audio/FootstepTracker.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Animation;
using Gordian.Core.Audio;
using Gordian.Core.World;
using Gordian.Core.World.Collision;

namespace Gordian.App.Audio
{
    /// <summary>A footstep to play: which sound, where.</summary>
    /// <param name="SoundId">The <c>.spw</c> id from the zone's <c>fses</c> / <c>fser</c> pointers.</param>
    /// <param name="Position">The actor's position (internal space).</param>
    /// <param name="IsLocalPlayer">Whether it is the listener's own character.</param>
    public readonly record struct FootstepEvent(int SoundId, Vector3 Position, bool IsLocalPlayer);

    /// <summary>
    /// Finds the frames a moving actor's foot lands and picks the step sound from the ground's collision terrain.
    /// </summary>
    /// <remarks>
    /// <para>Sound choice referenced from xi-tools <c>docs/sounds/footsteps.md</c> (https://github.com/vekien/xi-tools): the
    /// zone's <c>fses</c> pointer named <c>0&lt;terrain&gt;&lt;move&gt;&lt;shake+1&gt;</c>, the terrain from the
    /// collision triangle under the actor (<see cref="CollisionTriangle.Terrain"/>), and the move / shake digits from the
    /// footwear's <c>info</c> section. Running uses the zone's <c>fser</c> set (<see cref="ZoneSoundTable"/>).</para>
    /// <para>Provisional: retail fires on the animation's foot-landing state; GordianXI does not detect that yet, so a
    /// step plays at the start and the middle of each walk / run clip cycle. The footwear digits come from
    /// <see cref="FootwearInfo"/> through the caller's resolver; an unknown footwear uses <see cref="FootwearInfo.Default"/>.</para>
    /// </remarks>
    public sealed class FootstepTracker
    {
        /// <summary>Actors further than this from the listener are not tracked (provisional).</summary>
        public const float HearingRange = 30f;


        private readonly Dictionary<uint, float> _phases = new();
        private readonly HashSet<uint> _seen = new();

        /// <summary>Whether an animation category is a stepping gait.</summary>
        public static bool IsStepping(AnimationCategory category) => category is AnimationCategory.Walk or AnimationCategory.Run
            or AnimationCategory.CombatWalk or AnimationCategory.CombatRun or AnimationCategory.MoveBackward
            or AnimationCategory.MoveLeft or AnimationCategory.MoveRight or AnimationCategory.CombatMoveBackward
            or AnimationCategory.CombatMoveLeft or AnimationCategory.CombatMoveRight;

        /// <summary>Whether a gait uses the running step set.</summary>
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

        /// <summary>Checks every actor near the listener and adds a footstep for each landing this frame.</summary>
        public void Update(IEnumerable<WorldEntity> entities, uint localPlayerId, Vector3 listener, ZoneCollisionMesh? collision,
            ZoneSoundTable sounds, List<FootstepEvent> into, Func<WorldEntity, FootwearInfo>? footwear = null)
        {
            _seen.Clear();
            if (sounds.WalkSteps.Count == 0)
            {
                _phases.Clear();
                return;
            }

            foreach (WorldEntity entity in entities)
            {
                if (!entity.IsDrawn || Vector3.DistanceSquared(entity.Position, listener) > HearingRange * HearingRange)
                {
                    continue;
                }

                EntityAnimationState animation = entity.Animation;
                var clip = animation.CurrentClip;
                if (!IsStepping(animation.Current) || clip is null || clip.DurationSeconds <= 0f || animation.IsPlayingAction)
                {
                    continue;
                }

                uint id = entity.ServerId;
                _seen.Add(id);
                float phase = animation.ElapsedSeconds % clip.DurationSeconds / clip.DurationSeconds;
                float previous = _phases.TryGetValue(id, out float p) ? p : -1f;
                _phases[id] = phase;
                if (!CrossedLanding(previous, phase))
                {
                    continue;
                }

                int terrain = (int)CollisionTerrain.Object;
                if (collision is not null && collision.TryGetGround(entity.Position, 1.0f, 2.0f, out GroundHit ground))
                {
                    terrain = (int)ground.Terrain;
                }

                FootwearInfo feet = footwear?.Invoke(entity) ?? FootwearInfo.Default;
                int sound = sounds.FootstepSound(terrain, feet.MovementChar, feet.ShakeFactor, IsRunning(animation.Current));
                if (sound != 0)
                {
                    into.Add(new FootstepEvent(sound, entity.Position, id == localPlayerId));
                }
            }

            if (_phases.Count > _seen.Count)
            {
                var stale = new List<uint>();
                foreach (uint id in _phases.Keys)
                {
                    if (!_seen.Contains(id))
                    {
                        stale.Add(id);
                    }
                }

                foreach (uint id in stale)
                {
                    _phases.Remove(id);
                }
            }
        }
    }
}
