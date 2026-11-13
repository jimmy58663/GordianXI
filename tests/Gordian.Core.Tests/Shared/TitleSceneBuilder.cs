// tests/Gordian.Core.Tests/Shared/TitleSceneBuilder.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Gordian.Tests.Shared
{
    /// <summary>
    /// A synthetic title fly-through file (<c>ROM/0/23</c> layout, #552): camera routes, segment routines (op 0x7B zone,
    /// 0x7C weather, 0x7D clock, 0x04 shots, 0x0F fades) and a <c>loop</c> of random choices. Compiled into both test projects.
    /// </summary>
    internal sealed class TitleSceneBuilder
    {
        private readonly List<(string Name, byte Type, byte[] Payload)> _sections = new();

        /// <summary>A two-key camera route from <paramref name="from"/> to <paramref name="to"/>, looking down +Z.</summary>
        public TitleSceneBuilder Route(string name, float from, float to)
        {
            var payload = new byte[0x20 + 2 * 0x30];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0x10), 2);
            WriteKey(payload.AsSpan(0x20), from, 0f);
            WriteKey(payload.AsSpan(0x50), to, 1f);
            _sections.Add((name, 0x06, payload));
            return this;
        }

        /// <summary>
        /// A segment routine of <paramref name="totalFrames"/>: zone <paramref name="zone"/>, weather <paramref name="weather"/>
        /// and clock <paramref name="clock"/> at frame 0, a fade in over 100 frames, the shot <paramref name="route"/> over the
        /// whole segment, <paramref name="extra"/> commands at their frames, and a fade to black over the last 100 frames.
        /// </summary>
        public TitleSceneBuilder Segment(string name, int zone, string route, ushort totalFrames, string weather = "fine", uint clock = 2592000,
            params (int Frame, byte[] Command)[] extra)
        {
            var commands = new List<byte[]>
            {
                Fade(0, 100, 0x80808080),
                Number(0x7B, 0, (uint)zone, dwords: 6),
                Reference(0x7C, 0, weather),
                Number(0x7D, 0, clock),
                Shot(route, 0, totalFrames),
            };
            int at = 0;
            var timed = new List<(int Frame, byte[] Command)>(extra) { (totalFrames - 100, Fade(0, 100, 0x80000000)) };
            timed.Sort((a, b) => a.Frame.CompareTo(b.Frame));
            foreach (var (frame, command) in timed)
            {
                // A command starts at the sum of the delays before it: a marker (op 0x01) waits until its frame.
                if (frame > at) commands.Add(Head(0x01, 2, (ushort)(frame - at), 0));
                at = Math.Max(at, frame);
                commands.Add(command);
            }
            _sections.Add((name, 0x07, Routine(totalFrames, commands)));
            return this;
        }

        /// <summary>
        /// The <c>loop</c> routine: one random choice per group, opened by 0x3D (expansion 0) or 0xAF n, each entry a 0x3B
        /// play-and-wait, closed by 0x3E.
        /// </summary>
        public TitleSceneBuilder Loop(params (int Expansion, string[] Segments)[] groups)
        {
            var commands = new List<byte[]>();
            foreach (var (expansion, segments) in groups)
            {
                commands.Add(expansion == 0 ? Bare(0x3D) : Number(0xAF, 0, (uint)expansion));
                foreach (var segment in segments) commands.Add(Reference(0x3B, 0, segment, dwords: 4));
                commands.Add(Bare(0x3E));
            }
            _sections.Add(("loop", 0x07, Routine(0, commands)));
            return this;
        }

        /// <summary>A 0x2F environment section of the minimum size.</summary>
        public TitleSceneBuilder Environment(string name)
        {
            _sections.Add((name, 0x2F, new byte[0xB0]));
            return this;
        }

        public byte[] Build()
        {
            var bytes = new List<byte>();
            AddSection(bytes, "titl", 0x01, Array.Empty<byte>());
            foreach (var (name, type, payload) in _sections) AddSection(bytes, name, type, payload);
            AddSection(bytes, "end", 0x00, Array.Empty<byte>());
            return bytes.ToArray();
        }

        /// <summary>An op 0x7C weather command.</summary>
        public static byte[] Weather(string weather) => Reference(0x7C, 0, weather);

        /// <summary>An op 0x7D clock command (1/60 s of the game day: hour x 216,000).</summary>
        public static byte[] Clock(uint clock) => Number(0x7D, 0, clock);

        /// <summary>An op 0x7E environment command.</summary>
        public static byte[] UseEnvironment(string name) => Reference(0x7E, 0, name, dwords: 4);

        private static void AddSection(List<byte> into, string name, byte type, byte[] payload)
        {
            int size = 16 + (payload.Length + 15) / 16 * 16;
            var header = new byte[16];
            Encoding.ASCII.GetBytes(name).CopyTo(header, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), type | (uint)(size / 16) << 7);
            into.AddRange(header);
            into.AddRange(payload);
            into.AddRange(new byte[size - 16 - payload.Length]);
        }

        private static void WriteKey(Span<byte> key, float z, float time)
        {
            BinaryPrimitives.WriteSingleLittleEndian(key.Slice(0x08), z);
            BinaryPrimitives.WriteSingleLittleEndian(key.Slice(0x0C), 350f);
            BinaryPrimitives.WriteSingleLittleEndian(key.Slice(0x18), z + 10f);
            BinaryPrimitives.WriteSingleLittleEndian(key.Slice(0x20), time);
        }

        private static byte[] Routine(int totalFrames, List<byte[]> commands)
        {
            var bytes = new List<byte>(new byte[0x40]);
            var header = bytes.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0x14), 0x50);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0x1C), totalFrames);
            bytes = new List<byte>(header);
            bytes.AddRange(new byte[] { 0x01, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
            foreach (var command in commands) bytes.AddRange(command);
            bytes.AddRange(new byte[8]);
            return bytes.ToArray();
        }

        private static byte[] Head(byte op, int dwords, ushort delay, ushort duration)
        {
            var cmd = new byte[dwords * 4];
            cmd[0] = op;
            cmd[1] = (byte)dwords;
            BinaryPrimitives.WriteUInt16LittleEndian(cmd.AsSpan(4), delay);
            BinaryPrimitives.WriteUInt16LittleEndian(cmd.AsSpan(6), duration);
            return cmd;
        }

        private static byte[] Bare(byte op) => Head(op, 2, 0, 0);

        private static byte[] Number(byte op, ushort delay, uint value, int dwords = 3)
        {
            var cmd = Head(op, dwords, delay, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(cmd.AsSpan(8), value);
            return cmd;
        }

        private static byte[] Reference(byte op, ushort delay, string reference, int dwords = 3)
        {
            var cmd = Head(op, dwords, delay, 0);
            Encoding.ASCII.GetBytes(reference).CopyTo(cmd, 8);
            return cmd;
        }

        private static byte[] Fade(ushort delay, ushort duration, uint bgra)
        {
            var cmd = Head(0x0F, 3, delay, duration);
            BinaryPrimitives.WriteUInt32LittleEndian(cmd.AsSpan(8), bgra);
            return cmd;
        }

        /// <summary>A camera shot whose delay is its own duration, so the next command starts when it ends (as the title's do).</summary>
        private static byte[] Shot(string route, ushort delay, ushort duration)
        {
            var cmd = Head(0x04, 6, delay, duration);
            Encoding.ASCII.GetBytes(route).CopyTo(cmd, 8);
            return cmd;
        }
    }
}
