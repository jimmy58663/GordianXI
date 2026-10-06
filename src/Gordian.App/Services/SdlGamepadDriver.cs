// src/Gordian.App/Services/SdlGamepadDriver.cs
// Cross-platform gamepad driver powered by SDL3's gamepad subsystem through ppy.SDL3-CS.
// Natively supports XInput (Xbox), DirectInput, PS5 DualSense, PS4 DualShock, Switch Pro, and generic HID.

using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Gordian.Core.Diagnostics;
using Gordian.Core.Input;
using SDL;
using static SDL.SDL3;

namespace Gordian.App.Services
{
    /// <summary>
    /// Cross-platform controller driver using SDL3's gamepad subsystem via ppy.SDL3-CS
    /// (https://github.com/ppy/SDL3-CS). Unifies Xbox (USB/Bluetooth), PlayStation (DualSense/DualShock),
    /// Switch Pro, and generic HID gamepads into standard GordianXI GamepadState across Windows, Linux, and macOS.
    /// </summary>
    public sealed unsafe class SdlGamepadDriver : IGamepadDriver
    {
        private const SDL_InitFlags SubSystems =
            SDL_InitFlags.SDL_INIT_JOYSTICK | SDL_InitFlags.SDL_INIT_GAMEPAD | SDL_InitFlags.SDL_INIT_HAPTIC;

        private static readonly object ResolverLock = new();
        private static bool _resolverInstalled;

        private bool _isAvailable;
        private bool _rumbleEnabled = true;

        private readonly SDL_Gamepad*[] _controllers = new SDL_Gamepad*[4];
        private readonly SDL_JoystickID[] _slotToInstanceId = new SDL_JoystickID[4];
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
                    if (!_rumbleEnabled && _isAvailable)
                    {
                        // Instantly silence vibration on all open controllers
                        for (int i = 0; i < 4; i++)
                        {
                            if (_controllers[i] != null)
                            {
                                try
                                {
                                    SDL_RumbleGamepad(_controllers[i], 0, 0, 0);
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
                InstallNativeResolver();

                // Allow background joystick events so controller inputs work when window is unfocused
                SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");

                // Initialize Joystick, Gamepad, and Haptic subsystems (SDL3 returns true on success)
                if (SDL_Init(SubSystems))
                {
                    _isAvailable = true;
                    GordianLog.Info("INPUT", "SDL3 cross-platform gamepad driver initialized successfully.");
                }
                else
                {
                    GordianLog.Warn("INPUT", $"SDL_Init failed for the gamepad subsystem: {SDL_GetError()}");
                }
            }
            catch (Exception ex)
            {
                GordianLog.Warn("INPUT", $"Failed to initialize SDL3 gamepad driver: {ex.Message}");
                _isAvailable = false;
            }
        }

        /// <summary>
        /// Lets SDL3-CS's <c>SDL3</c> imports fall back to the copy NuGet placed under
        /// <c>runtimes/&lt;rid&gt;/native</c> beside the app. The default lookup finds that copy through the
        /// entry assembly's dependency manifest, which a foreign host (e.g. the Linux test host in CI) may not
        /// describe, although the bundled library is present. A resolver can be set once per assembly.
        /// </summary>
        private static void InstallNativeResolver()
        {
            lock (ResolverLock)
            {
                if (_resolverInstalled) return;
                NativeLibrary.SetDllImportResolver(typeof(SDL3).Assembly, ResolveSdl);
                _resolverInstalled = true;
            }
        }

        private static IntPtr ResolveSdl(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName != "SDL3") return IntPtr.Zero;

            if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out IntPtr handle))
            {
                return handle;
            }

            if (FindBundledSdlLibrary() is string bundledPath && NativeLibrary.TryLoad(bundledPath, out handle))
            {
                GordianLog.Info("INPUT", $"Loading bundled SDL3 from {bundledPath}.");
                return handle;
            }

            return IntPtr.Zero;
        }

        private static string? FindBundledSdlLibrary()
        {
            string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
            string fileName = os switch
            {
                "win" => "SDL3.dll",
                "osx" => "libSDL3.dylib",
                _ => "libSDL3.so"
            };
            string arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

            // Packages name the folder by RID ("linux-x64", "win-arm64", "osx-arm64").
            foreach (string rid in new[] { RuntimeInformation.RuntimeIdentifier, $"{os}-{arch}", os })
            {
                string candidate = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        public GamepadState Poll(int controllerIndex = 0)
        {
            if (!_isAvailable || controllerIndex < 0 || controllerIndex >= 4)
            {
                return GamepadState.Disconnected;
            }

            // Pump SDL message queue to process controller events and hotplugging
            SDL_PumpEvents();

            EnsureControllerSlot(controllerIndex);

            var gc = _controllers[controllerIndex];
            if (gc == null)
            {
                return GamepadState.Disconnected;
            }

            // Check if controller is still attached
            if (!SDL_GamepadConnected(gc))
            {
                CloseSlot(controllerIndex);
                return GamepadState.Disconnected;
            }

            // Map Buttons (SDL3 names the face buttons by position: South = Xbox A, East = B, West = X, North = Y)
            GamepadButton buttons = GamepadButton.None;

            if (SDL_GetGamepadButton(gc, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH)) buttons |= GamepadButton.A;
            if (SDL_GetGamepadButton(gc, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST)) buttons |= GamepadButton.B;
            if (SDL_GetGamepadButton(gc, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST)) buttons |= GamepadButton.X;
            if (SDL_GetGamepadButton(gc, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH)) buttons |= GamepadButton.Y;

            if (SDL_GetGamepadButton(gc, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK)) buttons |= GamepadButton.Back;
            if (SDL_GetGamepadButton(gc, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_GUIDE)) buttons |= GamepadButton.Guide;
            if (SDL_GetGamepadButton(gc, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START)) buttons |= GamepadButton.Start;

            if (SDL_GetGamepadButton(gc, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK)) buttons |= GamepadButton.LeftThumb;
            if (SDL_GetGamepadButton(gc, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK)) buttons |= GamepadButton.RightThumb;

            if (SDL_GetGamepadButton(gc, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER)) buttons |= GamepadButton.LeftShoulder;
            if (SDL_GetGamepadButton(gc, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER)) buttons |= GamepadButton.RightShoulder;

            if (SDL_GetGamepadButton(gc, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP)) buttons |= GamepadButton.DPadUp;
            if (SDL_GetGamepadButton(gc, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN)) buttons |= GamepadButton.DPadDown;
            if (SDL_GetGamepadButton(gc, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT)) buttons |= GamepadButton.DPadLeft;
            if (SDL_GetGamepadButton(gc, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT)) buttons |= GamepadButton.DPadRight;

            // Map Analog Sticks (-1.0 to +1.0)
            // Note: SDL Y-axis is negative for UP and positive for DOWN; we invert it to match 3D standard (+Y is forward/up)
            short rawLX = SDL_GetGamepadAxis(gc, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX);
            short rawLY = SDL_GetGamepadAxis(gc, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY);
            short rawRX = SDL_GetGamepadAxis(gc, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX);
            short rawRY = SDL_GetGamepadAxis(gc, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY);

            float lx = NormalizeAxis(rawLX);
            float ly = -NormalizeAxis(rawLY);
            float rx = NormalizeAxis(rawRX);
            float ry = -NormalizeAxis(rawRY);

            // Map Analog Triggers (0.0 to 1.0; SDL3 reports 0..32767 as SDL2 did)
            short rawLT = SDL_GetGamepadAxis(gc, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER);
            short rawRT = SDL_GetGamepadAxis(gc, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER);

            float lt = Math.Clamp(rawLT / 32767.0f, 0f, 1f);
            float rt = Math.Clamp(rawRT / 32767.0f, 0f, 1f);

            if (lt >= 0.15f) buttons |= GamepadButton.LeftTrigger;
            if (rt >= 0.15f) buttons |= GamepadButton.RightTrigger;

            _packetCounter++;

            string devName = !string.IsNullOrEmpty(_controllerNames[controllerIndex])
                ? _controllerNames[controllerIndex]
                : "SDL3 Gamepad";

            return new GamepadState(
                isConnected: true,
                buttons: buttons,
                leftThumb: new Vector2(lx, ly),
                rightThumb: new Vector2(rx, ry),
                leftTrigger: lt,
                rightTrigger: rt,
                packetNumber: _packetCounter,
                deviceName: $"SDL3: {devName}");
        }

        public void SetVibration(int controllerIndex, float leftMotor, float rightMotor)
        {
            if (!_isAvailable || controllerIndex < 0 || controllerIndex >= 4) return;

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
            SDL_RumbleGamepad(gc, low, high, 1000);
        }

        private void EnsureControllerSlot(int slot)
        {
            if (_controllers[slot] != null)
            {
                if (SDL_GamepadConnected(_controllers[slot]))
                {
                    return;
                }
                // Detached
                CloseSlot(slot);
            }

            // Slot N takes the Nth connected gamepad, as SDL lists them (SDL3 identifies devices by instance id)
            using var gamepads = SDL_GetGamepads();
            if (gamepads == null || slot >= gamepads.Count) return;

            SDL_JoystickID instanceId = gamepads[slot];
            var opened = SDL_OpenGamepad(instanceId);
            if (opened != null)
            {
                _controllers[slot] = opened;
                _slotToInstanceId[slot] = instanceId;
                string? name = SDL_GetGamepadName(opened);
                _controllerNames[slot] = !string.IsNullOrWhiteSpace(name) ? name : "Gamepad";
                GordianLog.Info("INPUT", $"SDL Gamepad Slot {slot} connected: '{name}' (Instance {(uint)instanceId}).");
            }
        }

        private void CloseSlot(int slot)
        {
            SDL_CloseGamepad(_controllers[slot]);
            _controllers[slot] = null;
            _slotToInstanceId[slot] = 0;
            _controllerNames[slot] = string.Empty;
        }

        private static float NormalizeAxis(short value)
        {
            return value < 0 ? value / 32768.0f : value / 32767.0f;
        }

        public void Dispose()
        {
            if (!_isAvailable) return;

            for (int i = 0; i < 4; i++)
            {
                if (_controllers[i] != null)
                {
                    try
                    {
                        SDL_RumbleGamepad(_controllers[i], 0, 0, 0);
                        SDL_CloseGamepad(_controllers[i]);
                    }
                    catch { }
                    _controllers[i] = null;
                }
            }

            SDL_QuitSubSystem(SubSystems);
            _isAvailable = false;
        }
    }
}
