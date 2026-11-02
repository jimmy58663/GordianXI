// src/Gordian.App/Graphics/StockUiRegionMapWindow.cs
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws a Region Info page's map (#488; <see cref="Gordian.Core.Resources.Tables.RegionMapFiles"/>): the region map over
    /// black at the page's fixed zoom (<see cref="StockUiOpenMenu.MapZoom"/>), panned by the movement input like the Map
    /// window (<see cref="StockUiMapWindow.Placement"/>) but never zoomed, under the HUD's windows; the page's own windows
    /// and its help bar draw over it as menus. As the maintainer's retail captures show it (round 1 of #488, image-13 to
    /// image-16; round 2, image-49 to image-51): the parchment fills the screen, darker than the image (<see cref="MapColor"/>,
    /// as the event map draws its zone map), and the conquest map labels its regions and the zones of the character's
    /// region (<see cref="StockUiConquestView.Markers"/>): a grey ball
    /// with the name above it or beside it in the log font, a city its crest under a lilac name.
    /// <para>
    /// PROVISIONAL: the ball and label sizes and colours are read off the captures at UI scale 1 (a ball about 12 px, the
    /// names about the log font's size at 0.75); retail's cursor, which moves between the markers and shows the region it
    /// is on, is not drawn.
    /// </para>
    /// </summary>
    public static class StockUiRegionMapWindow
    {
        /// <summary>The map image at half strength over the black, as <see cref="StockUiEventMapWindow"/> draws its zone map.</summary>
        private static readonly UiColor MapColor = new(0x80, 0x80, 0x80, 0x40);

        /// <summary>
        /// The marker ball (the pale ball of <c>colorbal</c>, cell 0,16, toned to retail's grey: <see cref="BallColor"/>) and the
        /// crests by city region (San d'Oria, Bastok, Windurst, Jeuno).
        /// </summary>
        private const string Ball = "colorbal";
        private const float BallCellX = 0, BallCellY = 16;
        private static readonly string[] Crests = { "scre", "bcre", "wcre", "jcre" };

        /// <summary>Sizes in layout px: the ball, the crest, the label's scale of the log font, the gap between a point and its label.</summary>
        private const float BallSize = 12, BallCell = 16, CrestSize = 16, LabelScale = 0.75f, LabelGap = 2;

        /// <summary>Label colours (half-scale): white, the character's zone in cream, a city in lilac (retail image-49, image-51).</summary>
        private static readonly UiColor Neutral = new(0x80, 0x80, 0x80, 0x80);
        private static readonly UiColor CurrentZone = new(0x80, 0x74, 0x68, 0x80);
        private static readonly UiColor City = new(0x58, 0x50, 0x80, 0x80);
        private static readonly UiColor BallColor = new(0x60, 0x5E, 0x58, 0x80);

        public static void Draw(StockUiRenderer renderer, StockUiOpenMenu page, DecodedTexture? texture, uint width, uint height,
            UiResourceLibrary? library = null, StockUiLogFont? logFont = null, float scale = 1)
        {
            if (width == 0 || height == 0) return;
            renderer.DrawScreenTint(width, height, System.Numerics.Vector3.Zero);
            if (texture == null || texture.Width <= 0 || texture.Height <= 0) return;
            var (x, y, w, h) = StockUiMapWindow.Placement(page, texture.Width, texture.Height, width, height);
            renderer.DrawTexture("regionmap:" + texture.Source, texture, x, y, w, h, MapColor);
            if (page.ConquestView is not { } view) return;
            foreach (var marker in view.Markers)
            {
                DrawMarker(renderer, library, logFont, marker, x + marker.X / texture.Width * w, y + marker.Y / texture.Height * h, scale);
            }
        }

        private static void DrawMarker(StockUiRenderer renderer, UiResourceLibrary? library, StockUiLogFont? logFont, StockUiConquestMarker marker,
            float px, float py, float s)
        {
            bool city = marker.Kind == StockUiConquestMarkerKind.City;
            float half;
            if (city && marker.Region is >= 0 and < 4 && library != null && library.TryGetTexture(Crests[marker.Region], out var crest) && crest != null)
            {
                half = CrestSize * 0.5f * s;
                renderer.DrawTextureRect(Crests[marker.Region], 0, 0, crest.Width, crest.Height, px - half, py - half, half * 2, half * 2, Neutral);
            }
            else
            {
                half = BallSize * 0.5f * s;
                renderer.DrawTextureRect(Ball, BallCellX, BallCellY, BallCell, BallCell, px - half, py - half, half * 2, half * 2, BallColor);
            }
            if (logFont == null || marker.Label.Length == 0) return;
            float ts = s * LabelScale;
            float textWidth = logFont.MeasureWidth(marker.Label) * ts;
            float row = StockUiLogFont.CellHeight * ts;
            var color = city ? City : marker.Kind == StockUiConquestMarkerKind.CurrentZone ? CurrentZone : Neutral;
            var (lx, ly) = marker.Side switch
            {
                StockUiMapLabelSide.Above => (px - textWidth * 0.5f, py - half - LabelGap * s - row),
                StockUiMapLabelSide.Left => (px - half - LabelGap * s - textWidth, py - row * 0.5f),
                _ => (px + half + LabelGap * s, py - row * 0.5f),
            };
            logFont.Draw(renderer, marker.Label, lx, ly, ts, color);
        }
    }
}
