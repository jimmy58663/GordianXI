// src/Gordian.Engine/Input/LocomotionTick.cs
using System.Numerics;

namespace Gordian.Engine.Input
{
    /// <summary>
    /// One finished locomotion tick, as <see cref="PlayerLocomotionController.LastTick"/> publishes it for the renderer
    /// (#547): every value is from the same tick.
    /// </summary>
    /// <param name="Timestamp"><see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> when the tick ran.</param>
    /// <param name="PlayerServerId">The local player's entity (0: none yet).</param>
    /// <param name="PlayerPosition">The player's position (server coordinates).</param>
    /// <param name="PlayerHeadingRadians">The player's heading, wire convention, to full precision (not the wire byte).</param>
    /// <param name="CameraYaw">The orbit yaw, degrees.</param>
    /// <param name="CameraPitch">The orbit pitch, degrees.</param>
    /// <param name="CameraDistance">The distance drawn with (lock-on zoom included).</param>
    public sealed record LocomotionTick(
        long Timestamp,
        uint PlayerServerId,
        Vector3 PlayerPosition,
        float PlayerHeadingRadians,
        float CameraYaw,
        float CameraPitch,
        float CameraDistance);
}
