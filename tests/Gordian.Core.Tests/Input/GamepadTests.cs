// tests/Gordian.Core.Tests/Input/GamepadTests.cs
using System;
using System.Numerics;
using Gordian.Core.Input;
using Xunit;

namespace Gordian.Core.Tests.Input
{
    public sealed class GamepadTests
    {
        [Theory]
        [InlineData("Pad:A", GamepadButton.A, InputModifiers.None)]
        [InlineData("Pad:B", GamepadButton.B, InputModifiers.None)]
        [InlineData("Pad:X", GamepadButton.X, InputModifiers.None)]
        [InlineData("Pad:Y", GamepadButton.Y, InputModifiers.None)]
        [InlineData("Pad:LB", GamepadButton.LeftShoulder, InputModifiers.None)]
        [InlineData("Pad:RB", GamepadButton.RightShoulder, InputModifiers.None)]
        [InlineData("Pad:LT", GamepadButton.LeftTrigger, InputModifiers.None)]
        [InlineData("Pad:RT", GamepadButton.RightTrigger, InputModifiers.None)]
        [InlineData("Pad:L3", GamepadButton.LeftThumb, InputModifiers.None)]
        [InlineData("Pad:R3", GamepadButton.RightThumb, InputModifiers.None)]
        [InlineData("Pad:DPadUp", GamepadButton.DPadUp, InputModifiers.None)]
        [InlineData("Pad:DPadDown", GamepadButton.DPadDown, InputModifiers.None)]
        [InlineData("Gamepad:Start", GamepadButton.Start, InputModifiers.None)]
        [InlineData("Pad:Select", GamepadButton.Back, InputModifiers.None)]
        [InlineData("Ctrl+Pad:A", GamepadButton.A, InputModifiers.Control)]
        [InlineData("Alt+Shift+Pad:LeftShoulder", GamepadButton.LeftShoulder, InputModifiers.Alt | InputModifiers.Shift)]
        public void InputChord_ParsesGamepadStringsAccurately(string text, GamepadButton expectedBtn, InputModifiers expectedMods)
        {
            Assert.True(InputChord.TryParse(text, out var chord));
            Assert.True(chord.IsGamepadChord);
            Assert.Equal(expectedBtn, chord.GamepadButton);
            Assert.Equal(expectedMods, chord.Modifiers);
        }

        [Fact]
        public void InputChord_GamepadToString_FormatsCleanly()
        {
            var chord1 = new InputChord(GamepadButton.A);
            Assert.Equal("Pad:A", chord1.ToString());

            var chord2 = new InputChord(GamepadButton.RightShoulder, InputModifiers.Control);
            Assert.Equal("Ctrl+Pad:RightShoulder", chord2.ToString());
        }

        [Fact]
        public void GamepadRadialDeadzone_FiltersInnerThresholdAndScalesOuterRange()
        {
            float deadzone = 0.20f;

            // Inside deadzone -> (0, 0)
            var zeroInput = new Vector2(0.10f, 0.05f);
            var filteredZero = GamepadState.ApplyRadialDeadzone(zeroInput, deadzone);
            Assert.Equal(Vector2.Zero, filteredZero);

            // Exactly at threshold -> (0, 0)
            var thresholdInput = new Vector2(0.20f, 0.0f);
            var filteredThreshold = GamepadState.ApplyRadialDeadzone(thresholdInput, deadzone);
            Assert.Equal(Vector2.Zero, filteredThreshold);

            // Full deflection (1.0) -> full magnitude (1.0)
            var maxInput = new Vector2(1.0f, 0.0f);
            var filteredMax = GamepadState.ApplyRadialDeadzone(maxInput, deadzone);
            Assert.Equal(1.0f, filteredMax.Length(), 3);
            Assert.Equal(1.0f, filteredMax.X, 3);
            Assert.Equal(0.0f, filteredMax.Y, 3);

            // Halfway beyond deadzone: (0.2 + 0.8 * 0.5) = 0.6 -> should normalize to 0.5 magnitude
            var midInput = new Vector2(0.6f, 0.0f);
            var filteredMid = GamepadState.ApplyRadialDeadzone(midInput, deadzone);
            Assert.Equal(0.5f, filteredMid.Length(), 3);
        }

        [Fact]
        public void InputProfile_CreateGamepadDefault_HasCorrectBindings()
        {
            var profile = InputProfile.CreateGamepadDefault();

            Assert.Equal("Gamepad (Standard)", profile.Name);
            Assert.NotNull(profile.GamepadSettings);

            Assert.True(profile.TryGetAction(new InputChord(GamepadButton.A), out var actA));
            Assert.Equal(InputAction.Confirm, actA);

            Assert.True(profile.TryGetAction(new InputChord(GamepadButton.B), out var actB));
            Assert.Equal(InputAction.Cancel, actB);

            Assert.True(profile.TryGetAction(new InputChord(GamepadButton.X), out var actX));
            Assert.Equal(InputAction.OpenMenu, actX);

            Assert.True(profile.TryGetAction(new InputChord(GamepadButton.Y), out var actY));
            Assert.Equal(InputAction.ToggleAutorun, actY);

            Assert.True(profile.TryGetAction(new InputChord(GamepadButton.LeftShoulder), out var actLB));
            Assert.Equal(InputAction.MacroCtrl1, actLB);

            Assert.True(profile.TryGetAction(new InputChord(GamepadButton.RightShoulder), out var actRB));
            Assert.Equal(InputAction.MacroAlt1, actRB);

            Assert.True(profile.TryGetAction(new InputChord(GamepadButton.LeftTrigger), out var actLT));
            Assert.Equal(InputAction.TargetPrevious, actLT);

            Assert.True(profile.TryGetAction(new InputChord(GamepadButton.RightTrigger), out var actRT));
            Assert.Equal(InputAction.TargetNearest, actRT);
        }

        [Fact]
        public void InputState_EvaluatesGamepadButtonsAndTriggerThresholds()
        {
            var state = new InputState();
            var profile = InputProfile.CreateGamepadDefault();

            // Set gamepad with A button pressed and RightTrigger at 80% (exceeding 15% threshold)
            var padState = new GamepadState(
                isConnected: true,
                buttons: GamepadButton.A,
                leftThumb: Vector2.Zero,
                rightThumb: Vector2.Zero,
                leftTrigger: 0.0f,
                rightTrigger: 0.80f,
                packetNumber: 1);

            state.SetGamepadState(padState);
            state.Update(profile, TimeSpan.FromMilliseconds(16));

            // Confirm (A) and TargetNearest (RightTrigger threshold) should be held and triggered
            Assert.True(state.IsActionHeld(InputAction.Confirm));
            Assert.True(state.WasActionTriggered(InputAction.Confirm));

            Assert.True(state.IsActionHeld(InputAction.TargetNearest));
            Assert.True(state.WasActionTriggered(InputAction.TargetNearest));

            // Next frame: keep holding
            state.Update(profile, TimeSpan.FromMilliseconds(16));
            Assert.True(state.IsActionHeld(InputAction.Confirm));
            Assert.False(state.WasActionTriggered(InputAction.Confirm));

            // Release
            state.SetGamepadState(new GamepadState(true, GamepadButton.None, Vector2.Zero, Vector2.Zero, 0f, 0f, 2));
            state.Update(profile, TimeSpan.FromMilliseconds(16));
            Assert.False(state.IsActionHeld(InputAction.Confirm));
            Assert.True(state.WasActionReleased(InputAction.Confirm));
        }

        [Fact]
        public void VirtualGamepadDriver_SimulatesStateAndRumble()
        {
            using var driver = new VirtualGamepadDriver();
            Assert.True(driver.IsAvailable);

            var testState = new GamepadState(
                isConnected: true,
                buttons: GamepadButton.A | GamepadButton.Start,
                leftThumb: new Vector2(0.5f, 0.5f),
                rightThumb: Vector2.Zero,
                leftTrigger: 0.2f,
                rightTrigger: 0.0f,
                packetNumber: 42);

            driver.SetState(0, testState);
            var polled = driver.Poll(0);

            Assert.True(polled.IsConnected);
            Assert.Equal(GamepadButton.A | GamepadButton.Start, polled.Buttons);
            Assert.Equal(new Vector2(0.5f, 0.5f), polled.LeftThumb);

            driver.SetVibration(0, 0.75f, 0.25f);
            var vib = driver.GetLastVibration(0);
            Assert.Equal(0.75f, vib.Left);
            Assert.Equal(0.25f, vib.Right);

            // When RumbleEnabled is turned off, motor speeds are suppressed to 0
            driver.RumbleEnabled = false;
            driver.SetVibration(0, 0.75f, 0.25f);
            var disabledVib = driver.GetLastVibration(0);
            Assert.Equal(0.0f, disabledVib.Left);
            Assert.Equal(0.0f, disabledVib.Right);
        }
    }
}
