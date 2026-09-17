// src/Gordian.Core/Input/VirtualGamepadDriver.cs
// In-memory virtual gamepad driver for headless testing and synthetic controller input in GordianXI.

using System;
using System.Collections.Concurrent;

namespace Gordian.Core.Input
{
    /// <summary>
    /// Testable in-memory implementation of <see cref="IGamepadDriver"/> allowing simulated
    /// controller states, analog thumbstick movements, button pushes, and verification of rumble feedback.
    /// </summary>
    public sealed class VirtualGamepadDriver : IGamepadDriver
    {
        private readonly ConcurrentDictionary<int, GamepadState> _states = new();
        private readonly ConcurrentDictionary<int, (float Left, float Right)> _vibrations = new();

        public bool IsAvailable => true;

        public bool RumbleEnabled { get; set; } = true;

        public VirtualGamepadDriver()
        {
        }

        public void SetState(int controllerIndex, GamepadState state)
        {
            _states[controllerIndex] = state;
        }

        public (float Left, float Right) GetLastVibration(int controllerIndex = 0)
        {
            return _vibrations.TryGetValue(controllerIndex, out var v) ? v : (0f, 0f);
        }

        public GamepadState Poll(int controllerIndex = 0)
        {
            return _states.TryGetValue(controllerIndex, out var state) ? state : GamepadState.Disconnected;
        }

        public void SetVibration(int controllerIndex, float leftMotor, float rightMotor)
        {
            if (!RumbleEnabled)
            {
                leftMotor = 0f;
                rightMotor = 0f;
            }
            _vibrations[controllerIndex] = (leftMotor, rightMotor);
        }

        public void Reset()
        {
            _states.Clear();
            _vibrations.Clear();
        }

        public void Dispose()
        {
            Reset();
        }
    }
}
