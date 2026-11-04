// src/Gordian.App/Graphics/StockUiRegionMapWindow.cs
using System.Numerics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws a Region Info page's map (#488; <see cref="Gordian.Core.Resources.Tables.RegionMapFiles"/>): the region map over
    /// black at the page's fixed zoom (<see cref="StockUiOpenMenu.MapZoom"/>), never zoomed, under the HUD's windows; the
    /// page's own windows and its help bar draw over it as menus. As the maintainer's retail captures show it (round 1 of
    /// #488, image-13 to image-16; rounds 2 and 3, image-49 to image-73 and a movement video): the parchment fills the
    /// screen, darker than the image (<see cref="MapColor"/>, as the event map draws its zone map), and the conquest map
    /// labels every region and city and the zones of the region under its cursor (<see cref="StockUiConquestView.Markers"/>):
    /// a grey ball with the name above, beside or under it in the log font, a city its crest with a lilac name. The cursor
    /// is the menu arrow at half size pointing at a small orange box (<see cref="DrawCursor"/>); the map scrolls to keep it
    /// on screen (<see cref="StockUiOpenMenu.KeepMapCursorInView"/>).
    /// <para>
    /// PROVISIONAL: the ball, box and label sizes and colours are read off the captures at UI scale 1 (a ball about 12 px,
    /// the box about 16 px, the names about the log font's size at 0.875).
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

        /// <summary>
        /// Sizes in layout px: the ball, the crest, the label's scale of the log font (0.75 in round 2, a little small beside
        /// retail's: round 3), the gap between a point and its label, the cursor's box and its line.
        /// </summary>
        private const float BallSize = 12, BallCell = 16, CrestSize = 16, LabelScale = 0.875f, LabelGap = 2, BoxSize = 16, BoxLine = 1.5f;

        /// <summary>How far (layout px) the cursor stays inside the screen's edges once the map cannot scroll further.</summary>
        private const float EdgeMargin = 12;

        /// <summary>The solid texel the box's lines are drawn with.</summary>
        private const string SolidTexture = "gauge";

        /// <summary>Label colours (half-scale): white, the character's zone in cream, a city in lilac (retail image-49, image-51).</summary>
        private static readonly UiColor Neutral = new(0x80, 0x80, 0x80, 0x80);
        private static readonly UiColor CurrentZone = new(0x80, 0x74, 0x68, 0x80);
        private static readonly UiColor City = new(0x58, 0x50, 0x80, 0x80);
        private static readonly UiColor BallColor = new(0x60, 0x5E, 0x58, 0x80);
        private static readonly UiColor Stronghold = new(0x7D, 0x4B, 0x78, 0x80);
        private static readonly UiColor Front = new(0x73, 0x55, 0x23, 0x80);

        /// <summary>The Besieged discs' and the region pages' crests' sizes (layout px; the 2026-11-03 captures).</summary>
        private const float HubSize = 24, RegionCrestSize = 22;

        /// <summary>The box's orange (image-55: about 255,165,70 on every side).</summary>
        private static readonly UiColor BoxColor = new(0x80, 0x52, 0x23, 0x80);

        /// <summary>The arrow's size against the menu cursor's (image-55: about three quarters).</summary>
        private const float ArrowScale = 1.5f;

        public static void Draw(StockUiRenderer renderer, StockUiOpenMenu page, DecodedTexture? texture, uint width, uint height,
            UiResourceLibrary? library = null, StockUiLogFont? logFont = null, float scale = 1, long timestamp = 0)
        {
            if (width == 0 || height == 0) return;
            renderer.DrawScreenTint(width, height, Vector3.Zero);
            if (texture == null || texture.Width <= 0 || texture.Height <= 0) return;
            var (x, y, w, h) = StockUiMapWindow.Placement(page, texture.Width, texture.Height, width, height);
            if (page.ConquestView != null || page.RegionInfoView != null)
            {
                float top = library != null ? StockUiMapWindow.Margin(library, scale, width, height) : EdgeMargin * scale;
                page.KeepMapCursorInView(w, h, width, height, EdgeMargin * scale, top);
                (x, y, w, h) = StockUiMapWindow.Placement(page, texture.Width, texture.Height, width, height);
            }
            renderer.DrawTexture("regionmap:" + texture.Source, texture, x, y, w, h, MapColor);
            if (page.RegionInfoView is { } info)
            {
                foreach (var marker in info.Markers)
                {
                    DrawRegionMarker(renderer, library, logFont, marker, x + marker.X / texture.Width * w, y + marker.Y / texture.Height * h, scale);
                }
                if (library != null) DrawCursor(renderer, library, new Vector2(x + page.MapCursorX * w, y + page.MapCursorY * h), scale, timestamp);
                return;
            }
            if (page.ConquestView is not { } view) return;
            foreach (var marker in view.Markers)
            {
                DrawMarker(renderer, library, logFont, marker, x + marker.X / texture.Width * w, y + marker.Y / texture.Height * h, scale);
            }
            if (library != null) DrawCursor(renderer, library, new Vector2(x + page.MapCursorX * w, y + page.MapCursorY * h), scale, timestamp);
        }

        /// <summary>The conquest map's cursor at (<paramref name="at"/>): the box centred there, the half-size menu arrow at its left.</summary>
        public static void DrawCursor(StockUiRenderer renderer, UiResourceLibrary library, Vector2 at, float s, long timestamp)
        {
            float half = BoxSize * 0.5f * s, line = BoxLine * s;
            float left = at.X - half, top = at.Y - half, size = half * 2;
            renderer.DrawTextureRect(SolidTexture, 1, 1, 1, 1, left, top, size, line, BoxColor);
            renderer.DrawTextureRect(SolidTexture, 1, 1, 1, 1, left + size - line, top, line, size, BoxColor);
            renderer.DrawTextureRect(SolidTexture, 1, 1, 1, 1, left, top, line, size, BoxColor);
            renderer.DrawTextureRect(SolidTexture, 1, 1, 1, 1, left, top + size - line, size, line, BoxColor);
            // StockUiTargetWindow's row selector is the menu arrow at half size; ArrowScale brings it to retail's.
            StockUiTargetWindow.DrawRowSelector(renderer, library, new Vector2(left, at.Y), s * ArrowScale, timestamp);
        }

        /// <summary>
        /// A Besieged, Campaign or Colonization marker (#492): the 2026-11-03 captures' discs (<c>aht0</c>, <c>aht1</c>, about 24 px),
        /// crests (about 22 px), grey balls, a Campaign zone's ball in its owner's colour (<c>colorbal</c>: violet for the
        /// beastmen, red, blue and yellow for the nations, dark with a "?" for no owner known), and the names in white, lilac (a city), pink (a stronghold) or gold
        /// (a front, without a point).
        /// </summary>
        private static void DrawRegionMarker(StockUiRenderer renderer, UiResourceLibrary? library, StockUiLogFont? logFont, StockUiRegionMarker marker,
            float px, float py, float s)
        {
            float half = 0;
            string? texture = marker.Look switch
            {
                StockUiRegionMarkerLook.Hub => "aht0",
                StockUiRegionMarkerLook.AlliedHub => "aht1",
                StockUiRegionMarkerLook.Stronghold => "mcre",
                StockUiRegionMarkerLook.City => marker.Crest,
                _ => null,
            };
            if (texture != null && library != null && library.TryGetTexture(texture, out var image) && image != null)
            {
                half = (marker.Look is StockUiRegionMarkerLook.Hub or StockUiRegionMarkerLook.AlliedHub ? HubSize : RegionCrestSize) * 0.5f * s;
                renderer.DrawTextureRect(texture, 0, 0, image.Width, image.Height, px - half, py - half, half * 2, half * 2, Neutral);
            }
            else if (marker.Look == StockUiRegionMarkerLook.CampaignZone)
            {
                half = BallSize * 0.5f * s;
                var (cellX, cellY) = marker.Owner switch { 1 => (32f, 0f), 2 => (0f, 0f), 3 => (16f, 16f), 4 => (48f, 16f), _ => (32f, 16f) };
                renderer.DrawTextureRect(Ball, cellX, cellY, BallCell, BallCell, px - half, py - half, half * 2, half * 2, Neutral);
                if (marker.Owner is < 1 or > 4 && logFont != null)
                {
                    // No owner known: the dark ball with a "?" (the retail video of 2026-11-04).
                    float mark = s * LabelScale;
                    logFont.Draw(renderer, "?", px - logFont.MeasureWidth("?") * mark * 0.5f, py - StockUiLogFont.CellHeight * mark * 0.5f, mark, Neutral);
                }
            }
            else if (marker.Look == StockUiRegionMarkerLook.Ball && marker.Crest == null)
            {
                half = BallSize * 0.5f * s;
                renderer.DrawTextureRect(Ball, BallCellX, BallCellY, BallCell, BallCell, px - half, py - half, half * 2, half * 2, BallColor);
            }
            else if (marker.Look == StockUiRegionMarkerLook.Ball)
            {
                half = BallSize * 0.5f * s; // a Campaign zone's label beside its dot
            }
            if (logFont == null || marker.Label.Length == 0) return;
            float ts = s * LabelScale;
            float textWidth = logFont.MeasureWidth(marker.Label) * ts;
            float row = StockUiLogFont.CellHeight * ts;
            var color = marker.Look switch
            {
                StockUiRegionMarkerLook.City => City,
                StockUiRegionMarkerLook.Stronghold => Stronghold,
                StockUiRegionMarkerLook.Front => Front,
                _ => Neutral,
            };
            var (lx, ly) = marker.Look == StockUiRegionMarkerLook.Front ? (px - textWidth * 0.5f, py - row * 0.5f) : marker.Side switch
            {
                StockUiMapLabelSide.Above => (px - textWidth * 0.5f, py - half - LabelGap * s - row),
                StockUiMapLabelSide.Below => (px - textWidth * 0.5f, py + half + LabelGap * s),
                StockUiMapLabelSide.Left => (px - half - LabelGap * s - textWidth, py - row * 0.5f),
                _ => (px + half + LabelGap * s, py - row * 0.5f),
            };
            logFont.Draw(renderer, marker.Label, lx, ly, ts, color);
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
                StockUiMapLabelSide.Below => (px - textWidth * 0.5f, py + half + LabelGap * s),
                StockUiMapLabelSide.Left => (px - half - LabelGap * s - textWidth, py - row * 0.5f),
                _ => (px + half + LabelGap * s, py - row * 0.5f),
            };
            logFont.Draw(renderer, marker.Label, lx, ly, ts, color);
        }
    }
}
