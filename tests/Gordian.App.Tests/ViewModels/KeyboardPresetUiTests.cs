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
    public void AutomaticMigrationAndConfirmedRestore_PreserveOriginalAndNonKeyboard_AndReload()
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
            Assert.NotNull(hub.Profile.KeyboardLayout);
            Assert.Equal(legacy.SaveToJson(), hub.Profile.KeyboardLayout.LegacySnapshot);
            Assert.Equal(KeyboardMode.Compact2, hub.Profile.KeyboardLayout.Mode);
            Assert.Contains(new InputChord(GordianKey.B), KeyboardLayout.Resolve(hub.Profile, KeyboardMode.Full).GetChords(InputAction.MoveForward));
            Assert.Empty(KeyboardLayout.Resolve(hub.Profile, KeyboardMode.Full).GetChords(InputAction.ToggleCameraMode));
            vm.LoadFullNumpadPresetCommand.Execute(null);
            var converted = hub.Profile.SaveToJson();
            vm.ResetDefaultsCommand.Execute(null);
            Assert.Equal(converted, hub.Profile.SaveToJson());
            Execute("CancelKeyboardChangeCommand");
            Assert.Equal(converted, hub.Profile.SaveToJson());
            vm.ResetDefaultsCommand.Execute(null);
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
    public void DashboardAndMenu_ShareModeAcrossCharacterSwitchAndDiskRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var hub = new InputProfileHub();
            var vm = new ControlsInputViewModel(path, hub);
            using var a = new Gordian.Engine.Sessions.CharacterSession("KeyboardA_" + Guid.NewGuid().ToString("N"), 1001, "fixture", new Gordian.Core.Network.SessionNetworkManager("127.0.0.1", 54230), hub);
            using var b = new Gordian.Engine.Sessions.CharacterSession("KeyboardB_" + Guid.NewGuid().ToString("N"), 1002, "fixture", new Gordian.Core.Network.SessionNetworkManager("127.0.0.1", 54230), hub);
            vm.SetSession(a);
            vm.LoadCompactPresetCommand.Execute(null);
            Assert.Equal(KeyboardMode.Compact2, hub.Profile.KeyboardLayout!.Mode);
            vm.SetSession(b);
            Assert.Contains("W", vm.Bindings.Single(r => r.Action == InputAction.MoveForward).BoundChords);
            b.ActionService.Menus.SetSetting(Gordian.Engine.Ui.StockUiSettingKey.KeyboardSize, 1);
            Assert.Contains("Compact1", vm.ActivePresetName);
            var fresh = new InputProfileHub();
            var restarted = new ControlsInputViewModel(path, fresh);
            Assert.Equal(KeyboardMode.Compact1, fresh.Profile.KeyboardLayout!.Mode);
            Assert.Contains("Compact1", restarted.ActivePresetName);
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
            var before = hub.Profile.KeyboardLayout!.Overrides["MoveForward"].ToArray();
            vm.LoadFullNumpadPresetCommand.Execute(null);
            Assert.Equal(KeyboardMode.Full, hub.Profile.KeyboardLayout.Mode);
            Assert.Equal(before, hub.Profile.KeyboardLayout.Overrides["MoveForward"]);
            var restarted = new ControlsInputViewModel(path, new InputProfileHub());
            Assert.Equal("B", restarted.Bindings.Single(r => r.Action == InputAction.MoveForward).BoundChords);
        }
        finally { File.Delete(path); }
    }
}
