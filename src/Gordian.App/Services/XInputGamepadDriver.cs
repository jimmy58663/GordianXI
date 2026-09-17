// src/Gordian.App/Services/XInputGamepadDriver.cs
// High-performance clean-room XInput gamepad driver for Windows in GordianXI.
// Uses dynamic P/Invoke to xinput1_4.dll / xinput9_1_0.dll with zero managed allocations.

using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Gordian.Core.Diagnostics;
using Gordian.Core.Input;

namespace Gordian.App.Services
{
    /// <summary>
    /// Native Windows XInput gamepad driver supporting up to 4 controllers,
    /// dynamic hot-plugging, dual-motor vibration feedback, and zero heap allocation in the poll loop.
    /// </summary>
    public sealed class XInputGamepadDriver : IGamepadDriver
    {
        private const uint ERROR_SUCCESS = 0;
        private const uint ERROR_DEVICE_NOT_CONNECTED = 1167;

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_VIBRATION
        {
            public ushort wLeftMotorSpeed;
            public ushort wRightMotorSpeed;
        }

        private delegate uint XInputGetStateDelegate(uint dwUserIndex, out XINPUT_STATE pState);
        private delegate uint XInputSetStateDelegate(uint dwUserIndex, ref XINPUT_VIBRATION pVibration);

        private readonly IntPtr _hModule = IntPtr.Zero;
        private readonly XInputGetStateDelegate? _getState;
        private readonly XInputSetStateDelegate? _setState;
        private readonly long[] _lastDisconnectedCheck = new long[4];
        private static readonly long ReconnectCheckInterval = System.Diagnostics.Stopwatch.Frequency; // ~1 second

        public bool IsAvailable => _getState != null;

        public XInputGamepadDriver()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            string[] candidateDlls = ["xinput1_4.dll", "xinput9_1_0.dll", "xinput1_3.dll"];
            foreach (var dll in candidateDlls)
            {
                if (NativeLibrary.TryLoad(dll, out _hModule))
                {
                    if (NativeLibrary.TryGetExport(_hModule, "XInputGetState", out var pGetState))
                    {
                        _getState = Marshal.GetDelegateForFunctionPointer<XInputGetStateDelegate>(pGetState);
                    }
                    if (NativeLibrary.TryGetExport(_hModule, "XInputSetState", out var pSetState))
                    {
                        _setState = Marshal.GetDelegateForFunctionPointer<XInputSetStateDelegate>(pSetState);
                    }

                    if (_getState != null)
                    {
                        GordianLog.Info("INPUT", $"XInput controller driver successfully initialized using '{dll}'.");
                        break;
                    }
                }
            }
        }

        public GamepadState Poll(int controllerIndex = 0)
        {
            if (_getState == null || controllerIndex < 0 || controllerIndex >= 4)
            {
                return GamepadState.Disconnected;
            }

            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_lastDisconnectedCheck[controllerIndex] != 0 &&
                (now - _lastDisconnectedCheck[controllerIndex]) < ReconnectCheckInterval)
            {
                // Throttle querying disconnected slots to prevent excessive OS kernel calls
                return GamepadState.Disconnected;
            }

            uint result = _getState((uint)controllerIndex, out var rawState);
            if (result == ERROR_DEVICE_NOT_CONNECTED)
            {
                _lastDisconnectedCheck[controllerIndex] = now;
                return GamepadState.Disconnected;
            }

            if (result != ERROR_SUCCESS)
            {
                return GamepadState.Disconnected;
            }

            _lastDisconnectedCheck[controllerIndex] = 0; // Device is active

            // Map buttons
            GamepadButton buttons = GamepadButton.None;
            ushort wb = rawState.Gamepad.wButtons;

            if ((wb & 0x0001) != 0) buttons |= GamepadButton.DPadUp;
            if ((wb & 0x0002) != 0) buttons |= GamepadButton.DPadDown;
            if ((wb & 0x0004) != 0) buttons |= GamepadButton.DPadLeft;
            if ((wb & 0x0008) != 0) buttons |= GamepadButton.DPadRight;
            if ((wb & 0x0010) != 0) buttons |= GamepadButton.Start;
            if ((wb & 0x0020) != 0) buttons |= GamepadButton.Back;
            if ((wb & 0x0040) != 0) buttons |= GamepadButton.LeftThumb;
            if ((wb & 0x0080) != 0) buttons |= GamepadButton.RightThumb;
            if ((wb & 0x0100) != 0) buttons |= GamepadButton.LeftShoulder;
            if ((wb & 0x0200) != 0) buttons |= GamepadButton.RightShoulder;
            if ((wb & 0x1000) != 0) buttons |= GamepadButton.A;
            if ((wb & 0x2000) != 0) buttons |= GamepadButton.B;
            if ((wb & 0x4000) != 0) buttons |= GamepadButton.X;
            if ((wb & 0x8000) != 0) buttons |= GamepadButton.Y;

            // Map analog sticks (-1.0 to +1.0)
            float lx = rawState.Gamepad.sThumbLX < 0 ? rawState.Gamepad.sThumbLX / 32768.0f : rawState.Gamepad.sThumbLX / 32767.0f;
            float ly = rawState.Gamepad.sThumbLY < 0 ? rawState.Gamepad.sThumbLY / 32768.0f : rawState.Gamepad.sThumbLY / 32767.0f;
            float rx = rawState.Gamepad.sThumbRX < 0 ? rawState.Gamepad.sThumbRX / 32768.0f : rawState.Gamepad.sThumbRX / 32767.0f;
            float ry = rawState.Gamepad.sThumbRY < 0 ? rawState.Gamepad.sThumbRY / 32768.0f : rawState.Gamepad.sThumbRY / 32767.0f;

            // Map analog triggers (0.0 to 1.0)
            float lt = rawState.Gamepad.bLeftTrigger / 255.0f;
            float rt = rawState.Gamepad.bRightTrigger / 255.0f;

            return new GamepadState(
                isConnected: true,
                buttons: buttons,
                leftThumb: new Vector2(lx, ly),
                rightThumb: new Vector2(rx, ry),
                leftTrigger: lt,
                rightTrigger: rt,
                packetNumber: rawState.dwPacketNumber);
        }

        private bool _rumbleEnabled = true;

        public bool RumbleEnabled
        {
            get => _rumbleEnabled;
            set
            {
                if (_rumbleEnabled != value)
                {
                    _rumbleEnabled = value;
                    if (!_rumbleEnabled && _setState != null)
                    {
                        // Instantly silence any active vibration across all controller slots
                        for (int i = 0; i < 4; i++)
                        {
                            try { SetVibration(i, 0f, 0f); } catch { }
                        }
                    }
                }
            }
        }

        public void SetVibration(int controllerIndex, float leftMotor, float rightMotor)
        {
            if (_setState == null || controllerIndex < 0 || controllerIndex >= 4) return;

            if (!_rumbleEnabled)
            {
                leftMotor = 0f;
                rightMotor = 0f;
            }

            var vib = new XINPUT_VIBRATION
            {
                wLeftMotorSpeed = (ushort)Math.Clamp((int)(leftMotor * 65535f), 0, 65535),
                wRightMotorSpeed = (ushort)Math.Clamp((int)(rightMotor * 65535f), 0, 65535)
            };

            _setState((uint)controllerIndex, ref vib);
        }

        public void Dispose()
        {
            if (_setState != null)
            {
                for (int i = 0; i < 4; i++)
                {
                    try { SetVibration(i, 0f, 0f); } catch { }
                }
            }

            if (_hModule != IntPtr.Zero)
            {
                NativeLibrary.Free(_hModule);
            }
        }
    }
}
