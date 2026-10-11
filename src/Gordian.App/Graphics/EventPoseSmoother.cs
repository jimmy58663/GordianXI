// src/Gordian.App/Graphics/EventPoseSmoother.cs
using System;
using System.Numerics;
using Gordian.Core.World;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws an event-posed entity smoothly: the event VM moves its pose once per game tick (irregular, 16-31 ms), which
    /// drawn as is makes walks stutter and turns snap. A walking pose is followed at its walk speed (a little faster to
    /// catch up), a turn eases toward the new heading at <see cref="TurnRate"/>; a placement (a jump farther than a walk
    /// covers in half a second, or any move while standing) is taken at once.
    /// </summary>
    public sealed class EventPoseSmoother
    {
        /// <summary>How fast a turn closes its remaining angle, per second (exponential; about 95% in 0.4 s).</summary>
        public const float TurnRate = 8f;

        /// <summary>How much faster than its walk speed the drawing may move to catch up with the pose.</summary>
        public const float CatchUp = 1.25f;

        private float _walkSpeed;

        public EventPoseSmoother(Vector3 position, float heading)
        {
            Position = position;
            Heading = heading;
        }

        /// <summary>
        /// How far (yalms, on the ground plane) an event's first pose may lie from where the entity is drawn and still count
        /// as the entity staying put: nearer, the first pose's heading is eased into from the drawn heading; farther, it is a
        /// placement and taken at once. Matches <c>EventDialogController.EventReturnSnapDistance</c>.
        /// </summary>
        public const float PlacementDistance = 1.5f;

        /// <summary>
        /// The smoother for an entity's first event pose. The position is taken at once. The heading starts from the one the
        /// entity is drawn with when the pose leaves it in place, so an NPC turning to face the player at the start of a talk
        /// (0x1E / 0x4A in the event's first tick) eases round at <see cref="TurnRate"/> like any other event turn instead of
        /// snapping; a pose that places the entity elsewhere takes its heading at once too.
        /// </summary>
        public static EventPoseSmoother Start(Vector3 drawnPosition, float drawnHeading, EventPose pose)
        {
            float dx = pose.Position.X - drawnPosition.X, dz = pose.Position.Z - drawnPosition.Z;
            bool placed = dx * dx + dz * dz > PlacementDistance * PlacementDistance;
            return new EventPoseSmoother(pose.Position, placed ? pose.Heading : drawnHeading);
        }

        public Vector3 Position { get; private set; }

        public float Heading { get; private set; }

        /// <summary>
        /// Moves the drawing toward <paramref name="pose"/>. A <paramref name="turnSpeed"/> the event set (0x59 sub 0 / 1,
        /// 4096ths of a turn per 60 Hz frame, provisional) turns at that constant rate instead of the ease.
        /// </summary>
        public void Advance(EventPose pose, float deltaSeconds, int turnSpeed = 0)
        {
            float dt = Math.Max(0f, deltaSeconds);
            if (pose.Speed > 0) _walkSpeed = pose.Speed;
            var toPose = pose.Position - Position;
            float distance = toPose.Length();
            float reach = Math.Max(1.5f, _walkSpeed * 0.5f);
            if (_walkSpeed <= 0 || distance > reach)
            {
                Position = pose.Position;
                if (pose.Speed <= 0) _walkSpeed = 0;
            }
            else
            {
                float step = _walkSpeed * CatchUp * dt;
                Position = step >= distance ? pose.Position : Position + toPose * (step / distance);
                if (step >= distance && pose.Speed <= 0) _walkSpeed = 0;
            }

            float turn = pose.Heading - Heading;
            while (turn > MathF.PI) turn -= 2f * MathF.PI;
            while (turn < -MathF.PI) turn += 2f * MathF.PI;
            if (turnSpeed > 0)
            {
                float step = turnSpeed * (2f * MathF.PI / 4096f) * 60f * dt;
                Heading += Math.Clamp(turn, -step, step);
            }
            else
            {
                Heading += turn * Math.Min(1f, dt * TurnRate);
            }
        }
    }
}
