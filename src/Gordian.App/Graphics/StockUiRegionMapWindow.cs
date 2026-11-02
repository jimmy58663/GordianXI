// src/Gordian.App/Graphics/StockUiRegionMapWindow.cs
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws a Region Info page's map (#488; <see cref="Gordian.Core.Resources.Tables.RegionMapFiles"/>): the region map over
    /// black at the page's fixed zoom (<see cref="StockUiOpenMenu.MapZoom"/>) around its centre point, panned by the
    /// movement input like the Map window (<see cref="StockUiMapWindow.Placement"/>) but never zoomed, under the HUD's
    /// windows; the page's own windows and its help bar draw over it as menus. As the maintainer's retail screenshots of
    /// the other region maps show them (round 1 of #488, image-13 to image-16): the parchment fills the screen, darker than
    /// the image (<see cref="MapColor"/>, as the event map draws its zone map).
    /// <para>
    /// PROVISIONAL: the zoom, the colour, and opening on the map's centre: retail labels each region on the map and opens
    /// on the character's, and where the regions sit on these maps is not known yet, so no labels are drawn.
    /// </para>
    /// </summary>
    public static class StockUiRegionMapWindow
    {
        /// <summary>The map image at half strength over the black, as <see cref="StockUiEventMapWindow"/> draws its zone map.</summary>
        private static readonly UiColor MapColor = new(0x80, 0x80, 0x80, 0x40);

        public static void Draw(StockUiRenderer renderer, StockUiOpenMenu page, DecodedTexture? texture, uint width, uint height)
        {
            if (width == 0 || height == 0) return;
            renderer.DrawScreenTint(width, height, System.Numerics.Vector3.Zero);
            if (texture == null || texture.Width <= 0 || texture.Height <= 0) return;
            var (x, y, w, h) = StockUiMapWindow.Placement(page, texture.Width, texture.Height, width, height);
            renderer.DrawTexture("regionmap:" + texture.Source, texture, x, y, w, h, MapColor);
        }
    }
}
