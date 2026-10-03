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
    /// One Section 0x07 command that starts another routine of the same directory (or a parent's): op 0x03 runs it once,
    /// op 0x73 runs it repeating until op 0x5F stops it.
    /// </summary>
    public readonly record struct EffectRoutineStart(string RoutineId, int StartFrame, bool Repeats);

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

        /// <summary>The routines this one starts (ops 0x03 and 0x73), in command order.</summary>
        public IReadOnlyList<EffectRoutineStart> Starts { get; init; } = Array.Empty<EffectRoutineStart>();

        /// <summary>
        /// Whether the routine's third command list holds op 0x01: the routine starts over when it ends (Bibiki Bay's
        /// shoreline <c>umi2/s000</c>). See <see cref="StartsOnZoneLoad"/>.
        /// </summary>
        public bool LoopsOnComplete { get; init; }

        /// <summary>
        /// Whether the routine holds op 0x52 (a replay the client times itself: birds, butterflies, lightning strikes).
        /// </summary>
        public bool HasTimedReplay { get; init; }

        /// <summary>
        /// Whether the client starts the routine itself when the zone loads (it loops on completion or replays on a timer);
        /// every other zone routine waits for a trigger: a map scheduler from the server (S2C 0x039) or an event, a
        /// routine that starts it, or its door's status.
        /// </summary>
        public bool StartsOnZoneLoad => LoopsOnComplete || HasTimedReplay;
    }

    /// <summary>
    /// Clean-room decoder for the generator timeline of FFXI DAT Section 0x07 (EffectRoutine) chunks, e.g. the ambient
    /// routines that roll Bibiki Bay's shoreline waves in. Payload +0x14 holds the command list offset (relative to the
    /// section start, including its 16-byte header) and +0x1C the total length. Each command is { u8 op, u16 size in
    /// dwords (low 5 bits), u8, u16 delay, u16 duration, 4-char reference, ... }; a command runs at the sum of the
    /// delays before it (its own delay is the wait after it). Op 0x02 spawns a generator; op 0x00 ends the list.
    /// Ops 0x03 / 0x73 start another routine (<see cref="EffectRoutine.Starts"/>) and op 0x52 marks a timed replay.
    /// Other commands advance the clock but are otherwise ignored. The walk has no command cap: retail routines run to
    /// 266 commands (Pso'Xja's barriers), and every routine's list ends on op 0x00 within its section, so the payload
    /// length bounds it (a zero-size command still advances one dword).
    /// <para>
    /// The header's three list offsets (+0x10, +0x14, +0x18) each lead to a command list ending on op 0x00; the
    /// timeline is the second. The third holds a single op 0x01 in the routines that loop on completion
    /// (<see cref="EffectRoutine.LoopsOnComplete"/>), as xim's autorun heuristic reads it (xi-tools
    /// docs/fx/effect_system.md "Triggering &amp; autoRun"); a walk of all 19,176 routines in the zone DATs
    /// (2026-10-03, #210) found the first list always empty and the third either empty (18,759) or exactly
    /// <c>01</c> with 0x100 at +4 (417, Bibiki Bay's <c>umi2/s000</c> among them).
    /// </para>
    /// Format referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer, ui/js/effect.js and
    /// ui/js/dat.js parseRoutine, after xi-tools and xim).
    /// </summary>
    public static class EffectRoutineDecoder
    {
        private const byte OpEnd = 0x00;
        private const byte OpLoopOnComplete = 0x01;
        private const byte OpSpawnGenerator = 0x02;
        private const byte OpStartRoutine = 0x03;
        private const byte OpTimedReplay = 0x52;
        private const byte OpLoopRoutine = 0x73;

        public static EffectRoutine? Decode(ReadOnlySpan<byte> payload, string datId)
        {
            if (payload.Length < 0x20) return null;

            int commandsOffset = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0x14)) - 16;
            int completionOffset = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0x18)) - 16;
            int totalFrames = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0x1C));
            if (commandsOffset < 0 || commandsOffset >= payload.Length) return null;

            var spawns = new List<EffectRoutineSpawn>();
            var starts = new List<EffectRoutineStart>();
            bool timedReplay = false;
            int clock = 0;
            int p = commandsOffset;
            while (p + 8 <= payload.Length)
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
                else if ((op == OpStartRoutine || op == OpLoopRoutine) && sizeDwords >= 3 && p + 12 <= payload.Length)
                {
                    string reference = ReadId(payload.Slice(p + 8, 4));
                    if (reference.Length > 0) starts.Add(new EffectRoutineStart(reference, start, op == OpLoopRoutine));
                }
                else if (op == OpTimedReplay)
                {
                    timedReplay = true;
                }

                p += Math.Max(1, sizeDwords) * 4;
            }

            bool loopsOnComplete = completionOffset >= 0 && completionOffset < payload.Length && payload[completionOffset] == OpLoopOnComplete;

            return new EffectRoutine
            {
                DatId = datId ?? string.Empty,
                TotalFrames = totalFrames > 0 ? totalFrames : clock,
                Spawns = spawns,
                Starts = starts,
                LoopsOnComplete = loopsOnComplete,
                HasTimedReplay = timedReplay
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
