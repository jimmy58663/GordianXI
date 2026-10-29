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
        [Fact]
        public void HomePointMap_CentresOnTheHomePoint_WithItsPin_AndBlackPastTheMapsEdge()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;
            var state = EventMapState.Closed.WithQueryEntry(1, new EventQueryMapEntry(1, EventQueryMapEntry.HomePointKind, 98, 0, 0, 0)).WithQueryMap();
            var focus = state.Focus(1)!; // Northern San d'Oria #4
            var map = ZoneMapFiles.LoadWithOrigin(screen.Resources, focus.Zone, 0);
            Assert.NotNull(map);
            var window = new StockUiEventMapWindow();
            var shot = screen.Render(r => window.Draw(r, screen.Library, screen.Font, screen.LogFont, focus, 0, map!.Value.Texture, map.Value.Origin,
                "Northern San d'Oria", 1, screen.Width, screen.Height), "gpu_event_map_home_point.png");
            // The home point's map pixel (-133 + 304, -195 + 304) is at the screen's centre.
            Assert.Equal(171f, window.Center.X, 2);
            Assert.Equal(109f, window.Center.Y, 2);
            // The pale-blue pin stands just above the centre.
            var pin = screen.Pixel(shot, (int)screen.Width / 2, (int)screen.Height / 2 - 8);
            Assert.True(pin.B > pin.R && pin.B > 120, $"pin pixel {pin}");
            // The parchment around it, warm.
            var parchment = screen.Pixel(shot, (int)screen.Width / 2 + 120, (int)screen.Height / 2 + 200);
            Assert.True(parchment.R > parchment.B, $"parchment pixel {parchment}");
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
