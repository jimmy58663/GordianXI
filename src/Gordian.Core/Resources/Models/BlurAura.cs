// src/Gordian.Core/Resources/Models/BlurAura.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Containers;

namespace Gordian.Core.Resources.Models
{
    /// <summary>One ring of a <see cref="BlurAura"/>: its tint (0-1), strength (the raw alpha byte) and radius.</summary>
    public readonly record struct BlurAuraRing(Vector3 Color, byte Alpha, float Radius);

    /// <summary>
    /// A model's blur aura, Section 0x5E (<see cref="DatSectionType.Blur"/>): the shimmering glow baked into the summoned
    /// avatars (models 16-23, 25, 28, 30), the Prime avatars (791-798) and about 40 other models (#82).
    /// <para>
    /// Layout after the 16-byte section header, from xi-tools (https://github.com/vekien/xi-tools,
    /// src/xi/entity/xi_blur.py and docs/entity/recolor.md "Blur aura", after xim): byte 0 layer count, byte 1 shimmer
    /// frequency, bytes 2-3 edge falloff (u16), bytes 4-7 and 8-11 two base colours (RGBA), bytes 12-15 the inner radius
    /// (f32), then 8-byte rings (RGB, alpha, radius f32). Every one of the 57 retail sections (model ids 0-3192,
    /// 2026-10-18) has seven rings with a radius, one ring at radius 0 and (all but Ifrit's 80-byte section) a closing
    /// record (0, 0, 128 or 160, alpha 63 or 64); <see cref="Rings"/> stops at the first ring under <see cref="MinRingRadius"/>.
    /// </para>
    /// <para>
    /// Garuda (model 21) authors neutral grey rings, alpha 10-12, radii 60-130 with base colour 192; Fenrir (17) seven
    /// cyan (128, 255, 255) rings, alpha 16, radius 110. That matches the maintainer's retail capture
    /// (<c>SMN_Avatars.mp4</c>, 2026-10-17): a wide neutral halo around Garuda, a thin cyan rim around Fenrir.
    /// </para>
    /// </summary>
    public sealed record BlurAura(
        byte Layers,
        byte Frequency,
        ushort Falloff,
        Vector4 BaseColor,
        Vector4 SecondColor,
        float InnerRadius,
        IReadOnlyList<BlurAuraRing> Rings)
    {
        private const int FixedSize = 16;
        private const int RingSize = 8;

        /// <summary>Rings below this radius end the list (retail radii are 20-140).</summary>
        public const float MinRingRadius = 1.0f;

        /// <summary>Decodes a 0x5E section body (after its 16-byte header); null when it is too short.</summary>
        public static BlurAura? Decode(ReadOnlySpan<byte> body)
        {
            if (body.Length < FixedSize) return null;
            var rings = new List<BlurAuraRing>();
            for (int o = FixedSize; o + RingSize <= body.Length; o += RingSize)
            {
                float radius = BinaryPrimitives.ReadSingleLittleEndian(body.Slice(o + 4, 4));
                // The ring "at radius 0" is not always exactly 0 (Garuda's reads as a tiny positive value).
                if (!(radius >= MinRingRadius) || float.IsInfinity(radius)) break;
                rings.Add(new BlurAuraRing(new Vector3(body[o], body[o + 1], body[o + 2]) / 255.0f, body[o + 3], radius));
            }
            return new BlurAura(
                body[0],
                body[1],
                BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(2, 2)),
                Color(body.Slice(4, 4)),
                Color(body.Slice(8, 4)),
                BinaryPrimitives.ReadSingleLittleEndian(body.Slice(12, 4)),
                rings);
        }

        /// <summary>The first 0x5E section of a model DAT, decoded; null when the DAT has none.</summary>
        public static BlurAura? Find(ReadOnlySpan<byte> dat)
        {
            foreach (var header in DatSectionWalker.ReadHeaders(dat))
            {
                if (header.TypeCode != DatSectionType.Blur || header.DataOffset + header.DataSizeBytes > dat.Length) continue;
                return Decode(dat.Slice(header.DataOffset, header.DataSizeBytes));
            }
            return null;
        }

        private static Vector4 Color(ReadOnlySpan<byte> rgba) => new Vector4(rgba[0], rgba[1], rgba[2], rgba[3]) / 255.0f;
    }
}
