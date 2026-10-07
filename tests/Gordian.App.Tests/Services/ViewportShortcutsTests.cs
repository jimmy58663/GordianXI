using Avalonia.Input;
using Gordian.App.Services;
using Xunit;

namespace Gordian.App.Tests.Services
{
    /// <summary>The viewport window's own shortcuts, read on the tunnelling key-down (#151).</summary>
    public class ViewportShortcutsTests
    {
        [Theory]
        [InlineData(Key.Tab, KeyModifiers.Control, ViewportShortcut.NextCharacter)]
        [InlineData(Key.Tab, KeyModifiers.Control | KeyModifiers.Shift, ViewportShortcut.PreviousCharacter)]
        [InlineData(Key.Tab, KeyModifiers.None, ViewportShortcut.None)] // target cycling, the character's
        [InlineData(Key.Tab, KeyModifiers.Shift, ViewportShortcut.None)]
        [InlineData(Key.F11, KeyModifiers.None, ViewportShortcut.ToggleFullscreen)]
        [InlineData(Key.F10, KeyModifiers.None, ViewportShortcut.CycleTimeOfDay)]
        [InlineData(Key.F10, KeyModifiers.Control, ViewportShortcut.ToggleFog)]
        [InlineData(Key.F9, KeyModifiers.None, ViewportShortcut.CycleWeather)]
        [InlineData(Key.F9, KeyModifiers.Control, ViewportShortcut.ToggleOceanWater)]
        [InlineData(Key.F8, KeyModifiers.Control, ViewportShortcut.TogglePostProcess)]
        [InlineData(Key.F8, KeyModifiers.None, ViewportShortcut.None)]
        [InlineData(Key.W, KeyModifiers.Control, ViewportShortcut.None)]
        public void Classify_MapsTheWindowShortcuts(Key key, KeyModifiers modifiers, ViewportShortcut expected)
        {
            Assert.Equal(expected, ViewportShortcuts.Classify(key, modifiers));
        }
    }
}
