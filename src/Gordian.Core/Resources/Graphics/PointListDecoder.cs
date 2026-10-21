// src/Gordian.Core/Resources/Graphics/PointListDecoder.cs
using System;
using System.Buffers.Binary;
using System.Numerics;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// Section 0x3E PointList: the path a particle follows (initializer 0x54, updater 0x34). Payload: u32 point count,
    /// 12 bytes of padding, then one 16-byte point per entry (x, y, z as f32 and a fourth f32, always 1 in the data read),
    /// offsets from the particle's origin in raw DAT axes.
    /// <para>
    /// Read from the retail DATs (2026-10-19): Phanauet Channel's bird path <c>weat/*/tobi/tbil</c> holds 7 points in a
    /// 128-byte payload, (0.41, 0, 39.41), (38.51, 0, 0.81), (0.28, 0, -41.45), (-40.63, 0, 0.31), (-24.25, 0, 33.99),
    /// (-10.98, 0, 39.66), (0.40, 0, 39.40): a circle of about 40 yalms in the horizontal plane, back to its start; the dive
    /// path <c>tbi1</c> is the same shape at about 12 yalms. Section type referenced from xi-model-viewer
    /// (https://github.com/vekien/xi-model-viewer, ui/js/dat/sections.js parsePointList). **Differs from xi-model-viewer:**
    /// it reads 12-byte points straight after the count, which lands between the padding and the fourth component.
    /// </para>
    /// </summary>
    public static class PointListDecoder
    {
        private const int HeaderSize = 16;
        private const int PointSize = 16;

        /// <summary>The points, or null when the payload holds none.</summary>
        public static Vector3[]? Decode(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < HeaderSize + PointSize) return null;
            int count = BinaryPrimitives.ReadInt32LittleEndian(payload);
            if (count <= 0) return null;
            count = Math.Min(count, (payload.Length - HeaderSize) / PointSize);
            var points = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                var point = payload.Slice(HeaderSize + i * PointSize);
                points[i] = new Vector3(
                    BinaryPrimitives.ReadSingleLittleEndian(point),
                    BinaryPrimitives.ReadSingleLittleEndian(point.Slice(4)),
                    BinaryPrimitives.ReadSingleLittleEndian(point.Slice(8)));
            }
            return points;
        }

        /// <summary>
        /// The position <paramref name="progress"/> (0-1) along the path: linear between consecutive points, as
        /// xi-model-viewer's PointListPositionUpdater samples it (after xim Path.getSplinePosition).
        /// </summary>
        public static Vector3 Sample(ReadOnlySpan<Vector3> points, float progress)
        {
            if (points.Length == 0) return Vector3.Zero;
            if (points.Length == 1) return points[0];
            float t = Math.Clamp(float.IsFinite(progress) ? progress : 0f, 0f, 1f) * (points.Length - 1);
            int i = Math.Min(points.Length - 2, (int)MathF.Floor(t));
            return Vector3.Lerp(points[i], points[i + 1], t - i);
        }
    }
}
