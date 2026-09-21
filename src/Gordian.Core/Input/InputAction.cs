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
        MacroAlt10
    }
}
