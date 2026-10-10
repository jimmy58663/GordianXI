// src/Gordian.App/Services/InputFocusTracker.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network;

namespace Gordian.App.Services
{
    /// <summary>
    /// Which GordianXI viewport window has the operating system's focus, and which character it shows (#150). The gamepad
    /// drives that character; with no viewport focused it keeps driving the character of the viewport focused last. Any
    /// feature that should follow window focus (sound, overlays) can read <see cref="FocusedSession"/> and listen to
    /// <see cref="Changed"/>. Windows are opaque keys, so this holds no Avalonia types and is used from the UI thread only.
    /// </summary>
    public sealed class InputFocusTracker
    {
        private readonly Dictionary<object, CharacterSession?> _windows = new(ReferenceEqualityComparer.Instance);
        private object? _focusedWindow;
        private object? _lastFocusedWindow;

        /// <summary>Raised when the focused window, the last focused window or the character either shows changes.</summary>
        public event EventHandler? Changed;

        /// <summary>True while one of the tracked viewport windows has the focus.</summary>
        public bool IsViewportFocused => _focusedWindow != null;

        /// <summary>
        /// The character shown in the focused viewport window; null when no viewport is focused or the focused one shows
        /// no character (a lobby, or a Log Out on its way back to it).
        /// </summary>
        public CharacterSession? FocusedSession => _focusedWindow != null ? _windows.GetValueOrDefault(_focusedWindow) : null;

        /// <summary>The character shown in the viewport window focused last (the focused one while one is).</summary>
        public CharacterSession? LastFocusedSession => _lastFocusedWindow != null ? _windows.GetValueOrDefault(_lastFocusedWindow) : null;

        /// <summary>
        /// Whether <paramref name="window"/> is the viewport the player is looking at (#301): the focused viewport window,
        /// or, while none has the focus (the control panel or another program does), the one focused last; every window
        /// before any has been focused. It draws every frame; the others draw at the background rate.
        /// </summary>
        public bool IsFocusTarget(object window)
        {
            ArgumentNullException.ThrowIfNull(window);
            if (_focusedWindow != null) return ReferenceEquals(_focusedWindow, window);
            return _lastFocusedWindow == null || ReferenceEquals(_lastFocusedWindow, window);
        }

        /// <summary>Starts tracking a window, or records that it now shows another character (null: none).</summary>
        public void SetWindowSession(object window, CharacterSession? session)
        {
            ArgumentNullException.ThrowIfNull(window);
            if (_windows.TryGetValue(window, out var current) && ReferenceEquals(current, session)) return;
            _windows[window] = session;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>A tracked window got the focus.</summary>
        public void Activated(object window)
        {
            ArgumentNullException.ThrowIfNull(window);
            if (!_windows.ContainsKey(window)) _windows[window] = null;
            if (ReferenceEquals(_focusedWindow, window) && ReferenceEquals(_lastFocusedWindow, window)) return;
            _focusedWindow = window;
            _lastFocusedWindow = window;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>A tracked window lost the focus; it stays the last focused one.</summary>
        public void Deactivated(object window)
        {
            if (!ReferenceEquals(_focusedWindow, window)) return;
            _focusedWindow = null;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>A window closed: it is forgotten, as focused and as last focused.</summary>
        public void Remove(object window)
        {
            if (!_windows.Remove(window)) return;
            if (ReferenceEquals(_focusedWindow, window)) _focusedWindow = null;
            if (ReferenceEquals(_lastFocusedWindow, window)) _lastFocusedWindow = null;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// The character the gamepad drives: the focused viewport's (none when it shows no character), otherwise the last
        /// focused viewport's, otherwise <paramref name="fallback"/> (the primary viewport's active character). Whether
        /// the pad is read at all while no GordianXI window is focused is the caller's (Always Enable Gamepad).
        /// </summary>
        public CharacterSession? ResolveGamepadTarget(CharacterSession? fallback)
        {
            if (IsViewportFocused) return FocusedSession;
            return LastFocusedSession ?? fallback;
        }
    }
}
