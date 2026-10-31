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
    /// as retail does: the maintainer's check, round 2). Survival guides (<c>mapsg</c>) are not drawn: their positions are not known yet.
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

        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu map,
            DecodedTexture? texture, string zoneName, float scale, uint width, uint height, ZoneMapOrigin? origin = null)
        {
            if (texture != null && texture.Width > 0 && texture.Height > 0 && width > 0 && height > 0)
            {
                var (x, y, w, h) = Placement(map, texture.Width, texture.Height, width, height, Margin(library, scale, width, height));
                renderer.DrawScreenTint(width, height, System.Numerics.Vector3.Zero);
                renderer.DrawTexture("zonemap:" + texture.Source, texture, x, y, w, h, Neutral);
                if (origin is { } o && ZoneMapProjection.ScaleOf(map.MapZone, map.MapNumber) is float s)
                {
                    float px = w / texture.Width, py = h / texture.Height;
                    foreach (var (hx, hz) in HomePoints.InZone(map.MapZone))
                    {
                        var (u, v) = ZoneMapProjection.ToMap(o, s, hx, hz);
                        StockUiEventMapWindow.DrawPin(renderer, null, EventMapPin.HomePoint, string.Empty, x + u * px, y + v * py, scale);
                    }
                }
            }
            StockUiHelpBar.DrawOnScreen(renderer, library, font, logFont, StockUiMap.Title, zoneName, scale, width, height);
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
