// src/Gordian.Core/Input/IGamepadDriver.cs
// Clean-room cross-platform gamepad driver contract for GordianXI.

using System;

namespace Gordian.Core.Input
{
    /// <summary>
    /// Represents a cross-platform or OS-specific hardware gamepad polling and feedback driver.
    /// </summary>
    public interface IGamepadDriver : IDisposable
    {
        /// <summary>
        /// Indicates whether this driver backend is supported and available on the current operating system.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Gets or sets whether vibration/rumble is enabled. When false, calls to <see cref="SetVibration"/>
        /// will suppress motor speeds to zero.
        /// </summary>
        bool RumbleEnabled { get; set; }

        /// <summary>
        /// Reads the current state of a connected controller (slot 0..3).
        /// Returns <see cref="GamepadState.Disconnected"/> if no controller is present in the specified slot.
        /// </summary>
        GamepadState Poll(int controllerIndex = 0);

        /// <summary>
        /// Sets the haptic rumble motor speeds for a controller (0.0f = stop, 1.0f = maximum speed).
        /// </summary>
        /// <param name="controllerIndex">Target controller slot (0..3).</param>
        /// <param name="leftMotor">Low-frequency heavy rumble motor speed (0.0 to 1.0).</param>
        /// <param name="rightMotor">High-frequency light rumble motor speed (0.0 to 1.0).</param>
        void SetVibration(int controllerIndex, float leftMotor, float rightMotor);
    }
}
