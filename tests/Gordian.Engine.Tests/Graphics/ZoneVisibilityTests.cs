using System.Numerics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Engine.Graphics;
using Xunit;

namespace Gordian.Engine.Tests.Graphics
{
    public class ZoneVisibilityTests
    {
        [Theory]
        [InlineData(252.5f, 1.0f, 252.5f)]
        [InlineData(252.5f, 3.0f, 757.5f)]  // West Ronfaure at the default multiplier
        [InlineData(10.0f, 1.0f, 43.0f)]    // never under the client's 43-yalm floor
        public void GlobalDrawDistance_MultipliesAndFloors(float environment, float multiplier, float expected)
        {
            Assert.Equal(expected, ZoneVisibility.GlobalDrawDistance(environment, multiplier), 3);
        }

        [Theory]
        [InlineData(5.0f, LodSlots.High)]
        [InlineData(10.0f, LodSlots.Mid)]   // the near threshold is exclusive
        [InlineData(100.0f, LodSlots.Mid)]  // the mid threshold is inclusive
        [InlineData(100.5f, LodSlots.Low)]
        public void SelectSlot_UsesTheRecordThresholds(float distance, LodSlots expected)
        {
            Assert.Equal(expected, ZoneVisibility.SelectSlot(distance * distance, 10.0f, 100.0f));
        }

        private static PlacementVisibility Placement(float x, float own = 0.0f, LodSlots slots = LodSlots.All, int record = 0) =>
            new(record, new Vector3(x, 0, 0), own, slots, 10.0f, 100.0f);

        [Fact]
        public void IsVisible_CullsAtTheOwnOrTheGlobalDistance()
        {
            Assert.True(ZoneVisibility.IsVisible(Placement(59, own: 60), Vector3.Zero, null, -1, 500.0f, 1.0f));
            Assert.False(ZoneVisibility.IsVisible(Placement(61, own: 60), Vector3.Zero, null, -1, 500.0f, 1.0f));
            Assert.True(ZoneVisibility.IsVisible(Placement(170, own: 60), Vector3.Zero, null, -1, 500.0f, 3.0f));
            Assert.True(ZoneVisibility.IsVisible(Placement(499), Vector3.Zero, null, -1, 500.0f, 1.0f));
            Assert.False(ZoneVisibility.IsVisible(Placement(501), Vector3.Zero, null, -1, 500.0f, 1.0f));
        }

        [Fact]
        public void IsVisible_DrawsOnlyTheSlotForTheDistance()
        {
            Assert.True(ZoneVisibility.IsVisible(Placement(5, slots: LodSlots.High), Vector3.Zero, null, -1, 500.0f));
            Assert.False(ZoneVisibility.IsVisible(Placement(5, slots: LodSlots.Mid | LodSlots.Low), Vector3.Zero, null, -1, 500.0f));
            Assert.True(ZoneVisibility.IsVisible(Placement(200, slots: LodSlots.Mid | LodSlots.Low), Vector3.Zero, null, -1, 500.0f));
        }

        [Fact]
        public void IsVisible_ScalesTheDetailThresholds()
        {
            // West Ronfaure's trees switch from _h at 30 yalms; the default scale keeps them high detail to 50.
            var high = new PlacementVisibility(0, new Vector3(45, 0, 0), 0.0f, LodSlots.High, 30.0f, 50.0f);
            Assert.False(ZoneVisibility.IsVisible(high, Vector3.Zero, null, -1, 500.0f, 3.0f, lodScale: 1.0f));
            Assert.True(ZoneVisibility.IsVisible(high, Vector3.Zero, null, -1, 500.0f, 3.0f, ZoneVisibility.DefaultLodDistanceScale));
            Assert.False(ZoneVisibility.IsVisible(high with { Origin = new Vector3(51, 0, 0) }, Vector3.Zero, null, -1, 500.0f));
        }

        [Theory]
        [InlineData(false, 3.0f, 900.0f, 1200.0f)]
        [InlineData(false, 1.0f, 300.0f, 400.0f)]
        [InlineData(true, 3.0f, 0.0f, 400.0f)] // indoors the authored fog stays
        public void ApplyKeyframe_MovesTheOutdoorFogWithTheMultiplier(bool indoors, float multiplier, float fogStart, float fogEnd)
        {
            var env = new ZoneEnvironmentSettings { DrawDistanceMultiplier = multiplier };
            env.ApplyKeyframe(new EnvironmentKeyframe { Indoors = indoors, TerrainFogStart = 0.0f, TerrainFogEnd = 400.0f, DrawDistance = 300.0f });
            Assert.Equal(fogStart, env.FogStart, 3);
            Assert.Equal(fogEnd, env.FogEnd, 3);
        }

        [Theory]
        [InlineData(false, 3.0f, 679.5f, 906.0f)] // Valkurm mist: stretched out to the 906-yalm cut
        [InlineData(true, 3.0f, 0.0f, 148.5f)]    // indoors the authored fog stays
        public void ApplyKeyframe_NeverClosesTheOutdoorFogBeforeTheDrawDistance(bool indoors, float multiplier, float fogStart, float fogEnd)
        {
            var env = new ZoneEnvironmentSettings { DrawDistanceMultiplier = multiplier };
            env.ApplyKeyframe(new EnvironmentKeyframe { Indoors = indoors, TerrainFogStart = 0.0f, TerrainFogEnd = 148.5f, DrawDistance = 302.0f });
            Assert.Equal(fogStart, env.FogStart, 1);
            Assert.Equal(fogEnd, env.FogEnd, 3);
        }

        [Fact]
        public void IsVisible_HonoursTheCameraFloorsTable()
        {
            // Table 0 lists record 1 only.
            byte[] payload = new byte[0x40];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x14, 4), 0x20);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x20, 4), 1);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x24, 4), 1);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x28, 4), 1);
            var tables = ZoneCullingTables.Parse(payload);

            Assert.True(ZoneVisibility.IsVisible(Placement(5, record: 1), Vector3.Zero, tables, 0, 500.0f));
            Assert.False(ZoneVisibility.IsVisible(Placement(5, record: 0), Vector3.Zero, tables, 0, 500.0f));
            Assert.True(ZoneVisibility.IsVisible(Placement(5, record: 0), Vector3.Zero, tables, -1, 500.0f)); // no table: all draw
            Assert.True(ZoneVisibility.IsVisible(Placement(5, record: -1), Vector3.Zero, tables, 0, 500.0f)); // generator-bound
            Assert.True(ZoneVisibility.IsVisible(PlacementVisibility.Always, new Vector3(9000, 0, 0), tables, 0, 10.0f));
        }
    }
}
