// src/Gordian.Core/Ui/StockUiDragController.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Ui;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// A persistent stock window's on-screen extent for one frame, as the HUD drew it: the hit rectangle the player
    /// can grab (screen pixels) and the placement origin (the DAT frame's top-left) that a move writes back through
    /// <see cref="StockUiLayout.MoveTo"/>. The two differ when the client draws more than the frame (a log window's
    /// title band sits above its frame).
    /// </summary>
    public readonly record struct StockUiDragRegion(string WindowId, UiMenuFrame Frame, float OriginX, float OriginY, float Scale,
        float X, float Y, float Width, float Height, bool Placeholder)
    {
        public bool Contains(float px, float py) => px >= X && py >= Y && px < X + Width && py < Y + Height;
    }

    /// <summary>
    /// The opt-in "unlocked" stock UI (Tier 2 chunk 4b): while unlocked, the persistent windows are outlined and can
    /// be dragged with the mouse, writing the layout's per-window overrides. Locked by default (legacy parity: retail
    /// cannot drag its stock windows) and never persisted, so every launch starts locked.
    /// <para>
    /// The HUD registers each window's region as it draws it (render thread); the viewport feeds mouse events (UI
    /// thread). A drag keeps a transient position that the HUD reads back through <see cref="TryGetDragPosition"/>, so
    /// the layout (and its file) is only written once, when the button is released. Runtime-placed windows
    /// (sub-menus, prompts) are never registered: they follow their parent.
    /// </para>
    /// </summary>
    public sealed class StockUiDragController
    {
        private readonly object _sync = new();
        private readonly List<StockUiDragRegion> _building = new();
        private List<StockUiDragRegion> _regions = new();
        private StockUiLayout? _layout;
        private float _screenWidth, _screenHeight;
        private float _mouseX = float.NaN, _mouseY = float.NaN;

        private StockUiDragRegion? _drag;
        private float _grabX, _grabY;   // where inside the region the pointer grabbed it (screen pixels)
        private float _dragX, _dragY;   // the region's current top-left while dragging (screen pixels)

        /// <summary>True while the player may drag windows. Off at every launch.</summary>
        public bool Unlocked
        {
            get { lock (_sync) return _unlocked; }
            set
            {
                lock (_sync)
                {
                    _unlocked = value;
                    if (!value) _drag = null;
                }
            }
        }
        private bool _unlocked;

        /// <summary>The window being dragged, or null.</summary>
        public string? DraggingWindow { get { lock (_sync) return _drag?.WindowId; } }

        /// <summary>The window under the pointer (topmost first), or null; only while unlocked.</summary>
        public string? HoveredWindow
        {
            get
            {
                lock (_sync)
                {
                    if (!_unlocked) return null;
                    if (_drag is { } d) return d.WindowId;
                    return float.IsNaN(_mouseX) ? null : HitTest(_regions, _mouseX, _mouseY)?.WindowId;
                }
            }
        }

        /// <summary>The regions registered on the last completed frame, in draw order (last on top).</summary>
        public IReadOnlyList<StockUiDragRegion> Regions { get { lock (_sync) return _regions; } }

        /// <summary>Starts a frame's registration; the layout is the one a released drag writes to.</summary>
        public void BeginFrame(StockUiLayout layout, float screenWidth, float screenHeight)
        {
            lock (_sync)
            {
                _layout = layout;
                _screenWidth = screenWidth;
                _screenHeight = screenHeight;
                _building.Clear();
            }
        }

        /// <summary>
        /// Registers a window drawn this frame. <paramref name="placement"/> is the frame's resolved placement (its
        /// origin is what a move writes back); the hit rectangle defaults to the frame's extent at that placement.
        /// <paramref name="placeholder"/> marks a window drawn only because the UI is unlocked (it has no content).
        /// </summary>
        public void Register(string windowId, UiMenuFrame frame, StockUiPlacement placement,
            float? hitX = null, float? hitY = null, float? hitWidth = null, float? hitHeight = null, bool placeholder = false)
        {
            var region = new StockUiDragRegion(windowId, frame, placement.X, placement.Y, placement.Scale,
                hitX ?? placement.X, hitY ?? placement.Y,
                hitWidth ?? frame.Width * placement.Scale, hitHeight ?? frame.Height * placement.Scale, placeholder);
            lock (_sync) _building.Add(region);
        }

        /// <summary>Publishes the frame's regions for hit testing.</summary>
        public void EndFrame()
        {
            lock (_sync)
            {
                _regions = new List<StockUiDragRegion>(_building);
                _building.Clear();
            }
        }

        /// <summary>
        /// The pointer pressed (left button) at a screen point. Returns true when a drag started, in which case the
        /// press is the drag's and not game input.
        /// </summary>
        public bool OnMouseDown(float x, float y)
        {
            lock (_sync)
            {
                _mouseX = x;
                _mouseY = y;
                if (!_unlocked || _drag != null) return false;
                var hit = HitTest(_regions, x, y);
                if (hit is not { } region) return false;
                _drag = region;
                _grabX = x - region.X;
                _grabY = y - region.Y;
                _dragX = region.X;
                _dragY = region.Y;
                return true;
            }
        }

        /// <summary>The pointer moved. Returns true while a drag is in progress (the move is the drag's).</summary>
        public bool OnMouseMove(float x, float y)
        {
            lock (_sync)
            {
                _mouseX = x;
                _mouseY = y;
                if (_drag is not { } region) return false;
                _dragX = Math.Clamp(x - _grabX, 0, Math.Max(0, _screenWidth - region.Width));
                _dragY = Math.Clamp(y - _grabY, 0, Math.Max(0, _screenHeight - region.Height));
                return true;
            }
        }

        /// <summary>
        /// The pointer was released. Ends the drag, writing the window's new placement to the layout (re-anchored to
        /// the nearest screen corner). Returns true when a drag ended.
        /// </summary>
        public bool OnMouseUp(float x, float y)
        {
            StockUiDragRegion region;
            StockUiLayout? layout;
            float originX, originY, screenWidth, screenHeight;
            lock (_sync)
            {
                _mouseX = x;
                _mouseY = y;
                if (_drag is not { } d) return false;
                region = d;
                _drag = null;
                layout = _layout;
                originX = _dragX + (region.OriginX - region.X);
                originY = _dragY + (region.OriginY - region.Y);
                screenWidth = _screenWidth;
                screenHeight = _screenHeight;
            }
            layout?.MoveTo(region.WindowId, region.Frame, originX, originY, screenWidth, screenHeight);
            return true;
        }

        /// <summary>Drops the current drag without moving the window (the layout is untouched).</summary>
        public void CancelDrag()
        {
            lock (_sync) _drag = null;
        }

        /// <summary>
        /// While <paramref name="windowId"/> is being dragged, its frame's current top-left (screen pixels).
        /// </summary>
        public bool TryGetDragPosition(string windowId, out float originX, out float originY)
        {
            lock (_sync)
            {
                if (_drag is { } region && string.Equals(region.WindowId, windowId, StringComparison.OrdinalIgnoreCase))
                {
                    originX = _dragX + (region.OriginX - region.X);
                    originY = _dragY + (region.OriginY - region.Y);
                    return true;
                }
            }
            originX = originY = 0;
            return false;
        }

        private static StockUiDragRegion? HitTest(List<StockUiDragRegion> regions, float x, float y)
        {
            // Regions are in draw order, so the last hit is the one on top.
            for (int i = regions.Count - 1; i >= 0; i--)
            {
                if (regions[i].Contains(x, y)) return regions[i];
            }
            return null;
        }
    }
}
