using Avalonia.Input;
using Gordian.App.Services;
using Xunit;

namespace Gordian.App.Tests.Services
{
    /// <summary>The viewport window's own shortcuts, read on the tunnelling key-down (#151).</summary>
    public class ViewportShortcutsTests
    {
        [Theory]
        [InlineData(Key.F9)] [InlineData(Key.F10)] [InlineData(Key.F11)]
        public void LayeredFunctionKeys_AreNeverTakenByDiagnosticWindowShortcuts(Key key)
        {
            var profile = Gordian.Engine.Input.InputProfile.CreateDefault();
            Assert.Equal(ViewportShortcut.None, ViewportShortcuts.Classify(key, KeyModifiers.None, profile));
            Assert.Equal(ViewportShortcut.RestoreStockUi, ViewportShortcuts.Classify(Key.F12, KeyModifiers.Control | KeyModifiers.Shift, profile));
        }

        [Theory]
        [InlineData(Key.Tab, KeyModifiers.Control, ViewportShortcut.NextCharacter)]
        [InlineData(Key.Tab, KeyModifiers.Control | KeyModifiers.Shift, ViewportShortcut.PreviousCharacter)]
        [InlineData(Key.Tab, KeyModifiers.None, ViewportShortcut.None)] // target cycling, the character's
        [InlineData(Key.Tab, KeyModifiers.Shift, ViewportShortcut.None)]
        [InlineData(Key.F11, KeyModifiers.None, ViewportShortcut.ToggleFullscreen)]
        [InlineData(Key.F10, KeyModifiers.None, ViewportShortcut.CycleTimeOfDay)]
        [InlineData(Key.F10, KeyModifiers.Control, ViewportShortcut.ToggleFog)]
        [InlineData(Key.F10, KeyModifiers.Control | KeyModifiers.Shift, ViewportShortcut.TogglePositionOverlay)]
        [InlineData(Key.F9, KeyModifiers.None, ViewportShortcut.CycleWeather)]
        [InlineData(Key.F9, KeyModifiers.Control, ViewportShortcut.ToggleOceanWater)]
        [InlineData(Key.F9, KeyModifiers.Control | KeyModifiers.Shift, ViewportShortcut.ToggleZoneBackFaceCulling)]
        [InlineData(Key.F8, KeyModifiers.Control, ViewportShortcut.TogglePostProcess)]
        [InlineData(Key.F8, KeyModifiers.None, ViewportShortcut.None)]
        [InlineData(Key.F12, KeyModifiers.Control, ViewportShortcut.CyclePerformanceOverlay)]
        [InlineData(Key.F12, KeyModifiers.None, ViewportShortcut.None)]
        [InlineData(Key.F12, KeyModifiers.Control | KeyModifiers.Shift, ViewportShortcut.RestoreStockUi)]
        [InlineData(Key.F11, KeyModifiers.Control | KeyModifiers.Shift, ViewportShortcut.ToggleImGuiTestWindow)]
        [InlineData(Key.W, KeyModifiers.Control, ViewportShortcut.None)]
        public void Classify_MapsTheWindowShortcuts(Key key, KeyModifiers modifiers, ViewportShortcut expected)
        {
            Assert.Equal(expected, ViewportShortcuts.Classify(key, modifiers));
        }
    }
}
