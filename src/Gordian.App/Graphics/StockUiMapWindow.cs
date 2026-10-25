// src/Gordian.App/Graphics/StockUiMapWindow.cs
using System;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws the Map window's map (#23, <see cref="StockUiMap"/>): the zone map's texture, opaque, covering the whole window
    /// at the open window's zoom (1 = the map's shorter side fits the window, the rest runs off the edges; up to
    /// <see cref="StockUiMap.MaxZoom"/>) around its centre point, with the help bar "Map" / the zone's name over it (round 1
    /// captures image-4 and image-5, 2026-10-25: the map fills the 2560 x 1440 window behind the help bar and the chat). The
    /// <c>map0</c> list draws over it like any menu.
    /// </summary>
    public static class StockUiMapWindow
    {
        private static readonly UiColor Neutral = new(0x80, 0x80, 0x80, 0x80);

        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu map,
            DecodedTexture? texture, string zoneName, float scale, uint width, uint height)
        {
            if (texture != null && texture.Width > 0 && texture.Height > 0 && width > 0 && height > 0)
            {
                var (x, y, w, h) = Placement(map, texture.Width, texture.Height, width, height);
                renderer.DrawTexture("zonemap:" + texture.Source, texture, x, y, w, h, Neutral);
            }
            StockUiHelpBar.DrawOnScreen(renderer, library, font, logFont, StockUiMap.Title, zoneName, scale, width, height);
        }

        /// <summary>
        /// Where the map texture goes on screen: scaled to cover the window times the zoom, the map's centre point on the window's
        /// centre, kept covering the window (the centre point is clamped on the map first).
        /// </summary>
        public static (float X, float Y, float Width, float Height) Placement(StockUiOpenMenu map, int textureWidth, int textureHeight, uint width, uint height)
        {
            float cover = Math.Max(width / (float)textureWidth, height / (float)textureHeight) * map.MapZoom;
            float w = textureWidth * cover, h = textureHeight * cover;
            map.ClampMapCenter(width * 0.5f / w, height * 0.5f / h);
            return (width * 0.5f - map.MapCenterX * w, height * 0.5f - map.MapCenterY * h, w, h);
        }
    }
}
