// src/Gordian.App/Graphics/ImGuiOverlayRegistry.cs
using System;
using System.Threading;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// An interactive Tier 3 overlay (#380): an ImGui window set that is drawn inside the viewport windows' ImGui frame
    /// and may take the mouse and keyboard while it is visible.
    /// </summary>
    public interface IImGuiOverlay
    {
        /// <summary>False while the overlay is hidden; a hidden overlay is not drawn and never takes input.</summary>
        bool IsVisible { get; }

        /// <summary>Issues the overlay's ImGui calls. Runs on a window's render thread, inside its ImGui frame.</summary>
        void Draw();
    }

    /// <summary>
    /// The process-wide list of interactive ImGui overlays (addon canvases, debug windows; #380). While none is visible
    /// the viewport runs no ImGui frame for them and feeds ImGui no input, so the game keeps all of it. Copy-on-write, so
    /// the render threads read it without locking.
    /// </summary>
    public static class ImGuiOverlayRegistry
    {
        private static readonly object Gate = new();
        private static IImGuiOverlay[] _overlays = [];

        /// <summary>True when at least one registered overlay is visible.</summary>
        public static bool AnyVisible
        {
            get
            {
                foreach (var overlay in Volatile.Read(ref _overlays))
                {
                    if (overlay.IsVisible) return true;
                }
                return false;
            }
        }

        public static void Add(IImGuiOverlay overlay)
        {
            ArgumentNullException.ThrowIfNull(overlay);
            lock (Gate)
            {
                if (Array.IndexOf(_overlays, overlay) >= 0) return;
                Volatile.Write(ref _overlays, [.. _overlays, overlay]);
            }
        }

        public static void Remove(IImGuiOverlay overlay)
        {
            lock (Gate)
            {
                int index = Array.IndexOf(_overlays, overlay);
                if (index < 0) return;
                var next = new IImGuiOverlay[_overlays.Length - 1];
                Array.Copy(_overlays, 0, next, 0, index);
                Array.Copy(_overlays, index + 1, next, index, next.Length - index);
                Volatile.Write(ref _overlays, next);
            }
        }

        /// <summary>Draws every visible overlay (inside an ImGui frame).</summary>
        public static void DrawAll()
        {
            foreach (var overlay in Volatile.Read(ref _overlays))
            {
                if (overlay.IsVisible) overlay.Draw();
            }
        }
    }
}
