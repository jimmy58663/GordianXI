// src/Gordian.App/Graphics/StockUiMapWindow.cs
using System;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Events;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws the Map window's map (#23, <see cref="StockUiMap"/>): the zone map's texture, opaque, covering the whole window
    /// at the open window's zoom (1 = the map's shorter side fits the window, the rest runs off the edges; up to
    /// <see cref="StockUiMap.MaxZoom"/>) around its centre point, with the help bar "Map" / the zone's name over it (round 1
    /// captures image-4 and image-5, 2026-10-25: the map fills the 2560 x 1440 window behind the help bar and the chat). The
    /// <c>map0</c> list draws over it like any menu. The zone's home points show on it as the event map's pale-blue
    /// <c>maphp</c> crystal, without a label, on the maps with a calibrated scale (<see cref="ZoneMapProjection.ScaleOf"/>;
    /// maintainer's request, round 1 of #168: retail shows them; every home point of the zone is drawn, registered or not,
    /// as retail does: the maintainer's check, round 2), with the survival guides' <c>mapsg</c> book (#479), each on the floor
    /// it stands on (#481). The character shows as the compass's red dot and the window opens centred on it.
    /// <para>
    /// <b>Beyond retail</b> (maintainer's request, round 2, 2026-10-26): the map can be panned a margin past each edge, the
    /// help bar's height (<see cref="Margin"/>), with black behind it, so the grid letters along the map's top edge come out
    /// from under the help bar. Retail stops the map at the window's edge, the help bar over the letters.
    /// </para>
    /// </summary>
    public static class StockUiMapWindow
    {
        private static readonly UiColor Neutral = new(0x80, 0x80, 0x80, 0x80);

        /// <summary>The space between the help bar's bottom edge and the map's top edge when panned up to it, layout px.</summary>
        private const float MarginGap = 4;

        /// <param name="player">The character's world x / z when it stands on the zone and floor shown (#481), else null.</param>
        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu map,
            DecodedTexture? texture, string zoneName, float scale, uint width, uint height, ZoneMapOrigin? origin = null,
            (float X, float Z)? player = null)
        {
            if (texture != null && texture.Width > 0 && texture.Height > 0 && width > 0 && height > 0)
            {
                float? mapScale = origin != null ? ZoneMapProjection.ScaleOf(map.MapZone, map.MapNumber) : null;
                if (map.MapCentreOnPlayer)
                {
                    // The window opens centred on the character (retail Main_Menu_Page1.mp4); once, so panning stays free.
                    map.MapCentreOnPlayer = false;
                    if (mapScale is float cs && origin is { } co && player is { } at)
                    {
                        var (cu, cv) = ZoneMapProjection.ToMap(co, cs, at.X, at.Z);
                        map.SetMapCenter(cu / texture.Width, cv / texture.Height);
                    }
                }
                var (x, y, w, h) = Placement(map, texture.Width, texture.Height, width, height, Margin(library, scale, width, height));
                renderer.DrawScreenTint(width, height, System.Numerics.Vector3.Zero);
                renderer.DrawTexture("zonemap:" + texture.Source, texture, x, y, w, h, Neutral);
                if (origin is { } o && mapScale is float s)
                {
                    float px = w / texture.Width, py = h / texture.Height;
                    DrawPoints(renderer, HomePoints.InZone(map.MapZone), EventMapPin.HomePoint, map.MapNumber, o, s, x, y, px, py, scale);
                    DrawPoints(renderer, SurvivalGuides.InZone(map.MapZone), EventMapPin.SurvivalGuide, map.MapNumber, o, s, x, y, px, py, scale);
                    if (player is { } me && library.TryGetGroup(StockUiCompass.GroupName, out var compass) && compass.Images.Count > StockUiCompass.DotImage)
                    {
                        // The red position dot (retail capture): the compass's own dot, at the UI's scale (PROVISIONAL size).
                        var (u, v) = ZoneMapProjection.ToMap(o, s, me.X, me.Z);
                        renderer.DrawImage(compass.Images[StockUiCompass.DotImage], x + u * px, y + v * py, scale);
                    }
                }
            }
            StockUiHelpBar.DrawOnScreen(renderer, library, font, logFont, StockUiMap.Title, zoneName, scale, width, height);
        }

        /// <summary>The travel points on the map shown: those on its floor, as their pins, without labels.</summary>
        private static void DrawPoints(StockUiRenderer renderer, System.Collections.Generic.IReadOnlyList<ZoneMapPoint> points, EventMapPin pin, int mapNumber,
            ZoneMapOrigin origin, float mapScale, float x, float y, float px, float py, float scale)
        {
            foreach (var point in points)
            {
                if (point.Map != mapNumber) continue;
                var (u, v) = ZoneMapProjection.ToMap(origin, mapScale, point.X, point.Z);
                StockUiEventMapWindow.DrawPin(renderer, null, pin, string.Empty, x + u * px, y + v * py, scale);
            }
        }

        /// <summary>The margin the map may be panned past each window edge (screen px): the help bar's bottom edge and a small gap.</summary>
        public static float Margin(UiResourceLibrary library, float scale, uint width, uint height)
        {
            if (!library.TryGetMenu(StockUiCheck.HelpMenu, out var help)) return 0;
            var f = help.Frame;
            var at = StockUiLayout.Place(f.Anchor, f.X, f.Y, f.Width, f.Height, scale, width, height);
            return at.Y + (f.Height + MarginGap) * scale;
        }

        /// <summary>
        /// Where the map texture goes on screen: scaled to cover the window times the zoom, the map's centre point on the window's
        /// centre, kept covering the window but for <paramref name="margin"/> px at each edge (the centre point is clamped on the
        /// map first).
        /// </summary>
        public static (float X, float Y, float Width, float Height) Placement(StockUiOpenMenu map, int textureWidth, int textureHeight, uint width, uint height, float margin = 0)
        {
            float cover = Math.Max(width / (float)textureWidth, height / (float)textureHeight) * map.MapZoom;
            float w = textureWidth * cover, h = textureHeight * cover;
            map.ClampMapCenter((width * 0.5f - margin) / w, (height * 0.5f - margin) / h);
            return (width * 0.5f - map.MapCenterX * w, height * 0.5f - map.MapCenterY * h, w, h);
        }
    }
}
