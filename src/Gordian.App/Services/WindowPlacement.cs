// src/Gordian.App/Services/WindowPlacement.cs
using System.Text.Json.Serialization;
using Avalonia.Controls;

namespace Gordian.App.Services
{
    /// <summary>
    /// Represents the persisted screen coordinates, dimensions, and window state of an Avalonia Window.
    /// </summary>
    public sealed class WindowPlacement
    {
        /// <summary>
        /// Gets or sets the X position on the desktop in physical screen coordinates.
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// Gets or sets the Y position on the desktop in physical screen coordinates.
        /// </summary>
        public int Y { get; set; }

        /// <summary>
        /// Gets or sets the width of the window in device-independent pixels (DIP).
        /// </summary>
        public double Width { get; set; }

        /// <summary>
        /// Gets or sets the height of the window in device-independent pixels (DIP).
        /// </summary>
        public double Height { get; set; }

        /// <summary>
        /// Gets or sets the window state (Normal or Maximized). Minimized states are normalized to Normal.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public WindowState WindowState { get; set; } = WindowState.Normal;

        /// <summary>
        /// Clones this placement record.
        /// </summary>
        public WindowPlacement Clone()
        {
            return new WindowPlacement
            {
                X = X,
                Y = Y,
                Width = Width,
                Height = Height,
                WindowState = WindowState
            };
        }
    }
}
