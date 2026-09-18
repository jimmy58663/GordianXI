// src/Gordian.App/Graphics/ViewportTabStyle.cs
namespace Gordian.App.Graphics
{
    /// <summary>
    /// Supported visual styles for the multi-character tab switcher in the 3D viewport window.
    /// </summary>
    public enum ViewportTabStyle
    {
        /// <summary>
        /// Minimalist translucent glass island pill at top-center (Default).
        /// Expands on hover to show all characters, zones, and tear-off actions.
        /// </summary>
        FloatingPill,

        /// <summary>
        /// Slide-out top ribbon that auto-hides at the top edge of the window.
        /// </summary>
        TopRibbon,

        /// <summary>
        /// Collapsible vertical deck docked along the left screen border with live vitals.
        /// </summary>
        SideRail,

        /// <summary>
        /// No on-screen tabs or overlay chrome. Switching is driven purely via hotkeys (Ctrl+Tab, F1-F6).
        /// </summary>
        HotkeysOnly
    }
}
