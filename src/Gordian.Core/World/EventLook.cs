// src/Gordian.Core/World/EventLook.cs
namespace Gordian.Core.World
{
    /// <summary>
    /// Whom an entity looks at during an event (XiEvents "Event VM Functions.md", XiEvent::lookatone, called by opcodes
    /// 0x1E, 0x4A and 0x79): retail sets the entity's <c>InteractionTargetIndex</c>, a <c>Render.Flags3</c> look mode and
    /// its <c>NpcSpeechFrame</c>; 0x7B clears them. The renderer turns the entity's head toward the target. 0x79 sub 2 sets
    /// look mode 2 instead, a fixed look axis with no target (<see cref="Axis"/>, made by <see cref="Fixed"/>).
    /// </summary>
    /// <param name="TargetServerId">The entity looked at (<see cref="uint.MaxValue"/> for a fixed axis).</param>
    /// <param name="SpeechFrame">Retail's <c>NpcSpeechFrame</c> (6 from 0x1E / 0x4A / 0x79 sub 0, a work value from sub 1); kept, but not drawn: the mouth moves per spoken line instead (<see cref="Animation.FaceMotion"/>).</param>
    public sealed record EventLook(uint TargetServerId, int SpeechFrame)
    {
        /// <summary>
        /// The fixed look axis of 0x79 sub 2 (retail <c>LookAxisX</c> / <c>LookAxisY</c>, two work values), or null for a
        /// look at <see cref="TargetServerId"/>. How the renderer reads it is <see cref="Animation.HeadLook.AxisAngles"/>.
        /// </summary>
        public LookAxis? Axis { get; init; }

        /// <summary>A look along a fixed axis (0x79 sub 2).</summary>
        public static EventLook Fixed(int axisX, int axisY) => new(uint.MaxValue, -1) { Axis = new LookAxis(axisX, axisY) };
    }

    /// <summary>The two values of 0x79 sub 2 as the script gave them (retail <c>LookAxisX</c> / <c>LookAxisY</c>).</summary>
    public readonly record struct LookAxis(int X, int Y);
}
