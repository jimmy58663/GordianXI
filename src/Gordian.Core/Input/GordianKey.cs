// src/Gordian.Core/Input/GordianKey.cs
// Clean-room cross-platform input key enumerations for GordianXI.
// Layout conventions referenced from Final Fantasy XI standard keyboard configurations.

using System;

namespace Gordian.Core.Input
{
    /// <summary>
    /// Bitwise modifier key flags for keyboard and mouse shortcuts.
    /// </summary>
    [Flags]
    public enum InputModifiers : byte
    {
        None = 0,
        Shift = 1 << 0,
        Control = 1 << 1,
        Alt = 1 << 2,
        Super = 1 << 3
    }

    /// <summary>
    /// Mouse buttons supported for gameplay interaction and camera manipulation.
    /// </summary>
    [Flags]
    public enum MouseButton : byte
    {
        None = 0,
        Left = 1 << 0,
        Right = 1 << 1,
        Middle = 1 << 2,
        XButton1 = 1 << 3,
        XButton2 = 1 << 4
    }

    /// <summary>
    /// Cross-platform physical keyboard key codes.
    /// Independent of OS-specific virtual key codes (e.g. Win32 VK or X11 keysyms).
    /// </summary>
    public enum GordianKey : ushort
    {
        None = 0,

        // Standard letters A-Z
        A = 1, B, C, D, E, F, G, H, I, J, K, L, M,
        N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

        // Digits 0-9
        D0 = 30, D1, D2, D3, D4, D5, D6, D7, D8, D9,

        // Function keys F1-F12
        F1 = 50, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,

        // Numeric keypad
        NumPad0 = 70, NumPad1, NumPad2, NumPad3, NumPad4,
        NumPad5, NumPad6, NumPad7, NumPad8, NumPad9,
        NumPadMultiply,
        NumPadAdd,
        NumPadSubtract,
        NumPadDecimal,
        NumPadDivide,
        NumPadEnter,

        // Navigation and editing
        Escape = 90,
        Enter,
        Space,
        Tab,
        Backspace,
        Insert,
        Delete,
        Home,
        End,
        PageUp,
        PageDown,

        // Directional arrows
        Up = 110,
        Down,
        Left,
        Right,

        // Modifiers (as standalone keys)
        LeftShift = 120,
        RightShift,
        LeftCtrl,
        RightCtrl,
        LeftAlt,
        RightAlt,
        LeftSuper,
        RightSuper,
        CapsLock,
        ScrollLock,
        NumLock,

        // Punctuation and symbols
        OemTilde = 140,      // ` or ~
        OemMinus,            // - or _
        OemPlus,             // = or +
        OemOpenBracket,      // [ or {
        OemCloseBracket,     // ] or }
        OemBackslash,        // \ or |
        OemSemicolon,        // ; or :
        OemQuotes,           // ' or "
        OemComma,            // , or <
        OemPeriod,           // . or >
        OemSlash             // / or ?
    }
}
