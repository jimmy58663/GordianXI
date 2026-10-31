// tests/Gordian.App.Tests/Graphics/StockUiEventMapWindowTests.cs
using System.Diagnostics;
using System.Numerics;
using Gordian.App.Graphics;
using Gordian.Core.Resources.Tables;
using Gordian.Engine.Events;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>The event map (#168): the home point map and the intro's map tutorial (GORDIAN_UI_DUMP writes the frames).</summary>
    public class StockUiEventMapWindowTests
    {
        /// <summary>
        /// The maintainer's retail screenshots (round 1 of #168, 2556 x 1438): the crystal at (853, 571) for Northern San
        /// d'Oria #1 and #4, at (989, 571) for Southern San d'Oria #2, whose map is held with its right edge at x = 1930.
        /// </summary>
        [Theory]
        [InlineData(3, 853, 571, 0)] // Northern San d'Oria #1 (image-5)
        [InlineData(98, 853, 571, 0)] // Northern San d'Oria #4 (image-7)
        [InlineData(1, 989, 571, 1930)] // Southern San d'Oria #2 (image-9)
        public void HomePointMap_PutsThePinWhereRetailDoes(int number, int pinX, int pinY, int mapRightEdge)
        {
            using var screen = StockUiOffscreen.TryCreate(2556, 1438);
            if (screen == null) return;
            var state = EventMapState.Closed.WithQueryEntry(1, new EventQueryMapEntry(1, EventQueryMapEntry.HomePointKind, number, 0, 0, 0)).WithQueryMap();
            var focus = state.Focus(1)!;
            var map = ZoneMapFiles.LoadWithOrigin(screen.Resources, focus.Zone, 0);
            Assert.NotNull(map);
            var window = new StockUiEventMapWindow();
            var shot = screen.Render(r => window.Draw(r, screen.Library, screen.Font, screen.LogFont, focus, 0, map!.Value.Texture, map.Value.Origin,
                "San d'Oria", 1, screen.Width, screen.Height), $"gpu_event_map_home_point_{number}.png");
            // The pale-blue crystal within a few px of retail's.
            Assert.True(HasPin(screen, shot, pinX, pinY, 8), $"no pin near ({pinX}, {pinY})");
            if (mapRightEdge > 0)
            {
                // Black past the map's right edge, parchment just inside it.
                var past = screen.Pixel(shot, mapRightEdge + 12, 900);
                Assert.True(past.R < 8 && past.G < 8 && past.B < 8, $"past the edge {past}");
                var inside = screen.Pixel(shot, mapRightEdge - 30, 900);
                Assert.True(inside.R > 60, $"inside the edge {inside}");
            }
        }

        /// <summary>
        /// Round 2 of #168: the crystal stands upright, as retail's 13 x 5 px one does (the DAT's sprite leans right). Drawn
        /// eight times larger, its pale-blue pixels are a tall, narrow shape whose top and bottom sit over its centre.
        /// </summary>
        [Fact]
        public void HomePointCrystal_StandsUpright()
        {
            using var screen = StockUiOffscreen.TryCreate(256, 256);
            if (screen == null) return;
            var shot = screen.Render(r => StockUiEventMapWindow.DrawPin(r, null, EventMapPin.HomePoint, string.Empty, 128, 128, 8),
                "gpu_event_map_crystal.png");
            int minX = int.MaxValue, maxX = 0, minY = int.MaxValue, maxY = 0;
            float topX = 0, bottomX = 0;
            for (int y = 0; y < 256; y++)
            {
                int rowMin = int.MaxValue, rowMax = -1;
                for (int x = 0; x < 256; x++)
                {
                    var p = screen.Pixel(shot, x, y);
                    if (p.B < 60) continue;
                    (rowMin, rowMax) = (Math.Min(rowMin, x), Math.Max(rowMax, x));
                }
                if (rowMax < 0) continue;
                if (minY == int.MaxValue) topX = (rowMin + rowMax) * 0.5f;
                bottomX = (rowMin + rowMax) * 0.5f;
                (minX, maxX, minY, maxY) = (Math.Min(minX, rowMin), Math.Max(maxX, rowMax), Math.Min(minY, y), y);
            }
            int width = maxX - minX + 1, height = maxY - minY + 1;
            Assert.True(height > 2 * width, $"crystal {width} x {height}");
            Assert.InRange(height, 90, 115); // 13 px at scale 1 is 104 at 8
            Assert.InRange(topX, 128 - 8, 128 + 8);
            Assert.InRange(bottomX, 128 - 8, 128 + 8);
        }

        private static bool HasPin(StockUiOffscreen screen, byte[] shot, int x, int y, int radius)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    var p = screen.Pixel(shot, x + dx, y + dy);
                    if (p.B > p.R + 20 && p.B > 120) return true;
                }
            }
            return false;
        }

        [Fact]
        public void Hold_LetsTheMapsEdgeInOnlyAQuarterOfTheScreen()
        {
            // The map's near edge for a 2560-px map on a 2556-px screen: free between -630 and 626.
            Assert.Equal(-100f, StockUiEventMapWindow.Hold(-100, 2560, 2556));
            Assert.Equal(1930f - 2560f, StockUiEventMapWindow.Hold(-765, 2560, 2556), 0);
            Assert.Equal(2556f * StockUiEventMapWindow.EdgeShare, StockUiEventMapWindow.Hold(900, 2560, 2556), 0);
        }

        [Fact]
        public void IntroMap_IsNotHeldAtItsEdge_SoTheScreenIsBlackPastIt()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;
            // A marker near Southern San d'Oria's west edge: past the map's left edge the screen is black.
            var focus = EventMapState.Closed.Open(230, 0).Mark(new EventMapMarker(0, -270, -10, "Ailevia")).Focus(null)!;
            var map = ZoneMapFiles.LoadWithOrigin(screen.Resources, 230, 0)!.Value;
            var window = new StockUiEventMapWindow();
            var shot = screen.Render(r => window.Draw(r, screen.Library, screen.Font, screen.LogFont, focus, 0, map.Texture, map.Origin,
                "Southern San d'Oria", 1, screen.Width, screen.Height), "gpu_event_map_intro.png");
            var left = screen.Pixel(shot, 8, (int)screen.Height / 2);
            Assert.True(left.R < 8 && left.G < 8 && left.B < 8, $"left edge pixel {left}");
        }

        [Fact]
        public void ANewPointOnTheSameMap_JumpsMostOfTheWayThenSlides_ANewMapSnaps()
        {
            var window = new StockUiEventMapWindow();
            long now = Stopwatch.GetTimestamp();
            window.Advance((231, 0), new Vector2(100, 100), now);
            Assert.Equal(new Vector2(100, 100), window.Center);
            window.Advance((231, 0), new Vector2(200, 100), now);
            // Jumped JumpShare of the way at once.
            Assert.Equal(100 + 100 * StockUiEventMapWindow.JumpShare, window.Center.X, 3);
            long later = now + (long)(Stopwatch.Frequency * (StockUiEventMapWindow.SlideSeconds + 0.1));
            window.Advance((231, 0), new Vector2(200, 100), later);
            Assert.Equal(new Vector2(200, 100), window.Center);
            window.Advance((230, 0), new Vector2(10, 10), later);
            Assert.Equal(new Vector2(10, 10), window.Center);
        }
    }
}
