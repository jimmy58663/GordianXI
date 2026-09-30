// src/Gordian.Core/World/EventPose.cs
using System.Numerics;

namespace Gordian.Core.World
{
    /// <summary>
    /// An entity's place during an event (cutscene): the event VM's <c>EventPos</c> / <c>EventDir</c> for that entity.
    /// </summary>
    /// <param name="Position">Game units, internal axes (X, Y = height, Z), like <see cref="WorldEntity.Position"/>.</param>
    /// <param name="Heading">Radians in the wire convention (<see cref="WorldEntity.HeadingOf"/>): 0 = East, π/2 = South.</param>
    /// <param name="Speed">Yalms per second while the script walks the entity (opcodes 0x1F / 0x5A), 0 while it stands.</param>
    public sealed record EventPose(Vector3 Position, float Heading, float Speed)
    {
        /// <summary>The walk / run split of the classifier (base run 5.0 yalms/s, walking at half of it or slower).</summary>
        public const float WalkSpeedLimit = 2.5f;
    }
}
