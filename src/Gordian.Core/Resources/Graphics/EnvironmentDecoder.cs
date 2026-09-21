// src/Gordian.Core/Resources/Graphics/EnvironmentDecoder.cs
using System;
using System.Buffers.Binary;
using System.Numerics;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// Clean-room binary decoder for FFXI DAT Section 0x2F (Environment and Time-of-Day Lighting) chunks.
    /// Extracts time-of-day keyframes containing sun, moon, ambient, and fog parameters,
    /// clear color, draw distance, and sky dome elevation slices.
    /// Protocol specification referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer)
    /// and xi-tools (xim EnvironmentSection).
    /// </summary>
    public static class EnvironmentDecoder
    {
        public const int MinimumPayloadSize = 0xAC; // 172 bytes

        /// <summary>
        /// Decodes a Section 0x2F payload into an <see cref="EnvironmentKeyframe"/>.
        /// </summary>
        /// <param name="payload">Section 0x2F payload data excluding the 16-byte chunk header.</param>
        /// <param name="datId">4-character chunk identifier string representing the time (e.g. '0600' for 6:00 AM).</param>
        /// <returns>Decoded keyframe or null if the payload is undersized or malformed.</returns>
        public static EnvironmentKeyframe? DecodeEnvironmentKeyframe(ReadOnlySpan<byte> payload, string datId)
        {
            if (payload.Length < MinimumPayloadSize)
            {
                return null;
            }

            int hour = 12;
            if (!string.IsNullOrEmpty(datId))
            {
                string cleanId = datId.Trim();
                if (int.TryParse(cleanId, out int rawHhmm))
                {
                    hour = Math.Clamp(rawHhmm / 100, 0, 23);
                }
            }

            uint indoorsVal = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x00, 4));
            bool indoors = indoorsVal == 1;

            // Model Lighting (+0x0C)
            var modelSun = ReadRgbaNormalized(payload.Slice(0x0C, 4));
            var modelMoon = ReadRgbaNormalized(payload.Slice(0x10, 4));
            var modelAmbient = ReadRgbaNormalized(payload.Slice(0x14, 4));
            var modelFog = ReadRgbaNormalized(payload.Slice(0x18, 4));
            float modelFogEnd = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(0x1C, 4));
            float modelFogStart = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(0x20, 4));
            float modelDiffuseMult = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(0x24, 4));

            // Terrain Lighting (+0x2C)
            var terrainSun = ReadRgbaNormalized(payload.Slice(0x2C, 4));
            var terrainMoon = ReadRgbaNormalized(payload.Slice(0x30, 4));
            var terrainAmbient = ReadRgbaNormalized(payload.Slice(0x34, 4));
            var terrainFog = ReadRgbaNormalized(payload.Slice(0x38, 4));
            float terrainFogEnd = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(0x3C, 4));
            float terrainFogStart = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(0x40, 4));
            float terrainDiffuseMult = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(0x44, 4));

            // View & Dome Configuration
            var clearColor = ReadRgbaNormalized(payload.Slice(0x4C, 4));
            float drawDistance = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(0x58, 4));
            ushort spokes = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0x5E, 2));
            float radius = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(0x68, 4));

            if (spokes == 0) spokes = 16;
            if (radius <= 0f) radius = 1000f;
            if (drawDistance <= 0f) drawDistance = 300f;

            var keyframe = new EnvironmentKeyframe
            {
                Hour = hour,
                Indoors = indoors,
                ClearColor = clearColor,
                DrawDistance = drawDistance,
                Spokes = spokes,
                Radius = radius,

                TerrainSunColor = terrainSun,
                TerrainMoonColor = terrainMoon,
                TerrainAmbientColor = terrainAmbient,
                TerrainFogColor = terrainFog,
                TerrainFogStart = terrainFogStart,
                TerrainFogEnd = terrainFogEnd,
                TerrainDiffuseMult = terrainDiffuseMult,

                ModelSunColor = modelSun,
                ModelMoonColor = modelMoon,
                ModelAmbientColor = modelAmbient,
                ModelFogColor = modelFog,
                ModelFogStart = modelFogStart,
                ModelFogEnd = modelFogEnd,
                ModelDiffuseMult = modelDiffuseMult
            };

            // Sky Slices (+0x6C for colors, +0x8C for elevations)
            for (int i = 0; i < 8; i++)
            {
                var sliceColor = ReadRgbaNormalized(payload.Slice(0x6C + (i * 4), 4));
                float sliceElev = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(0x8C + (i * 4), 4));
                keyframe.Slices.Add(new SkySlice(sliceColor, sliceElev));
            }

            return keyframe;
        }

        private static Vector4 ReadRgbaNormalized(ReadOnlySpan<byte> span)
        {
            return new Vector4(
                span[0] / 255.0f,
                span[1] / 255.0f,
                span[2] / 255.0f,
                span[3] / 255.0f);
        }
    }
}
