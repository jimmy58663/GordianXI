// src/Gordian.Core/World/EventRenderFlags.cs
using System;

namespace Gordian.Core.World
{
    /// <summary>
    /// The retail render-flag bits a running event sets on an entity that GordianXI keeps without a known effect, plus
    /// the 0x81 blink switch. Retail keeps these in the entity's <c>Render.Flags0</c>-<c>Flags7</c> words; the bit
    /// positions here are GordianXI's own, the names say which retail word and bit each one stands for. The bits whose
    /// effect is known have their own <see cref="WorldEntity"/> properties instead (<see cref="WorldEntity.IsEventHidden"/>,
    /// <see cref="WorldEntity.KeepsEventHeight"/>, <see cref="WorldEntity.HidesEventName"/>). Cleared when the event ends.
    /// Event VM opcodes referenced from XiEvents (https://github.com/atom0s/XiEvents, OpCodes/0x0081, 0x0094, 0x00AB,
    /// 0x00C0); see docs/reference/entity-flags.md.
    /// </summary>
    [Flags]
    public enum EventRenderFlags : uint
    {
        None = 0,

        /// <summary><c>Render.Flags0</c> bit 1: 0xAB sub 1 sets, sub 2 clears (event entity). Effect unknown.</summary>
        Flags0Bit1 = 1 << 0,

        /// <summary>
        /// <c>Render.Flags0</c> bit 2: 0xAB sub 3 sets, sub 4 clears (event entity). Retail's door (0x4C / 0x4D) and event
        /// status opcodes (0x4F, 0x8E, 0x8F) act only while it is clear; a lock on the event status (inference).
        /// </summary>
        Flags0Bit2 = 1 << 1,

        /// <summary><c>Render.Flags0</c> bit 3: 0xAB sub 5 sets, sub 6 clears (event entity). Effect unknown.</summary>
        Flags0Bit3 = 1 << 2,

        /// <summary><c>Render.Flags0</c> bit 6: 0xAB sub 0x0B sets, 0x0C clears (event entity). Effect unknown.</summary>
        Flags0Bit6 = 1 << 3,

        /// <summary><c>Render.Flags2</c> bit 1: 0xAB sub 8 sets, sub 7 clears (event entity). Effect unknown.</summary>
        Flags2Bit1 = 1 << 4,

        /// <summary><c>Render.Flags2</c> bit 24: 0xAB sub 0x12 sets, 0x13 clears (event entity). Effect unknown.</summary>
        Flags2Bit24 = 1 << 5,

        /// <summary><c>Render.Flags3</c> bit 12: 0xC0 (event entity, from a work value). Effect unknown.</summary>
        Flags3Bit12 = 1 << 6,

        /// <summary>
        /// <c>Render.Flags3</c> bit 17: 0x94 (an actor). Effect unknown; not the name plate (Port Jeuno 324 sets it on the
        /// player, whose plate stays).
        /// </summary>
        Flags3Bit17 = 1 << 7,

        /// <summary><c>Render.Flags4</c> bit 1: 0xAB sub 0x0D sets, 0x0E clears (event entity). Effect unknown.</summary>
        Flags4Bit1 = 1 << 8,

        /// <summary>
        /// <c>Render.Flags7</c> bit 19: 0xAB sub 0x19 sets / 0x1A clears (event entity), 0x1B sets / 0x1C clears (an
        /// actor). Effect unknown.
        /// </summary>
        Flags7Bit19 = 1 << 9,

        /// <summary>
        /// The event turned the actor's eye blink off (0x81 with 0; retail clears <c>is_blinkeye</c> on the actor's
        /// skeleton, not a render word): the face does not blink until 0x81 turns it on or the event ends.
        /// </summary>
        NoBlink = 1 << 10,
    }
}
