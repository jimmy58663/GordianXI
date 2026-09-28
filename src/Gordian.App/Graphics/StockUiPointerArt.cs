// src/Gordian.App/Graphics/StockUiPointerArt.cs
using System;
using Gordian.Core.Resources.Graphics;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The mouse pointer's art, drawn in code (GordianXI's own, MIT): a yellow arrow and the ring shown over a
    /// clickable menu entry. Retail's arrow is a Windows cursor in the client's FFXiResource.dll and its hover ring is
    /// not in any UI DAT found so far; neither is copied. These are look-alikes in the same spirit (a warm yellow
    /// arrow with a dark outline; a grey ring), not traced from retail, so they differ in shape and shading.
    /// </summary>
    public static class StockUiPointerArt
    {
        /// <summary>Both images are this square size (RGBA, straight alpha).</summary>
        public const int Size = 32;

        /// <summary>The arrow's tip, the pointer's hotspot, in image pixels.</summary>
        public const int HotspotX = 1, HotspotY = 1;

        /// <summary>Where the ring's centre sits relative to the hotspot when drawn over an entry.</summary>
        public const float RingOffsetX = 2, RingOffsetY = 2;

        /// <summary>The ring's outer radius (image pixels), inside its dark rim.</summary>
        public const float RingOuterRadius = 5f;

        private static readonly Lazy<DecodedTexture> ArrowTexture = new(() => new DecodedTexture("gordian:pointer-arrow", Size, Size, BuildArrow()));
        private static readonly Lazy<DecodedTexture> RingTexture = new(() => new DecodedTexture("gordian:pointer-ring", Size, Size, BuildRing()));

        public static DecodedTexture Arrow => ArrowTexture.Value;
        public static DecodedTexture Ring => RingTexture.Value;

        // The arrow outline, tip first (image pixels): a classic pointer with a short tail.
        private static readonly (float X, float Y)[] ArrowShape =
        {
            (1, 1), (1, 16), (4.8f, 12.6f), (7.6f, 18.4f), (10.2f, 17.2f), (7.5f, 11.6f), (12.4f, 11.6f),
        };

        private static byte[] BuildArrow()
        {
            // Coverage by 4 x 4 supersampling, then a one-pixel dark outline grown around it.
            var fill = new float[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < 4; sy++)
                    {
                        for (int sx = 0; sx < 4; sx++)
                        {
                            if (Contains(ArrowShape, x + (sx + 0.5f) / 4, y + (sy + 0.5f) / 4)) inside++;
                        }
                    }
                    fill[y * Size + x] = inside / 16f;
                }
            }

            var rgba = new byte[Size * Size * 4];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float f = fill[y * Size + x];
                    float edge = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && ny >= 0 && nx < Size && ny < Size) edge = Math.Max(edge, fill[ny * Size + nx]);
                        }
                    }
                    // Pale yellow at the tip warming to amber at the tail, lit from the upper left.
                    float t = Math.Clamp((x + y - 2) / 24f, 0, 1);
                    float r = 255, g = Lerp(246, 176, t), b = Lerp(168, 40, t);
                    var (outR, outG, outB) = (72f, 40f, 12f);
                    float a = Math.Max(f, edge);
                    int i = (y * Size + x) * 4;
                    if (a <= 0) continue;
                    float w = f / a; // share of fill over outline
                    rgba[i] = (byte)Lerp(outR, r, w);
                    rgba[i + 1] = (byte)Lerp(outG, g, w);
                    rgba[i + 2] = (byte)Lerp(outB, b, w);
                    rgba[i + 3] = (byte)(255 * a);
                }
            }
            return rgba;
        }

        private static byte[] BuildRing()
        {
            // A thin grey ring (outer radius 5, inner 3.5) with a dark rim, its hollow faintly lit; centred in the image.
            const float cx = Size / 2f, cy = Size / 2f, outer = RingOuterRadius, inner = 3.5f, rimWidth = 0.75f;
            var rgba = new byte[Size * Size * 4];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float ring = 0, rim = 0, hollow = 0;
                    for (int sy = 0; sy < 4; sy++)
                    {
                        for (int sx = 0; sx < 4; sx++)
                        {
                            float px = x + (sx + 0.5f) / 4 - cx, py = y + (sy + 0.5f) / 4 - cy;
                            float d = MathF.Sqrt(px * px + py * py);
                            if (d <= outer && d >= inner) ring++;
                            else if ((d > outer && d <= outer + rimWidth) || (d < inner && d >= inner - rimWidth)) rim++;
                            else if (d < inner - rimWidth) hollow++;
                        }
                    }
                    ring /= 16; rim /= 16; hollow /= 16;
                    float a = ring + rim + hollow * 0.35f;
                    if (a <= 0) continue;
                    // The ring shades from light at the upper left to mid grey at the lower right.
                    float shade = Lerp(236, 150, Math.Clamp((x + y - 12) / 18f, 0, 1));
                    float c = (ring * shade + rim * 48 + hollow * 0.35f * 255) / a;
                    int i = (y * Size + x) * 4;
                    rgba[i] = rgba[i + 1] = (byte)c;
                    rgba[i + 2] = (byte)Math.Min(255, c + 6);
                    rgba[i + 3] = (byte)(255 * Math.Min(1, a));
                }
            }
            return rgba;
        }

        private static bool Contains((float X, float Y)[] polygon, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                var (xi, yi) = polygon[i];
                var (xj, yj) = polygon[j];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
