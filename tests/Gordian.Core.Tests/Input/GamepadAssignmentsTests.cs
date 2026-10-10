// tests/Gordian.Core.Tests/Input/GamepadAssignmentsTests.cs
using System;
using System.Linq;
using Gordian.Core.Input;
using Xunit;

namespace Gordian.Core.Tests.Input
{
    /// <summary>The remappable gamepad binding model (#21) and the #326 default shortcuts.</summary>
    public class GamepadAssignmentsTests
    {
        private static GamepadFunction Function(InputAction first) => GamepadAssignments.Functions.Single(f => f.Actions[0] == first);

        [Fact]
        public void Defaults_ShowEachFunctionsButtons()
        {
            var profile = InputProfile.CreateCompact();
            Assert.Equal("A", GamepadAssignments.Describe(profile, Function(InputAction.Confirm)));
            Assert.Equal("Start, X", GamepadAssignments.Describe(profile, Function(InputAction.OpenMenu)));
            Assert.Equal("L3", GamepadAssignments.Describe(profile, Function(InputAction.ToggleLockOn)));
            Assert.Equal("RB", GamepadAssignments.Describe(profile, Function(InputAction.ToggleAutorun)));
            Assert.Equal("LB", GamepadAssignments.Describe(profile, Function(InputAction.CycleCharacter)));
            Assert.Equal("D-Pad Left", GamepadAssignments.Describe(profile, Function(InputAction.MenuLeft)));
            Assert.Equal("-", GamepadAssignments.Describe(profile, Function(InputAction.ToggleWalkRun)));
        }

        [Fact]
        public void Assign_SwapsWithTheFunctionThatHadTheButton()
        {
            var profile = InputProfile.CreateCompact();
            GamepadAssignments.Assign(profile, Function(InputAction.ToggleLockOn), GamepadButton.Y);

            Assert.True(profile.TryGetAction(new InputChord(GamepadButton.Y), out var y));
            Assert.Equal(InputAction.ToggleLockOn, y);
            // Select Window had Y: it takes Lock On's old button, L3.
            Assert.True(profile.TryGetAction(new InputChord(GamepadButton.LeftThumb), out var l3));
            Assert.Equal(InputAction.CycleLogWindow, l3);
            // Keyboard bindings are untouched.
            Assert.Contains(new InputChord(GordianKey.T), profile.GetChords(InputAction.ToggleLockOn));
            Assert.Contains(new InputChord(GordianKey.NumPadAdd), profile.GetChords(InputAction.CycleLogWindow));
        }

        [Fact]
        public void Assign_ToAFunctionWithoutAButton_TakesItFromTheOther()
        {
            var profile = InputProfile.CreateCompact();
            GamepadAssignments.Assign(profile, Function(InputAction.ToggleWalkRun), GamepadButton.RightShoulder);

            Assert.True(profile.TryGetAction(new InputChord(GamepadButton.RightShoulder), out var rb));
            Assert.Equal(InputAction.ToggleWalkRun, rb);
            Assert.Empty(GamepadAssignments.ButtonsOf(profile, Function(InputAction.ToggleAutorun)));
        }

        [Fact]
        public void Assign_ADirection_DrivesTheMenuAndTheTargetCursor()
        {
            var profile = InputProfile.CreateCompact();
            GamepadAssignments.Assign(profile, Function(InputAction.MenuLeft), GamepadButton.X);

            var x = new InputChord(GamepadButton.X);
            Assert.True(profile.TryGetAction(x, menuContext: true, out var inMenu));
            Assert.Equal(InputAction.MenuLeft, inMenu);
            Assert.True(profile.TryGetAction(x, menuContext: false, out var outside));
            Assert.Equal(InputAction.TargetCursorLeft, outside);
            // Open Main Menu had X (a game function): it gets D-Pad Left; Start stays.
            Assert.Contains(new InputChord(GamepadButton.DPadLeft), profile.GetChords(InputAction.OpenMenu));
            Assert.Contains(new InputChord(GamepadButton.Start), profile.GetChords(InputAction.OpenMenu));
            Assert.DoesNotContain(x, profile.GetChords(InputAction.OpenMenu));
        }

        [Fact]
        public void Assign_ReplacesEveryButtonOfTheFunction()
        {
            var profile = InputProfile.CreateCompact();
            GamepadAssignments.Assign(profile, Function(InputAction.OpenMenu), GamepadButton.Back);

            Assert.Equal(new[] { GamepadButton.Back }, GamepadAssignments.ButtonsOf(profile, Function(InputAction.OpenMenu)));
            // Open Chat had Back: it gets Open Main Menu's first button (Start, the lower bit).
            Assert.Equal(new[] { GamepadButton.Start }, GamepadAssignments.ButtonsOf(profile, Function(InputAction.OpenChat)));
            Assert.Throws<ArgumentException>(() => GamepadAssignments.Assign(profile, Function(InputAction.Confirm), GamepadButton.A | GamepadButton.B));
        }

        [Fact]
        public void ResetDefaults_RestoresTheButtons_KeepsKeyboardAndSettings()
        {
            var profile = InputProfile.CreateFullNumpad();
            profile.GamepadSettings.InvertCameraY = true;
            GamepadAssignments.Assign(profile, Function(InputAction.Confirm), GamepadButton.B);
            GamepadAssignments.ResetDefaults(profile);

            Assert.Equal("A", GamepadAssignments.Describe(profile, Function(InputAction.Confirm)));
            Assert.Equal("B", GamepadAssignments.Describe(profile, Function(InputAction.Cancel)));
            Assert.True(profile.GamepadSettings.InvertCameraY);
            Assert.Contains(new InputChord(GordianKey.NumPad5), profile.GetChords(InputAction.Confirm));
        }

        [Fact]
        public void FirstButton_IsTheLowestBit()
        {
            Assert.Equal(GamepadButton.None, GamepadAssignments.FirstButton(GamepadButton.None));
            Assert.Equal(GamepadButton.A, GamepadAssignments.FirstButton(GamepadButton.A | GamepadButton.RightTrigger));
            Assert.Equal(GamepadButton.DPadUp, GamepadAssignments.FirstButton(GamepadButton.DPadUp | GamepadButton.Y));
        }

        [Fact]
        public void SavedProfileFromBefore326_GetsTheNewShortcuts_OnlyWhereTheOldDefaultsStand()
        {
            var old = InputProfile.CreateCompact();
            old.Bindings.Remove(InputAction.CycleCharacter);
            old.Unbind(InputAction.ToggleLockOn, new InputChord(GamepadButton.LeftThumb));
            old.Unbind(InputAction.ToggleAutorun, new InputChord(GamepadButton.RightShoulder));
            old.Bind(InputAction.ToggleWalkRun, new InputChord(GamepadButton.LeftThumb));
            old.Bind(InputAction.MacroAlt1, new InputChord(GamepadButton.RightShoulder));
            old.Bind(InputAction.MacroCtrl1, new InputChord(GamepadButton.X)); // the user moved Ctrl+1 off LB
            string json = old.SaveToJson();

            var loaded = InputProfile.FromJson(json);
            Assert.True(loaded.TryGetAction(new InputChord(GamepadButton.LeftThumb), out var l3));
            Assert.Equal(InputAction.ToggleLockOn, l3);
            Assert.True(loaded.TryGetAction(new InputChord(GamepadButton.RightShoulder), out var rb));
            Assert.Equal(InputAction.ToggleAutorun, rb);
            Assert.Empty(loaded.GetChords(InputAction.CycleCharacter)); // LB was not on Ctrl+1: left alone
            Assert.DoesNotContain(new InputChord(GamepadButton.LeftThumb), loaded.GetChords(InputAction.ToggleWalkRun));

            // Once migrated (CycleCharacter saved, even empty), a later load changes nothing.
            loaded.Bind(InputAction.ToggleWalkRun, new InputChord(GamepadButton.LeftThumb));
            loaded.Unbind(InputAction.ToggleLockOn, new InputChord(GamepadButton.LeftThumb));
            var again = InputProfile.FromJson(loaded.SaveToJson());
            Assert.True(again.TryGetAction(new InputChord(GamepadButton.LeftThumb), out var stays));
            Assert.Equal(InputAction.ToggleWalkRun, stays);
        }
    }
}
