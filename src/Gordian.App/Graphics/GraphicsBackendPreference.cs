// src/Gordian.App/Graphics/GraphicsBackendPreference.cs
namespace Gordian.App.Graphics
{
    /// <summary>
    /// Specifies the preferred graphics backend for the 3D viewport surface.
    /// </summary>
    public enum GraphicsBackendPreference
    {
        /// <summary>
        /// Automatically selects the best native backend: Direct3D 11 on Windows, Vulkan on Linux, Vulkan through MoltenVK on macOS.
        /// </summary>
        Auto = 0,

        /// <summary>
        /// Direct3D 11 backend (Windows only).
        /// </summary>
        Direct3D11 = 1,

        /// <summary>
        /// Vulkan backend (Windows and Linux).
        /// </summary>
        Vulkan = 2,

        /// <summary>
        /// Legacy value kept so saved settings still load: NeoVeldrid has no Metal backend, so it selects
        /// Vulkan, which runs on macOS through NeoVeldrid's bundled MoltenVK. Not offered in the settings list.
        /// </summary>
        Metal = 3,

        /// <summary>
        /// Legacy value kept so saved settings still load: OpenGL was never implemented and is no longer a choice
        /// (maintainer decision 2026-10-13, #390; macOS runs Vulkan through MoltenVK). A saved OpenGL setting loads as
        /// <see cref="Auto"/> with a one-time notice (<c>ViewportViewModel</c>). Not offered in the settings list.
        /// </summary>
        OpenGL = 4
    }
}
