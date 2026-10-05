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
        /// <summary>Anything not played (markers, sound 0x60, motion clips 0x05, scene colours 0x29 / 0x46).</summary>
        Other,

        /// <summary>Op 0x04: plays the camera route <see cref="SceneCommand.Reference"/> over the command's duration.</summary>
        Camera,

        /// <summary>Op 0x0F: fades the 3D scene toward <see cref="SceneCommand.Color"/> (the <c>fdo?</c> / <c>fdi?</c> fades).</summary>
        SceneFade,

        /// <summary>Op 0x51: fades the 2D interface toward <see cref="SceneCommand.Color"/> (the <c>fao?</c> / <c>fai?</c> fades).</summary>
        InterfaceFade,

        /// <summary>
        /// Op 0x72: moves the colour added over the 3D scene toward <see cref="SceneCommand.Color"/> (B, G, R full scale;
        /// the <c>who?</c> fades to white, <c>whi?</c> back to none, Port Jeuno 324's <c>fall</c> flash).
        /// </summary>
        ScreenFlash,

        /// <summary>
        /// Op 0x0E: moves the motion blur toward <see cref="SceneCommand.Color"/> (B, G, R: the trail's tint, 0x80 = as
        /// drawn; A: how much of the previous frame stays, 0x80 = all) and <see cref="SceneCommand.Factor"/> (the
        /// previous frame's zoom, 1 = none): <c>blon</c> / <c>blof</c>, the <c>?dkn</c> pulses.
        /// </summary>
        Blur,

        /// <summary>Op 0x10: dissolves from the frame before it into the scene over its duration (<c>ovl1</c> / <c>ovl2</c>).</summary>
        CrossDissolve,

        /// <summary>Op 0x02: starts the generator <see cref="SceneCommand.Reference"/> emitting for the command's duration.</summary>
        SpawnGenerator,

        /// <summary>Op 0x1E: kills the generator <see cref="SceneCommand.Reference"/> and its particles.</summary>
        KillGenerator,

        /// <summary>Op 0x3F: kills the generator <see cref="SceneCommand.Reference"/> and starts <see cref="SceneCommand.Reference2"/>.</summary>
        ReplaceGenerator,

        /// <summary>Op 0x03: starts the routine <see cref="SceneCommand.Reference"/> of the same file on the same actors.</summary>
        StartRoutine,

        /// <summary>Op 0x73: starts the routine <see cref="SceneCommand.Reference"/> repeating until it is stopped.</summary>
        LoopRoutine,

        /// <summary>Op 0x5F: stops the running routine <see cref="SceneCommand.Reference"/> (its particles live out their life).</summary>
        StopRoutine,

        /// <summary>
        /// Ops 0x0A (at the source actor), 0x0B (at the target), 0x4A / 0x53 / 0x60 (player-only / nearest / global
        /// variants): play the 0x3D sound section <see cref="SceneCommand.Reference"/> (xi-tools docs/fx/effect_system.md).
        /// Scene files use 0x60 (57129 <c>se00</c>, 30905 <c>who1</c>).
        /// </summary>
        Sound,
    }

    /// <summary>
    /// One command of a scene routine, starting <see cref="StartFrame"/> 60 Hz frames into it and lasting
    /// <see cref="Duration"/> frames. <see cref="Color"/> is B, G, R, A from the low byte, 0x80 = unchanged.
    /// <see cref="Reference2"/> is the second FourCC of op 0x3F, <see cref="Factor"/> the float of op 0x0E.
    /// </summary>
    public readonly record struct SceneCommand(byte Opcode, SceneCommandKind Kind, int StartFrame, int Duration, string Reference, uint Color, string Reference2 = "", float Factor = 1f)
    {
        /// <summary>Whether the command runs a particle generator or a routine (what <see cref="Gordian.Core.Graphics.SceneEffectPlayer"/> plays).</summary>
        public bool IsEffect => Kind is SceneCommandKind.SpawnGenerator or SceneCommandKind.KillGenerator or SceneCommandKind.ReplaceGenerator
            or SceneCommandKind.StartRoutine or SceneCommandKind.LoopRoutine or SceneCommandKind.StopRoutine;
    }

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
    /// screen as its narration lines come and go, so it is read as the interface fade.
    /// </para>
    /// <para>
    /// Post-process commands (#205, read from 30904, 30812 and 51328): op 0x0E carries B, G, R, A at +8 and a float at
    /// +12, moved to over its duration like the fades: <c>blon</c> goes to A0 A0 A0 30 / 0.98 in 15 frames and
    /// <c>blof</c> back to 80 80 80 00 / 1.0, the <c>?dkn</c> pulses jump to 80 80 80 2D / 0.92 and ease back, Port
    /// Jeuno 324's <c>fall</c> goes to 80 80 80 20 / 1.0 and back. It is read as the feedback motion blur: A how much of
    /// the previous frame is kept (0 = off), B G R its tint, the float its zoom. Op 0x10 (<c>ovl1</c> / <c>ovl2</c>,
    /// 60 / 120 frames; <c>olp1</c> / <c>olp2</c> in 30812) carries only its duration: a cross-dissolve from the frame
    /// before it.
    /// </para>
    /// <para>
    /// Effect commands (#192, read from Port Jeuno event 324's files 51402, 51327 and 51328): op 0x72 has 0x0F's layout
    /// with a colour added over the scene at full scale (the <c>who?</c> / <c>whi?</c> white fades of 30905); ops 0x02,
    /// 0x1E, 0x03, 0x73 and 0x5F name a generator or routine FourCC at +8, op 0x3F two (at +8 and +16). What each does is
    /// in <see cref="Gordian.Core.Graphics.SceneEffectPlayer"/>.
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
                string reference2 = string.Empty;
                uint color = 0;
                float factor = 1f;
                if (op == 0x04 && size >= 12 && p + 12 <= payload.Length)
                {
                    reference = ReadFourCc(payload.Slice(p + 8, 4));
                    if (reference.Length > 0) kind = SceneCommandKind.Camera;
                }
                else if ((op == 0x0F || op == 0x51 || op == 0x72) && size >= 12 && p + 12 <= payload.Length)
                {
                    color = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(p + 8));
                    kind = op switch { 0x0F => SceneCommandKind.SceneFade, 0x51 => SceneCommandKind.InterfaceFade, _ => SceneCommandKind.ScreenFlash };
                }
                else if (op == 0x0E && size >= 16 && p + 16 <= payload.Length)
                {
                    color = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(p + 8));
                    factor = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(p + 12));
                    kind = SceneCommandKind.Blur;
                }
                else if (op == 0x10)
                {
                    kind = SceneCommandKind.CrossDissolve;
                }
                else if (op is 0x0A or 0x0B or 0x4A or 0x53 or 0x60 && size >= 12 && p + 12 <= payload.Length)
                {
                    reference = ReadFourCc(payload.Slice(p + 8, 4));
                    if (reference.Length > 0) kind = SceneCommandKind.Sound;
                }
                else if (EffectKind(op) is { } effect && size >= 12 && p + 12 <= payload.Length)
                {
                    reference = ReadFourCc(payload.Slice(p + 8, 4));
                    if (op == 0x3F && size >= 20 && p + 20 <= payload.Length) reference2 = ReadFourCc(payload.Slice(p + 16, 4));
                    if (reference.Length > 0) kind = effect;
                }
                commands.Add(new SceneCommand(op, kind, start, duration, reference, color, reference2, factor));
                p += size;
            }
            return new SceneRoutine { Name = name, TotalFrames = total > 0 ? total : clock, Commands = commands };
        }

        private static SceneCommandKind? EffectKind(byte op) => op switch
        {
            0x02 => SceneCommandKind.SpawnGenerator,
            0x1E => SceneCommandKind.KillGenerator,
            0x3F => SceneCommandKind.ReplaceGenerator,
            0x03 => SceneCommandKind.StartRoutine,
            0x73 => SceneCommandKind.LoopRoutine,
            0x5F => SceneCommandKind.StopRoutine,
            _ => null,
        };

        /// <summary>Whether the routine runs generators or other routines (see <see cref="SceneCommand.IsEffect"/>).</summary>
        public bool HasEffects
        {
            get
            {
                foreach (var command in Commands)
                {
                    if (command.IsEffect) return true;
                }
                return false;
            }
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
    /// <summary>
    /// A sound generator of a scene file: its sound and its audible range (init op 0x4C <c>f32 far, f32 near</c>).
    /// Port Jeuno 324's lightning (57129 <c>6041</c>, <c>2088</c>, played on the sky-flash marker) is authored with
    /// far = 3000 yalms, so it is heard from anywhere; the <c>7124</c> rumble on the player 15.
    /// </summary>
    public readonly record struct SceneSoundGenerator(int SoundId, float Far, float Near);

    public sealed class EventSceneResource
    {
        private readonly Dictionary<string, CameraRoute> _routes;
        private readonly Dictionary<string, SceneRoutine> _routines;

        private readonly Dictionary<string, int> _sounds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SceneSoundGenerator> _generatorSounds = new(StringComparer.Ordinal);

        private EventSceneResource(Dictionary<string, CameraRoute> routes, Dictionary<string, SceneRoutine> routines)
        {
            _routes = routes;
            _routines = routines;
        }

        /// <summary>
        /// The sound effect id of the file's 0x3D section <paramref name="name"/> (what a routine's sound command names,
        /// e.g. 57129 <c>se00</c> → <c>1060</c> → 41060; the section name is not the id).
        /// </summary>
        public bool TryGetSound(string name, out int soundId) => _sounds.TryGetValue(name, out soundId);

        /// <summary>The sound of generator <paramref name="name"/> when it is a sound generator (its linked data is a 0x3D section).</summary>
        public bool TryGetGeneratorSound(string name, out SceneSoundGenerator generator) => _generatorSounds.TryGetValue(name, out generator);

        /// <summary>The first file id of the scheduler resources (<c>p</c> = 0).</summary>
        public const int BaseFileId = 30704;

        /// <summary>The DAT file id of scheduler resource <paramref name="p"/> (XiEvents <c>FUNC_DatIdHelper</c>).</summary>
        public static int GetFileId(int p) => BaseFileId + (p >= 600 ? p + 39643 : p >= 300 ? p + 25937 : p);

        /// <summary>The first file of the second scene range (0x9F / 0xA2 / 0xA3; XiEvents OpCodes/0x009F).</summary>
        public const int SecondBaseFileId = 51183;

        /// <summary>The scene DAT of the second range for work value <paramref name="p"/>: 51183 + p, without the remapping of <see cref="GetFileId"/>.</summary>
        public static int GetSecondFileId(int p) => SecondBaseFileId + p;

        /// <summary>
        /// The first file of the scene range a scheduler opcode other than 0x45 / 0x52 / 0x55 loads from (start, wait,
        /// stop): 0x62 / 0xA0 5012, 0x9F / 0xA2 / 0xA3 51183, 0xBB-0xBD 56685, 0xC5-0xC7 67355, 0xCD-0xCF 70435,
        /// 0xD0-0xD2 70691, 0xD5-0xD7 102449; -1 for any other opcode (0xA1, in the 0x62 family's stop place, uses 0x52's
        /// base and <see cref="GetFileId"/>). Bases referenced from XiEvents (https://github.com/atom0s/XiEvents,
        /// OpCodes/0x0062, 0x009F, 0x00A1, 0x00BB, 0x00C5, 0x00CD, 0x00D0, 0x00D5 and their wait / stop partners).
        /// <para>
        /// Files 5013-5109 (the 0x62 band) are each one self-contained effect package: one folder named for the effect
        /// (<c>wp00</c> / <c>wp01</c> warp, <c>kira</c>, <c>kone</c>...) with its routines, generators, keyframes,
        /// meshes, textures and sounds (retail DATs, 2026-10-03).
        /// </para>
        /// </summary>
        public static int GetBandBase(byte opcode) => opcode switch
        {
            0x62 or 0xA0 => 5012,
            0x9F or 0xA2 or 0xA3 => SecondBaseFileId,
            0xBB or 0xBC or 0xBD => 56685,
            0xC5 or 0xC6 or 0xC7 => 67355,
            0xCD or 0xCE or 0xCF => 70435,
            0xD0 or 0xD1 or 0xD2 => 70691,
            0xD5 or 0xD6 or 0xD7 => 102449,
            _ => -1,
        };

        /// <summary>The scene DAT scheduler opcode <paramref name="opcode"/> loads for work value <paramref name="p"/>: <see cref="GetBandBase"/> + p, not remapped.</summary>
        public static int GetBandFileId(byte opcode, int p) => GetBandBase(opcode) is var b && b >= 0 ? b + p : -1;

        public IReadOnlyDictionary<string, CameraRoute> Routes => _routes;

        public IReadOnlyDictionary<string, SceneRoutine> Routines => _routines;

        public bool TryGetRoutine(string name, out SceneRoutine routine) => _routines.TryGetValue(name, out routine!);

        public bool TryGetRoute(string name, out CameraRoute route) => _routes.TryGetValue(name, out route!);

        /// <summary>Reads the routes and routines of a scene resource DAT (sections of other kinds are skipped).</summary>
        public static EventSceneResource Parse(ReadOnlySpan<byte> file)
        {
            var routes = new Dictionary<string, CameraRoute>(StringComparer.Ordinal);
            var routines = new Dictionary<string, SceneRoutine>(StringComparer.Ordinal);
            var sounds = new Dictionary<string, int>(StringComparer.Ordinal);
            var soundGenerators = new Dictionary<string, (string Pointer, float Far, float Near)>(StringComparer.Ordinal);
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
                else if (header.RawTypeCode == 0x3D)
                {
                    if (Audio.SoundEffectPointer.TryDecode(payload, out int soundId)) sounds.TryAdd(header.DatId, soundId);
                }
                else if (header.RawTypeCode == 0x05)
                {
                    try
                    {
                        if (Graphics.ParticleGeneratorDecoder.DecodeGenerator(payload, header.DatId) is { Setup: { LinkedDataType: Graphics.ParticleLinkedDataType.Audio } setup } def)
                        {
                            float far = 0f, near = 0f;
                            foreach (var op in def.Initializers)
                            {
                                if (op.OpCode == 0x4C && op.Args.Length >= 2)
                                {
                                    far = BitConverter.UInt32BitsToSingle(op.Args[0]);
                                    near = BitConverter.UInt32BitsToSingle(op.Args[1]);
                                }
                            }

                            soundGenerators.TryAdd(header.DatId, (setup.LinkedDataId, float.IsFinite(far) ? far : 0f, float.IsFinite(near) ? near : 0f));
                        }
                    }
                    catch (Exception)
                    {
                        // A generator that does not decode is not a sound.
                    }
                }
            }
            var resource = new EventSceneResource(routes, routines);
            foreach (var (name, id) in sounds) resource._sounds[name] = id;
            foreach (var (generator, link) in soundGenerators)
            {
                if (sounds.TryGetValue(link.Pointer, out int id)) resource._generatorSounds[generator] = new SceneSoundGenerator(id, link.Far, link.Near);
            }
            return resource;
        }
    }
}
