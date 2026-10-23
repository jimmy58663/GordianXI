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
    /// Picks the step sound for each foot landing (<see cref="FootfallDetector"/>, the footfall hook) from the ground's
    /// collision terrain.
    /// </summary>
    /// <remarks>
    /// <para>Sound choice referenced from xi-tools <c>docs/sounds/footsteps.md</c> (https://github.com/vekien/xi-tools): the
    /// zone's <c>fses</c> pointer named <c>0&lt;terrain&gt;&lt;move&gt;&lt;shake+1&gt;</c>, the terrain from the
    /// collision triangle under the actor (<see cref="CollisionTriangle.Terrain"/>), and the move / shake digits from the
    /// footwear's <c>info</c> section. In the rain the zone's <c>fser</c> set plays instead (#342,
    /// <see cref="ZoneSoundTable.IsWetWeather"/>); walking and running use the same pointer.</para>
    /// <para>The footwear digits come from <see cref="FootwearInfo"/> through the caller's resolver; an unknown footwear
    /// uses <see cref="FootwearInfo.Default"/>.</para>
    /// </remarks>
    public sealed class FootstepTracker
    {
        /// <summary>Actors further than this from the listener are not tracked (provisional).</summary>
        public const float HearingRange = 30f;

        private readonly FootfallDetector _footfalls = new();

        /// <summary>Forgets every actor (the heard character changed: its world's actors are other objects).</summary>
        public void Clear() => _footfalls.Clear();

        /// <summary>Checks every actor near the listener and adds a footstep for each landing this frame.</summary>
        /// <param name="wet">The ground is wet (rain): the zone's <c>fser</c> steps play.</param>
        public void Update(IEnumerable<WorldEntity> entities, uint localPlayerId, Vector3 listener, ZoneCollisionMesh? collision,
            ZoneSoundTable sounds, List<FootstepEvent> into, Func<WorldEntity, FootwearInfo>? footwear = null, bool wet = false)
        {
            if (sounds.DrySteps.Count == 0)
            {
                _footfalls.Clear();
                return;
            }

            foreach (WorldEntity entity in entities)
            {
                if (!entity.IsDrawn || Vector3.DistanceSquared(entity.Position, listener) > HearingRange * HearingRange ||
                    !_footfalls.Step(entity))
                {
                    continue;
                }

                int terrain = (int)CollisionTerrain.Object;
                if (collision is not null && collision.TryGetGround(entity.Position, 1.0f, 2.0f, out GroundHit ground))
                {
                    terrain = (int)ground.Terrain;
                }

                FootwearInfo feet = footwear?.Invoke(entity) ?? FootwearInfo.Default;
                int sound = sounds.FootstepSound(terrain, feet.MovementChar, feet.ShakeFactor, wet);
                if (sound != 0)
                {
                    into.Add(new FootstepEvent(sound, entity.Position, entity.ServerId == localPlayerId));
                }
            }

            _footfalls.EndFrame();
        }
    }
}
