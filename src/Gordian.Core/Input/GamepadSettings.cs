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
                AlwaysEnableGamepad = AlwaysEnableGamepad
            };
        }
    }
}
