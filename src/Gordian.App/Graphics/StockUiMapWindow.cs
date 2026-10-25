// src/Gordian.App/Graphics/StockUiMapWindow.cs
using System;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws the Map window's map (#23, <see cref="StockUiMap"/>): the zone map's 512 x 512 texture, opaque, as large as fits
    /// the screen under the help bar and centred, with the help bar "Map" / the zone's name over it (retail capture
    /// <c>Main_Menu_Page1.mp4</c>, 5:20). The <c>map0</c> list draws over it like any menu.
    /// </summary>
    public static class StockUiMapWindow
    {
        private static readonly UiColor Neutral = new(0x80, 0x80, 0x80, 0x80);

        /// <summary>The help bar's bottom edge in layout pixels (<c>helpwind</c> at y 16, 20 tall), the map's top margin.</summary>
        private const float TopMargin = 40;

        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, DecodedTexture? map,
            string zoneName, float scale, uint width, uint height)
        {
            if (map != null && map.Width > 0 && map.Height > 0)
            {
                float top = TopMargin * scale;
                float side = Math.Min(width, height - top);
                if (side > 0)
                {
                    float w = side * map.Width / Math.Max(map.Width, map.Height), h = side * map.Height / Math.Max(map.Width, map.Height);
                    renderer.DrawTexture("zonemap:" + map.Source, map, (width - w) * 0.5f, top + (height - top - h) * 0.5f, w, h, Neutral);
                }
            }
            StockUiHelpBar.DrawOnScreen(renderer, library, font, logFont, StockUiMap.Title, zoneName, scale, width, height);
        }
    }
}
