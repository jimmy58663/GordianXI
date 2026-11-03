// tests/Gordian.App.Tests/Graphics/StockUiCompassRenderTests.cs
using System.Numerics;
using Gordian.App.Graphics;
using Gordian.Core.Resources.Tables;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// The compass above the log (#485) rendered from the retail menu DAT on D3D11, set up as the maintainer's turned
    /// capture (image-2, 2026-10-31): Iceday 11:25 in Lower Jeuno (J-5), the camera looking about 123 degrees, an NPC dot
    /// and a player dot. GORDIAN_UI_DUMP writes <c>gpu_compass.png</c>.
    /// </summary>
    public class StockUiCompassRenderTests
    {
        [Fact]
        public void LowerJeunoCapturePosition_ReadsJ5()
        {
            using var screen = StockUiOffscreen.TryCreate(64, 64);
            if (screen == null) return;
            var map = ZoneMapFiles.LoadWithOrigin(screen.Resources, 245, 0);
            Assert.NotNull(map?.Origin);
            float scale = ZoneMapProjection.ScaleOf(245, 0)!.Value;
            // The captures' /pos (Windower x, y): east 31.353, north 70.201.
            var (u, v) = ZoneMapProjection.ToMap(map!.Value.Origin!.Value, scale, 31.353f, 70.201f);
            Assert.Equal("J-5", StockUiCompass.GridLabel(u, v));
        }

        [Fact]
        public void FieldMaps_HaveASquare()
        {
            // Round 2 of #485: no square outside towns. West Ronfaure's Gachemage (-176, 382.4 in LandSandBoat's npc_list),
            // near the gate to San d'Oria, is on the drawn land at I-5; Konschtat Highlands' centre has a square too.
            using var screen = StockUiOffscreen.TryCreate(64, 64);
            if (screen == null) return;
            ZoneMapOrigin? OriginOf(int zone, int map) => ZoneMapFiles.LoadWithOrigin(screen.Resources, zone, map)?.Origin;
            Assert.Equal("I-5", StockUiCompass.MapSquare(100, -176f, 382.4f, OriginOf));
            Assert.NotNull(StockUiCompass.MapSquare(108, 0, 0, OriginOf));
            Assert.Equal("J-5", StockUiCompass.MapSquare(245, 31.353f, 70.201f, OriginOf));
        }

        [Fact]
        public void RendersRingLettersDotsAndTheTimeLine()
        {
            using var screen = StockUiOffscreen.TryCreate(200, 120);
            if (screen == null) return;
            const float camera = 123.3f;
            var dots = new[]
            {
                new StockUiCompassDot(StockUiCompass.DotOffset(-6, 6, camera)!.Value, StockUiCompass.DotKind.Npc),
                new StockUiCompassDot(StockUiCompass.DotOffset(15, -3, camera)!.Value, StockUiCompass.DotKind.Player),
                new StockUiCompassDot(StockUiCompass.DotOffset(4, -8, camera)!.Value, StockUiCompass.DotKind.Monster),
            };
            const float lineBottom = 100;
            bool drawn = false;
            var shot = screen.Render(r => drawn = StockUiCompassWindow.Draw(r, screen.Library, screen.Font, camera, 4,
                StockUiCompass.LineText(11, 25, "J-5"), dots, lineBottom, 1), "gpu_compass.png");
            Assert.True(drawn);

            var center = new Vector2(StockUiCompass.CenterX, lineBottom - StockUiCompass.CenterRise);
            // The red N sits down and left of the centre at this camera bearing, as in the capture (about 38 left, 11 down).
            var n = center + StockUiCompass.Project(0, StockUiCompass.LetterRadius, camera);
            Assert.InRange(n.X - center.X, -42, -34);
            Assert.InRange(n.Y - center.Y, 8, 14);
            Assert.True(HasReddishPixel(screen, shot, (int)n.X - 5, (int)n.X + 5, (int)n.Y - 5, (int)n.Y + 5), "red N");

            // The NPC dot is green.
            var npc = center + dots[0].Offset;
            var g = screen.Pixel(shot, (int)npc.X, (int)npc.Y);
            Assert.True(g.G > g.R + 30 && g.G > g.B, $"NPC dot {g}");

            // The Iceday orb is light blue, and the time's digits are light, on the text line.
            var orb = screen.Pixel(shot, (int)(StockUiCompass.OrbX + StockUiCompass.OrbSize / 2), (int)(lineBottom - StockUiCompass.OrbRise));
            Assert.True(orb.B > 150 && orb.B > orb.R, $"orb {orb}");
            bool light = false;
            for (int y = (int)(lineBottom - StockUiCompass.LineHeight); y < lineBottom; y++)
                light |= screen.HasLightPixel(shot, (int)StockUiCompass.TextX, (int)StockUiCompass.TextX + 60, y, 160);
            Assert.True(light, "time text");
        }

        [Fact]
        public void ItemInfoWindowOnTheLog_CoversTheCompass()
        {
            // #506: the item info window over the log's top edge is no menu placement, yet hides the compass.
            using var screen = StockUiOffscreen.TryCreate(640, 480);
            if (screen == null) return;
            const float logTop = 400;
            int coverFrom = 0;
            screen.Render(r =>
            {
                coverFrom = r.DrawnWindows.Count;
                float h = StockUiItemInfoWindow.HeightOf(screen.Library, null, 1);
                StockUiItemInfoWindow.Draw(r, screen.Library, screen.Font, screen.LogFont, null, 0, null,
                    new StockUiPlacement(16, logTop - h, 1, false), 0);
            });
            Assert.True(screen.Renderer.DrawnWindows.Count > coverFrom);
            Assert.True(StockUiCompass.IsCovered(StockUiCompass.Bounds(StockUiCompass.LineBottom(logTop, 480, 1), 1),
                screen.Renderer.DrawnWindows, coverFrom));
        }

        private static bool HasReddishPixel(StockUiOffscreen screen, byte[] shot, int x0, int x1, int y0, int y1)
        {
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var p = screen.Pixel(shot, x, y);
                    if (p.R > 120 && p.R > p.G + 40) return true;
                }
            }
            return false;
        }
    }
}
