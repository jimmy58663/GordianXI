// src/Gordian.Core/Input/GamepadSettings.cs
// Clean-room gamepad configuration parameters for GordianXI.

using System.Text.Json.Serialization;

namespace Gordian.Core.Input
{
    /// <summary>
    /// Specifies how gamepad analog stick movement directs player locomotion.
    /// </summary>
    public enum GamepadLocomotionMode
    {
        /// <summary>
        /// Camera-relative 3D movement (FFXI Type A / Modern action standard):
        /// Tilting the stick directs the character relative to the current camera viewing angle.
        /// </summary>
        CameraRelative = 0,

        /// <summary>
        /// Character-relative / Tank steering (FFXI Type B):
        /// Stick Y controls forward/backward movement, Stick X turns the character left/right.
        /// </summary>
        CharacterRelative = 1
    }

    /// <summary>
    /// The camera axis settings in effect for one view (first or third person): sensitivity and inversion per axis.
    /// </summary>
    public readonly record struct CameraAxisSettings(float SensitivityX, float SensitivityY, bool InvertX, bool InvertY);

    /// <summary>
    /// Configuration options for gamepad analog thumbsticks, triggers, camera responsiveness, and vibration.
    /// </summary>
    public sealed class GamepadSettings
    {
        [JsonPropertyName("locomotionMode")]
        public GamepadLocomotionMode LocomotionMode { get; set; } = GamepadLocomotionMode.CameraRelative;

        [JsonPropertyName("leftStickDeadzone")]
        public float LeftStickDeadzone { get; set; } = 0.15f;

        [JsonPropertyName("rightStickDeadzone")]
        public float RightStickDeadzone { get; set; } = 0.15f;

        [JsonPropertyName("triggerThreshold")]
        public float TriggerThreshold { get; set; } = 0.15f;

        [JsonPropertyName("invertCameraY")]
        public bool InvertCameraY { get; set; } = false;

        [JsonPropertyName("invertCameraX")]
        public bool InvertCameraX { get; set; } = false;

        [JsonPropertyName("cameraSensitivityX")]
        public float CameraSensitivityX { get; set; } = 1.0f;

        [JsonPropertyName("cameraSensitivityY")]
        public float CameraSensitivityY { get; set; } = 1.0f;

        [JsonPropertyName("rumbleEnabled")]
        public bool RumbleEnabled { get; set; } = true;

        [JsonPropertyName("walkTiltThreshold")]
        public float WalkTiltThreshold { get; set; } = 0.55f;

        [JsonPropertyName("gamepadEnabled")]
        public bool GamepadEnabled { get; set; } = true;

        [JsonPropertyName("alwaysEnableGamepad")]
        public bool AlwaysEnableGamepad { get; set; } = false;

        /// <summary>
        /// When on, the first-person view uses its own camera sensitivity and inversion below (#306); off (the default),
        /// first person uses the same values as third person, as before.
        /// </summary>
        [JsonPropertyName("separateFirstPersonCamera")]
        public bool SeparateFirstPersonCamera { get; set; } = false;

        [JsonPropertyName("firstPersonInvertCameraY")]
        public bool FirstPersonInvertCameraY { get; set; } = false;

        [JsonPropertyName("firstPersonInvertCameraX")]
        public bool FirstPersonInvertCameraX { get; set; } = false;

        [JsonPropertyName("firstPersonCameraSensitivityX")]
        public float FirstPersonCameraSensitivityX { get; set; } = 1.0f;

        [JsonPropertyName("firstPersonCameraSensitivityY")]
        public float FirstPersonCameraSensitivityY { get; set; } = 1.0f;

        /// <summary>The right stick's camera settings for the view: the first-person set when it is separate, else the shared one.</summary>
        public CameraAxisSettings Camera(bool firstPerson) => firstPerson && SeparateFirstPersonCamera
            ? new CameraAxisSettings(FirstPersonCameraSensitivityX, FirstPersonCameraSensitivityY, FirstPersonInvertCameraX, FirstPersonInvertCameraY)
            : new CameraAxisSettings(CameraSensitivityX, CameraSensitivityY, InvertCameraX, InvertCameraY);

        public GamepadSettings Clone()
        {
            return new GamepadSettings
            {
                LocomotionMode = LocomotionMode,
                LeftStickDeadzone = LeftStickDeadzone,
                RightStickDeadzone = RightStickDeadzone,
                TriggerThreshold = TriggerThreshold,
                InvertCameraY = InvertCameraY,
                InvertCameraX = InvertCameraX,
                CameraSensitivityX = CameraSensitivityX,
                CameraSensitivityY = CameraSensitivityY,
                RumbleEnabled = RumbleEnabled,
                WalkTiltThreshold = WalkTiltThreshold,
                GamepadEnabled = GamepadEnabled,
                AlwaysEnableGamepad = AlwaysEnableGamepad,
                SeparateFirstPersonCamera = SeparateFirstPersonCamera,
                FirstPersonInvertCameraY = FirstPersonInvertCameraY,
                FirstPersonInvertCameraX = FirstPersonInvertCameraX,
                FirstPersonCameraSensitivityX = FirstPersonCameraSensitivityX,
                FirstPersonCameraSensitivityY = FirstPersonCameraSensitivityY
            };
        }
    }
}
