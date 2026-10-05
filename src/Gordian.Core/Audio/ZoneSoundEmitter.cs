// src/Gordian.Core/Audio/ZoneSoundEmitter.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;

namespace Gordian.Core.Audio
{
    /// <summary>
    /// A zone's sound source: a Section 0x05 generator whose linked data is a sound pointer (<c>0x3D</c>), placed in the
    /// zone (fountains, fires, birds, a river) or following a path (shoreline waves).
    /// </summary>
    /// <param name="Name">The generator's name.</param>
    /// <param name="SoundId">The <c>.spw</c> id from the linked 0x3D section.</param>
    /// <param name="Position">The generator's base position (internal space, the same as entity positions).</param>
    /// <param name="Near">Full-volume radius (init op 0x4C, second float).</param>
    /// <param name="Far">Audible range (init op 0x4C, first float).</param>
    /// <param name="Path">Points of the path the sound follows (init op 0x6B → a 0x4A section), or empty.</param>
    /// <param name="TimeVolume">Volume by time of day (init op 0x68 → a 0x19 curve over the day fraction, applied by update op 0x43), or null.</param>
    /// <param name="AutoRun">Whether it starts on zone load (otherwise a routine spawns it).</param>
    /// <param name="FramesPerEmission">The generator's emission period (60 Hz frames), used to repeat one-shot sounds.</param>
    public sealed record ZoneSoundEmitter(string Name, int SoundId, Vector3 Position, float Near, float Far,
        IReadOnlyList<Vector3> Path, KeyFrameCurve? TimeVolume, bool AutoRun, int FramesPerEmission)
    {
        /// <summary>
        /// Where the sound is heard from for a listener: the base position, or for a path sound the nearest point on the
        /// path (provisional reading of retail's path sound).
        /// </summary>
        public Vector3 SourceFor(Vector3 listener)
        {
            if (Path.Count == 0)
            {
                return Position;
            }

            if (Path.Count == 1)
            {
                return Path[0];
            }

            Vector3 best = Path[0];
            float bestDistance = float.MaxValue;
            for (int i = 0; i + 1 < Path.Count; i++)
            {
                Vector3 a = Path[i];
                Vector3 ab = Path[i + 1] - a;
                float lengthSquared = ab.LengthSquared();
                float t = lengthSquared > 1e-6f ? Math.Clamp(Vector3.Dot(listener - a, ab) / lengthSquared, 0f, 1f) : 0f;
                Vector3 p = a + ab * t;
                float d = Vector3.DistanceSquared(p, listener);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = p;
                }
            }

            return best;
        }

        /// <summary>The time-of-day volume (1 without a curve) at a day fraction (0 = midnight).</summary>
        public float VolumeAt(float dayFraction) => TimeVolume is null ? 1f : Math.Clamp(TimeVolume.Evaluate(dayFraction), 0f, 1f);
    }

    /// <summary>
    /// Finds the sound generators of a zone model DAT and reads their sound, range, path and time-of-day volume.
    /// </summary>
    /// <remarks>
    /// <para>Op names referenced from xi-tools <c>docs/reference/ps2_beta_2001.md</c> (https://github.com/vekien/xi-tools):
    /// Section 2 (init) 0x4C <c>InitSoundElemParam</c> (audio range), 0x68 <c>InitCorrectKeyTimeVolume</c> (time-of-day
    /// volume key), 0x6B <c>InitPathSound</c> (path reference); Section 3 (update) 0x43 <c>IdleCorrectKeyTimeVolume</c>.
    /// The generator's linked data is a 0x3D section name resolved in its directory then its parents (xi-tools
    /// <c>docs/fx/effect_system.md</c>).</para>
    /// <para><b>Beyond xi-tools:</b> the argument layouts were read from the retail zone DATs: 0x4C is
    /// <c>f32 far, f32 near, f32 ?</c> (Bibiki Bay <c>mina</c> 60 / 10, <c>gake</c> 80 / 40, <c>hama</c> 60 / 25; Southern
    /// San d'Oria 8-30 / 0); 0x6B arg 0 and 0x68 arg 1 are four-character names (<c>mina</c>, <c>tmvo</c>). The 0x4A path
    /// section is <c>"RAB\0"</c>, u32 7, ..., u32 point count at +0x30, then from +0x40 points of 0x20 bytes, each starting
    /// with <c>f32 x, y, z, w</c> (w 10 in Bibiki Bay, 1 in San d'Oria; meaning unknown). San d'Oria's <c>tmvo</c> curve is
    /// silent until 06:00, loud 06:36-18:36, silent from 19:12 (day fractions 0.25 / 0.275 / 0.775 / 0.8).</para>
    /// </remarks>
    public static class ZoneSoundEmitterDecoder
    {
        private const int PathHeaderSize = 0x40;
        private const int PathPointStride = 0x20;

        /// <summary>Every sound generator under <paramref name="root"/>.</summary>
        public static List<ZoneSoundEmitter> Read(DatDirectoryNode root)
        {
            var result = new List<ZoneSoundEmitter>();
            foreach (DatResourceEntry entry in root.CollectByTypeRecursive(DatSectionType.ParticleGenerator))
            {
                ParticleGeneratorDefinition? def;
                try
                {
                    def = ParticleGeneratorDecoder.DecodeGenerator(entry.Payload.Span, entry.DatId);
                }
                catch (Exception)
                {
                    continue;
                }

                if (def?.Setup is not { LinkedDataType: ParticleLinkedDataType.Audio } setup)
                {
                    continue;
                }

                DatDirectoryNode dir = entry.Parent;
                DatResourceEntry? pointer = dir.SearchLocalAndParents(setup.LinkedDataId, DatSectionType.SoundEffectPointer);
                if (pointer is null || !SoundEffectPointer.TryDecode(pointer.Payload.Span, out int soundId))
                {
                    continue;
                }

                float far = 0f, near = 0f;
                IReadOnlyList<Vector3> path = Array.Empty<Vector3>();
                KeyFrameCurve? timeVolume = null;
                foreach (ParticleOpcode op in def.Initializers)
                {
                    switch (op.OpCode)
                    {
                        case 0x4C when op.Args.Length >= 2:
                            far = AsFloat(op.Args[0]);
                            near = AsFloat(op.Args[1]);
                            break;
                        case 0x6B when op.Args.Length >= 1:
                            if (dir.SearchLocalAndParents(FourCc(op.Args[0]), DatSectionType.Path) is { } pathEntry)
                            {
                                path = ReadPath(pathEntry.Payload.Span, setup.BasePosition.Y);
                            }

                            break;
                        case 0x68 when op.Args.Length >= 2:
                            if (dir.SearchLocalAndParents(FourCc(op.Args[1]), DatSectionType.ParticleKeyFrameData) is { } keyEntry)
                            {
                                timeVolume = ParticleKeyFrameDecoder.DecodeKeyFrame(keyEntry.Payload.Span, keyEntry.DatId);
                            }

                            break;
                    }
                }

                if (!float.IsFinite(far) || far <= 0f)
                {
                    far = 30f; // no range authored: a provisional default
                }

                near = float.IsFinite(near) ? Math.Clamp(near, 0f, far) : 0f;
                result.Add(new ZoneSoundEmitter(entry.DatId, soundId, setup.BasePosition, near, far, path, timeVolume,
                    def.AutoRun, def.FramesPerEmission));
            }

            return result;
        }

        /// <summary>
        /// Reads the points of a 0x4A path section payload. A path whose points all have height 0 (Bibiki Bay's
        /// shorelines) is taken as flat and given <paramref name="flatHeight"/> (the generator's height; provisional).
        /// </summary>
        public static Vector3[] ReadPath(ReadOnlySpan<byte> payload, float flatHeight = 0f)
        {
            if (payload.Length < PathHeaderSize || !payload.StartsWith("RAB"u8))
            {
                return Array.Empty<Vector3>();
            }

            int count = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0x30));
            count = Math.Clamp(count, 0, (payload.Length - PathHeaderSize) / PathPointStride);
            var points = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> p = payload.Slice(PathHeaderSize + i * PathPointStride);
                points[i] = new Vector3(
                    BinaryPrimitives.ReadSingleLittleEndian(p),
                    BinaryPrimitives.ReadSingleLittleEndian(p.Slice(4)),
                    BinaryPrimitives.ReadSingleLittleEndian(p.Slice(8)));
            }

            if (points.Length > 0 && Array.TrueForAll(points, p => p.Y == 0f))
            {
                for (int i = 0; i < points.Length; i++)
                {
                    points[i].Y = flatHeight;
                }
            }

            return points;
        }

        private static float AsFloat(uint bits) => BitConverter.UInt32BitsToSingle(bits);

        private static string FourCc(uint value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            int length = bytes.IndexOf((byte)0);
            return Encoding.ASCII.GetString(length < 0 ? bytes : bytes.Slice(0, length));
        }
    }
}
