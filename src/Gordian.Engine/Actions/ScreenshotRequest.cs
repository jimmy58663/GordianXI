// src/Gordian.Engine/Actions/ScreenshotRequest.cs
namespace Gordian.Engine.Actions
{
    /// <summary>
    /// A <c>/screenshot</c> asked for by the player: the viewport showing the character saves its next frame.
    /// </summary>
    /// <param name="HideUi">Saves the 3D scene without the stock UI and overlays.</param>
    /// <param name="Name">The file name without its extension, or null for a timestamped name.</param>
    public sealed record ScreenshotRequest(bool HideUi, string? Name);
}
