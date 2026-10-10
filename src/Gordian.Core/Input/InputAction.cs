// src/Gordian.Core/Input/InputAction.cs
// Clean-room logical action definitions for GordianXI.
// Layout conventions referenced from Final Fantasy XI standard keyboard configurations.

namespace Gordian.Core.Input
{
    /// <summary>
    /// Enumerates high-level logical gameplay actions triggerable by player input.
    /// Decoupled from physical input devices (keyboard, mouse, gamepad).
    /// </summary>
    public enum InputAction : ushort
    {
        None = 0,

        // Locomotion & Movement
        MoveForward = 1,
        MoveBackward,
        TurnLeft,
        TurnRight,
        StrafeLeft,
        StrafeRight,
        ToggleAutorun,
        ToggleWalkRun,

        // Camera Manipulation
        CameraPitchUp = 20,
        CameraPitchDown,
        CameraYawLeft,
        CameraYawRight,
        CameraZoomIn,
        CameraZoomOut,
        ResetCamera,
        ToggleCameraMode,
        ToggleFreeCam,

        // Targeting & Interaction
        Confirm = 40,
        Cancel,
        TargetNearest,
        TargetPrevious,
        TargetSelf,
        ToggleLockOn,
        TargetParty1,
        TargetParty2,
        TargetParty3,
        TargetParty4,
        TargetParty5,
        TargetParty6,
        OpenMenu,
        OpenChat,

        /// <summary>
        /// Selects the next log window to scroll through (retail: numeric keypad +, gamepad Y), then none; the status
        /// icons (to cancel a buff) are to follow in the cycle.
        /// </summary>
        CycleLogWindow,

        /// <summary>
        /// Moves the target cursor to the next target to the left on screen (retail: gamepad d-pad left); with nothing
        /// targeted it targets yourself, and past the left edge it returns to you. See <see cref="TargetCycling"/>.
        /// </summary>
        TargetCursorLeft = 57,

        /// <summary>The d-pad right counterpart of <see cref="TargetCursorLeft"/>.</summary>
        TargetCursorRight = 58,

        // Menu navigation (only resolved while a stock menu is open; the same keys drive the camera otherwise)
        MenuUp = 60,
        MenuDown,
        MenuLeft,
        MenuRight,

        // Hotbar / Macro Palettes (Palette 1: Ctrl 1-10, Palette 2: Alt 1-10)
        MacroCtrl1 = 70,
        MacroCtrl2,
        MacroCtrl3,
        MacroCtrl4,
        MacroCtrl5,
        MacroCtrl6,
        MacroCtrl7,
        MacroCtrl8,
        MacroCtrl9,
        MacroCtrl10,

        MacroAlt1 = 90,
        MacroAlt2,
        MacroAlt3,
        MacroAlt4,
        MacroAlt5,
        MacroAlt6,
        MacroAlt7,
        MacroAlt8,
        MacroAlt9,
        MacroAlt10,

        /// <summary>
        /// GordianXI multi-box (not retail): shows the next character in the primary view, as Ctrl+Tab does (#151, #326).
        /// Default: the gamepad's left bumper (L1 / LB).
        /// </summary>
        CycleCharacter = 110,

        /// <summary>/logout (a toggle: again cancels). Default: the gamepad's Start (retail, the maintainer's check, 2026-10-10).</summary>
        Logout = 111,

        /// <summary>
        /// Held, the gamepad's other buttons pick from the Ctrl macro palette instead of their own functions (retail: hold
        /// the left trigger, then a button). Default: LT. The palette window and running the macros are not built yet;
        /// while it is held the pad's other buttons drive nothing.
        /// </summary>
        MacroPaletteCtrl = 112,

        /// <summary>The Alt macro palette counterpart of <see cref="MacroPaletteCtrl"/>. Default: RT.</summary>
        MacroPaletteAlt = 113
    }
}
