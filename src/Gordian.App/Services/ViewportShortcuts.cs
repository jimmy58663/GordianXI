// src/Gordian.App/Services/ViewportShortcuts.cs
using Avalonia.Input;

namespace Gordian.App.Services
{
    /// <summary>A viewport window's own shortcut, handled by the window and never passed to the character.</summary>
    public enum ViewportShortcut
    {
        None,
        NextCharacter,
        PreviousCharacter,
        ToggleFullscreen,
        ToggleFog,
        TogglePostProcess,
        ToggleOceanWater,
        CycleWeather,
        CycleTimeOfDay,
    }

    /// <summary>
    /// The viewport window's shortcuts. They are read on the tunnelling key-down (before any control sees the key): on
    /// the bubbling one Avalonia's keyboard navigation takes Tab, Ctrl+Tab included, whenever a control in the window
    /// (a switcher button, the display mode box) has the focus, so Ctrl+Tab never reached the switch (#151).
    /// </summary>
    public static class ViewportShortcuts
    {
        public static ViewportShortcut Classify(Key key, KeyModifiers modifiers)
        {
            bool ctrl = (modifiers & KeyModifiers.Control) != 0;
            bool shift = (modifiers & KeyModifiers.Shift) != 0;
            return key switch
            {
                Key.Tab when ctrl => shift ? ViewportShortcut.PreviousCharacter : ViewportShortcut.NextCharacter,
                Key.F11 => ViewportShortcut.ToggleFullscreen,
                Key.F10 when ctrl => ViewportShortcut.ToggleFog,
                Key.F10 => ViewportShortcut.CycleTimeOfDay,
                Key.F9 when ctrl => ViewportShortcut.ToggleOceanWater,
                Key.F9 => ViewportShortcut.CycleWeather,
                Key.F8 when ctrl => ViewportShortcut.TogglePostProcess,
                _ => ViewportShortcut.None,
            };
        }
    }
}
