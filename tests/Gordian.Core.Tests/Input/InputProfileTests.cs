// tests/Gordian.Core.Tests/Input/InputProfileTests.cs
using System;
using System.IO;
using Gordian.Engine.Input;
using Xunit;

namespace Gordian.Core.Tests.Input
{
    public sealed class InputProfileTests
    {
        [Fact]
        public void CreateCompact_BindsExpectedDefaultActions()
        {
            var profile = InputProfile.CreateCompact();

            Assert.Equal("Compact (WASD)", profile.Name);

            // Movement
            Assert.True(profile.TryGetAction(new InputChord(GordianKey.W), out var actW));
            Assert.Equal(InputAction.MoveForward, actW);

            Assert.True(profile.TryGetAction(new InputChord(GordianKey.S), out var actS));
            Assert.Equal(InputAction.MoveBackward, actS);

            Assert.True(profile.TryGetAction(new InputChord(GordianKey.A), out var actA));
            Assert.Equal(InputAction.TurnLeft, actA);

            Assert.True(profile.TryGetAction(new InputChord(GordianKey.D), out var actD));
            Assert.Equal(InputAction.TurnRight, actD);

            Assert.True(profile.TryGetAction(new InputChord(GordianKey.Q), out var actQ));
            Assert.Equal(InputAction.StrafeLeft, actQ);

            Assert.True(profile.TryGetAction(new InputChord(GordianKey.E), out var actE));
            Assert.Equal(InputAction.StrafeRight, actE);

            Assert.True(profile.TryGetAction(new InputChord(GordianKey.R), out var actR));
            Assert.Equal(InputAction.ToggleAutorun, actR);

            // Camera
            Assert.True(profile.TryGetAction(new InputChord(GordianKey.I), out var actI));
            Assert.Equal(InputAction.CameraPitchUp, actI);

            Assert.True(profile.TryGetAction(new InputChord(GordianKey.K), out var actK));
            Assert.Equal(InputAction.CameraPitchDown, actK);

            Assert.True(profile.TryGetAction(new InputChord(GordianKey.J), out var actJ));
            Assert.Equal(InputAction.CameraYawLeft, actJ);

            Assert.True(profile.TryGetAction(new InputChord(GordianKey.L), out var actL));
            Assert.Equal(InputAction.CameraYawRight, actL);

            // Targeting
            Assert.True(profile.TryGetAction(new InputChord(GordianKey.Tab), out var actTab));
            Assert.Equal(InputAction.TargetNearest, actTab);

            Assert.True(profile.TryGetAction(new InputChord(GordianKey.Tab, InputModifiers.Shift), out var actShiftTab));
            Assert.Equal(InputAction.TargetPrevious, actShiftTab);

            // Macros
            Assert.True(profile.TryGetAction(new InputChord(GordianKey.D1, InputModifiers.Control), out var actCtrl1));
            Assert.Equal(InputAction.MacroCtrl1, actCtrl1);

            Assert.True(profile.TryGetAction(new InputChord(GordianKey.D2, InputModifiers.Alt), out var actAlt2));
            Assert.Equal(InputAction.MacroAlt2, actAlt2);
        }

        [Fact]
        public void CreateFullNumpad_BindsExpectedDefaultActions()
        {
            var profile = InputProfile.CreateFullNumpad();

            Assert.Equal("Full (Numpad)", profile.Name);

            // Movement on Numpad
            Assert.True(profile.TryGetAction(new InputChord(GordianKey.NumPad8), out var act8));
            Assert.Equal(InputAction.MoveForward, act8);

            Assert.True(profile.TryGetAction(new InputChord(GordianKey.NumPad2), out var act2));
            Assert.Equal(InputAction.MoveBackward, act2);

            Assert.True(profile.TryGetAction(new InputChord(GordianKey.NumPad4), out var act4));
            Assert.Equal(InputAction.TurnLeft, act4);

            Assert.True(profile.TryGetAction(new InputChord(GordianKey.NumPad6), out var act6));
            Assert.Equal(InputAction.TurnRight, act6);

            Assert.True(profile.TryGetAction(new InputChord(GordianKey.NumPad7), out var act7));
            Assert.Equal(InputAction.StrafeLeft, act7);

            Assert.True(profile.TryGetAction(new InputChord(GordianKey.NumPad9), out var act9));
            Assert.Equal(InputAction.StrafeRight, act9);

            // Confirm on Numpad Enter and Numpad 5
            Assert.True(profile.TryGetAction(new InputChord(GordianKey.NumPadEnter), out var actEnter));
            Assert.Equal(InputAction.Confirm, actEnter);

            Assert.True(profile.TryGetAction(new InputChord(GordianKey.NumPad5), out var act5));
            Assert.Equal(InputAction.Confirm, act5);

            // Cancel on Numpad 0
            Assert.True(profile.TryGetAction(new InputChord(GordianKey.NumPad0), out var act0));
            Assert.Equal(InputAction.Cancel, act0);
        }

        [Fact]
        public void RebindAndUnbind_UpdatesMappingCorrectly()
        {
            var profile = new InputProfile("Custom", "Test Profile");
            var chord = new InputChord(GordianKey.Space, InputModifiers.Control);

            profile.Bind(InputAction.Confirm, chord);
            Assert.True(profile.TryGetAction(chord, out var action));
            Assert.Equal(InputAction.Confirm, action);

            bool unbound = profile.Unbind(InputAction.Confirm, chord);
            Assert.True(unbound);
            Assert.False(profile.TryGetAction(chord, out _));
        }

        [Fact]
        public void JsonSerialization_RoundTripsAccurately()
        {
            var profile = InputProfile.CreateCompact();
            profile.InvertMouseY = true;
            profile.MouseSensitivityX = 1.75f;
            profile.WalkSpeed = 30;

            string json = profile.SaveToJson(indented: true);
            var deserialized = InputProfile.FromJson(json);

            Assert.Equal(profile.Name, deserialized.Name);
            Assert.Equal(profile.InvertMouseY, deserialized.InvertMouseY);
            Assert.Equal(profile.MouseSensitivityX, deserialized.MouseSensitivityX);
            Assert.Equal(profile.WalkSpeed, deserialized.WalkSpeed);

            Assert.True(deserialized.TryGetAction(new InputChord(GordianKey.W), out var actW));
            Assert.Equal(InputAction.MoveForward, actW);

            Assert.True(deserialized.TryGetAction(new InputChord(GordianKey.D1, InputModifiers.Control), out var actCtrl1));
            Assert.Equal(InputAction.MacroCtrl1, actCtrl1);
        }

        [Fact]
        public void CreateGamepadDefault_IncludesFullCompactKeyboardLayout()
        {
            var profile = InputProfile.CreateGamepadDefault();

            Assert.Equal("Gamepad (Standard)", profile.Name);

            // The gamepad default must never leave keyboard coverage incomplete - it must include
            // every keyboard action from the Compact preset (camera, targeting, etc.), not just a
            // partial movement/menu subset.
            Assert.True(profile.TryGetAction(new InputChord(GordianKey.I), out var actPitchUp));
            Assert.Equal(InputAction.CameraPitchUp, actPitchUp);

            Assert.True(profile.TryGetAction(new InputChord(GordianKey.J), out var actYawLeft));
            Assert.Equal(InputAction.CameraYawLeft, actYawLeft);

            Assert.True(profile.TryGetAction(new InputChord(GordianKey.PageUp), out var actZoomIn));
            Assert.Equal(InputAction.CameraZoomIn, actZoomIn);

            Assert.True(profile.TryGetAction(new InputChord(GordianKey.F1), out var actTargetSelf));
            Assert.Equal(InputAction.TargetSelf, actTargetSelf);

            Assert.True(profile.TryGetAction(new InputChord(GordianKey.F6), out var actParty5));
            Assert.Equal(InputAction.TargetParty5, actParty5);

            // The slash key opens the chat line (it starts the line with the slash); walk/run moved to keypad /.
            Assert.True(profile.TryGetAction(new InputChord(GordianKey.OemSlash), out var actChat));
            Assert.Equal(InputAction.OpenChat, actChat);
            Assert.Contains(new InputChord(GordianKey.NumPadDivide), profile.GetChords(InputAction.ToggleWalkRun));

            // Gamepad buttons are still bound as usual.
            Assert.True(profile.TryGetAction(new InputChord(GamepadButton.A), out var actConfirm));
            Assert.Equal(InputAction.Confirm, actConfirm);
        }

        [Fact]
        public void ReplaceKeyboardBindings_PreservesExistingGamepadBindingsAndSettings()
        {
            var profile = InputProfile.CreateCompact();

            // Simulate a user who customized their gamepad button mapping.
            profile.ClearAction(InputAction.Confirm);
            profile.Bind(InputAction.Confirm, new InputChord(GamepadButton.X));
            profile.GamepadSettings.LeftStickDeadzone = 0.33f;

            profile.ReplaceKeyboardBindings(InputProfile.CreateFullNumpad());

            // Keyboard layout switched to Full (Numpad)...
            Assert.Equal("Full (Numpad)", profile.Name);
            Assert.True(profile.TryGetAction(new InputChord(GordianKey.NumPad8), out var actForward));
            Assert.Equal(InputAction.MoveForward, actForward);

            // ...but the custom gamepad button binding and gamepad settings survived untouched.
            Assert.True(profile.TryGetAction(new InputChord(GamepadButton.X), out var actConfirm));
            Assert.Equal(InputAction.Confirm, actConfirm);
            Assert.Equal(0.33f, profile.GamepadSettings.LeftStickDeadzone);
        }

        [Fact]
        public void ApplyGamepadDefaults_LeavesKeyboardBindingsUntouched()
        {
            var profile = InputProfile.CreateFullNumpad();

            // Simulate a user who rebound a keyboard key away from the preset default.
            profile.ClearAction(InputAction.MoveForward);
            profile.Bind(InputAction.MoveForward, new InputChord(GordianKey.Up));

            profile.ApplyGamepadDefaults();

            // Keyboard rebind survives...
            Assert.True(profile.TryGetAction(new InputChord(GordianKey.Up), out var actForward));
            Assert.Equal(InputAction.MoveForward, actForward);
            Assert.Equal("Full (Numpad)", profile.Name);

            // ...and gamepad buttons are reset to the standard defaults.
            Assert.True(profile.TryGetAction(new InputChord(GamepadButton.A), out var actConfirm));
            Assert.Equal(InputAction.Confirm, actConfirm);
        }

        [Fact]
        public void InputChord_StringParsingAndFormatting_Succeeds()
        {
            var chord1 = new InputChord(GordianKey.W, InputModifiers.Control);
            Assert.Equal("Ctrl+W", chord1.ToString());

            var chord2 = new InputChord(GordianKey.Tab, InputModifiers.Shift | InputModifiers.Control);
            Assert.Equal("Ctrl+Shift+Tab", chord2.ToString());

            var chord3 = new InputChord(MouseButton.Right, InputModifiers.Alt);
            Assert.Equal("Alt+MouseRight", chord3.ToString());

            Assert.True(InputChord.TryParse("Ctrl+W", out var parsed1));
            Assert.Equal(chord1, parsed1);

            Assert.True(InputChord.TryParse("Ctrl+Shift+Tab", out var parsed2));
            Assert.Equal(chord2, parsed2);

            Assert.True(InputChord.TryParse("Alt+MouseRight", out var parsed3));
            Assert.Equal(chord3, parsed3);

            Assert.False(InputChord.TryParse("InvalidKey_xyz", out _));
        }

        /// <summary>
        /// #358: the gamepad and camera settings survive a restart, even when the file was written by another build that
        /// knows actions or chords this one does not (a newer branch's action names). Such a file used to fail to load,
        /// and the load then saved the default profile over it, losing every setting.
        /// </summary>
        [Fact]
        public void LoadOrCreate_KeepsSettings_WhenTheFileHasUnknownActionsOrChords()
        {
            string path = Path.Combine(Path.GetTempPath(), $"gordian_profile_{Guid.NewGuid():N}.json");
            try
            {
                var profile = InputProfile.CreateCompact();
                profile.GamepadSettings.InvertCameraX = true;
                profile.GamepadSettings.InvertCameraY = true;
                profile.GamepadSettings.SeparateFirstPersonCamera = true;
                profile.GamepadSettings.FirstPersonInvertCameraY = true;
                profile.InvertMouseY = true;
                profile.SaveToFile(path);

                // Another build's names: an action and a chord this build does not know.
                string json = File.ReadAllText(path).Replace("\"bindings\": {", "\"bindings\": {\n    \"SomeFutureAction\": [ \"Pad:FutureButton\" ],");
                json = json.Replace("\"MoveForward\": [", "\"MoveForward\": [\n      \"NotAKey\",");
                File.WriteAllText(path, json);

                var loaded = InputProfile.LoadOrCreate(path);
                Assert.True(loaded.GamepadSettings.InvertCameraX);
                Assert.True(loaded.GamepadSettings.InvertCameraY);
                Assert.True(loaded.GamepadSettings.SeparateFirstPersonCamera);
                Assert.True(loaded.GamepadSettings.FirstPersonInvertCameraY);
                Assert.True(loaded.InvertMouseY);
                Assert.Equal(new[] { new InputChord(GordianKey.W) }, loaded.GetChords(InputAction.MoveForward));

                // What this build cannot read is written back unchanged, for the build that can.
                loaded.SaveToFile(path);
                string saved = File.ReadAllText(path);
                Assert.Contains("SomeFutureAction", saved);
                Assert.Contains("Pad:FutureButton", saved);
                Assert.Contains("NotAKey", saved);
                Assert.Equal(new[] { new InputChord(GordianKey.W) }, InputProfile.LoadOrCreate(path).GetChords(InputAction.MoveForward));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>An unreadable file is set aside (".bad") rather than overwritten with the defaults, and the save is atomic.</summary>
        [Fact]
        public void LoadOrCreate_SetsAnUnreadableFileAside_InsteadOfOverwritingIt()
        {
            string path = Path.Combine(Path.GetTempPath(), $"gordian_profile_{Guid.NewGuid():N}.json");
            string bad = path + ".bad";
            try
            {
                File.WriteAllText(path, "{ \"gamepadSettings\": { \"invertCameraX\": tr");
                var loaded = InputProfile.LoadOrCreate(path);
                Assert.False(loaded.GamepadSettings.InvertCameraX);
                Assert.True(File.Exists(bad));
                Assert.StartsWith("{ \"gamepadSettings\"", File.ReadAllText(bad));

                loaded.GamepadSettings.InvertCameraX = true;
                loaded.SaveToFile(path);
                Assert.True(InputProfile.LoadOrCreate(path).GamepadSettings.InvertCameraX);
                Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + "*.tmp"));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(bad)) File.Delete(bad);
            }
        }

    }
}
