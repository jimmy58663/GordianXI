// tests/Gordian.Core.Tests/Input/InputProfileSharingTests.cs
using Gordian.Engine.Input;
using Gordian.Core.Network;
using Gordian.Engine.Sessions;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Core.Tests.Input
{
    /// <summary>Every multi-boxed character runs on its own copy of the user's input profile (#314), with per-view camera settings (#306).</summary>
    public class InputProfileSharingTests
    {
        private static CharacterSession NewSession(string name, uint id, InputProfileHub hub) =>
            new(name, id, $"user_{name}", new SessionNetworkManager("127.0.0.1", 54230), hub);

        [Fact]
        public void Clone_CopiesEverySetting_AndIsIndependent()
        {
            var profile = InputProfile.CreateFullNumpad();
            profile.InvertMouseX = true;
            profile.SeparateFirstPersonMouse = true;
            profile.FirstPersonInvertMouseY = true;
            profile.GamepadSettings.LeftStickDeadzone = 0.3f;
            profile.GamepadSettings.InvertCameraX = true;
            profile.GamepadSettings.SeparateFirstPersonCamera = true;
            profile.GamepadSettings.FirstPersonCameraSensitivityY = 2f;

            var copy = profile.Clone();
            Assert.True(copy.InvertMouseX);
            Assert.True(copy.SeparateFirstPersonMouse);
            Assert.True(copy.FirstPersonInvertMouseY);
            Assert.Equal(0.3f, copy.GamepadSettings.LeftStickDeadzone);
            Assert.True(copy.GamepadSettings.InvertCameraX);
            Assert.True(copy.GamepadSettings.SeparateFirstPersonCamera);
            Assert.Equal(2f, copy.GamepadSettings.FirstPersonCameraSensitivityY);
            Assert.Equal(profile.GetChords(InputAction.Confirm), copy.GetChords(InputAction.Confirm));

            copy.GamepadSettings.LeftStickDeadzone = 0.1f;
            copy.Unbind(InputAction.Confirm, new InputChord(GordianKey.NumPad5));
            Assert.Equal(0.3f, profile.GamepadSettings.LeftStickDeadzone);
            Assert.Contains(new InputChord(GordianKey.NumPad5), profile.GetChords(InputAction.Confirm));
        }

        [Fact]
        public void EverySession_UsesTheUsersProfile_AndFollowsItsChanges()
        {
            var user = InputProfile.CreateFullNumpad();
            user.GamepadSettings.RightStickDeadzone = 0.25f;
            var hub = new InputProfileHub(user);
            using var first = NewSession("SharingTestA", 3, hub);
            using var second = NewSession("SharingTestB", 1, hub);

            foreach (var session in new[] { first, second })
            {
                Assert.NotSame(user, session.Locomotion.Profile);
                Assert.Equal("Full (Numpad)", session.Locomotion.Profile.Name);
                Assert.Equal(0.25f, session.Locomotion.Profile.GamepadSettings.RightStickDeadzone);
            }
            Assert.NotSame(first.Locomotion.Profile, second.Locomotion.Profile);

            hub.Edit(p => GamepadAssignments.Assign(p, GamepadAssignments.Functions[0], GamepadButton.B));
            foreach (var session in new[] { first, second })
            {
                Assert.True(session.Locomotion.Profile.TryGetAction(new InputChord(GamepadButton.B), out var action));
                Assert.Equal(InputAction.Confirm, action);
            }
        }

        [Fact]
        public void SessionCopies_AreTheirOwn_SoOneCharactersConfigChoicesStayWithIt()
        {
            var hub = new InputProfileHub(InputProfile.CreateCompact());
            using var first = NewSession("SharingTestA", 3, hub);
            using var second = NewSession("SharingTestB", 1, hub);

            // What the session does with its config page's third-person row (CharacterSession.ApplyCameraSettings).
            var settings = new StockUiSettings();
            settings.SetValue(StockUiSettingKey.ThirdPersonInvertY, 1);
            CharacterSession.ApplyCameraSettings(first.Locomotion.Profile, settings);
            Assert.True(first.Locomotion.Profile.InvertMouseY);
            Assert.False(second.Locomotion.Profile.InvertMouseY);
            Assert.False(hub.Profile.InvertMouseY);
        }

        [Fact]
        public void FirstPersonConfigRows_TurnOnTheSeparateFirstPersonMouse()
        {
            var profile = InputProfile.CreateCompact();
            profile.MouseSensitivityX = 1.5f;
            var settings = new StockUiSettings();
            CharacterSession.ApplyCameraSettings(profile, settings);
            Assert.False(profile.SeparateFirstPersonMouse); // nothing chosen: nothing changes

            settings.SetValue(StockUiSettingKey.FirstPersonInvertY, 1);
            CharacterSession.ApplyCameraSettings(profile, settings);
            Assert.True(profile.SeparateFirstPersonMouse);
            Assert.True(profile.FirstPersonInvertMouseY);
            Assert.False(profile.FirstPersonInvertMouseX);
            Assert.Equal(1.5f, profile.FirstPersonMouseSensitivityX); // started from the shared set
            Assert.False(profile.MouseCamera(firstPerson: false).InvertY);
            Assert.True(profile.MouseCamera(firstPerson: true).InvertY);
        }

        [Fact]
        public void CameraSettings_FirstPersonFollowsThirdPersonUntilSeparated()
        {
            var profile = InputProfile.CreateCompact();
            profile.InvertMouseY = true;
            profile.MouseSensitivityX = 2f;
            profile.FirstPersonInvertMouseY = false;
            Assert.Equal(profile.MouseCamera(false), profile.MouseCamera(true));

            profile.SeparateFirstPersonMouse = true;
            Assert.False(profile.MouseCamera(true).InvertY);
            Assert.Equal(1f, profile.MouseCamera(true).SensitivityX);
            Assert.True(profile.MouseCamera(false).InvertY);

            var pad = profile.GamepadSettings;
            pad.InvertCameraX = true;
            Assert.True(pad.Camera(true).InvertX);
            pad.SeparateFirstPersonCamera = true;
            Assert.False(pad.Camera(true).InvertX);
            Assert.True(pad.Camera(false).InvertX);
        }
    }
}
