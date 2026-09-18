// src/Gordian.Core/Input/GamepadState.cs
// Clean-room gamepad state structure and analog filtering for GordianXI.

using System;
using System.Numerics;

namespace Gordian.Core.Input
{
    /// <summary>
    /// Represents an immutable snapshot of a physical gamepad's buttons, analog thumbsticks,
    /// and triggers for a single poll or frame tick.
    /// </summary>
    public readonly struct GamepadState : IEquatable<GamepadState>
    {
        public bool IsConnected { get; }
        public GamepadButton Buttons { get; }
        public Vector2 LeftThumb { get; }
        public Vector2 RightThumb { get; }
        public float LeftTrigger { get; }
        public float RightTrigger { get; }
        public uint PacketNumber { get; }
        public string DeviceName { get; }

        public static GamepadState Disconnected => new GamepadState(
            isConnected: false,
            buttons: GamepadButton.None,
            leftThumb: Vector2.Zero,
            rightThumb: Vector2.Zero,
            leftTrigger: 0.0f,
            rightTrigger: 0.0f,
            packetNumber: 0,
            deviceName: string.Empty);

        public GamepadState(
            bool isConnected,
            GamepadButton buttons,
            Vector2 leftThumb,
            Vector2 rightThumb,
            float leftTrigger,
            float rightTrigger,
            uint packetNumber = 0,
            string? deviceName = null)
        {
            IsConnected = isConnected;
            Buttons = buttons;
            LeftThumb = leftThumb;
            RightThumb = rightThumb;
            LeftTrigger = Math.Clamp(leftTrigger, 0.0f, 1.0f);
            RightTrigger = Math.Clamp(rightTrigger, 0.0f, 1.0f);
            PacketNumber = packetNumber;
            DeviceName = deviceName ?? string.Empty;
        }

        public bool IsButtonDown(GamepadButton button)
        {
            return (Buttons & button) == button;
        }

        /// <summary>
        /// Applies standard radial deadzone filtering to a 2D analog thumbstick input.
        /// Inside the deadzone, input is zeroed. Outside, deflection is scaled smoothly from 0.0 to 1.0
        /// without sudden jump or angular distortion.
        /// </summary>
        public static Vector2 ApplyRadialDeadzone(Vector2 stick, float deadzone)
        {
            float magnitude = stick.Length();
            if (magnitude <= deadzone)
            {
                return Vector2.Zero;
            }

            // Scale smoothly from 0 to 1 across the active range
            float normalizedMagnitude = MathF.Min(1.0f, (magnitude - deadzone) / (1.0f - deadzone));
            return (stick / magnitude) * normalizedMagnitude;
        }

        public bool Equals(GamepadState other)
        {
            return IsConnected == other.IsConnected &&
                   Buttons == other.Buttons &&
                   LeftThumb.Equals(other.LeftThumb) &&
                   RightThumb.Equals(other.RightThumb) &&
                   Math.Abs(LeftTrigger - other.LeftTrigger) < 0.001f &&
                   Math.Abs(RightTrigger - other.RightTrigger) < 0.001f &&
                   PacketNumber == other.PacketNumber;
        }

        public override bool Equals(object? obj) => obj is GamepadState other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(IsConnected, (uint)Buttons, LeftThumb, RightThumb, LeftTrigger, RightTrigger, PacketNumber);

        public static bool operator ==(GamepadState left, GamepadState right) => left.Equals(right);
        public static bool operator !=(GamepadState left, GamepadState right) => !left.Equals(right);

        public override string ToString()
        {
            if (!IsConnected) return "Gamepad (Disconnected)";
            return $"Gamepad (Connected, Btns: {Buttons}, LStick: ({LeftThumb.X:F2}, {LeftThumb.Y:F2}), RStick: ({RightThumb.X:F2}, {RightThumb.Y:F2}), LT: {LeftTrigger:F2}, RT: {RightTrigger:F2})";
        }
    }
}
