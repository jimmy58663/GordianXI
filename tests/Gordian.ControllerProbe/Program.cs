using Gordian.App.Services;
using Gordian.Engine.Input;
using SDL;
using static SDL.SDL3;

// Run only in an isolated device namespace (see docs/input/controller-followup-648.md).
// No video, global events, owner profile, network session, or rumble is used.
unsafe
{
    if (!OperatingSystem.IsLinux() || Directory.Exists("/dev/input") || Directory.Exists("/dev/dri")
        || Environment.GetEnvironmentVariable("GORDIAN_ISOLATED_CONTROLLER_PROBE") != "1")
        throw new InvalidOperationException("Requires the documented private /dev runner.");
    using var driver = new SdlGamepadDriver();
    Check(driver.IsAvailable, "SDL initialized");
    int count = 0;
    var existing = SDL_GetJoysticks(&count);
    SDL_free(existing);
    Check(count == 0, "no physical devices visible");
    var desc = new SDL_VirtualJoystickDesc
    {
        version = (uint)sizeof(SDL_VirtualJoystickDesc),
        type = (ushort)SDL_JoystickType.SDL_JOYSTICK_TYPE_GAMEPAD,
        naxes = 6, nbuttons = 15, button_mask = 0x7fff, axis_mask = 0x3f,
    };
    var id = SDL_AttachVirtualJoystick(&desc);
    var joystick = SDL_OpenJoystick(id);
    Check(joystick != null, "virtual joystick attached");
    try
    {
        // Independent numeric ABI oracle: pinned SDL3-CS left/right shoulder = 9/10,
        // d-pad up/down = 11/12. Exercise native SDL state -> actual production Poll.
        foreach (var (index, expected) in new[] {
            (9, GamepadButton.LeftShoulder), (10, GamepadButton.RightShoulder),
            (11, GamepadButton.DPadUp), (12, GamepadButton.DPadDown) })
        {
            Check(SDL_SetJoystickVirtualButton(joystick, index, true), "inject native button");
            SDL_UpdateJoysticks();
            var state = driver.Poll();
            Check(state.IsConnected && state.Buttons == expected, $"SDL {index} -> {expected}");
            var input = new InputState();
            input.SetGamepadState(state);
            var profile = InputProfile.CreateCompact();
            input.Update(profile, TimeSpan.FromMilliseconds(16));
            var action = expected switch {
                GamepadButton.LeftShoulder => InputAction.CycleCharacter,
                GamepadButton.RightShoulder => InputAction.ToggleAutorun,
                GamepadButton.DPadUp => InputAction.TargetPartyUp,
                _ => InputAction.TargetPartyDown };
            Check(input.WasActionTriggered(action), "native button -> " + action);
            Check(SDL_SetJoystickVirtualButton(joystick, index, false), "release native button");
            SDL_UpdateJoysticks();
            Check(driver.Poll().Buttons == GamepadButton.None, "release reaches Poll");
        }
        foreach (var (axis, value) in new[] { (2, (short)16000), (3, (short)-16000) })
        {
            Check(SDL_SetJoystickVirtualAxis(joystick, axis, value), "inject native axis");
            SDL_UpdateJoysticks();
            var state = driver.Poll();
            Check(axis == 2 ? state.RightThumb.X > 0 : state.RightThumb.Y > 0, "right/up normalization unchanged");
            SDL_SetJoystickVirtualAxis(joystick, axis, 0);
        }
    }
    finally
    {
        SDL_CloseJoystick(joystick);
        SDL_DetachVirtualJoystick(id);
    }
    Console.WriteLine("PASS native virtual SDL ingress; physical controller mapping remains untested.");
}

static void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    Console.WriteLine("PASS " + label);
}
