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
using Gordian.Core.Config;
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
        private readonly string _profilePath;
        private readonly InputProfileHub _hub;
        private CharacterSession? _currentSession;
        private InputProfile _activeProfile;

        private string _activePresetName = "Compact (WASD)";
        private string _statusMessage = "Ready";

        public static string GetDefaultProfilePath() => Path.Combine(GordianStorage.UserSettingsDirectory, "input_profile.json");

        /// <summary>
        /// An edit made here: saved, and published to every character's session (each takes a fresh copy of the
        /// profile, #314).
        /// </summary>
        private void Commit()
        {
            AutoSave();
            _hub.NotifyChanged(this);
        }

        /// <summary>The profile was edited elsewhere (the in-game gamepad key-assignment list): show and save it.</summary>
        private void OnHubChanged(object? source)
        {
            if (ReferenceEquals(source, this)) return;
            if (!ReferenceEquals(_hub.Profile, _activeProfile)) return;
            NotifyProfilePropertiesChanged();
            PopulateBindingsTable();
            AutoSave();
        }

        private void AutoSave()
        {
            try
            {
                _activeProfile.SaveToFile(_profilePath);
            }
            catch (Exception ex)
            {
                GordianLog.Warn("INPUT", $"Auto-save failed: {ex.Message}");
            }
        }

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
                    Commit();
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
                    Commit();
                }
            }
        }

        public bool InvertMouseX
        {
            get => _activeProfile.InvertMouseX;
            set
            {
                if (_activeProfile.InvertMouseX != value)
                {
                    _activeProfile.InvertMouseX = value;
                    OnPropertyChanged();
                    Commit();
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
                    Commit();
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
                    Commit();
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

        // Gamepad Settings & Telemetry
        private string _gamepadStatusText = "No Gamepad Detected";
        private string _gamepadSticksText = "L: (0.00, 0.00) | R: (0.00, 0.00)";
        private string _gamepadTriggersText = "LT: 0% | RT: 0%";
        private string _gamepadButtonsText = "None";

        public string GamepadStatusText
        {
            get => _gamepadStatusText;
            private set => SetProperty(ref _gamepadStatusText, value);
        }

        public string GamepadSticksText
        {
            get => _gamepadSticksText;
            private set => SetProperty(ref _gamepadSticksText, value);
        }

        public string GamepadTriggersText
        {
            get => _gamepadTriggersText;
            private set => SetProperty(ref _gamepadTriggersText, value);
        }

        public string GamepadButtonsText
        {
            get => _gamepadButtonsText;
            private set => SetProperty(ref _gamepadButtonsText, value);
        }

        public bool GamepadEnabled
        {
            get => _activeProfile.GamepadSettings.GamepadEnabled;
            set
            {
                if (_activeProfile.GamepadSettings.GamepadEnabled != value)
                {
                    _activeProfile.GamepadSettings.GamepadEnabled = value;
                    OnPropertyChanged();
                    Commit();
                }
            }
        }

        public bool AlwaysEnableGamepad
        {
            get => _activeProfile.GamepadSettings.AlwaysEnableGamepad;
            set
            {
                if (_activeProfile.GamepadSettings.AlwaysEnableGamepad != value)
                {
                    _activeProfile.GamepadSettings.AlwaysEnableGamepad = value;
                    OnPropertyChanged();
                    Commit();
                }
            }
        }

        public float LeftStickDeadzone
        {
            get => _activeProfile.GamepadSettings.LeftStickDeadzone;
            set
            {
                if (Math.Abs(_activeProfile.GamepadSettings.LeftStickDeadzone - value) > 0.01f)
                {
                    _activeProfile.GamepadSettings.LeftStickDeadzone = value;
                    OnPropertyChanged();
                    Commit();
                }
            }
        }

        public float RightStickDeadzone
        {
            get => _activeProfile.GamepadSettings.RightStickDeadzone;
            set
            {
                if (Math.Abs(_activeProfile.GamepadSettings.RightStickDeadzone - value) > 0.01f)
                {
                    _activeProfile.GamepadSettings.RightStickDeadzone = value;
                    OnPropertyChanged();
                    Commit();
                }
            }
        }

        public float GamepadCameraSensitivity
        {
            get => _activeProfile.GamepadSettings.CameraSensitivityX;
            set
            {
                if (Math.Abs(_activeProfile.GamepadSettings.CameraSensitivityX - value) > 0.01f)
                {
                    _activeProfile.GamepadSettings.CameraSensitivityX = value;
                    _activeProfile.GamepadSettings.CameraSensitivityY = value;
                    OnPropertyChanged();
                    Commit();
                }
            }
        }

        public bool InvertGamepadCameraX
        {
            get => _activeProfile.GamepadSettings.InvertCameraX;
            set
            {
                if (_activeProfile.GamepadSettings.InvertCameraX != value)
                {
                    _activeProfile.GamepadSettings.InvertCameraX = value;
                    OnPropertyChanged();
                    Commit();
                }
            }
        }

        public bool InvertGamepadCameraY
        {
            get => _activeProfile.GamepadSettings.InvertCameraY;
            set
            {
                if (_activeProfile.GamepadSettings.InvertCameraY != value)
                {
                    _activeProfile.GamepadSettings.InvertCameraY = value;
                    OnPropertyChanged();
                    Commit();
                }
            }
        }

        public bool GamepadRumbleEnabled
        {
            get => _activeProfile.GamepadSettings.RumbleEnabled;
            set
            {
                if (_activeProfile.GamepadSettings.RumbleEnabled != value)
                {
                    _activeProfile.GamepadSettings.RumbleEnabled = value;
                    OnPropertyChanged();
                    Commit();
                }
            }
        }

        public bool IsCameraRelativeLocomotion
        {
            get => _activeProfile.GamepadSettings.LocomotionMode == GamepadLocomotionMode.CameraRelative;
            set
            {
                var target = value ? GamepadLocomotionMode.CameraRelative : GamepadLocomotionMode.CharacterRelative;
                if (_activeProfile.GamepadSettings.LocomotionMode != target)
                {
                    _activeProfile.GamepadSettings.LocomotionMode = target;
                    OnPropertyChanged();
                    Commit();
                }
            }
        }

        // First-person camera settings (#306): off by default, first person then uses the settings above.

        public bool SeparateFirstPersonMouse
        {
            get => _activeProfile.SeparateFirstPersonMouse;
            set
            {
                if (_activeProfile.SeparateFirstPersonMouse == value) return;
                if (value && !_activeProfile.SeparateFirstPersonMouse)
                {
                    // Start the first-person set from the shared one, so turning it on changes nothing by itself.
                    _activeProfile.FirstPersonMouseSensitivityX = _activeProfile.MouseSensitivityX;
                    _activeProfile.FirstPersonMouseSensitivityY = _activeProfile.MouseSensitivityY;
                    _activeProfile.FirstPersonInvertMouseX = _activeProfile.InvertMouseX;
                    _activeProfile.FirstPersonInvertMouseY = _activeProfile.InvertMouseY;
                }
                _activeProfile.SeparateFirstPersonMouse = value;
                NotifyProfilePropertiesChanged();
                Commit();
            }
        }

        public float FirstPersonMouseSensitivityX
        {
            get => _activeProfile.FirstPersonMouseSensitivityX;
            set
            {
                if (Math.Abs(_activeProfile.FirstPersonMouseSensitivityX - value) <= 0.01f) return;
                _activeProfile.FirstPersonMouseSensitivityX = value;
                OnPropertyChanged();
                Commit();
            }
        }

        public float FirstPersonMouseSensitivityY
        {
            get => _activeProfile.FirstPersonMouseSensitivityY;
            set
            {
                if (Math.Abs(_activeProfile.FirstPersonMouseSensitivityY - value) <= 0.01f) return;
                _activeProfile.FirstPersonMouseSensitivityY = value;
                OnPropertyChanged();
                Commit();
            }
        }

        public bool FirstPersonInvertMouseX
        {
            get => _activeProfile.FirstPersonInvertMouseX;
            set
            {
                if (_activeProfile.FirstPersonInvertMouseX == value) return;
                _activeProfile.FirstPersonInvertMouseX = value;
                OnPropertyChanged();
                Commit();
            }
        }

        public bool FirstPersonInvertMouseY
        {
            get => _activeProfile.FirstPersonInvertMouseY;
            set
            {
                if (_activeProfile.FirstPersonInvertMouseY == value) return;
                _activeProfile.FirstPersonInvertMouseY = value;
                OnPropertyChanged();
                Commit();
            }
        }

        public bool SeparateFirstPersonGamepadCamera
        {
            get => _activeProfile.GamepadSettings.SeparateFirstPersonCamera;
            set
            {
                var pad = _activeProfile.GamepadSettings;
                if (pad.SeparateFirstPersonCamera == value) return;
                if (value)
                {
                    pad.FirstPersonCameraSensitivityX = pad.CameraSensitivityX;
                    pad.FirstPersonCameraSensitivityY = pad.CameraSensitivityY;
                    pad.FirstPersonInvertCameraX = pad.InvertCameraX;
                    pad.FirstPersonInvertCameraY = pad.InvertCameraY;
                }
                pad.SeparateFirstPersonCamera = value;
                NotifyProfilePropertiesChanged();
                Commit();
            }
        }

        public float FirstPersonGamepadCameraSensitivity
        {
            get => _activeProfile.GamepadSettings.FirstPersonCameraSensitivityX;
            set
            {
                var pad = _activeProfile.GamepadSettings;
                if (Math.Abs(pad.FirstPersonCameraSensitivityX - value) <= 0.01f) return;
                pad.FirstPersonCameraSensitivityX = value;
                pad.FirstPersonCameraSensitivityY = value;
                OnPropertyChanged();
                Commit();
            }
        }

        public bool FirstPersonInvertGamepadCameraX
        {
            get => _activeProfile.GamepadSettings.FirstPersonInvertCameraX;
            set
            {
                if (_activeProfile.GamepadSettings.FirstPersonInvertCameraX == value) return;
                _activeProfile.GamepadSettings.FirstPersonInvertCameraX = value;
                OnPropertyChanged();
                Commit();
            }
        }

        public bool FirstPersonInvertGamepadCameraY
        {
            get => _activeProfile.GamepadSettings.FirstPersonInvertCameraY;
            set
            {
                if (_activeProfile.GamepadSettings.FirstPersonInvertCameraY == value) return;
                _activeProfile.GamepadSettings.FirstPersonInvertCameraY = value;
                OnPropertyChanged();
                Commit();
            }
        }

        public ICommand LoadCompactPresetCommand { get; }
        public ICommand LoadFullNumpadPresetCommand { get; }
        public ICommand LoadGamepadPresetCommand { get; }
        public ICommand ResetDefaultsCommand { get; }
        public ICommand SaveProfileCommand { get; }

        public ControlsInputViewModel(string? customProfilePath = null, InputProfileHub? hub = null)
        {
            _profilePath = customProfilePath ?? GetDefaultProfilePath();
            _hub = hub ?? InputProfileHub.Default;

            if (File.Exists(_profilePath))
            {
                try
                {
                    _activeProfile = InputProfile.LoadOrCreate(_profilePath);
                    _activePresetName = _activeProfile.Name;
                }
                catch (Exception ex)
                {
                    GordianLog.Warn("INPUT", $"Failed to load input profile from '{_profilePath}': {ex.Message}");
                    _activeProfile = InputProfile.CreateCompact();
                }
            }
            else
            {
                _activeProfile = InputProfile.CreateCompact();
            }

            LoadCompactPresetCommand = new RelayCommand(LoadCompactPreset);
            LoadFullNumpadPresetCommand = new RelayCommand(LoadFullNumpadPreset);
            LoadGamepadPresetCommand = new RelayCommand(LoadGamepadPreset);
            ResetDefaultsCommand = new RelayCommand(ResetToDefaults);
            SaveProfileCommand = new RelayCommand(SaveProfile);

            PopulateBindingsTable();

            // Every character uses this profile (#314): the sessions copy it from the hub and follow its changes.
            _hub.SetProfile(_activeProfile, this);
            _hub.Changed += OnHubChanged;
        }

        /// <summary>
        /// The character whose telemetry the page shows (the console's selected one). The profile is not given to it:
        /// every session already runs on a copy of the shared profile (#314).
        /// </summary>
        public void SetSession(CharacterSession? session)
        {
            _currentSession = session;
        }

        public void LoadCompactPreset()
        {
            // Only replaces keyboard/mouse bindings; current gamepad bindings and settings
            // (default or user-customized) are left exactly as they are.
            _activeProfile.ReplaceKeyboardBindings(InputProfile.CreateCompact());
            ActivePresetName = _activeProfile.Name;
            NotifyProfilePropertiesChanged();
            PopulateBindingsTable();
            Commit();
            StatusMessage = "Loaded standard FFXI Compact (WASD) keyboard preset.";
        }

        public void LoadFullNumpadPreset()
        {
            // Only replaces keyboard/mouse bindings; current gamepad bindings and settings
            // (default or user-customized) are left exactly as they are.
            _activeProfile.ReplaceKeyboardBindings(InputProfile.CreateFullNumpad());
            ActivePresetName = _activeProfile.Name;
            NotifyProfilePropertiesChanged();
            PopulateBindingsTable();
            Commit();
            StatusMessage = "Loaded standard FFXI Full (Numpad) keyboard preset.";
        }

        public void LoadGamepadPreset()
        {
            // Only resets gamepad button bindings and gamepad settings to defaults; the active
            // keyboard layout (Compact/Full/Custom) is left completely untouched.
            _activeProfile.ApplyGamepadDefaults();
            NotifyProfilePropertiesChanged();
            PopulateBindingsTable();
            Commit();
            StatusMessage = "Reset gamepad button mappings to defaults. Keyboard bindings were not changed.";
        }

        public void ResetToDefaults()
        {
            LoadCompactPreset();
        }

        public void SaveProfile()
        {
            try
            {
                _activeProfile.SaveToFile(_profilePath);
                StatusMessage = $"Input profile saved to: {_profilePath}";
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
                GamepadStatusText = !GamepadEnabled ? "Disabled (Polling Off)" : "No Gamepad Detected";
                GamepadSticksText = "L: (0.00, 0.00) | R: (0.00, 0.00)";
                GamepadTriggersText = "LT: 0% | RT: 0%";
                GamepadButtonsText = "None";
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

            // Gamepad Telemetry
            if (!GamepadEnabled)
            {
                GamepadStatusText = "Disabled (Polling Off)";
                GamepadSticksText = "L: (0.00, 0.00) | R: (0.00, 0.00)";
                GamepadTriggersText = "LT: 0% | RT: 0%";
                GamepadButtonsText = "None";
            }
            else
            {
                var pad = input.CurrentGamepad;
                if (pad.IsConnected)
                {
                    string info = !string.IsNullOrWhiteSpace(pad.DeviceName) ? pad.DeviceName : "Connected";
                    GamepadStatusText = $"Connected [{info}]";
                    GamepadSticksText = $"L: ({pad.LeftThumb.X:+0.00;-0.00;0.00}, {pad.LeftThumb.Y:+0.00;-0.00;0.00}) | R: ({pad.RightThumb.X:+0.00;-0.00;0.00}, {pad.RightThumb.Y:+0.00;-0.00;0.00})";
                    GamepadTriggersText = $"LT: {pad.LeftTrigger * 100f:F0}% | RT: {pad.RightTrigger * 100f:F0}%";
                    GamepadButtonsText = pad.Buttons != GamepadButton.None ? pad.Buttons.ToString() : "None";
                }
                else
                {
                    GamepadStatusText = "No Gamepad Detected";
                    GamepadSticksText = "L: (0.00, 0.00) | R: (0.00, 0.00)";
                    GamepadTriggersText = "LT: 0% | RT: 0%";
                    GamepadButtonsText = "None";
                }
            }
        }

        private void NotifyProfilePropertiesChanged()
        {
            OnPropertyChanged(nameof(MouseSensitivityX));
            OnPropertyChanged(nameof(MouseSensitivityY));
            OnPropertyChanged(nameof(InvertMouseX));
            OnPropertyChanged(nameof(InvertMouseY));
            OnPropertyChanged(nameof(TurnSpeed));
            OnPropertyChanged(nameof(GamepadEnabled));
            OnPropertyChanged(nameof(AlwaysEnableGamepad));
            OnPropertyChanged(nameof(LeftStickDeadzone));
            OnPropertyChanged(nameof(RightStickDeadzone));
            OnPropertyChanged(nameof(GamepadCameraSensitivity));
            OnPropertyChanged(nameof(InvertGamepadCameraX));
            OnPropertyChanged(nameof(InvertGamepadCameraY));
            OnPropertyChanged(nameof(GamepadRumbleEnabled));
            OnPropertyChanged(nameof(IsCameraRelativeLocomotion));
            OnPropertyChanged(nameof(SeparateFirstPersonMouse));
            OnPropertyChanged(nameof(FirstPersonMouseSensitivityX));
            OnPropertyChanged(nameof(FirstPersonMouseSensitivityY));
            OnPropertyChanged(nameof(FirstPersonInvertMouseX));
            OnPropertyChanged(nameof(FirstPersonInvertMouseY));
            OnPropertyChanged(nameof(SeparateFirstPersonGamepadCamera));
            OnPropertyChanged(nameof(FirstPersonGamepadCameraSensitivity));
            OnPropertyChanged(nameof(FirstPersonInvertGamepadCameraX));
            OnPropertyChanged(nameof(FirstPersonInvertGamepadCameraY));
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
            AddBindingRow(InputAction.ToggleCameraMode, "Camera", "Change View (Third / First Person / Free Camera)");

            // Targeting & Menus
            AddBindingRow(InputAction.Confirm, "Interaction", "Confirm / Action");
            AddBindingRow(InputAction.Cancel, "Interaction", "Cancel / Clear Target");
            AddBindingRow(InputAction.TargetNearest, "Targeting", "Cycle Target Right (skips yourself)");
            AddBindingRow(InputAction.TargetPrevious, "Targeting", "Cycle Target Left (skips yourself)");
            AddBindingRow(InputAction.TargetCursorRight, "Targeting", "Target Cursor Right (via yourself)");
            AddBindingRow(InputAction.TargetCursorLeft, "Targeting", "Target Cursor Left (via yourself)");
            AddBindingRow(InputAction.ToggleLockOn, "Targeting", "Toggle Lock-On");
            AddBindingRow(InputAction.LockOnOrHeal, "Targeting", "Lock-On (target) / Heal (no target)");
            AddBindingRow(InputAction.TargetSelf, "Targeting", "Target Self");
            AddBindingRow(InputAction.TargetParty1, "Targeting", "Target Party Member 2");
            AddBindingRow(InputAction.TargetParty2, "Targeting", "Target Party Member 3");
            AddBindingRow(InputAction.TargetParty3, "Targeting", "Target Party Member 4");
            AddBindingRow(InputAction.TargetParty4, "Targeting", "Target Party Member 5");
            AddBindingRow(InputAction.TargetParty5, "Targeting", "Target Party Member 6");
            AddBindingRow(InputAction.OpenMenu, "Interface", "Open Main Menu");
            AddBindingRow(InputAction.OpenChat, "Interface", "Focus Chat Prompt");
            AddBindingRow(InputAction.CycleLogWindow, "Interface", "Select Log Window");
            AddBindingRow(InputAction.CycleCharacter, "Interface", "Next Character (multi-box, like Ctrl+Tab)");
            AddBindingRow(InputAction.Logout, "Interface", "Log Out (/logout)");
            AddBindingRow(InputAction.MacroPaletteCtrl, "Hotbar", "Macro Palette Ctrl (hold)");
            AddBindingRow(InputAction.MacroPaletteAlt, "Hotbar", "Macro Palette Alt (hold)");

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
