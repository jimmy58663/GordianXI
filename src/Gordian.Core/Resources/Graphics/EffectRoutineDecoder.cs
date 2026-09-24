// src/Gordian.Core/Resources/Graphics/EffectRoutineDecoder.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// One Section 0x07 "spawn generator" command: the generator starts emitting <see cref="StartFrame"/> frames into the
    /// routine and emits for <see cref="Duration"/> frames (its emission window).
    /// </summary>
    public readonly record struct EffectRoutineSpawn(string GeneratorId, int StartFrame, int Duration);

    /// <summary>
    /// A decoded Section 0x07 effect routine's generator timeline.
    /// </summary>
    public sealed class EffectRoutine
    {
        public string DatId { get; init; } = string.Empty;

        /// <summary>
        /// Total routine length in 60 Hz frames (the sum of its command delays).
        /// </summary>
        public int TotalFrames { get; init; }

        public IReadOnlyList<EffectRoutineSpawn> Spawns { get; init; } = Array.Empty<EffectRoutineSpawn>();
    }

    /// <summary>
    /// Clean-room decoder for the generator timeline of FFXI DAT Section 0x07 (EffectRoutine) chunks, e.g. the ambient
    /// routines that roll Bibiki Bay's shoreline waves in. Payload +0x14 holds the command list offset (relative to the
    /// section start, including its 16-byte header) and +0x1C the total length. Each command is { u8 op, u16 size in
    /// dwords (low 5 bits), u8, u16 delay, u16 duration, 4-char reference, ... }; a command runs at the sum of the
    /// delays before it (its own delay is the wait after it). Op 0x02 spawns a generator; op 0x00 ends the list.
    /// Other commands advance the clock but are otherwise ignored.
    /// Format referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer, ui/js/effect.js and
    /// ui/js/dat.js parseRoutine, after xi-tools and xim).
    /// </summary>
    public static class EffectRoutineDecoder
    {
        private const byte OpEnd = 0x00;
        private const byte OpSpawnGenerator = 0x02;
        private const int MaxCommands = 128;

        public static EffectRoutine? Decode(ReadOnlySpan<byte> payload, string datId)
        {
            if (payload.Length < 0x20) return null;

            int commandsOffset = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0x14)) - 16;
            int totalFrames = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0x1C));
            if (commandsOffset < 0 || commandsOffset >= payload.Length) return null;

            var spawns = new List<EffectRoutineSpawn>();
            int clock = 0;
            int p = commandsOffset;
            for (int guard = 0; guard < MaxCommands && p + 8 <= payload.Length; guard++)
            {
                byte op = payload[p];
                int sizeDwords = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(p + 1)) & 0x1F;
                if (op == OpEnd) break;

                int start = clock;
                clock += BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(p + 4));
                if (op == OpSpawnGenerator && p + 12 <= payload.Length)
                {
                    int duration = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(p + 6));
                    string reference = ReadId(payload.Slice(p + 8, 4));
                    if (reference.Length > 0) spawns.Add(new EffectRoutineSpawn(reference, start, duration));
                }

                p += Math.Max(1, sizeDwords) * 4;
            }

            return new EffectRoutine
            {
                DatId = datId ?? string.Empty,
                TotalFrames = totalFrames > 0 ? totalFrames : clock,
                Spawns = spawns
            };
        }

        private static string ReadId(ReadOnlySpan<byte> span)
        {
            int end = span.IndexOf((byte)0);
            if (end < 0) end = span.Length;
            foreach (byte b in span.Slice(0, end))
            {
                if (b < 0x20 || b > 0x7E) return string.Empty;
            }
            return Encoding.ASCII.GetString(span.Slice(0, end)).TrimEnd();
        }
    }
}
