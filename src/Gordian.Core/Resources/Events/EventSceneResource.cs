// src/Gordian.Core/Resources/Events/EventSceneResource.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Gordian.Core.Resources.Containers;

namespace Gordian.Core.Resources.Events
{
    /// <summary>One key of a <see cref="CameraRoute"/>: where the camera stands and looks at a point of the move.</summary>
    /// <param name="Eye">Camera position (DAT / internal axes: Y is the height, -Y up).</param>
    /// <param name="FocalLength">Focal length; the vertical field of view is <c>2 atan2(192, focal)</c> (350 is about 57.5 degrees).</param>
    /// <param name="LookAt">The point the camera looks at.</param>
    /// <param name="Roll">Roll around the view direction, radians.</param>
    /// <param name="Time">Where the key sits in the move, 0 (start) to 1 (end).</param>
    public readonly record struct CameraKey(Vector3 Eye, float FocalLength, Vector3 LookAt, float Roll, float Time)
    {
        /// <summary>The vertical field of view of <see cref="FocalLength"/>, radians.</summary>
        public float FieldOfView => FocalLength > 0 ? 2f * MathF.Atan2(192f, FocalLength) : 2f * MathF.Atan2(192f, 350f);
    }

    /// <summary>A camera pose sampled from a route (internal axes).</summary>
    public readonly record struct CameraRoutePose(Vector3 Eye, Vector3 LookAt, float FieldOfView, float Roll);

    /// <summary>
    /// A cutscene camera move: Section 0x06 ("Route") of a scene resource DAT. A 32-byte header (+0x00 flags, +0x10
    /// key count, +0x14 easing mode 0-4) and 48-byte keys: eye (3 floats), focal length, look-at (3 floats), roll,
    /// normalized time, 12 bytes of padding.
    /// <para>
    /// Layout referenced from xi-tools (https://github.com/vekien/xi-tools, docs/events/scene_dat_writer.md,
    /// camera_scene_ids.md and cutscenes.md: decoded over 22.5k retail routes). Not established there, so chosen here:
    /// the easing of the modes follows the five smoothing kinds a client reimplementation names (xi-tools
    /// cutscenes.md, "How the camera works": linear, decelerate, accelerate, decelerate to the midpoint then accelerate,
    /// accelerate and decelerate); two keys are joined by a line, more by a Catmull-Rom curve through the keys; flag
    /// bit 0 (seen on a ship shot and on the shared twelve-direction orbit shots, whose points lie a few yalms around
    /// the origin) places the route relative to the task's first actor.
    /// </para>
    /// </summary>
    public sealed class CameraRoute
    {
        public const int HeaderSize = 0x20;
        public const int KeySize = 0x30;

        public string Name { get; init; } = string.Empty;

        /// <summary>The header's first word; bit 0 = relative to the task's first actor (see the class notes).</summary>
        public uint Flags { get; init; }

        /// <summary>The easing mode 0-4 (see the class notes).</summary>
        public int Mode { get; init; }

        public IReadOnlyList<CameraKey> Keys { get; init; } = Array.Empty<CameraKey>();

        /// <summary>Whether the points are offsets from the task's first actor rather than zone positions.</summary>
        public bool IsActorRelative => (Flags & 1) != 0;

        public static CameraRoute? Decode(ReadOnlySpan<byte> payload, string name)
        {
            if (payload.Length < HeaderSize) return null;
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(payload);
            int count = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0x10));
            int mode = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0x14));
            if (count <= 0 || HeaderSize + count * KeySize > payload.Length) return null;
            var keys = new CameraKey[count];
            for (int i = 0; i < count; i++)
            {
                var key = payload.Slice(HeaderSize + i * KeySize, KeySize);
                keys[i] = new CameraKey(ReadVector(key), ReadFloat(key, 0x0C), ReadVector(key.Slice(0x10)), ReadFloat(key, 0x1C), ReadFloat(key, 0x20));
            }
            return new CameraRoute { Name = name, Flags = flags, Mode = mode, Keys = keys };
        }

        /// <summary>
        /// The pose at <paramref name="progress"/> (0 = start, 1 = end of the move), eased by <see cref="Mode"/>;
        /// <paramref name="origin"/> is added to the points of an actor-relative route.
        /// </summary>
        public CameraRoutePose Evaluate(float progress, Vector3 origin = default)
        {
            var keys = Keys;
            if (keys.Count == 0) return new CameraRoutePose(origin, origin + Vector3.UnitZ, new CameraKey().FieldOfView, 0);
            Vector3 offset = IsActorRelative ? origin : Vector3.Zero;
            float t = Ease(Math.Clamp(float.IsFinite(progress) ? progress : 1f, 0f, 1f), Mode);
            if (keys.Count == 1 || t <= keys[0].Time) return Pose(keys[0], offset);
            if (t >= keys[^1].Time) return Pose(keys[^1], offset);

            int i = 0;
            while (i < keys.Count - 2 && t > keys[i + 1].Time) i++;
            var a = keys[i];
            var b = keys[i + 1];
            float span = b.Time - a.Time;
            float u = span > 1e-6f ? (t - a.Time) / span : 1f;
            if (keys.Count == 2)
            {
                return new CameraRoutePose(Vector3.Lerp(a.Eye, b.Eye, u) + offset, Vector3.Lerp(a.LookAt, b.LookAt, u) + offset,
                    Lerp(a.FieldOfView, b.FieldOfView, u), Lerp(a.Roll, b.Roll, u));
            }
            var before = keys[Math.Max(0, i - 1)];
            var after = keys[Math.Min(keys.Count - 1, i + 2)];
            return new CameraRoutePose(
                CatmullRom(before.Eye, a.Eye, b.Eye, after.Eye, u) + offset,
                CatmullRom(before.LookAt, a.LookAt, b.LookAt, after.LookAt, u) + offset,
                CatmullRom(before.FieldOfView, a.FieldOfView, b.FieldOfView, after.FieldOfView, u),
                CatmullRom(before.Roll, a.Roll, b.Roll, after.Roll, u));
        }

        /// <summary>The progress curve of an easing mode (see the class notes).</summary>
        public static float Ease(float t, int mode) => mode switch
        {
            1 => 1f - (1f - t) * (1f - t),
            2 => t * t,
            3 => t < 0.5f ? 0.5f * (1f - (1f - 2f * t) * (1f - 2f * t)) : 0.5f + 0.5f * (2f * t - 1f) * (2f * t - 1f),
            4 => t * t * (3f - 2f * t),
            _ => t,
        };

        private static CameraRoutePose Pose(CameraKey key, Vector3 offset) => new(key.Eye + offset, key.LookAt + offset, key.FieldOfView, key.Roll);

        private static float Lerp(float a, float b, float u) => a + (b - a) * u;

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u) =>
            new(CatmullRom(p0.X, p1.X, p2.X, p3.X, u), CatmullRom(p0.Y, p1.Y, p2.Y, p3.Y, u), CatmullRom(p0.Z, p1.Z, p2.Z, p3.Z, u));

        private static float CatmullRom(float p0, float p1, float p2, float p3, float u)
        {
            float u2 = u * u, u3 = u2 * u;
            return 0.5f * (2f * p1 + (p2 - p0) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (3f * p1 - p0 - 3f * p2 + p3) * u3);
        }

        private static float ReadFloat(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadSingleLittleEndian(span.Slice(offset));

        private static Vector3 ReadVector(ReadOnlySpan<byte> span) => new(ReadFloat(span, 0), ReadFloat(span, 4), ReadFloat(span, 8));
    }

    /// <summary>What a scene routine command does, as far as the event presentation plays it.</summary>
    public enum SceneCommandKind : byte
    {
        /// <summary>Anything not played (markers, blur 0x0E, cross-dissolve 0x10, effects, sound).</summary>
        Other,

        /// <summary>Op 0x04: plays the camera route <see cref="SceneCommand.Reference"/> over the command's duration.</summary>
        Camera,

        /// <summary>Op 0x0F: fades the 3D scene toward <see cref="SceneCommand.Color"/> (the <c>fdo?</c> / <c>fdi?</c> fades).</summary>
        SceneFade,

        /// <summary>Op 0x51: fades the 2D interface toward <see cref="SceneCommand.Color"/> (the <c>fao?</c> / <c>fai?</c> fades).</summary>
        InterfaceFade,
    }

    /// <summary>
    /// One command of a scene routine, starting <see cref="StartFrame"/> 60 Hz frames into it and lasting
    /// <see cref="Duration"/> frames. <see cref="Color"/> is B, G, R, A from the low byte, 0x80 = unchanged.
    /// </summary>
    public readonly record struct SceneCommand(byte Opcode, SceneCommandKind Kind, int StartFrame, int Duration, string Reference, uint Color);

    /// <summary>
    /// A cutscene shot, fade or other timeline: Section 0x07 of a scene resource DAT, read with the same command walk as
    /// the effect routines (<see cref="Graphics.EffectRoutineDecoder"/>): payload +0x14 the command list (section
    /// relative), +0x1C the total length in frames; each command <c>{ u8 op, u16 size in dwords (low 5 bits), u8,
    /// u16 delay, u16 duration, ... }</c> runs at the sum of the delays before it.
    /// <para>
    /// The command meanings were read from the retail scene DATs (the Port Bastok and Windurst Woods intros' files
    /// 30840, 30843, 30845 and the shared fades of 30904): op 0x04 names a Route FourCC at +8 (every <c>sNNN</c> shot
    /// plays <c>cNNN</c>, and xi-tools' scene writer emits the same command, docs/events/scene_dat_writer.md); op 0x0F
    /// carries a colour at +8 that the fade routines move between (0x00 0x00 0x00 in <c>fdo?</c>, 0x80 0x80 0x80 in
    /// <c>fdi?</c>, 0x80 = the scene as drawn), and the opening shot <c>s00s</c> starts black and fades in with it; op 0x51
    /// has the same layout in the <c>fao?</c> / <c>fai?</c> routines, which the Windurst Woods intro alternates on a black
    /// screen as its narration lines come and go, so it is read as the interface fade. Op 0x0E (<c>blon</c> / <c>blof</c>:
    /// a colour and a factor) and 0x10 (<c>ovl?</c>: a cross-dissolve between shots) are not played.
    /// </para>
    /// </summary>
    public sealed class SceneRoutine
    {
        public string Name { get; init; } = string.Empty;

        /// <summary>The routine's length in 60 Hz frames (header +0x1C, else the sum of its delays).</summary>
        public int TotalFrames { get; init; }

        public IReadOnlyList<SceneCommand> Commands { get; init; } = Array.Empty<SceneCommand>();

        public static SceneRoutine? Decode(ReadOnlySpan<byte> payload, string name)
        {
            if (payload.Length < 0x20) return null;
            int commandsOffset = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0x14)) - DatSectionHeader.HeaderSize;
            int total = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0x1C));
            if (commandsOffset < 0 || commandsOffset >= payload.Length) return null;

            var commands = new List<SceneCommand>();
            int clock = 0;
            int p = commandsOffset;
            while (p + 8 <= payload.Length)
            {
                byte op = payload[p];
                if (op == 0x00) break;
                int size = Math.Max(1, BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(p + 1)) & 0x1F) * 4;
                int start = clock;
                clock += BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(p + 4));
                int duration = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(p + 6));
                var kind = SceneCommandKind.Other;
                string reference = string.Empty;
                uint color = 0;
                if (op == 0x04 && size >= 12 && p + 12 <= payload.Length)
                {
                    reference = ReadFourCc(payload.Slice(p + 8, 4));
                    if (reference.Length > 0) kind = SceneCommandKind.Camera;
                }
                else if ((op == 0x0F || op == 0x51) && size >= 12 && p + 12 <= payload.Length)
                {
                    color = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(p + 8));
                    kind = op == 0x0F ? SceneCommandKind.SceneFade : SceneCommandKind.InterfaceFade;
                }
                commands.Add(new SceneCommand(op, kind, start, duration, reference, color));
                p += size;
            }
            return new SceneRoutine { Name = name, TotalFrames = total > 0 ? total : clock, Commands = commands };
        }

        private static string ReadFourCc(ReadOnlySpan<byte> span)
        {
            int end = span.IndexOf((byte)0);
            if (end < 0) end = span.Length;
            foreach (byte b in span.Slice(0, end))
            {
                if (b < 0x20 || b > 0x7E) return string.Empty;
            }
            return Encoding.ASCII.GetString(span.Slice(0, end));
        }
    }

    /// <summary>
    /// A cutscene scene resource: the DAT an event's scheduler opcodes (0x45 / 0x52 / 0x55) load by a small number
    /// <c>p</c> from the actor block's immediate data: <c>evte</c>, the camera Routes (Section 0x06, <see cref="CameraRoute"/>),
    /// the routines (Section 0x07, <see cref="SceneRoutine"/>: the <c>sNNN</c> shots, <c>fdo?</c> / <c>fdi?</c> fades and
    /// others) and particle generators, then <c>end</c>.
    /// <para>
    /// File id mapping referenced from XiEvents (https://github.com/atom0s/XiEvents, OpCodes/0x0045.md,
    /// <c>FUNC_DatIdHelper</c>) and xi-tools (docs/events/camera_scene_ids.md): <c>30704 + p</c> for p below 300,
    /// <c>56641 + p</c> for 300-599, <c>70347 + p</c> from 600.
    /// </para>
    /// </summary>
    public sealed class EventSceneResource
    {
        private readonly Dictionary<string, CameraRoute> _routes;
        private readonly Dictionary<string, SceneRoutine> _routines;

        private EventSceneResource(Dictionary<string, CameraRoute> routes, Dictionary<string, SceneRoutine> routines)
        {
            _routes = routes;
            _routines = routines;
        }

        /// <summary>The first file id of the scheduler resources (<c>p</c> = 0).</summary>
        public const int BaseFileId = 30704;

        /// <summary>The DAT file id of scheduler resource <paramref name="p"/> (XiEvents <c>FUNC_DatIdHelper</c>).</summary>
        public static int GetFileId(int p) => BaseFileId + (p >= 600 ? p + 39643 : p >= 300 ? p + 25937 : p);

        public IReadOnlyDictionary<string, CameraRoute> Routes => _routes;

        public IReadOnlyDictionary<string, SceneRoutine> Routines => _routines;

        public bool TryGetRoutine(string name, out SceneRoutine routine) => _routines.TryGetValue(name, out routine!);

        public bool TryGetRoute(string name, out CameraRoute route) => _routes.TryGetValue(name, out route!);

        /// <summary>Reads the routes and routines of a scene resource DAT (sections of other kinds are skipped).</summary>
        public static EventSceneResource Parse(ReadOnlySpan<byte> file)
        {
            var routes = new Dictionary<string, CameraRoute>(StringComparer.Ordinal);
            var routines = new Dictionary<string, SceneRoutine>(StringComparer.Ordinal);
            foreach (var header in DatSectionWalker.ReadHeaders(file))
            {
                if (header.DataOffset + header.DataSizeBytes > file.Length) continue;
                var payload = file.Slice(header.DataOffset, header.DataSizeBytes);
                if (header.RawTypeCode == 0x06)
                {
                    if (CameraRoute.Decode(payload, header.DatId) is { } route) routes[header.DatId] = route;
                }
                else if (header.RawTypeCode == 0x07)
                {
                    if (SceneRoutine.Decode(payload, header.DatId) is { } routine) routines[header.DatId] = routine;
                }
            }
            return new EventSceneResource(routes, routines);
        }
    }
}
