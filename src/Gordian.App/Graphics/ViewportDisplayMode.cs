// src/Gordian.App/Graphics/ViewportDisplayMode.cs
namespace Gordian.App.Graphics
{
    /// <summary>
    /// Supported display modes for GordianXI 3D graphics rendering windows.
    /// </summary>
    public enum ViewportDisplayMode
    {
        /// <summary>
        /// Borderless window matching the monitor work area with no OS frame decorations.
        /// </summary>
        BorderlessWindow,

        /// <summary>
        /// Resizable and movable windowed mode with title bar and border decorations.
        /// </summary>
        Windowed,

        /// <summary>
        /// Fullscreen mode occupying the entire monitor display.
        /// </summary>
        Fullscreen
    }
}
