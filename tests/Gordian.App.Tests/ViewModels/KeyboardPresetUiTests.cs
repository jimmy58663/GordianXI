using System;
using System.IO;
using System.Linq;
using Gordian.App.ViewModels;
using Gordian.Engine.Input;
using Xunit;

namespace Gordian.App.Tests.ViewModels;
public class KeyboardPresetUiTests
{
    [Fact]
    public void ConversionAndRestore_AreConfirmed_PreserveOriginalAndNonKeyboard_AndReload()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var legacy = InputProfile.CreateCompact();
        legacy.MouseSensitivityX = 3.25f;
        legacy.Bindings[InputAction.MoveForward] = new() { new InputChord(GordianKey.B) };
        legacy.Bindings[InputAction.ToggleCameraMode] = new();
        legacy.SaveToFile(path);
        try
        {
            var hub = new InputProfileHub();
            var vm = new ControlsInputViewModel(path, hub);
            void Execute(string property)
            {
                var p = typeof(ControlsInputViewModel).GetProperty(property);
                Assert.NotNull(p);
                Assert.IsAssignableFrom<System.Windows.Input.ICommand>(p.GetValue(vm)).Execute(null);
            }
            var before = hub.Profile.SaveToJson();
            Execute("ConvertKeyboardCommand");
            Assert.Equal(before, hub.Profile.SaveToJson());
            Execute("CancelKeyboardChangeCommand");
            Assert.Equal(before, hub.Profile.SaveToJson());
            Execute("ConvertKeyboardCommand");
            Execute("ConfirmKeyboardChangeCommand");
            Assert.NotNull(hub.Profile.KeyboardLayout);
            Assert.Contains(new InputChord(GordianKey.B), KeyboardLayout.Resolve(hub.Profile, KeyboardMode.Full).GetChords(InputAction.MoveForward));
            Assert.Empty(KeyboardLayout.Resolve(hub.Profile, KeyboardMode.Full).GetChords(InputAction.ToggleCameraMode));
            vm.LoadFullNumpadPresetCommand.Execute(null);
            var converted = hub.Profile.SaveToJson();
            vm.ResetDefaultsCommand.Execute(null);
            Assert.Equal(converted, hub.Profile.SaveToJson());
            Execute("ConfirmKeyboardChangeCommand");
            var loaded = InputProfile.LoadOrCreate(path);
            Assert.Equal(3.25f, loaded.MouseSensitivityX);
            Assert.Equal(legacy.GamepadSettings.CameraSensitivityX, loaded.GamepadSettings.CameraSensitivityX);
            Assert.Contains(new InputChord(GamepadButton.RightShoulder), loaded.GetChords(InputAction.ToggleAutorun));
            Assert.Contains(new InputChord(GordianKey.NumPad8), KeyboardLayout.Resolve(loaded, KeyboardMode.Full).GetChords(InputAction.MoveForward));
            Assert.Contains("legacySnapshot", loaded.SaveToJson());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FreshDashboard_SelectsActualLayersAndShowsEffectiveKeys()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var hub = new InputProfileHub();
            var vm = new ControlsInputViewModel(path, hub);
            Assert.NotNull(hub.Profile.KeyboardLayout);
            vm.LoadFullNumpadPresetCommand.Execute(null);
            Assert.Contains("NumPad8", vm.Bindings.Single(b => b.Action == InputAction.MoveForward).BoundChords);
            vm.LoadCompactPresetCommand.Execute(null);
            Assert.Equal(KeyboardMode.Compact2, hub.Profile.KeyboardLayout.Mode);
            Assert.Contains("W", vm.Bindings.Single(b => b.Action == InputAction.MoveForward).BoundChords);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Dashboard_FollowsSharedProfileReplacement()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var hub = new InputProfileHub();
            var vm = new ControlsInputViewModel(path, hub);
            var replacement = InputProfile.CreateDefault();
            replacement.KeyboardLayout!.Overrides["MoveForward"] = new() { "B" };
            hub.SetProfile(replacement);
            Assert.Equal("B", vm.Bindings.Single(b => b.Action == InputAction.MoveForward).BoundChords);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LegacyPresetSelection_DoesNotSilentlyOverwriteCustomKeys()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var legacy = InputProfile.CreateCompact();
        legacy.Bindings[InputAction.MoveForward] = new() { new InputChord(GordianKey.B) };
        legacy.SaveToFile(path);
        try
        {
            var hub = new InputProfileHub();
            var vm = new ControlsInputViewModel(path, hub);
            var before = hub.Profile.SaveToJson();
            vm.LoadFullNumpadPresetCommand.Execute(null);
            Assert.Equal(before, hub.Profile.SaveToJson());
            Assert.Contains("legacy", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }
}
