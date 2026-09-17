// tests/Gordian.Core.Tests/Input/InputStateTests.cs
using System;
using Gordian.Core.Input;
using Xunit;

namespace Gordian.Core.Tests.Input
{
    public sealed class InputStateTests
    {
        [Fact]
        public void KeyDownAndUp_TracksHeldState()
        {
            var state = new InputState();
            Assert.False(state.IsKeyHeld(GordianKey.W));

            state.SetKeyDown(GordianKey.W);
            Assert.True(state.IsKeyHeld(GordianKey.W));

            state.SetKeyUp(GordianKey.W);
            Assert.False(state.IsKeyHeld(GordianKey.W));
        }

        [Fact]
        public void FrameUpdate_ResolvesActiveAndTriggeredActions()
        {
            var state = new InputState();
            var profile = InputProfile.CreateCompact();

            // Press W (MoveForward)
            state.SetKeyDown(GordianKey.W);

            // Frame 1: Action should be both held and triggered
            state.Update(profile, TimeSpan.FromMilliseconds(16));
            Assert.True(state.IsActionHeld(InputAction.MoveForward));
            Assert.True(state.WasActionTriggered(InputAction.MoveForward));
            Assert.False(state.WasActionReleased(InputAction.MoveForward));

            // Frame 2: Still held, but no longer triggered
            state.Update(profile, TimeSpan.FromMilliseconds(16));
            Assert.True(state.IsActionHeld(InputAction.MoveForward));
            Assert.False(state.WasActionTriggered(InputAction.MoveForward));
            Assert.False(state.WasActionReleased(InputAction.MoveForward));

            // Release W
            state.SetKeyUp(GordianKey.W);

            // Frame 3: No longer held, now released
            state.Update(profile, TimeSpan.FromMilliseconds(16));
            Assert.False(state.IsActionHeld(InputAction.MoveForward));
            Assert.False(state.WasActionTriggered(InputAction.MoveForward));
            Assert.True(state.WasActionReleased(InputAction.MoveForward));
        }

        [Fact]
        public void ModifierChord_DistinguishesBetweenPlainAndModifiedActions()
        {
            var state = new InputState();
            var profile = InputProfile.CreateCompact();

            // Tab = TargetNearest, Shift+Tab = TargetPrevious
            state.SetKeyDown(GordianKey.Tab);
            state.Update(profile, TimeSpan.FromMilliseconds(16));
            Assert.True(state.IsActionHeld(InputAction.TargetNearest));
            Assert.False(state.IsActionHeld(InputAction.TargetPrevious));

            state.SetKeyUp(GordianKey.Tab);
            state.SetKeyDown(GordianKey.LeftShift);
            state.SetKeyDown(GordianKey.Tab);
            state.Update(profile, TimeSpan.FromMilliseconds(16));

            Assert.True(state.IsActionHeld(InputAction.TargetPrevious));
        }

        [Fact]
        public void AutorunToggle_TogglesOnTriggerAndCancelsOnMoveBackward()
        {
            var state = new InputState();
            var profile = InputProfile.CreateCompact();

            Assert.False(state.AutorunActive);

            // Trigger ToggleAutorun (R)
            state.SetKeyDown(GordianKey.R);
            state.Update(profile, TimeSpan.FromMilliseconds(16));
            Assert.True(state.AutorunActive);

            // Release R
            state.SetKeyUp(GordianKey.R);
            state.Update(profile, TimeSpan.FromMilliseconds(16));
            Assert.True(state.AutorunActive); // stays on!

            // Press MoveBackward (S)
            state.SetKeyDown(GordianKey.S);
            state.Update(profile, TimeSpan.FromMilliseconds(16));
            Assert.False(state.AutorunActive); // automatically cancelled!
        }

        [Fact]
        public void WalkRunToggle_TogglesWalkingMode()
        {
            var state = new InputState();
            var profile = InputProfile.CreateCompact();

            Assert.False(state.IsWalking);

            // Trigger ToggleWalkRun (/)
            state.SetKeyDown(GordianKey.OemSlash);
            state.Update(profile, TimeSpan.FromMilliseconds(16));
            Assert.True(state.IsWalking);

            state.SetKeyUp(GordianKey.OemSlash);
            state.Update(profile, TimeSpan.FromMilliseconds(16));
            Assert.True(state.IsWalking);

            // Toggle again
            state.SetKeyDown(GordianKey.OemSlash);
            state.Update(profile, TimeSpan.FromMilliseconds(16));
            Assert.False(state.IsWalking);
        }

        [Fact]
        public void MouseDeltas_AccumulateAndResetOnConsume()
        {
            var state = new InputState();

            state.AddMouseDelta(15.5f, -8.0f);
            state.AddMouseWheel(1.0f);

            Assert.Equal(15.5f, state.MouseDeltaX);
            Assert.Equal(-8.0f, state.MouseDeltaY);
            Assert.Equal(1.0f, state.MouseWheelDelta);

            state.ConsumeMouseDeltas(out float dx, out float dy, out float wheel);

            Assert.Equal(15.5f, dx);
            Assert.Equal(-8.0f, dy);
            Assert.Equal(1.0f, wheel);

            Assert.Equal(0.0f, state.MouseDeltaX);
            Assert.Equal(0.0f, state.MouseDeltaY);
            Assert.Equal(0.0f, state.MouseWheelDelta);
        }
    }
}
