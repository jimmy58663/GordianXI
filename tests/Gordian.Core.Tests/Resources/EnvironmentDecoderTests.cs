// tests/Gordian.Core.Tests/Resources/EnvironmentDecoderTests.cs
using System;
using System.Buffers.Binary;
using System.Numerics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class EnvironmentDecoderTests
    {
        private static byte[] BuildSyntheticEnvironmentPayload(
            bool indoors = false,
            float drawDistance = 280f,
            ushort spokes = 24,
            float radius = 950f)
        {
            byte[] payload = new byte[EnvironmentDecoder.MinimumPayloadSize];

            // Indoors (+0x00)
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x00, 4), indoors ? 1u : 0u);

            // Model Light (+0x0C): Sun, Moon, Ambient, Fog, FogEnd, FogStart, DiffuseMult
            payload[0x0C] = 255; payload[0x0D] = 250; payload[0x0E] = 240; payload[0x0F] = 255; // Sun (1.0, 0.98, 0.94)
            payload[0x10] = 20;  payload[0x11] = 30;  payload[0x12] = 40;  payload[0x13] = 255; // Moon
            payload[0x14] = 80;  payload[0x15] = 90;  payload[0x16] = 100; payload[0x17] = 255; // Ambient
            payload[0x18] = 128; payload[0x19] = 160; payload[0x1A] = 200; payload[0x1B] = 255; // Fog
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0x1C, 4), 220f); // FogEnd
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0x20, 4), 30f);  // FogStart
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0x24, 4), 1.2f); // DiffuseMult

            // Terrain Light (+0x2C)
            payload[0x2C] = 240; payload[0x2D] = 230; payload[0x2E] = 220; payload[0x2F] = 255;
            payload[0x30] = 10;  payload[0x31] = 15;  payload[0x32] = 20;  payload[0x33] = 255;
            payload[0x34] = 90;  payload[0x35] = 95;  payload[0x36] = 105; payload[0x37] = 255;
            payload[0x38] = 130; payload[0x39] = 170; payload[0x3A] = 210; payload[0x3B] = 255;
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0x3C, 4), 250f); // FogEnd
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0x40, 4), 40f);  // FogStart
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0x44, 4), 1.0f); // DiffuseMult

            // Clear color (+0x4C)
            payload[0x4C] = 100; payload[0x4D] = 150; payload[0x4E] = 220; payload[0x4F] = 255;

            // DrawDistance (+0x58), Spokes (+0x5E), Radius (+0x68)
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0x58, 4), drawDistance);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0x5E, 2), spokes);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0x68, 4), radius);

            // 8 Sky Slices: Colors at +0x6C, Elevations at +0x8C
            for (int i = 0; i < 8; i++)
            {
                byte c = (byte)(50 + (i * 25));
                payload[0x6C + (i * 4) + 0] = c;
                payload[0x6C + (i * 4) + 1] = c;
                payload[0x6C + (i * 4) + 2] = (byte)Math.Min(255, c + 30);
                payload[0x6C + (i * 4) + 3] = 255;

                float elev = (float)i / 7.0f; // 0.0 at horizon, 1.0 at zenith
                BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0x8C + (i * 4), 4), elev);
            }

            return payload;
        }

        [Fact]
        public void DecodeEnvironmentKeyframe_ValidPayload_ParsesAccurately()
        {
            byte[] payload = BuildSyntheticEnvironmentPayload(indoors: false, drawDistance: 320f, spokes: 20, radius: 1000f);

            var keyframe = EnvironmentDecoder.DecodeEnvironmentKeyframe(payload, "0600");

            Assert.NotNull(keyframe);
            Assert.Equal(6, keyframe.Hour);
            Assert.False(keyframe.Indoors);
            Assert.Equal(320f, keyframe.DrawDistance);
            Assert.Equal(20, keyframe.Spokes);
            Assert.Equal(1000f, keyframe.Radius);

            Assert.Equal(250f, keyframe.TerrainFogEnd);
            Assert.Equal(40f, keyframe.TerrainFogStart);
            Assert.Equal(8, keyframe.Slices.Count);
            Assert.Equal(0.0f, keyframe.Slices[0].Elevation);
            Assert.Equal(1.0f, keyframe.Slices[7].Elevation, 3);
        }

        [Fact]
        public void DecodeEnvironmentKeyframe_UndersizedPayload_ReturnsNull()
        {
            byte[] shortPayload = new byte[100];
            var keyframe = EnvironmentDecoder.DecodeEnvironmentKeyframe(shortPayload, "1200");
            Assert.Null(keyframe);
        }

        [Fact]
        public void ZoneEnvironmentData_Interpolate_SmoothlyBlendsKeyframes()
        {
            var envData = new ZoneEnvironmentData();

            var k06 = new EnvironmentKeyframe
            {
                Hour = 6,
                TerrainFogStart = 20f,
                TerrainFogEnd = 200f,
                ClearColor = new Vector4(0.2f, 0.4f, 0.6f, 1f)
            };
            k06.Slices.Add(new SkySlice(new Vector4(0.5f, 0.5f, 0.5f, 1f), 0.0f));
            k06.Slices.Add(new SkySlice(new Vector4(0.1f, 0.2f, 0.8f, 1f), 1.0f));

            var k12 = new EnvironmentKeyframe
            {
                Hour = 12,
                TerrainFogStart = 50f,
                TerrainFogEnd = 300f,
                ClearColor = new Vector4(0.6f, 0.8f, 1.0f, 1f)
            };
            k12.Slices.Add(new SkySlice(new Vector4(0.7f, 0.8f, 0.9f, 1f), 0.0f));
            k12.Slices.Add(new SkySlice(new Vector4(0.2f, 0.5f, 0.9f, 1f), 1.0f));

            envData.AddKeyframe("weat", k06);
            envData.AddKeyframe("weat", k12);

            // Interpolate at 9:00 AM (midway between 6 and 12, factor = 0.5)
            var mid = envData.Interpolate(9f, "weat");

            Assert.NotNull(mid);
            Assert.Equal(9, mid.Hour);
            Assert.Equal(35f, mid.TerrainFogStart, 2);
            Assert.Equal(250f, mid.TerrainFogEnd, 2);
            Assert.Equal(0.4f, mid.ClearColor.X, 2);
            Assert.Equal(0.6f, mid.ClearColor.Y, 2);
            Assert.Equal(0.8f, mid.ClearColor.Z, 2);

            Assert.Equal(2, mid.Slices.Count);
            Assert.Equal(0.6f, mid.Slices[0].Color.X, 2); // (0.5 + 0.7) / 2
        }

        [Fact]
        public void ParseZoneContainer_ParsesAndAttachesEnvironmentData()
        {
            // Build a container with 0x20 Texture, 0x2E ZoneMesh, and 0x2F Environment
            byte[] texPayload = new byte[16];
            byte[] texSection = BuildChunk(DatSectionType.Texture, texPayload);

            byte[] envPayload = BuildSyntheticEnvironmentPayload();
            byte[] envSection = BuildChunk(DatSectionType.Environment, envPayload, "1200");

            byte[] container = new byte[texSection.Length + envSection.Length];
            texSection.CopyTo(container, 0);
            envSection.CopyTo(container, texSection.Length);

            var zone = ZoneDataLoader.ParseZoneContainer(container, zoneId: 100);

            Assert.NotNull(zone);
            Assert.NotNull(zone.EnvironmentData);
            Assert.NotEmpty(zone.EnvironmentData.WeatherKeyframes);

            var noon = zone.EnvironmentData.Interpolate(12f);
            Assert.NotNull(noon);
            Assert.Equal(12, noon.Hour);
        }

        [Fact]
        public void ZoneEnvironmentSettings_Presets_HaveAuthenticVisibilityParameters()
        {
            var day = Gordian.Core.Graphics.ZoneEnvironmentSettings.CreateDay();
            Assert.False(day.FogEnabled);
            Assert.True(day.FogStart >= 300f);
            Assert.True(day.FogEnd >= 1000f);

            var night = Gordian.Core.Graphics.ZoneEnvironmentSettings.CreateNight();
            Assert.False(night.FogEnabled);
            Assert.True(night.FogStart >= 250f);

            var dusk = Gordian.Core.Graphics.ZoneEnvironmentSettings.CreateDusk();
            Assert.False(dusk.FogEnabled);

            var overcast = Gordian.Core.Graphics.ZoneEnvironmentSettings.CreateOvercast();
            Assert.True(overcast.FogEnabled);
            Assert.Equal(150f, overcast.FogStart);
            Assert.Equal(600f, overcast.FogEnd);
        }

        [Fact]
        public void ZoneEnvironmentSettings_ApplyKeyframe_PushesZeroFogStartToDistantHorizon()
        {
            var settings = new Gordian.Core.Graphics.ZoneEnvironmentSettings();

            // Retail DAT keyframe with FogStart = 0, FogEnd = 302
            var keyframe = new EnvironmentKeyframe
            {
                Hour = 12,
                TerrainFogStart = 0f,
                TerrainFogEnd = 302f,
                DrawDistance = 302f
            };

            settings.ApplyKeyframe(keyframe);

            Assert.True(settings.FogEnabled);
            Assert.Equal(302f, settings.FogEnd);
            // Must push FogStart to 75% of 302 (approx 226.5 yalms) rather than 0
            Assert.Equal(302f * 0.75f, settings.FogStart, 1);
        }

        [Fact]
        public void ZoneEnvironmentSettings_ApplyKeyframe_DisablesFogWhenFarIsNonPositive()
        {
            var settings = new Gordian.Core.Graphics.ZoneEnvironmentSettings();

            var keyframe = new EnvironmentKeyframe
            {
                Hour = 12,
                TerrainFogStart = 0f,
                TerrainFogEnd = 0f
            };

            settings.ApplyKeyframe(keyframe);

            Assert.False(settings.FogEnabled);
        }

        private static byte[] BuildChunk(DatSectionType type, byte[] payload, string datId = "")
        {
            int payloadPadded = (payload.Length + 15) & ~15;
            int total = 16 + payloadPadded;
            uint units = (uint)(total / 16);
            byte[] chunk = new byte[total];

            if (!string.IsNullOrEmpty(datId))
            {
                var idBytes = System.Text.Encoding.ASCII.GetBytes(datId);
                int copyLen = Math.Min(idBytes.Length, 4);
                Array.Copy(idBytes, 0, chunk, 0, copyLen);
            }

            uint meta = ((uint)type & 0x7F) | (units << 7);
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4, 4), meta);

            payload.CopyTo(chunk, 16);
            return chunk;
        }
    }
}
