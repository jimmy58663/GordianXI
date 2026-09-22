// tests/Gordian.App.Tests/Graphics/SkyDomeRendererTests.cs
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Gordian.App.Graphics;
using Gordian.Core.Graphics;
using Gordian.Core.Resources.Graphics;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    public class SkyDomeRendererTests
    {
        [Fact]
        public void SkyDomeVertex_HasExpectedMemoryLayout()
        {
            // Vertex: Vector3 Position (12 bytes) + Vector4 Color (16 bytes) = 28 bytes
            int size = Marshal.SizeOf<SkyDomeVertex>();
            Assert.Equal(28, size);
        }

        [Fact]
        public void GenerateDomeVertices_ProceduralDay_GeneratesValidTriangles()
        {
            var env = ZoneEnvironmentSettings.CreateDay();
            var vertices = SkyDomeRenderer.GenerateDomeVertices(env);

            Assert.NotEmpty(vertices);
            Assert.Equal(0, vertices.Length % 3); // Must form complete triangles

            float maxRadius = 0f;
            float maxElevationY = float.MinValue;
            float minElevationY = float.MaxValue;

            for (int i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i];
                Assert.False(float.IsNaN(v.Position.X));
                Assert.False(float.IsNaN(v.Position.Y));
                Assert.False(float.IsNaN(v.Position.Z));

                Assert.InRange(v.Color.X, 0f, 1f);
                Assert.InRange(v.Color.Y, 0f, 1f);
                Assert.InRange(v.Color.Z, 0f, 1f);

                float distXZ = MathF.Sqrt(v.Position.X * v.Position.X + v.Position.Z * v.Position.Z);
                if (distXZ > maxRadius) maxRadius = distXZ;
                if (v.Position.Y > maxElevationY) maxElevationY = v.Position.Y;
                if (v.Position.Y < minElevationY) minElevationY = v.Position.Y;
            }

            Assert.True(maxRadius > 800f, $"Expected sky dome radius ~950, got {maxRadius}");
            Assert.True(maxElevationY > 800f, $"Expected celestial zenith Y ~950, got {maxElevationY}");
            Assert.True(minElevationY < 0f, $"Expected lower skirt below horizon, got {minElevationY}");
        }

        [Fact]
        public void GenerateDomeVertices_WithExplicitSlices_UsesAuthoredBands()
        {
            var env = new ZoneEnvironmentSettings
            {
                Spokes = 16
            };
            env.SkySlices.Add(new SkySlice(new Vector4(0.8f, 0.4f, 0.2f, 1.0f), 0.0f)); // Horizon sunset orange
            env.SkySlices.Add(new SkySlice(new Vector4(0.4f, 0.2f, 0.6f, 1.0f), 0.5f)); // Mid twilight purple
            env.SkySlices.Add(new SkySlice(new Vector4(0.1f, 0.1f, 0.3f, 1.0f), 1.0f)); // Zenith night blue

            var vertices = SkyDomeRenderer.GenerateDomeVertices(env);

            Assert.NotEmpty(vertices);
            // 3 bands (including prepended -0.15 skirt) * 16 spokes * 2 triangles/quad * 3 vertices/triangle = 288 vertices
            Assert.Equal(288, vertices.Length);

            // Zenith vertices should have highest Y and near-zero XZ
            SkyDomeVertex highest = vertices[0];
            SkyDomeVertex lowest = vertices[0];
            for (int i = 1; i < vertices.Length; i++)
            {
                if (vertices[i].Position.Y > highest.Position.Y)
                {
                    highest = vertices[i];
                }
                if (vertices[i].Position.Y < lowest.Position.Y)
                {
                    lowest = vertices[i];
                }
            }

            Assert.True(highest.Position.Y > 900f);
            Assert.True(lowest.Position.Y < 0f, "Lowest elevation should be negative due to skirt");
            Assert.Equal(0.1f, highest.Color.X, 2);
            Assert.Equal(0.1f, highest.Color.Y, 2);
            Assert.Equal(0.3f, highest.Color.Z, 2);
            Assert.Equal(0.8f, lowest.Color.X, 2); // Matches horizon slice color
        }

        [Fact]
        public void Presets_HaveDistinctSkyColors()
        {
            var day = ZoneEnvironmentSettings.CreateDay();
            var night = ZoneEnvironmentSettings.CreateNight();
            var dusk = ZoneEnvironmentSettings.CreateDusk();
            var overcast = ZoneEnvironmentSettings.CreateOvercast();

            // Day zenith is bright blue
            Assert.True(day.SkyZenithColor.Z > day.SkyZenithColor.X);

            // Night is very dark
            Assert.True(night.SkyZenithColor.X < 0.1f && night.SkyZenithColor.Y < 0.1f);

            // Dusk horizon is warm orange/amber (red > blue)
            Assert.True(dusk.SkyHorizonColor.X > dusk.SkyHorizonColor.Z);

            // Overcast has balanced neutral grey values
            Assert.InRange(MathF.Abs(overcast.SkyZenithColor.X - overcast.SkyZenithColor.Y), 0f, 0.1f);
        }

        [Fact]
        public void Presets_SynchronizeClearColorAndFogColorWithSkyHorizonColor()
        {
            var day = ZoneEnvironmentSettings.CreateDay();
            Assert.Equal(day.SkyHorizonColor, day.ClearColor);
            Assert.Equal(day.SkyHorizonColor, day.FogColor);

            var night = ZoneEnvironmentSettings.CreateNight();
            Assert.Equal(night.SkyHorizonColor, night.ClearColor);
            Assert.Equal(night.SkyHorizonColor, night.FogColor);

            var dusk = ZoneEnvironmentSettings.CreateDusk();
            Assert.Equal(dusk.SkyHorizonColor, dusk.ClearColor);
            Assert.Equal(dusk.SkyHorizonColor, dusk.FogColor);

            var overcast = ZoneEnvironmentSettings.CreateOvercast();
            Assert.Equal(overcast.SkyHorizonColor, overcast.ClearColor);
            Assert.Equal(overcast.SkyHorizonColor, overcast.FogColor);
        }
    }
}
