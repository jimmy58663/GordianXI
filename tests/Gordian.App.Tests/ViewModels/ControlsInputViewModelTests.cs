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
            vm.InvertMouseY = true;
            vm.TurnSpeed = 240.0f;

            Assert.Equal(2.5f, vm.MouseSensitivityX);
            Assert.Equal(1.8f, vm.MouseSensitivityY);
            Assert.True(vm.InvertMouseY);
            Assert.Equal(240.0f, vm.TurnSpeed);
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
        }
    }
}
