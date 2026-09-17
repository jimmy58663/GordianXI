// src/Gordian.App/ViewModels/ControlsInputViewModel.cs
// ViewModel powering the Controls & Input configuration and real-time telemetry dashboard.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using Gordian.App.Common;
using Gordian.Core.Diagnostics;
using Gordian.Core.Input;
using Gordian.Core.Network;

namespace Gordian.App.ViewModels
{
    public sealed class InputBindingItemViewModel : ViewModelBase
    {
        public InputAction Action { get; }
        public string Category { get; }
        public string ActionName { get; }
        public string BoundChords { get; private set; }

        public InputBindingItemViewModel(InputAction action, string category, string actionName, IEnumerable<InputChord> chords)
        {
            Action = action;
            Category = category;
            ActionName = actionName;
            BoundChords = string.Join(", ", chords);
        }

        public void UpdateChords(IEnumerable<InputChord> chords)
        {
            BoundChords = string.Join(", ", chords);
            OnPropertyChanged(nameof(BoundChords));
        }
    }

    public sealed class ControlsInputViewModel : ViewModelBase
    {
        private CharacterSession? _currentSession;
        private InputProfile _activeProfile;

        private string _activePresetName = "Compact (WASD)";
        private string _statusMessage = "Ready";

        // Telemetry
        private string _currentSpeedText = "0.0 y/s (Stationary)";
        private string _currentHeadingText = "0° (East, Dir 0)";
        private string _cameraInfoText = "Pitch: 15.0°, Yaw: 0.0°, Dist: 6.0y";
        private string _autorunStatusText = "Inactive";
        private string _walkingStatusText = "Running";
        private string _heldKeysText = "None";
        private string _activeActionsText = "None";

        public ObservableCollection<InputBindingItemViewModel> Bindings { get; } = new ObservableCollection<InputBindingItemViewModel>();

        public string ActivePresetName
        {
            get => _activePresetName;
            set => SetProperty(ref _activePresetName, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public float MouseSensitivityX
        {
            get => _activeProfile.MouseSensitivityX;
            set
            {
                if (Math.Abs(_activeProfile.MouseSensitivityX - value) > 0.01f)
                {
                    _activeProfile.MouseSensitivityX = value;
                    OnPropertyChanged();
                }
            }
        }

        public float MouseSensitivityY
        {
            get => _activeProfile.MouseSensitivityY;
            set
            {
                if (Math.Abs(_activeProfile.MouseSensitivityY - value) > 0.01f)
                {
                    _activeProfile.MouseSensitivityY = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool InvertMouseY
        {
            get => _activeProfile.InvertMouseY;
            set
            {
                if (_activeProfile.InvertMouseY != value)
                {
                    _activeProfile.InvertMouseY = value;
                    OnPropertyChanged();
                }
            }
        }

        public float TurnSpeed
        {
            get => _activeProfile.TurnSpeedDegreesPerSec;
            set
            {
                if (Math.Abs(_activeProfile.TurnSpeedDegreesPerSec - value) > 0.1f)
                {
                    _activeProfile.TurnSpeedDegreesPerSec = value;
                    OnPropertyChanged();
                }
            }
        }

        public string CurrentSpeedText
        {
            get => _currentSpeedText;
            private set => SetProperty(ref _currentSpeedText, value);
        }

        public string CurrentHeadingText
        {
            get => _currentHeadingText;
            private set => SetProperty(ref _currentHeadingText, value);
        }

        public string CameraInfoText
        {
            get => _cameraInfoText;
            private set => SetProperty(ref _cameraInfoText, value);
        }

        public string AutorunStatusText
        {
            get => _autorunStatusText;
            private set => SetProperty(ref _autorunStatusText, value);
        }

        public string WalkingStatusText
        {
            get => _walkingStatusText;
            private set => SetProperty(ref _walkingStatusText, value);
        }

        public string HeldKeysText
        {
            get => _heldKeysText;
            private set => SetProperty(ref _heldKeysText, value);
        }

        public string ActiveActionsText
        {
            get => _activeActionsText;
            private set => SetProperty(ref _activeActionsText, value);
        }

        public ICommand LoadCompactPresetCommand { get; }
        public ICommand LoadFullNumpadPresetCommand { get; }
        public ICommand ResetDefaultsCommand { get; }
        public ICommand SaveProfileCommand { get; }

        public ControlsInputViewModel()
        {
            _activeProfile = InputProfile.CreateCompact();

            LoadCompactPresetCommand = new RelayCommand(LoadCompactPreset);
            LoadFullNumpadPresetCommand = new RelayCommand(LoadFullNumpadPreset);
            ResetDefaultsCommand = new RelayCommand(ResetToDefaults);
            SaveProfileCommand = new RelayCommand(SaveProfile);

            PopulateBindingsTable();
        }

        public void SetSession(CharacterSession? session)
        {
            _currentSession = session;
            if (_currentSession != null)
            {
                _currentSession.Locomotion.Profile = _activeProfile;
            }
        }

        public void LoadCompactPreset()
        {
            _activeProfile = InputProfile.CreateCompact();
            ActivePresetName = "Compact (WASD)";
            NotifyProfilePropertiesChanged();
            PopulateBindingsTable();
            if (_currentSession != null)
            {
                _currentSession.Locomotion.Profile = _activeProfile;
            }
            StatusMessage = "Loaded standard FFXI Compact (WASD) keyboard preset.";
        }

        public void LoadFullNumpadPreset()
        {
            _activeProfile = InputProfile.CreateFullNumpad();
            ActivePresetName = "Full (Numpad)";
            NotifyProfilePropertiesChanged();
            PopulateBindingsTable();
            if (_currentSession != null)
            {
                _currentSession.Locomotion.Profile = _activeProfile;
            }
            StatusMessage = "Loaded standard FFXI Full (Numpad) keyboard preset.";
        }

        public void ResetToDefaults()
        {
            LoadCompactPreset();
        }

        public void SaveProfile()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string configPath = Path.Combine(localAppData, "GordianXI", "input_profile.json");
                _activeProfile.SaveToFile(configPath);
                StatusMessage = $"Input profile saved to: {configPath}";
            }
            catch (Exception ex)
            {
                GordianLog.Error("INPUT", $"Failed to save input profile: {ex.Message}", ex);
                StatusMessage = $"Save error: {ex.Message}";
            }
        }

        public void UpdateTelemetry()
        {
            if (_currentSession == null)
            {
                CurrentSpeedText = "No active session";
                CurrentHeadingText = "N/A";
                CameraInfoText = "N/A";
                AutorunStatusText = "N/A";
                WalkingStatusText = "N/A";
                HeldKeysText = "None";
                ActiveActionsText = "None";
                return;
            }

            var locomotion = _currentSession.Locomotion;
            var input = locomotion.InputState;

            // Speed
            float yps = 0;
            if (_currentSession.World.TryGetByServerId(_currentSession.LocalPlayer.ServerId, out var localEnt) && localEnt != null)
            {
                yps = localEnt.Speed * 0.1f;
                byte dir = localEnt.Direction;
                float headingDeg = (dir / 256.0f) * 360.0f;
                string cardinal = GetCardinal(headingDeg);
                CurrentHeadingText = $"{headingDeg:F0}° ({cardinal}, Dir {dir})";
            }
            else
            {
                CurrentHeadingText = "0° (Dir 0)";
            }

            string mode = input.IsWalking ? "Walking" : "Running";
            CurrentSpeedText = yps > 0 ? $"{yps:F1} y/s ({mode})" : $"0.0 y/s (Stationary)";
            AutorunStatusText = input.AutorunActive ? "Active" : "Inactive";
            WalkingStatusText = mode;

            // Camera
            CameraInfoText = $"Pitch: {locomotion.CameraPitch:F1}°, Yaw: {locomotion.CameraYaw:F1}°, Dist: {locomotion.CameraDistance:F1}y";

            // Held Keys
            var keys = input.GetCurrentHeldKeys();
            HeldKeysText = keys.Count > 0 ? string.Join(", ", keys) : "None";

            // Active Actions
            var actions = input.GetCurrentHeldActions();
            ActiveActionsText = actions.Count > 0 ? string.Join(", ", actions) : "None";
        }

        private void NotifyProfilePropertiesChanged()
        {
            OnPropertyChanged(nameof(MouseSensitivityX));
            OnPropertyChanged(nameof(MouseSensitivityY));
            OnPropertyChanged(nameof(InvertMouseY));
            OnPropertyChanged(nameof(TurnSpeed));
        }

        private void PopulateBindingsTable()
        {
            Bindings.Clear();

            // Locomotion
            AddBindingRow(InputAction.MoveForward, "Locomotion", "Move Forward");
            AddBindingRow(InputAction.MoveBackward, "Locomotion", "Move Backward");
            AddBindingRow(InputAction.TurnLeft, "Locomotion", "Turn Left");
            AddBindingRow(InputAction.TurnRight, "Locomotion", "Turn Right");
            AddBindingRow(InputAction.StrafeLeft, "Locomotion", "Strafe Left");
            AddBindingRow(InputAction.StrafeRight, "Locomotion", "Strafe Right");
            AddBindingRow(InputAction.ToggleAutorun, "Locomotion", "Toggle Autorun");
            AddBindingRow(InputAction.ToggleWalkRun, "Locomotion", "Toggle Walk / Run");

            // Camera
            AddBindingRow(InputAction.CameraPitchUp, "Camera", "Pitch Up (Look Up)");
            AddBindingRow(InputAction.CameraPitchDown, "Camera", "Pitch Down (Look Down)");
            AddBindingRow(InputAction.CameraYawLeft, "Camera", "Yaw Left (Rotate Left)");
            AddBindingRow(InputAction.CameraYawRight, "Camera", "Yaw Right (Rotate Right)");
            AddBindingRow(InputAction.CameraZoomIn, "Camera", "Zoom In (Closer)");
            AddBindingRow(InputAction.CameraZoomOut, "Camera", "Zoom Out (Farther)");
            AddBindingRow(InputAction.ResetCamera, "Camera", "Reset / Center Camera");

            // Targeting & Menus
            AddBindingRow(InputAction.Confirm, "Interaction", "Confirm / Action");
            AddBindingRow(InputAction.Cancel, "Interaction", "Cancel / Clear Target");
            AddBindingRow(InputAction.TargetNearest, "Targeting", "Target Nearest Entity");
            AddBindingRow(InputAction.TargetPrevious, "Targeting", "Target Previous Entity");
            AddBindingRow(InputAction.TargetSelf, "Targeting", "Target Self");
            AddBindingRow(InputAction.TargetParty1, "Targeting", "Target Party Member 1");
            AddBindingRow(InputAction.TargetParty2, "Targeting", "Target Party Member 2");
            AddBindingRow(InputAction.TargetParty3, "Targeting", "Target Party Member 3");
            AddBindingRow(InputAction.OpenMenu, "Interface", "Open Main Menu");
            AddBindingRow(InputAction.OpenChat, "Interface", "Focus Chat Prompt");

            // Macros
            AddBindingRow(InputAction.MacroCtrl1, "Hotbar", "Palette 1: Slot 1 (Ctrl+1)");
            AddBindingRow(InputAction.MacroCtrl2, "Hotbar", "Palette 1: Slot 2 (Ctrl+2)");
            AddBindingRow(InputAction.MacroAlt1, "Hotbar", "Palette 2: Slot 1 (Alt+1)");
            AddBindingRow(InputAction.MacroAlt2, "Hotbar", "Palette 2: Slot 2 (Alt+2)");
        }

        private void AddBindingRow(InputAction action, string category, string displayName)
        {
            var chords = _activeProfile.GetChords(action);
            Bindings.Add(new InputBindingItemViewModel(action, category, displayName, chords));
        }

        private static string GetCardinal(float deg)
        {
            deg %= 360.0f;
            if (deg < 0) deg += 360.0f;
            if (deg >= 337.5f || deg < 22.5f) return "East";
            if (deg >= 22.5f && deg < 67.5f) return "South-East";
            if (deg >= 67.5f && deg < 112.5f) return "South";
            if (deg >= 112.5f && deg < 157.5f) return "South-West";
            if (deg >= 157.5f && deg < 202.5f) return "West";
            if (deg >= 202.5f && deg < 247.5f) return "North-West";
            if (deg >= 247.5f && deg < 292.5f) return "North";
            return "North-East";
        }
    }
}
