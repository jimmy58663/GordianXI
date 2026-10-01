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

        public Vector3 Position { get; private set; }

        public float Heading { get; private set; }

        public void Advance(EventPose pose, float deltaSeconds)
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
            Heading += turn * Math.Min(1f, dt * TurnRate);
        }
    }
}
