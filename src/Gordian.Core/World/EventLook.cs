// src/Gordian.Core/World/EventLook.cs
namespace Gordian.Core.World
{
    /// <summary>
    /// Whom an entity looks at during an event (XiEvents "Event VM Functions.md", XiEvent::lookatone, called by opcodes
    /// 0x1E, 0x4A and 0x79): retail sets the entity's <c>InteractionTargetIndex</c>, a <c>Render.Flags3</c> look mode and
    /// its <c>NpcSpeechFrame</c>; 0x7B clears them. The renderer turns the entity's head toward the target.
    /// </summary>
    /// <param name="TargetServerId">The entity looked at.</param>
    /// <param name="SpeechFrame">Retail's <c>NpcSpeechFrame</c> (6 from 0x1E / 0x4A / 0x79 sub 0, a work value from sub 1); while it is 0 or more the actor talks (<see cref="Animation.FaceMotion"/>).</param>
    public sealed record EventLook(uint TargetServerId, int SpeechFrame);
}
