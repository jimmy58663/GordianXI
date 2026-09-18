// src/Gordian.App/Services/SdlGamepadDriver.cs
// Cross-platform gamepad driver powered by Silk.NET.SDL (SDL2 GameController subsystem).
// Natively supports XInput (Xbox), DirectInput, PS5 DualSense, PS4 DualShock, Switch Pro, and generic HID.

using System;
using System.Numerics;
using Gordian.Core.Diagnostics;
using Gordian.Core.Input;
using Silk.NET.SDL;

namespace Gordian.App.Services
{
    /// <summary>
    /// Cross-platform controller driver utilizing SDL's GameController subsystem via Silk.NET.
    /// Unifies Xbox (USB/Bluetooth), PlayStation (DualSense/DualShock), Switch Pro, and generic HID
    /// gamepads into standard GordianXI GamepadState across Windows, Linux, and macOS.
    /// </summary>
    public sealed unsafe class SdlGamepadDriver : IGamepadDriver
    {
        private readonly Sdl? _sdl;
        private bool _isAvailable;
        private bool _rumbleEnabled = true;

        private readonly GameController*[] _controllers = new GameController*[4];
        private readonly int[] _slotToDeviceIndex = new int[4] { -1, -1, -1, -1 };
        private readonly string[] _controllerNames = new string[4] { string.Empty, string.Empty, string.Empty, string.Empty };
        private uint _packetCounter;

        public bool IsAvailable => _isAvailable;

        public bool RumbleEnabled
        {
            get => _rumbleEnabled;
            set
            {
                if (_rumbleEnabled != value)
                {
                    _rumbleEnabled = value;
                    if (!_rumbleEnabled && _sdl != null)
                    {
                        // Instantly silence vibration on all open controllers
                        for (int i = 0; i < 4; i++)
                        {
                            if (_controllers[i] != null)
                            {
                                try
                                {
                                    _sdl.GameControllerRumble(_controllers[i], 0, 0, 0);
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
        }

        public SdlGamepadDriver()
        {
            try
            {
                _sdl = Sdl.GetApi();
                if (_sdl != null)
                {
                    // Allow background joystick events so controller inputs work when window is unfocused
                    _sdl.SetHint(Sdl.HintJoystickAllowBackgroundEvents, "1");

                    // Initialize Joystick, GameController, and Haptic subsystems
                    uint flags = Sdl.InitJoystick | Sdl.InitGamecontroller | Sdl.InitHaptic;
                    if (_sdl.Init(flags) == 0)
                    {
                        _isAvailable = true;
                        GordianLog.Info("INPUT", "Silk.NET.SDL cross-platform GameController driver initialized successfully.");
                    }
                    else
                    {
                        string err = _sdl.GetErrorS();
                        GordianLog.Warn("INPUT", $"SDL_Init failed for GameController subsystem: {err}");
                    }
                }
            }
            catch (Exception ex)
            {
                GordianLog.Warn("INPUT", $"Failed to initialize Silk.NET.SDL Gamepad driver: {ex.Message}");
                _isAvailable = false;
            }
        }

        public GamepadState Poll(int controllerIndex = 0)
        {
            if (_sdl == null || !_isAvailable || controllerIndex < 0 || controllerIndex >= 4)
            {
                return GamepadState.Disconnected;
            }

            // Pump SDL message queue to process controller events and hotplugging
            _sdl.PumpEvents();

            EnsureControllerSlot(controllerIndex);

            var gc = _controllers[controllerIndex];
            if (gc == null)
            {
                return GamepadState.Disconnected;
            }

            // Check if controller is still attached
            if (_sdl.GameControllerGetAttached(gc) != SdlBool.True)
            {
                _sdl.GameControllerClose(gc);
                _controllers[controllerIndex] = null;
                _slotToDeviceIndex[controllerIndex] = -1;
                return GamepadState.Disconnected;
            }

            // Map Buttons
            GamepadButton buttons = GamepadButton.None;

            if (_sdl.GameControllerGetButton(gc, GameControllerButton.A) != 0) buttons |= GamepadButton.A;
            if (_sdl.GameControllerGetButton(gc, GameControllerButton.B) != 0) buttons |= GamepadButton.B;
            if (_sdl.GameControllerGetButton(gc, GameControllerButton.X) != 0) buttons |= GamepadButton.X;
            if (_sdl.GameControllerGetButton(gc, GameControllerButton.Y) != 0) buttons |= GamepadButton.Y;

            if (_sdl.GameControllerGetButton(gc, GameControllerButton.Back) != 0) buttons |= GamepadButton.Back;
            if (_sdl.GameControllerGetButton(gc, GameControllerButton.Guide) != 0) buttons |= GamepadButton.Guide;
            if (_sdl.GameControllerGetButton(gc, GameControllerButton.Start) != 0) buttons |= GamepadButton.Start;

            if (_sdl.GameControllerGetButton(gc, GameControllerButton.Leftstick) != 0) buttons |= GamepadButton.LeftThumb;
            if (_sdl.GameControllerGetButton(gc, GameControllerButton.Rightstick) != 0) buttons |= GamepadButton.RightThumb;

            if (_sdl.GameControllerGetButton(gc, GameControllerButton.Leftshoulder) != 0) buttons |= GamepadButton.LeftShoulder;
            if (_sdl.GameControllerGetButton(gc, GameControllerButton.Rightshoulder) != 0) buttons |= GamepadButton.RightShoulder;

            if (_sdl.GameControllerGetButton(gc, GameControllerButton.DpadUp) != 0) buttons |= GamepadButton.DPadUp;
            if (_sdl.GameControllerGetButton(gc, GameControllerButton.DpadDown) != 0) buttons |= GamepadButton.DPadDown;
            if (_sdl.GameControllerGetButton(gc, GameControllerButton.DpadLeft) != 0) buttons |= GamepadButton.DPadLeft;
            if (_sdl.GameControllerGetButton(gc, GameControllerButton.DpadRight) != 0) buttons |= GamepadButton.DPadRight;

            // Map Analog Sticks (-1.0 to +1.0)
            // Note: SDL Y-axis is negative for UP and positive for DOWN; we invert it to match 3D standard (+Y is forward/up)
            short rawLX = _sdl.GameControllerGetAxis(gc, GameControllerAxis.Leftx);
            short rawLY = _sdl.GameControllerGetAxis(gc, GameControllerAxis.Lefty);
            short rawRX = _sdl.GameControllerGetAxis(gc, GameControllerAxis.Rightx);
            short rawRY = _sdl.GameControllerGetAxis(gc, GameControllerAxis.Righty);

            float lx = NormalizeAxis(rawLX);
            float ly = -NormalizeAxis(rawLY);
            float rx = NormalizeAxis(rawRX);
            float ry = -NormalizeAxis(rawRY);

            // Map Analog Triggers (0.0 to 1.0)
            short rawLT = _sdl.GameControllerGetAxis(gc, GameControllerAxis.Triggerleft);
            short rawRT = _sdl.GameControllerGetAxis(gc, GameControllerAxis.Triggerright);

            float lt = Math.Clamp(rawLT / 32767.0f, 0f, 1f);
            float rt = Math.Clamp(rawRT / 32767.0f, 0f, 1f);

            if (lt >= 0.15f) buttons |= GamepadButton.LeftTrigger;
            if (rt >= 0.15f) buttons |= GamepadButton.RightTrigger;

            _packetCounter++;

            string devName = !string.IsNullOrEmpty(_controllerNames[controllerIndex])
                ? _controllerNames[controllerIndex]
                : "Silk.NET.SDL GameController";

            return new GamepadState(
                isConnected: true,
                buttons: buttons,
                leftThumb: new Vector2(lx, ly),
                rightThumb: new Vector2(rx, ry),
                leftTrigger: lt,
                rightTrigger: rt,
                packetNumber: _packetCounter,
                deviceName: $"Silk.NET.SDL: {devName}");
        }

        public void SetVibration(int controllerIndex, float leftMotor, float rightMotor)
        {
            if (_sdl == null || !_isAvailable || controllerIndex < 0 || controllerIndex >= 4) return;

            var gc = _controllers[controllerIndex];
            if (gc == null) return;

            if (!_rumbleEnabled)
            {
                leftMotor = 0f;
                rightMotor = 0f;
            }

            ushort low = (ushort)Math.Clamp((int)(leftMotor * 65535f), 0, 65535);
            ushort high = (ushort)Math.Clamp((int)(rightMotor * 65535f), 0, 65535);

            // 1000ms duration ensures sustained rumble while refreshed each tick
            _sdl.GameControllerRumble(gc, low, high, 1000);
        }

        private void EnsureControllerSlot(int slot)
        {
            if (_sdl == null) return;

            if (_controllers[slot] != null)
            {
                if (_sdl.GameControllerGetAttached(_controllers[slot]) == SdlBool.True)
                {
                    return;
                }
                // Detached
                _sdl.GameControllerClose(_controllers[slot]);
                _controllers[slot] = null;
                _slotToDeviceIndex[slot] = -1;
                _controllerNames[slot] = string.Empty;
            }

            // Find an unassigned game controller
            int numJoysticks = _sdl.NumJoysticks();
            int candidateIndex = 0;
            int assignedCount = 0;

            for (int i = 0; i < numJoysticks; i++)
            {
                if (_sdl.IsGameController(i) == SdlBool.True)
                {
                    if (assignedCount == slot)
                    {
                        candidateIndex = i;
                        var opened = _sdl.GameControllerOpen(candidateIndex);
                        if (opened != null)
                        {
                            _controllers[slot] = opened;
                            _slotToDeviceIndex[slot] = candidateIndex;
                            string name = _sdl.GameControllerNameS(opened);
                            _controllerNames[slot] = !string.IsNullOrWhiteSpace(name) ? name : "GameController";
                            GordianLog.Info("INPUT", $"SDL Gamepad Slot {slot} connected: '{name}' (Device Index {candidateIndex}).");
                        }
                        return;
                    }
                    assignedCount++;
                }
            }
        }

        private static float NormalizeAxis(short value)
        {
            return value < 0 ? value / 32768.0f : value / 32767.0f;
        }

        public void Dispose()
        {
            if (_sdl != null)
            {
                for (int i = 0; i < 4; i++)
                {
                    if (_controllers[i] != null)
                    {
                        try
                        {
                            _sdl.GameControllerRumble(_controllers[i], 0, 0, 0);
                            _sdl.GameControllerClose(_controllers[i]);
                        }
                        catch { }
                        _controllers[i] = null;
                    }
                }

                _sdl.QuitSubSystem(Sdl.InitJoystick | Sdl.InitGamecontroller | Sdl.InitHaptic);
                _sdl.Dispose();
            }
        }
    }
}
