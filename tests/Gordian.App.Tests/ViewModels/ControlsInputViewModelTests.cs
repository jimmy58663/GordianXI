// tests/Gordian.App.Tests/ViewModels/ControlsInputViewModelTests.cs
using System;
using System.IO;
using Gordian.App.ViewModels;
using Gordian.Core.Input;
using Xunit;

namespace Gordian.App.Tests.ViewModels
{
    public sealed class ControlsInputViewModelTests
    {
        private static (ControlsInputViewModel vm, string tempFile) CreateIsolatedViewModel()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"gordian_input_test_{Guid.NewGuid():N}.json");
            var vm = new ControlsInputViewModel(tempFile, new InputProfileHub());
            return (vm, tempFile);
        }

        [Fact]
        public void InitialState_LoadsCompactPresetByDefault()
        {
            var (vm, tempFile) = CreateIsolatedViewModel();
            try
            {
                Assert.Equal("Compact (WASD)", vm.ActivePresetName);
                Assert.NotEmpty(vm.Bindings);
                Assert.Contains(vm.Bindings, b => b.Action == InputAction.MoveForward && b.BoundChords.Contains("W"));
                Assert.Contains(vm.Bindings, b => b.Action == InputAction.CameraPitchUp && b.BoundChords.Contains("I"));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void LoadFullNumpadPreset_UpdatesPresetAndBindings()
        {
            var (vm, tempFile) = CreateIsolatedViewModel();
            try
            {
                vm.LoadFullNumpadPresetCommand.Execute(null);

                Assert.Equal("Full (Numpad)", vm.ActivePresetName);
                Assert.Contains(vm.Bindings, b => b.Action == InputAction.MoveForward && b.BoundChords.Contains("NumPad8"));
                Assert.Contains(vm.Bindings, b => b.Action == InputAction.Confirm && b.BoundChords.Contains("NumPadEnter"));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void SensitivityAndTurnSpeed_UpdatePropertiesCorrectly()
        {
            var (vm, tempFile) = CreateIsolatedViewModel();
            try
            {
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
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void LoadGamepadPreset_ResetsGamepadBindingsWithoutChangingKeyboardLayout()
        {
            var (vm, tempFile) = CreateIsolatedViewModel();
            try
            {
                // Starts on the default Compact keyboard layout.
                Assert.Equal("Compact (WASD)", vm.ActivePresetName);

                vm.LoadGamepadPresetCommand.Execute(null);

                // Resetting gamepad bindings must never change (or discard) the keyboard layout.
                Assert.Equal("Compact (WASD)", vm.ActivePresetName);
                Assert.Contains(vm.Bindings, b => b.Action == InputAction.MoveForward && b.BoundChords.Contains("W"));

                Assert.Contains(vm.Bindings, b => b.Action == InputAction.Confirm && b.BoundChords.Contains("Pad:A"));
                Assert.Contains(vm.Bindings, b => b.Action == InputAction.Cancel && b.BoundChords.Contains("Pad:B"));
                Assert.Contains(vm.Bindings, b => b.Action == InputAction.OpenMenu && b.BoundChords.Contains("Pad:X"));
                Assert.Contains(vm.Bindings, b => b.Action == InputAction.CycleLogWindow && b.BoundChords.Contains("Pad:Y"));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void LoadFullNumpadPreset_PreservesCustomGamepadBindings()
        {
            var (vm, tempFile) = CreateIsolatedViewModel();
            try
            {
                // Reset gamepad to defaults first so we have a known gamepad chord to check survives.
                vm.LoadGamepadPresetCommand.Execute(null);
                Assert.Contains(vm.Bindings, b => b.Action == InputAction.Confirm && b.BoundChords.Contains("Pad:A"));

                vm.LoadFullNumpadPresetCommand.Execute(null);

                Assert.Equal("Full (Numpad)", vm.ActivePresetName);
                Assert.Contains(vm.Bindings, b => b.Action == InputAction.MoveForward && b.BoundChords.Contains("NumPad8"));
                // Switching keyboard layout must not have discarded the gamepad binding.
                Assert.Contains(vm.Bindings, b => b.Action == InputAction.Confirm && b.BoundChords.Contains("Pad:A"));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void GamepadSettings_UpdatePropertiesCorrectly()
        {
            var (vm, tempFile) = CreateIsolatedViewModel();
            try
            {
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
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void PropertyChanges_AutoSavesAndPersistsAcrossInstances()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"gordian_input_test_{Guid.NewGuid():N}.json");
            try
            {
                var vm1 = new ControlsInputViewModel(tempFile);
                vm1.InvertMouseY = true;
                vm1.InvertGamepadCameraY = true;
                vm1.MouseSensitivityX = 3.5f;
                vm1.GamepadCameraSensitivity = 2.5f;

                Assert.True(File.Exists(tempFile));

                // Reinstantiate to simulate application restart
                var vm2 = new ControlsInputViewModel(tempFile);
                Assert.True(vm2.InvertMouseY);
                Assert.True(vm2.InvertGamepadCameraY);
                Assert.Equal(3.5f, vm2.MouseSensitivityX);
                Assert.Equal(2.5f, vm2.GamepadCameraSensitivity);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        /// <summary>
        /// #358: the gamepad and first-person camera settings (#306) survive a restart all the way to a character's
        /// session: saved by the Controls &amp; Input page, loaded by a new page on a new hub, copied into a new session.
        /// </summary>
        [Fact]
        public void GamepadAndCameraSettings_SurviveARestart_IntoTheSession()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"gordian_input_test_{Guid.NewGuid():N}.json");
            try
            {
                var vm1 = new ControlsInputViewModel(tempFile, new InputProfileHub());
                vm1.InvertGamepadCameraX = true;
                vm1.InvertGamepadCameraY = true;
                vm1.SeparateFirstPersonGamepadCamera = true;
                vm1.FirstPersonInvertGamepadCameraX = false;
                vm1.FirstPersonGamepadCameraSensitivity = 2.5f;
                vm1.SeparateFirstPersonMouse = true;
                vm1.FirstPersonInvertMouseY = true;
                vm1.IsCameraRelativeLocomotion = false;

                // Restart: a new hub and page read the file; a character logging in copies the profile.
                var hub = new InputProfileHub();
                var vm2 = new ControlsInputViewModel(tempFile, hub);
                Assert.True(vm2.InvertGamepadCameraX);
                Assert.True(vm2.InvertGamepadCameraY);
                Assert.True(vm2.SeparateFirstPersonGamepadCamera);
                Assert.False(vm2.FirstPersonInvertGamepadCameraX);
                Assert.Equal(2.5f, vm2.FirstPersonGamepadCameraSensitivity);
                Assert.True(vm2.FirstPersonInvertMouseY);
                Assert.False(vm2.IsCameraRelativeLocomotion);

                using var session = new Gordian.Core.Network.CharacterSession("RestartTest", 1, "user1",
                    new Gordian.Core.Network.SessionNetworkManager("127.0.0.1", 54230), hub);
                var pad = session.Locomotion.Profile.GamepadSettings;
                Assert.Equal(new CameraAxisSettings(1f, 1f, true, true), pad.Camera(firstPerson: false));
                Assert.Equal(new CameraAxisSettings(2.5f, 2.5f, false, true), pad.Camera(firstPerson: true));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void GamepadDisabled_TelemetryReportsDisabled()
        {
            var (vm, tempFile) = CreateIsolatedViewModel();
            try
            {
                vm.GamepadEnabled = false;
                vm.UpdateTelemetry();

                Assert.Equal("Disabled (Polling Off)", vm.GamepadStatusText);
                Assert.Equal("L: (0.00, 0.00) | R: (0.00, 0.00)", vm.GamepadSticksText);
                Assert.Equal("None", vm.GamepadButtonsText);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void TelemetryWithoutSession_ReportsNoActiveSession()
        {
            var (vm, tempFile) = CreateIsolatedViewModel();
            try
            {
                vm.SetSession(null);
                vm.UpdateTelemetry();

                Assert.Equal("No active session", vm.CurrentSpeedText);
                Assert.Equal("N/A", vm.CurrentHeadingText);
                Assert.Equal("N/A", vm.CameraInfoText);
                Assert.Equal("No Gamepad Detected", vm.GamepadStatusText);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void TelemetryWithConnectedGamepad_ReportsDeviceName()
        {
            var (vm, tempFile) = CreateIsolatedViewModel();
            try
            {
                var netManager = new Gordian.Core.Network.SessionNetworkManager("127.0.0.1", 54230);
                var session = new Gordian.Core.Network.CharacterSession("TestChar", 12345, "user1", netManager);
                vm.SetSession(session);

                var pad = new GamepadState(
                    isConnected: true,
                    buttons: GamepadButton.A,
                    leftThumb: new System.Numerics.Vector2(0.5f, -0.5f),
                    rightThumb: System.Numerics.Vector2.Zero,
                    leftTrigger: 0.25f,
                    rightTrigger: 0.75f,
                    deviceName: "Silk.NET.SDL: DualSense Wireless Controller");

                session.InputState.SetGamepadState(pad);
                vm.UpdateTelemetry();

                Assert.Equal("Connected [Silk.NET.SDL: DualSense Wireless Controller]", vm.GamepadStatusText);
                Assert.Contains("A", vm.GamepadButtonsText);
                Assert.Equal("LT: 25% | RT: 75%", vm.GamepadTriggersText);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void EveryCharacter_GetsTheProfile_AndItsChanges_NotOnlyTheSelectedOne()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"gordian_input_test_{Guid.NewGuid():N}.json");
            var hub = new InputProfileHub();
            try
            {
                var vm = new ControlsInputViewModel(tempFile, hub);
                Assert.Equal(hub.Profile.Name, vm.ActivePresetName);
                using var selected = new Gordian.Core.Network.CharacterSession("VmTestA", 1, "user1", new Gordian.Core.Network.SessionNetworkManager("127.0.0.1", 54230), hub);
                using var other = new Gordian.Core.Network.CharacterSession("VmTestB", 2, "user2", new Gordian.Core.Network.SessionNetworkManager("127.0.0.1", 54230), hub);
                vm.SetSession(selected);

                vm.RightStickDeadzone = 0.3f;
                vm.InvertGamepadCameraY = true;
                vm.LoadFullNumpadPresetCommand.Execute(null);

                foreach (var session in new[] { selected, other })
                {
                    var profile = session.Locomotion.Profile;
                    Assert.NotSame(hub.Profile, profile);
                    Assert.Equal(0.3f, profile.GamepadSettings.RightStickDeadzone);
                    Assert.True(profile.GamepadSettings.InvertCameraY);
                    Assert.True(profile.TryGetAction(new InputChord(GordianKey.NumPad8), out var forward));
                    Assert.Equal(InputAction.MoveForward, forward);
                }
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void ProfileEditedInGame_IsShownAndSaved()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"gordian_input_test_{Guid.NewGuid():N}.json");
            var hub = new InputProfileHub();
            try
            {
                var vm = new ControlsInputViewModel(tempFile, hub);
                hub.Edit(p => GamepadAssignments.Assign(p, GamepadAssignments.Functions[0], GamepadButton.Y), source: "menus");

                Assert.Contains(vm.Bindings, b => b.Action == InputAction.Confirm && b.BoundChords.Contains("Pad:Y"));
                var saved = InputProfile.LoadOrCreate(tempFile);
                Assert.True(saved.TryGetAction(new InputChord(GamepadButton.Y), out var action));
                Assert.Equal(InputAction.Confirm, action);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void SeparateFirstPersonSettings_StartFromTheSharedOnes()
        {
            var (vm, tempFile) = CreateIsolatedViewModel();
            try
            {
                vm.InvertMouseY = true;
                vm.MouseSensitivityX = 2f;
                vm.SeparateFirstPersonMouse = true;
                Assert.True(vm.FirstPersonInvertMouseY);
                Assert.Equal(2f, vm.FirstPersonMouseSensitivityX);
                vm.FirstPersonInvertMouseY = false;
                Assert.True(vm.InvertMouseY);

                vm.InvertGamepadCameraX = true;
                vm.SeparateFirstPersonGamepadCamera = true;
                Assert.True(vm.FirstPersonInvertGamepadCameraX);
                vm.FirstPersonGamepadCameraSensitivity = 2.5f;
                Assert.Equal(1f, vm.GamepadCameraSensitivity);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }
}
