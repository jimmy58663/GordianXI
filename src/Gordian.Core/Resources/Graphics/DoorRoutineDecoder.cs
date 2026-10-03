// src/Gordian.Core/Resources/Graphics/DoorRoutineDecoder.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>How a door routine command moves a part.</summary>
    public enum DoorMoveKind : byte
    {
        /// <summary>Routine op 0x0C: slide the part to an offset from its placement (local axes).</summary>
        Slide,

        /// <summary>Routine op 0x0D: turn the part to an angle about its placement origin (local axes, radians).</summary>
        Rotate,
    }

    /// <summary>
    /// One part move of a door routine: from <see cref="StartFrame"/> the part goes to the absolute <see cref="Target"/>
    /// over <see cref="Duration"/> frames (60 Hz). <see cref="Part"/> is the index of the door's leaf.
    /// </summary>
    public readonly record struct DoorMove(DoorMoveKind Kind, int StartFrame, int Duration, Vector3 Target, int Part);

    /// <summary>A decoded door routine (<c>open</c>, <c>clos</c>, <c>into</c>, <c>intc</c>): its part moves and length.</summary>
    public sealed class DoorRoutine
    {
        public static readonly DoorRoutine Empty = new();

        /// <summary>The routine's length in 60 Hz frames.</summary>
        public int TotalFrames { get; init; }

        public IReadOnlyList<DoorMove> Moves { get; init; } = Array.Empty<DoorMove>();
    }

    /// <summary>
    /// Clean-room decoder for the part moves of a zone door's Section 0x07 routines, found under
    /// <c>&lt;zone code&gt;/door/&lt;door id&gt;/</c> in the zone DAT. The command list is the ordinary routine layout
    /// (<see cref="EffectRoutineDecoder"/>): { u8 op, u16 size in dwords (low 5 bits), u8, u16 delay, u16 duration }, then
    /// the body; a command starts at the sum of the delays before it. Op 0x0D (rotate) and op 0x0C (slide) carry x, y, z
    /// (f32: radians or an offset) and the part index (u32); the targets are absolute, so <c>clos</c> turns back to 0.
    /// The other ops (sounds 0x0B, routine links 0x03, generators 0x02, ...) are not decoded here.
    /// Format referenced from xi-tools (https://github.com/vekien/xi-tools, docs/zone/doors.md, after the PS2 client's
    /// <c>XiDoorActor</c>); the part index operand was read back from Metalworks (zone 237) and a walk of every retail
    /// zone's door routines (2026-10-03: 3,424 doors with <c>open</c> / <c>clos</c>, 3,031 with <c>into</c> / <c>intc</c>).
    /// </summary>
    public static class DoorRoutineDecoder
    {
        private const byte OpEnd = 0x00;
        private const byte OpSlide = 0x0C;
        private const byte OpRotate = 0x0D;

        public static DoorRoutine? Decode(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 0x20) return null;

            int commandsOffset = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0x14)) - 16;
            int totalFrames = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0x1C));
            if (commandsOffset < 0 || commandsOffset >= payload.Length) return null;

            var moves = new List<DoorMove>();
            int clock = 0;
            int p = commandsOffset;
            while (p + 8 <= payload.Length)
            {
                byte op = payload[p];
                int sizeDwords = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(p + 1)) & 0x1F;
                if (op == OpEnd) break;

                int start = clock;
                clock += BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(p + 4));
                if ((op == OpSlide || op == OpRotate) && sizeDwords >= 6 && p + 24 <= payload.Length)
                {
                    int duration = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(p + 6));
                    var target = new Vector3(
                        BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(p + 8)),
                        BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(p + 12)),
                        BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(p + 16)));
                    int part = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(p + 20));
                    if (part >= 0 && float.IsFinite(target.X) && float.IsFinite(target.Y) && float.IsFinite(target.Z))
                    {
                        moves.Add(new DoorMove(op == OpRotate ? DoorMoveKind.Rotate : DoorMoveKind.Slide, start, duration, target, part));
                    }
                }

                p += Math.Max(1, sizeDwords) * 4;
            }

            return new DoorRoutine { TotalFrames = Math.Max(totalFrames, clock), Moves = moves };
        }
    }
}
