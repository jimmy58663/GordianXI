// src/Gordian.App/Services/AvaloniaInputMapper.cs
// Maps Avalonia UI input events to cross-platform Gordian.Engine.Input abstractions.

using System;
using Avalonia.Input;
using Gordian.Engine.Input;

namespace Gordian.App.Services
{
    /// <summary>
    /// Utility class providing translation between Avalonia UI keyboard and mouse structures
    /// and clean-room Gordian.Engine.Input cross-platform types.
    /// </summary>
    public static class AvaloniaInputMapper
    {
        /// <summary>
        /// Whether a key may produce platform text. X11 can omit KeySymbol for shifted
        /// digits/punctuation even though the following TextInput contains the right character.
        /// Leave those key presses unhandled; never synthesize text from the key or a US layout.
        /// Editing keys and Ctrl/Alt shortcuts still belong to the active field.
        /// </summary>
        public static bool IsTextInputKey(Key key, string? symbol, KeyModifiers modifiers)
        {
            if ((modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0) return false;
            if (Gordian.Engine.Ui.StockUiChatInput.IsTypedSymbol(symbol)) return true;
            if (!string.IsNullOrEmpty(symbol)) return false;
            return key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9
                or >= Key.NumPad0 and <= Key.NumPad9 or Key.Space
                or Key.Multiply or Key.Add or Key.Subtract or Key.Decimal or Key.Divide
                or Key.OemTilde or Key.OemMinus or Key.OemPlus or Key.OemOpenBrackets
                or Key.OemCloseBrackets or Key.OemPipe or Key.OemBackslash
                or Key.OemSemicolon or Key.OemQuotes or Key.OemComma or Key.OemPeriod or Key.OemQuestion;
        }

        /// <summary>Preserves gameplay keypad identity regardless of Num Lock.</summary>
        public static GordianKey ToGordianKey(Key key, PhysicalKey physicalKey) => physicalKey switch
        {
            PhysicalKey.NumPad0 => GordianKey.NumPad0,
            PhysicalKey.NumPad1 => GordianKey.NumPad1,
            PhysicalKey.NumPad2 => GordianKey.NumPad2,
            PhysicalKey.NumPad3 => GordianKey.NumPad3,
            PhysicalKey.NumPad4 => GordianKey.NumPad4,
            PhysicalKey.NumPad5 => GordianKey.NumPad5,
            PhysicalKey.NumPad6 => GordianKey.NumPad6,
            PhysicalKey.NumPad7 => GordianKey.NumPad7,
            PhysicalKey.NumPad8 => GordianKey.NumPad8,
            PhysicalKey.NumPad9 => GordianKey.NumPad9,
            PhysicalKey.NumPadAdd => GordianKey.NumPadAdd,
            PhysicalKey.NumPadDecimal => GordianKey.NumPadDecimal,
            PhysicalKey.NumPadDivide => GordianKey.NumPadDivide,
            PhysicalKey.NumPadEnter => GordianKey.NumPadEnter,
            PhysicalKey.NumPadMultiply => GordianKey.NumPadMultiply,
            PhysicalKey.NumPadSubtract => GordianKey.NumPadSubtract,
            _ => ToGordianKey(key)
        };

        public static GordianKey ToGordianKey(Key key)
        {
            return key switch
            {
                // Alphabet
                Key.A => GordianKey.A,
                Key.B => GordianKey.B,
                Key.C => GordianKey.C,
                Key.D => GordianKey.D,
                Key.E => GordianKey.E,
                Key.F => GordianKey.F,
                Key.G => GordianKey.G,
                Key.H => GordianKey.H,
                Key.I => GordianKey.I,
                Key.J => GordianKey.J,
                Key.K => GordianKey.K,
                Key.L => GordianKey.L,
                Key.M => GordianKey.M,
                Key.N => GordianKey.N,
                Key.O => GordianKey.O,
                Key.P => GordianKey.P,
                Key.Q => GordianKey.Q,
                Key.R => GordianKey.R,
                Key.S => GordianKey.S,
                Key.T => GordianKey.T,
                Key.U => GordianKey.U,
                Key.V => GordianKey.V,
                Key.W => GordianKey.W,
                Key.X => GordianKey.X,
                Key.Y => GordianKey.Y,
                Key.Z => GordianKey.Z,

                // Digits
                Key.D0 => GordianKey.D0,
                Key.D1 => GordianKey.D1,
                Key.D2 => GordianKey.D2,
                Key.D3 => GordianKey.D3,
                Key.D4 => GordianKey.D4,
                Key.D5 => GordianKey.D5,
                Key.D6 => GordianKey.D6,
                Key.D7 => GordianKey.D7,
                Key.D8 => GordianKey.D8,
                Key.D9 => GordianKey.D9,

                // Numeric Keypad
                Key.NumPad0 => GordianKey.NumPad0,
                Key.NumPad1 => GordianKey.NumPad1,
                Key.NumPad2 => GordianKey.NumPad2,
                Key.NumPad3 => GordianKey.NumPad3,
                Key.NumPad4 => GordianKey.NumPad4,
                Key.NumPad5 => GordianKey.NumPad5,
                Key.NumPad6 => GordianKey.NumPad6,
                Key.NumPad7 => GordianKey.NumPad7,
                Key.NumPad8 => GordianKey.NumPad8,
                Key.NumPad9 => GordianKey.NumPad9,
                Key.Multiply => GordianKey.NumPadMultiply,
                Key.Add => GordianKey.NumPadAdd,
                Key.Subtract => GordianKey.NumPadSubtract,
                Key.Decimal => GordianKey.NumPadDecimal,
                Key.Divide => GordianKey.NumPadDivide,

                // Function Keys
                Key.F1 => GordianKey.F1,
                Key.F2 => GordianKey.F2,
                Key.F3 => GordianKey.F3,
                Key.F4 => GordianKey.F4,
                Key.F5 => GordianKey.F5,
                Key.F6 => GordianKey.F6,
                Key.F7 => GordianKey.F7,
                Key.F8 => GordianKey.F8,
                Key.F9 => GordianKey.F9,
                Key.F10 => GordianKey.F10,
                Key.F11 => GordianKey.F11,
                Key.F12 => GordianKey.F12,

                // Navigation & Editing
                Key.Escape => GordianKey.Escape,
                Key.Enter => GordianKey.Enter,
                Key.Space => GordianKey.Space,
                Key.Tab => GordianKey.Tab,
                Key.Back => GordianKey.Backspace,
                Key.Insert => GordianKey.Insert,
                Key.Delete => GordianKey.Delete,
                Key.Home => GordianKey.Home,
                Key.End => GordianKey.End,
                Key.PageUp => GordianKey.PageUp,
                Key.PageDown => GordianKey.PageDown,
                Key.Scroll => GordianKey.ScrollLock,
                Key.Print or Key.PrintScreen => GordianKey.PrintScreen,
                Key.Pause => GordianKey.Pause,

                // Arrows
                Key.Up => GordianKey.Up,
                Key.Down => GordianKey.Down,
                Key.Left => GordianKey.Left,
                Key.Right => GordianKey.Right,

                // Modifiers
                Key.LeftShift => GordianKey.LeftShift,
                Key.RightShift => GordianKey.RightShift,
                Key.LeftCtrl => GordianKey.LeftCtrl,
                Key.RightCtrl => GordianKey.RightCtrl,
                Key.LeftAlt => GordianKey.LeftAlt,
                Key.RightAlt => GordianKey.RightAlt,
                Key.LWin => GordianKey.LeftSuper,
                Key.RWin => GordianKey.RightSuper,

                // Symbols
                Key.OemTilde => GordianKey.OemTilde,
                Key.OemMinus => GordianKey.OemMinus,
                Key.OemPlus => GordianKey.OemPlus,
                Key.OemOpenBrackets => GordianKey.OemOpenBracket,
                Key.OemCloseBrackets => GordianKey.OemCloseBracket,
                Key.OemPipe or Key.OemBackslash => GordianKey.OemBackslash,
                Key.OemSemicolon => GordianKey.OemSemicolon,
                Key.OemQuotes => GordianKey.OemQuotes,
                Key.OemComma => GordianKey.OemComma,
                Key.OemPeriod => GordianKey.OemPeriod,
                Key.OemQuestion => GordianKey.OemSlash,

                _ => GordianKey.None
            };
        }

        public static InputModifiers ToInputModifiers(KeyModifiers modifiers)
        {
            InputModifiers result = InputModifiers.None;
            if (modifiers.HasFlag(KeyModifiers.Shift)) result |= InputModifiers.Shift;
            if (modifiers.HasFlag(KeyModifiers.Control)) result |= InputModifiers.Control;
            if (modifiers.HasFlag(KeyModifiers.Alt)) result |= InputModifiers.Alt;
            if (modifiers.HasFlag(KeyModifiers.Meta)) result |= InputModifiers.Super;
            return result;
        }

        public static Gordian.Engine.Input.MouseButton ToMouseButton(PointerPointProperties props)
        {
            var btn = Gordian.Engine.Input.MouseButton.None;
            if (props.IsLeftButtonPressed) btn |= Gordian.Engine.Input.MouseButton.Left;
            if (props.IsRightButtonPressed) btn |= Gordian.Engine.Input.MouseButton.Right;
            if (props.IsMiddleButtonPressed) btn |= Gordian.Engine.Input.MouseButton.Middle;
            if (props.IsXButton1Pressed) btn |= Gordian.Engine.Input.MouseButton.XButton1;
            if (props.IsXButton2Pressed) btn |= Gordian.Engine.Input.MouseButton.XButton2;
            return btn;
        }

        public static Gordian.Engine.Input.MouseButton ToMouseButton(Avalonia.Input.MouseButton button)
        {
            return button switch
            {
                Avalonia.Input.MouseButton.Left => Gordian.Engine.Input.MouseButton.Left,
                Avalonia.Input.MouseButton.Right => Gordian.Engine.Input.MouseButton.Right,
                Avalonia.Input.MouseButton.Middle => Gordian.Engine.Input.MouseButton.Middle,
                Avalonia.Input.MouseButton.XButton1 => Gordian.Engine.Input.MouseButton.XButton1,
                Avalonia.Input.MouseButton.XButton2 => Gordian.Engine.Input.MouseButton.XButton2,
                _ => Gordian.Engine.Input.MouseButton.None
            };
        }
    }
}
