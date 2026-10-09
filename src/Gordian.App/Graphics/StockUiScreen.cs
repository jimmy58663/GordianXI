// src/Gordian.App/Graphics/StockUiScreen.cs
namespace Gordian.App.Graphics
{
    /// <summary>
    /// The screen a menu is drawn on, for windows placed against it: its size in pixels and Window 1's top edge (null
    /// while the log is hidden). The default (zero size) makes such windows fall back to their authored places.
    /// </summary>
    public readonly record struct StockUiScreen(float Width, float Height, float? Window1Top);
}
