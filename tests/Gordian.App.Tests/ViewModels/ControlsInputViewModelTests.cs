// tests/Gordian.App.Tests/ViewModels/ControlsInputViewModelTests.cs
using System.IO;
using Gordian.App.ViewModels;
using Gordian.Core.Input;
using Xunit;

namespace Gordian.App.Tests.ViewModels
{
    public sealed class ControlsInputViewModelTests
    {
        [Fact]
        public void InitialState_LoadsCompactPresetByDefault()
        {
            var vm = new ControlsInputViewModel();

            Assert.Equal("Compact (WASD)", vm.ActivePresetName);
            Assert.NotEmpty(vm.Bindings);
            Assert.Contains(vm.Bindings, b => b.Action == InputAction.MoveForward && b.BoundChords.Contains("W"));
            Assert.Contains(vm.Bindings, b => b.Action == InputAction.CameraPitchUp && b.BoundChords.Contains("I"));
        }

        [Fact]
        public void LoadFullNumpadPreset_UpdatesPresetAndBindings()
        {
            var vm = new ControlsInputViewModel();

            vm.LoadFullNumpadPresetCommand.Execute(null);

            Assert.Equal("Full (Numpad)", vm.ActivePresetName);
            Assert.Contains(vm.Bindings, b => b.Action == InputAction.MoveForward && b.BoundChords.Contains("NumPad8"));
            Assert.Contains(vm.Bindings, b => b.Action == InputAction.Confirm && b.BoundChords.Contains("NumPadEnter"));
        }

        [Fact]
        public void SensitivityAndTurnSpeed_UpdatePropertiesCorrectly()
        {
            var vm = new ControlsInputViewModel();

            vm.MouseSensitivityX = 2.5f;
            vm.MouseSensitivityY = 1.8f;
            vm.InvertMouseX = true;
            vm.InvertMouseY = true;
            vm.TurnSpeed = 240.0f;

            Assert.Equal(2.5f, vm.MouseSensitivityX);
            Assert.Equal(1.8f, vm.MouseSensitivityY);
            Assert.True(vm.InvertMouseX);
            Assert.True(vm.InvertMouseY);
            Assert.Equal(240.0f, vm.TurnSpeed);
        }

        [Fact]
        public void LoadGamepadPreset_UpdatesPresetAndGamepadBindings()
        {
            var vm = new ControlsInputViewModel();

            vm.LoadGamepadPresetCommand.Execute(null);

            Assert.Equal("Gamepad (Standard)", vm.ActivePresetName);
            Assert.Contains(vm.Bindings, b => b.Action == InputAction.Confirm && b.BoundChords.Contains("Pad:A"));
            Assert.Contains(vm.Bindings, b => b.Action == InputAction.Cancel && b.BoundChords.Contains("Pad:B"));
            Assert.Contains(vm.Bindings, b => b.Action == InputAction.OpenMenu && b.BoundChords.Contains("Pad:X"));
            Assert.Contains(vm.Bindings, b => b.Action == InputAction.ToggleAutorun && b.BoundChords.Contains("Pad:Y"));
        }

        [Fact]
        public void GamepadSettings_UpdatePropertiesCorrectly()
        {
            var vm = new ControlsInputViewModel();

            vm.LeftStickDeadzone = 0.25f;
            vm.RightStickDeadzone = 0.30f;
            vm.GamepadCameraSensitivity = 2.0f;
            vm.InvertGamepadCameraX = true;
            vm.InvertGamepadCameraY = true;
            vm.GamepadRumbleEnabled = false;
            vm.IsCameraRelativeLocomotion = false;
            vm.GamepadEnabled = false;
            vm.AlwaysEnableGamepad = true;

            Assert.Equal(0.25f, vm.LeftStickDeadzone, 2);
            Assert.Equal(0.30f, vm.RightStickDeadzone, 2);
            Assert.Equal(2.0f, vm.GamepadCameraSensitivity, 1);
            Assert.True(vm.InvertGamepadCameraX);
            Assert.True(vm.InvertGamepadCameraY);
            Assert.False(vm.GamepadRumbleEnabled);
            Assert.False(vm.IsCameraRelativeLocomotion);
            Assert.False(vm.GamepadEnabled);
            Assert.True(vm.AlwaysEnableGamepad);
        }

        [Fact]
        public void GamepadDisabled_TelemetryReportsDisabled()
        {
            var vm = new ControlsInputViewModel();
            vm.GamepadEnabled = false;
            vm.UpdateTelemetry();

            Assert.Equal("Disabled (Polling Off)", vm.GamepadStatusText);
            Assert.Equal("L: (0.00, 0.00) | R: (0.00, 0.00)", vm.GamepadSticksText);
            Assert.Equal("None", vm.GamepadButtonsText);
        }

        [Fact]
        public void TelemetryWithoutSession_ReportsNoActiveSession()
        {
            var vm = new ControlsInputViewModel();
            vm.SetSession(null);
            vm.UpdateTelemetry();

            Assert.Equal("No active session", vm.CurrentSpeedText);
            Assert.Equal("N/A", vm.CurrentHeadingText);
            Assert.Equal("N/A", vm.CameraInfoText);
            Assert.Equal("No Gamepad Detected", vm.GamepadStatusText);
        }
    }
}
