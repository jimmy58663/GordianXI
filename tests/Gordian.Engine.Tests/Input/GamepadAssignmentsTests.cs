// tests/Gordian.Engine.Tests/Input/GamepadAssignmentsTests.cs
using System;
using System.Linq;
using Gordian.Engine.Input;
using Xunit;

namespace Gordian.Engine.Tests.Input
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
            Assert.Equal("X", GamepadAssignments.Describe(profile, Function(InputAction.OpenMenu)));
            Assert.Equal("Start", GamepadAssignments.Describe(profile, Function(InputAction.Logout)));
            Assert.Equal("LT", GamepadAssignments.Describe(profile, Function(InputAction.MacroPaletteCtrl)));
            Assert.Equal("RT", GamepadAssignments.Describe(profile, Function(InputAction.MacroPaletteAlt)));
            Assert.Equal("L3", GamepadAssignments.Describe(profile, Function(InputAction.LockOnOrHeal)));
            Assert.Equal("R3", GamepadAssignments.Describe(profile, Function(InputAction.ToggleCameraMode)));
            Assert.Equal("D-Pad Up", GamepadAssignments.Describe(profile, Function(InputAction.MenuUp)));
            Assert.Equal("RB", GamepadAssignments.Describe(profile, Function(InputAction.ToggleAutorun)));
            Assert.Equal("LB", GamepadAssignments.Describe(profile, Function(InputAction.CycleCharacter)));
            Assert.Equal("D-Pad Left", GamepadAssignments.Describe(profile, Function(InputAction.MenuLeft)));
            Assert.Equal("-", GamepadAssignments.Describe(profile, Function(InputAction.ToggleWalkRun)));
        }

        [Fact]
        public void Assign_SwapsWithTheFunctionThatHadTheButton()
        {
            var profile = InputProfile.CreateCompact();
            GamepadAssignments.Assign(profile, Function(InputAction.LockOnOrHeal), GamepadButton.Y);

            Assert.True(profile.TryGetAction(new InputChord(GamepadButton.Y), out var y));
            Assert.Equal(InputAction.LockOnOrHeal, y);
            // Select Window had Y: it takes Lock On's old button, L3.
            Assert.True(profile.TryGetAction(new InputChord(GamepadButton.LeftThumb), out var l3));
            Assert.Equal(InputAction.CycleLogWindow, l3);
            // Keyboard bindings are untouched.
            Assert.Contains(new InputChord(GordianKey.T), profile.GetChords(InputAction.ToggleLockOn));
            Assert.DoesNotContain(new InputChord(GamepadButton.Y), profile.GetChords(InputAction.ToggleLockOn));
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
            // Open Main Menu had X (a game function): it gets D-Pad Left.
            Assert.Contains(new InputChord(GamepadButton.DPadLeft), profile.GetChords(InputAction.OpenMenu));
            Assert.DoesNotContain(x, profile.GetChords(InputAction.OpenMenu));
        }

        [Fact]
        public void Assign_ReplacesEveryButtonOfTheFunction()
        {
            var profile = InputProfile.CreateCompact();
            profile.Bind(InputAction.OpenMenu, new InputChord(GamepadButton.Guide)); // two buttons
            GamepadAssignments.Assign(profile, Function(InputAction.OpenMenu), GamepadButton.Back);

            Assert.Equal(new[] { GamepadButton.Back }, GamepadAssignments.ButtonsOf(profile, Function(InputAction.OpenMenu)));
            // Open Chat had Back: it gets Open Main Menu's first button (Guide, the lower bit).
            Assert.Equal(new[] { GamepadButton.Guide }, GamepadAssignments.ButtonsOf(profile, Function(InputAction.OpenChat)));
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

        /// <summary>A profile as an older build saved it: the round-0 gamepad layout, before #326 and the retail rounds.</summary>
        private static InputProfile Round0Profile()
        {
            var p = InputProfile.CreateCompact();
            foreach (var chords in p.Bindings.Values) chords.RemoveAll(c => c.IsGamepadChord);
            foreach (var a in new[] { InputAction.CycleCharacter, InputAction.MacroPaletteCtrl, InputAction.MacroPaletteAlt, InputAction.Logout, InputAction.LockOnOrHeal })
            {
                p.Bindings.Remove(a);
            }
            void Pad(InputAction a, GamepadButton b) => p.Bind(a, new InputChord(b));
            Pad(InputAction.Confirm, GamepadButton.A);
            Pad(InputAction.Cancel, GamepadButton.B);
            Pad(InputAction.OpenMenu, GamepadButton.X);
            Pad(InputAction.CycleLogWindow, GamepadButton.Y);
            Pad(InputAction.ToggleWalkRun, GamepadButton.LeftThumb);
            Pad(InputAction.ResetCamera, GamepadButton.RightThumb);
            Pad(InputAction.TargetPrevious, GamepadButton.LeftTrigger);
            Pad(InputAction.TargetNearest, GamepadButton.RightTrigger);
            Pad(InputAction.OpenMenu, GamepadButton.Start);
            Pad(InputAction.OpenChat, GamepadButton.Back);
            Pad(InputAction.MacroCtrl1, GamepadButton.LeftShoulder);
            Pad(InputAction.MacroAlt1, GamepadButton.RightShoulder);
            return p;
        }

        [Fact]
        public void SavedProfileFromBefore326_GetsTheNewShortcuts_OnlyWhereTheOldDefaultsStand()
        {
            var old = Round0Profile();
            old.Unbind(InputAction.MacroCtrl1, new InputChord(GamepadButton.LeftShoulder));
            old.Bind(InputAction.MacroCtrl1, new InputChord(GamepadButton.Guide)); // the user moved Ctrl+1 off LB

            var loaded = InputProfile.FromJson(old.SaveToJson());
            Assert.True(loaded.TryGetAction(new InputChord(GamepadButton.LeftThumb), out var l3));
            Assert.Equal(InputAction.LockOnOrHeal, l3);           // walk/run -> lock-on (#326) -> lock-on / heal (round 3)
            Assert.True(loaded.TryGetAction(new InputChord(GamepadButton.RightThumb), out var r3));
            Assert.Equal(InputAction.ToggleCameraMode, r3);
            Assert.True(loaded.TryGetAction(new InputChord(GamepadButton.RightShoulder), out var rb));
            Assert.Equal(InputAction.ToggleAutorun, rb);
            Assert.Empty(loaded.GetChords(InputAction.CycleCharacter)); // LB was not on Ctrl+1: left alone
            // The d-pad (which this profile had lost) gets its menu and targeting functions back.
            var left = new InputChord(GamepadButton.DPadLeft);
            Assert.True(loaded.TryGetAction(left, menuContext: true, out var inMenu));
            Assert.Equal(InputAction.MenuLeft, inMenu);
            Assert.True(loaded.TryGetAction(left, menuContext: false, out var outside));
            Assert.Equal(InputAction.TargetCursorLeft, outside);

            // Once migrated, a later load changes nothing.
            loaded.Bind(InputAction.ToggleWalkRun, new InputChord(GamepadButton.LeftThumb));
            loaded.Unbind(InputAction.LockOnOrHeal, new InputChord(GamepadButton.LeftThumb));
            var again = InputProfile.FromJson(loaded.SaveToJson());
            Assert.True(again.TryGetAction(new InputChord(GamepadButton.LeftThumb), out var stays));
            Assert.Equal(InputAction.ToggleWalkRun, stays);
        }

        [Fact]
        public void DPadMigration_RestoresOnlyContextsWhereTheDirectionDrivesNothing()
        {
            // A round-2 profile after its gamepad reset: the d-pad kept its targeting but lost the menu cursor.
            var p = InputProfile.CreateCompact();
            foreach (var a in new[] { InputAction.MenuUp, InputAction.MenuDown, InputAction.MenuLeft, InputAction.MenuRight })
            {
                p.Bindings[a].RemoveAll(c => c.IsGamepadChord);
            }
            // The user put Lock On on d-pad up; d-pad down is free in game.
            p.Unbind(InputAction.TargetParty1, new InputChord(GamepadButton.DPadUp));
            p.Bind(InputAction.ToggleLockOn, new InputChord(GamepadButton.DPadUp));
            p.Unbind(InputAction.TargetParty2, new InputChord(GamepadButton.DPadDown));

            p.EnsureDPadBindings();
            Assert.True(p.TryGetAction(new InputChord(GamepadButton.DPadUp), menuContext: true, out var up));
            Assert.Equal(InputAction.MenuUp, up);
            Assert.True(p.TryGetAction(new InputChord(GamepadButton.DPadUp), menuContext: false, out var upGame));
            Assert.Equal(InputAction.ToggleLockOn, upGame);           // the user's choice stays
            Assert.Contains(new InputChord(GamepadButton.DPadDown), p.GetChords(InputAction.TargetParty2));
            Assert.Contains(new InputChord(GamepadButton.DPadRight), p.GetChords(InputAction.MenuRight));
        }

        [Fact]
        public void ResetDefaults_KeepsTheDPadsMenuFunctions()
        {
            var profile = InputProfile.CreateCompact();
            GamepadAssignments.ResetDefaults(profile);
            Assert.True(profile.TryGetAction(new InputChord(GamepadButton.DPadDown), menuContext: true, out var down));
            Assert.Equal(InputAction.MenuDown, down);
            Assert.True(profile.TryGetAction(new InputChord(GamepadButton.DPadRight), menuContext: false, out var right));
            Assert.Equal(InputAction.TargetCursorRight, right);
        }

        [Fact]
        public void SavedProfileFromRound1_GetsTheRetailTriggersAndStart_OnlyWhereTheOldDefaultsStand()
        {
            var old = InputProfile.CreateCompact();
            foreach (var a in new[] { InputAction.MacroPaletteCtrl, InputAction.MacroPaletteAlt, InputAction.Logout }) old.Bindings.Remove(a);
            old.Bind(InputAction.TargetPrevious, new InputChord(GamepadButton.LeftTrigger));
            old.Bind(InputAction.OpenMenu, new InputChord(GamepadButton.Start));
            old.Bind(InputAction.Confirm, new InputChord(GamepadButton.RightTrigger)); // the user moved RT

            var loaded = InputProfile.FromJson(old.SaveToJson());
            Assert.True(loaded.TryGetAction(new InputChord(GamepadButton.LeftTrigger), out var lt));
            Assert.Equal(InputAction.MacroPaletteCtrl, lt);
            Assert.True(loaded.TryGetAction(new InputChord(GamepadButton.Start), out var start));
            Assert.Equal(InputAction.Logout, start);
            Assert.True(loaded.TryGetAction(new InputChord(GamepadButton.RightTrigger), out var rt));
            Assert.Equal(InputAction.Confirm, rt);                // left alone
            Assert.Empty(loaded.GetChords(InputAction.MacroPaletteAlt));
            Assert.Contains(new InputChord(GordianKey.Tab), loaded.GetChords(InputAction.TargetNearest));
        }

        [Fact]
        public void KeyboardOnlyActions_HaveNoRow_AndLoseAnyPadButtonOnLoad()
        {
            // Round 4: Cycle Target Left / Right and plain Lock On are covered by the combined rows; their pad buttons
            // would be invisible in the list, so a load takes them off (keyboard bindings stay).
            foreach (var action in new[] { InputAction.TargetPrevious, InputAction.TargetNearest, InputAction.ToggleLockOn })
            {
                Assert.False(GamepadAssignments.IsListed(action));
            }
            Assert.Contains(GamepadAssignments.Functions, f => f.Label == "Menu Up / Target Party Up");
            Assert.Contains(GamepadAssignments.Functions, f => f.Label == "Menu Down / Target Party Down");

            var saved = InputProfile.CreateCompact();
            saved.Bind(InputAction.TargetNearest, new InputChord(GamepadButton.Guide));   // as the round-1 list could
            saved.Bind(InputAction.ToggleLockOn, new InputChord(GamepadButton.Back, InputModifiers.Control)); // a modifier chord stays
            var loaded = InputProfile.FromJson(saved.SaveToJson());
            Assert.DoesNotContain(new InputChord(GamepadButton.Guide), loaded.GetChords(InputAction.TargetNearest));
            Assert.Contains(new InputChord(GordianKey.Tab), loaded.GetChords(InputAction.TargetNearest));
            Assert.Contains(new InputChord(GordianKey.T), loaded.GetChords(InputAction.ToggleLockOn));
            Assert.Contains(new InputChord(GamepadButton.Back, InputModifiers.Control), loaded.GetChords(InputAction.ToggleLockOn));
            // The list's own defaults are untouched by the clean-up.
            Assert.Equal("D-Pad Left", GamepadAssignments.Describe(loaded, Function(InputAction.MenuLeft)));
        }
    }
}
