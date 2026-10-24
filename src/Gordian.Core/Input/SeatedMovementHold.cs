// src/Gordian.Core/Input/SeatedMovementHold.cs
using Gordian.Core.Animation;

namespace Gordian.Engine.Input
{
    /// <summary>
    /// Keeps the player rooted while resting or sitting and while standing up (#343 round 1, the maintainer's retail check):
    /// moving out of /heal or /sit first ends it on the server, the stand-up clip plays where the character sits, and only
    /// when it ends does the character move. Rooted means no movement and no turn, so the position reported to the server
    /// does not change.
    /// <list type="number">
    /// <item>While the server status is seated (33 Healing, 47 Sit, <see cref="RestPosture.CategoryFor"/>).</item>
    /// <item>After it leaves, while the drawn character is still seated (the renderer has not caught up yet) or standing up
    /// (<see cref="EntityAnimationState.IsStandingUp"/>), for at most <see cref="MaxStandUpSeconds"/>: an entity that is not
    /// drawn (culled, no model) never plays the clip.</item>
    /// </list>
    /// </summary>
    public sealed class SeatedMovementHold
    {
        /// <summary>
        /// The longest the stand-up may hold the player after the status leaves the seated posture: past the longest retail
        /// stand-up (<c>si2</c>, 120 ticks = 2.0 s; <c>rx2</c> is 0.93 s) with room for the status update's arrival.
        /// </summary>
        public const double MaxStandUpSeconds = 3.0;

        private bool _wasSeated;
        private double _leftSeatAt = -1.0;

        /// <summary>Whether the player is rooted now (<paramref name="nowSeconds"/>: any monotonic clock in seconds).</summary>
        public bool IsRooted(byte serverStatus, EntityAnimationState animation, double nowSeconds)
        {
            if (RestPosture.CategoryFor(serverStatus) != null)
            {
                _wasSeated = true;
                _leftSeatAt = -1.0;
                return true;
            }
            if (!_wasSeated) return false;

            if (_leftSeatAt < 0.0) _leftSeatAt = nowSeconds;
            bool stillDown = animation.IsStandingUp || RestPosture.IsSeated(animation.Current);
            if (stillDown && nowSeconds - _leftSeatAt < MaxStandUpSeconds) return true;

            _wasSeated = false;
            _leftSeatAt = -1.0;
            return false;
        }
    }
}
