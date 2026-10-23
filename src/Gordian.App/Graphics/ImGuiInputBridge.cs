// src/Gordian.App/Graphics/ImGuiInputBridge.cs
using System.Collections.Generic;
using Avalonia.Input;
using Hexa.NET.ImGui;

namespace Gordian.App.Graphics
{
    /// <summary>The kind of an input event queued for the ImGui context.</summary>
    public enum ImGuiInputKind : byte
    {
        MousePos,
        MouseButton,
        MouseWheel,
        Key,
        Text,
        Focus,
    }

    /// <summary>One input event for the ImGui context. <see cref="X"/>/<see cref="Y"/> are the position or the wheel delta.</summary>
    public readonly record struct ImGuiInputEvent(ImGuiInputKind Kind, float X = 0, float Y = 0, int Code = 0, bool Down = false, string? Text = null);

    /// <summary>
    /// Carries mouse and keyboard input from a viewport window's UI thread to its ImGui layer on the render thread, and
    /// the layer's answer back (#380): whether ImGui wants the mouse or keyboard. The window feeds every event here while
    /// an interactive overlay is visible, and gives the event to the game only when ImGui does not want it; while every
    /// overlay is hidden nothing is queued and nothing is captured, so the game's input path is unchanged.
    /// </summary>
    public sealed class ImGuiInputBridge
    {
        /// <summary>Most events held between two frames; further ones are dropped (a stalled render thread).</summary>
        public const int MaxQueued = 1024;

        private readonly object _gate = new();
        private List<ImGuiInputEvent> _queue = new(64);
        private List<ImGuiInputEvent> _spare = new(64);
        private volatile bool _wantMouse;
        private volatile bool _wantKeyboard;
        private volatile bool _wantText;
        private volatile int _cursor;

        /// <summary>The pointer is over an ImGui window or dragging in one, as of the last ImGui frame.</summary>
        public bool WantCaptureMouse => _wantMouse;

        /// <summary>An ImGui item is active with the keyboard (a text field, a drag), as of the last ImGui frame.</summary>
        public bool WantCaptureKeyboard => _wantKeyboard;

        /// <summary>A text field is being edited, so typed characters go to ImGui.</summary>
        public bool WantTextInput => _wantText;

        /// <summary>The cursor shape ImGui wants over its windows.</summary>
        public ImGuiMouseCursor MouseCursor => (ImGuiMouseCursor)_cursor;

        /// <summary>True when overlays are visible, so input is worth queueing.</summary>
        public static bool Accepting => ImGuiOverlayRegistry.AnyVisible;

        public void MouseMove(float x, float y) => Enqueue(new ImGuiInputEvent(ImGuiInputKind.MousePos, x, y));

        /// <summary>The pointer left the surface: ImGui sees no hover.</summary>
        public void MouseLeave() => Enqueue(new ImGuiInputEvent(ImGuiInputKind.MousePos, -float.MaxValue, -float.MaxValue));

        /// <param name="button">0 left, 1 right, 2 middle.</param>
        public void MouseButton(int button, bool down) => Enqueue(new ImGuiInputEvent(ImGuiInputKind.MouseButton, Code: button, Down: down));

        public void MouseWheel(float deltaX, float deltaY) => Enqueue(new ImGuiInputEvent(ImGuiInputKind.MouseWheel, deltaX, deltaY));

        public void Key(ImGuiKey key, bool down)
        {
            if (key != ImGuiKey.None) Enqueue(new ImGuiInputEvent(ImGuiInputKind.Key, Code: (int)key, Down: down));
        }

        public void Text(string text)
        {
            if (!string.IsNullOrEmpty(text)) Enqueue(new ImGuiInputEvent(ImGuiInputKind.Text, Text: text));
        }

        public void Focus(bool focused) => Enqueue(new ImGuiInputEvent(ImGuiInputKind.Focus, Down: focused));

        private void Enqueue(in ImGuiInputEvent e)
        {
            if (!Accepting) return;
            lock (_gate)
            {
                if (_queue.Count < MaxQueued) _queue.Add(e);
            }
        }

        /// <summary>Takes the queued events (render thread); the returned list is reused, so use it before the next call.</summary>
        internal List<ImGuiInputEvent> Drain()
        {
            lock (_gate)
            {
                (_queue, _spare) = (_spare, _queue);
                _queue.Clear();
                return _spare;
            }
        }

        /// <summary>Records what ImGui wants after a frame (render thread).</summary>
        internal void Publish(bool wantMouse, bool wantKeyboard, bool wantText, ImGuiMouseCursor cursor)
        {
            _wantMouse = wantMouse;
            _wantKeyboard = wantKeyboard;
            _wantText = wantText;
            _cursor = (int)cursor;
        }

        /// <summary>No overlay is shown: nothing is queued, nothing is captured.</summary>
        internal void Reset()
        {
            lock (_gate)
            {
                _queue.Clear();
            }
            Publish(false, false, false, ImGuiMouseCursor.Arrow);
        }
    }

    /// <summary>Avalonia key to Dear ImGui key.</summary>
    public static class ImGuiKeyMap
    {
        public static ImGuiKey FromAvalonia(Key key)
        {
            if (key >= Key.A && key <= Key.Z) return ImGuiKey.A + (key - Key.A);
            if (key >= Key.D0 && key <= Key.D9) return ImGuiKey.Key0 + (key - Key.D0);
            if (key >= Key.NumPad0 && key <= Key.NumPad9) return ImGuiKey.Keypad0 + (key - Key.NumPad0);
            if (key >= Key.F1 && key <= Key.F12) return ImGuiKey.F1 + (key - Key.F1);
            return key switch
            {
                Key.Tab => ImGuiKey.Tab,
                Key.Left => ImGuiKey.LeftArrow,
                Key.Right => ImGuiKey.RightArrow,
                Key.Up => ImGuiKey.UpArrow,
                Key.Down => ImGuiKey.DownArrow,
                Key.PageUp => ImGuiKey.PageUp,
                Key.PageDown => ImGuiKey.PageDown,
                Key.Home => ImGuiKey.Home,
                Key.End => ImGuiKey.End,
                Key.Insert => ImGuiKey.Insert,
                Key.Delete => ImGuiKey.Delete,
                Key.Back => ImGuiKey.Backspace,
                Key.Space => ImGuiKey.Space,
                Key.Enter => ImGuiKey.Enter,
                Key.Escape => ImGuiKey.Escape,
                Key.LeftCtrl => ImGuiKey.LeftCtrl,
                Key.RightCtrl => ImGuiKey.RightCtrl,
                Key.LeftShift => ImGuiKey.LeftShift,
                Key.RightShift => ImGuiKey.RightShift,
                Key.LeftAlt => ImGuiKey.LeftAlt,
                Key.RightAlt => ImGuiKey.RightAlt,
                Key.LWin => ImGuiKey.LeftSuper,
                Key.RWin => ImGuiKey.RightSuper,
                Key.OemComma => ImGuiKey.Comma,
                Key.OemPeriod => ImGuiKey.Period,
                Key.OemMinus => ImGuiKey.Minus,
                Key.OemPlus => ImGuiKey.Equal,
                Key.OemSemicolon => ImGuiKey.Semicolon,
                Key.OemQuotes => ImGuiKey.Apostrophe,
                Key.OemOpenBrackets => ImGuiKey.LeftBracket,
                Key.OemCloseBrackets => ImGuiKey.RightBracket,
                Key.OemPipe => ImGuiKey.Backslash,
                Key.OemBackslash => ImGuiKey.Backslash,
                Key.OemQuestion => ImGuiKey.Slash,
                Key.OemTilde => ImGuiKey.GraveAccent,
                Key.Decimal => ImGuiKey.KeypadDecimal,
                Key.Divide => ImGuiKey.KeypadDivide,
                Key.Multiply => ImGuiKey.KeypadMultiply,
                Key.Subtract => ImGuiKey.KeypadSubtract,
                Key.Add => ImGuiKey.KeypadAdd,
                Key.CapsLock => ImGuiKey.CapsLock,
                _ => ImGuiKey.None,
            };
        }

        /// <summary>The Avalonia cursor shape for an ImGui one.</summary>
        public static StandardCursorType ToAvaloniaCursor(ImGuiMouseCursor cursor) => cursor switch
        {
            ImGuiMouseCursor.TextInput => StandardCursorType.Ibeam,
            ImGuiMouseCursor.ResizeAll => StandardCursorType.SizeAll,
            ImGuiMouseCursor.ResizeNs => StandardCursorType.SizeNorthSouth,
            ImGuiMouseCursor.ResizeEw => StandardCursorType.SizeWestEast,
            ImGuiMouseCursor.ResizeNesw => StandardCursorType.TopRightCorner,
            ImGuiMouseCursor.ResizeNwse => StandardCursorType.TopLeftCorner,
            ImGuiMouseCursor.Hand => StandardCursorType.Hand,
            ImGuiMouseCursor.NotAllowed => StandardCursorType.No,
            ImGuiMouseCursor.None => StandardCursorType.None,
            _ => StandardCursorType.Arrow,
        };
    }
}
