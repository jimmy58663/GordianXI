// tests/Gordian.Core.Tests/Shared/MotionRoutineBuilder.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;

namespace Gordian.Tests.Shared
{
    /// <summary>Synthetic Section 0x07 motion routine payloads (actor motion commands) for the decoder and playback tests. Compiled into both test projects.</summary>
    internal static class MotionRoutineBuilder
    {
        /// <summary>Builds a Section 0x07 payload (after the 16-byte section header) from encoded commands.</summary>
        internal static byte[] Routine(int totalTicks, params byte[][] commands)
        {
            var bytes = new List<byte>(new byte[0x20]);
            foreach (var command in commands) bytes.AddRange(command);
            bytes.AddRange(new byte[8]); // op 0x00 ends the list
            var payload = bytes.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0x14), 0x20 + 16);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0x1C), totalTicks);
            return payload;
        }

        internal static byte[] Command(byte op, int dwords, int delay, int duration, string reference = "")
        {
            var command = new byte[dwords * 4];
            command[0] = op;
            BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(1), (ushort)dwords);
            BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(4), (ushort)delay);
            BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(6), (ushort)duration);
            if (reference.Length > 0 && dwords >= 3) Encoding.ASCII.GetBytes(reference).CopyTo(command, 8);
            return command;
        }

        internal static byte[] PlayClip(string reference, int delay, int duration, int blendIn, int blendOut, int loops, float speed = 1f)
        {
            var command = Command(0x05, 8, delay, duration, reference);
            BinaryPrimitives.WriteSingleLittleEndian(command.AsSpan(0x10), speed);
            BinaryPrimitives.WriteSingleLittleEndian(command.AsSpan(0x14), 1f);
            BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(0x18), (ushort)blendIn);
            BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(0x1C), (ushort)blendOut);
            BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(0x1E), (ushort)loops);
            return command;
        }

        internal static byte[] Link(byte op, string reference, int delay) => Command(op, 4, delay, 0, reference);

        internal static byte[] Flinch(int ticks)
        {
            var command = Command(0x21, 9, 2, 0);
            BinaryPrimitives.WriteSingleLittleEndian(command.AsSpan(0x18), ticks);
            return command;
        }

        internal static byte[] PoseFlash(int pose, int ticks)
        {
            var command = Command(0x5A, 7, 0, 0);
            command[0x0C] = 4;
            BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(0x0E), (ushort)pose);
            BinaryPrimitives.WriteSingleLittleEndian(command.AsSpan(0x10), 1f);
            BinaryPrimitives.WriteSingleLittleEndian(command.AsSpan(0x14), ticks);
            return command;
        }
    }
}
