// src/Gordian.Core/Input/GamepadButton.cs
// Clean-room gamepad button definitions for GordianXI.
// Standard XInput and DirectInput physical controller layouts referenced.

using System;

namespace Gordian.Core.Input
{
    /// <summary>
    /// Bitwise flags representing physical digital buttons and threshold-triggered analog controls on a gamepad.
    /// Compatible with standard Xbox, PlayStation (Cross/Circle/Square/Triangle), and generic HID controllers.
    /// </summary>
    [Flags]
    public enum GamepadButton : uint
    {
        None = 0,

        // Directional Pad
        DPadUp = 1 << 0,
        DPadDown = 1 << 1,
        DPadLeft = 1 << 2,
        DPadRight = 1 << 3,

        // Menu / System
        Start = 1 << 4,
        Back = 1 << 5, // Select / Share / View

        // Thumbstick Clicks
        LeftThumb = 1 << 6,  // L3
        RightThumb = 1 << 7, // R3

        // Shoulders / Bumpers
        LeftShoulder = 1 << 8,  // LB / L1
        RightShoulder = 1 << 9, // RB / R1

        // Guide / Home button
        Guide = 1 << 10,

        // Primary Face Buttons (Xbox: A/B/X/Y, PlayStation: Cross/Circle/Square/Triangle, Nintendo: B/A/Y/X)
        A = 1 << 12, // South (Cross)
        B = 1 << 13, // East (Circle)
        X = 1 << 14, // West (Square)
        Y = 1 << 15, // North (Triangle)

        // Digital Triggers (fired when analog trigger exceeds threshold)
        LeftTrigger = 1 << 16,  // LT / L2
        RightTrigger = 1 << 17  // RT / R2
    }
}
