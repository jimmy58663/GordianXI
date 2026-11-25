using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Gordian.Core.Network;
using Gordian.Core.Resources.Ui;
using Gordian.Core.World;
using Gordian.Engine.Graphics;
using Gordian.Engine.Input;
using Gordian.Engine.Sessions;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Input;

public class CameraConfigSessionTests
{
    [Theory]
    [InlineData(false, false, "pad")]
    [InlineData(false, true, "pad")]
    [InlineData(true, false, "pad")]
    [InlineData(true, true, "pad")]
    [InlineData(false, false, "mouse")]
    [InlineData(false, true, "mouse")]
    [InlineData(true, false, "mouse")]
    [InlineData(true, true, "mouse")]
    [InlineData(false, true, "keys")]
    [InlineData(true, true, "keys")]
    public void MenuChoice_ChangesMotion_Persists_AndSurvivesProfileReplacement(bool first, bool x, string device)
    {
        // Unique synthetic characters, disconnected network manager, no DATs or live profiles.
        string name = "CameraConfig_" + Guid.NewGuid().ToString("N");
        string path = StockUiSettings.GetSettingsPath(name);
        var hub = new InputProfileHub(InputProfile.CreateCompact());
        using var session = new CharacterSession(name, 1001, "fixture", new SessionNetworkManager("127.0.0.1", 54230), hub);
        session.World.UpsertEntity(new PlayerEntity(1001, 0) { IsSpawned = true });
        session.LocalPlayer.ServerId = 1001;
        Assert.True(StockUiConfigPages.TryGet(StockUiConfigPages.MouseCameraPage, out var definition));
        var axisRows = definition.Rows.OfType<StockUiOptionRow>().Where(r => r.Key is
            StockUiSettingKey.ThirdPersonInvertX or StockUiSettingKey.ThirdPersonInvertY or
            StockUiSettingKey.FirstPersonInvertX or StockUiSettingKey.FirstPersonInvertY).ToArray();
        var buttons = axisRows.SelectMany(r => r.Choices).Select(c => new UiMenuButton
        {
            ButtonId = (short)c.ButtonId, Width = 88, Height = 16,
        }).ToList();
        var menus = new StockUiMenuController
        {
            Settings = session.ActionService.UiSettings,
            Library = UiResourceLibrary.FromDefinitions(new[] { new UiMenuDefinition
            {
                Name = StockUiConfigPages.MouseCameraPage,
                Frame = new UiMenuFrame { Width = 366, Height = 248 },
                Buttons = buttons,
            }}),
        };
        Assert.True(menus.Open(StockUiConfigPages.MouseCameraPage));
        var key = (first, x) switch
        {
            (true, true) => StockUiSettingKey.FirstPersonInvertX,
            (true, false) => StockUiSettingKey.FirstPersonInvertY,
            (false, true) => StockUiSettingKey.ThirdPersonInvertX,
            _ => StockUiSettingKey.ThirdPersonInvertY,
        };
        var row = axisRows.Single(r => r.Key == key);
        void Choose(StockUiOptionRow option, int value)
        {
            menus.Top!.SelectedButtonId = option.ButtonFor(value);
            menus.Activate();
            Assert.True(menus.Top.IsMarked(option.ButtonFor(value)));
        }
        try
        {
            foreach (var axis in axisRows) Choose(axis, 0);
            var normal = Motion(session.Locomotion, session.InputState, first, device);
            // Absolute retail baseline for normalized right/up, not just opposite toggles.
            // Mouse has an independent, unchanged baseline; Q/E use the virtual pad X.
            Assert.Equal(device != "mouse" && first ? 1 : -1, Math.Sign(normal.X));
            Assert.Equal(device == "keys" ? 0 : 1, Math.Sign(normal.Y));
            var other = Motion(session.Locomotion, session.InputState, !first, device);
            Choose(row, 1);
            var inverted = Motion(session.Locomotion, session.InputState, first, device);
            Assert.NotEqual(0f, x ? normal.X : normal.Y);
            Assert.Equal(x ? -normal.X : normal.X, inverted.X, 4);
            Assert.Equal(x ? normal.Y : -normal.Y, inverted.Y, 4);
            Assert.Equal(other, Motion(session.Locomotion, session.InputState, !first, device));
            Assert.Equal(1, StockUiSettings.LoadOrDefault(path).GetValue(key));

            // The hub reloads a fresh profile, but character choices still win, without editing the hub.
            hub.SetProfile(InputProfile.CreateCompact());
            Assert.Equal(inverted, Motion(session.Locomotion, session.InputState, first, device));
            Assert.False(hub.Profile.GamepadSettings.InvertCameraX);
            Assert.False(hub.Profile.GamepadSettings.InvertCameraY);
            Assert.False(hub.Profile.GamepadSettings.SeparateFirstPersonCamera);

            // Read real persisted bytes into an independent settings/profile pair (not the store cache).
            var loadedProfile = hub.CreateSessionCopy();
            CharacterSession.ApplyCameraSettings(loadedProfile, StockUiSettings.LoadOrDefault(path));
            var input = new InputState();
            var controller = new PlayerLocomotionController(input, loadedProfile, session.World, session.LocalPlayer);
            Assert.Equal(inverted, Motion(controller, input, first, device));
            Choose(row, 0);
            Assert.Equal(normal, Motion(session.Locomotion, session.InputState, first, device));
            Assert.True(StockUiSettings.LoadOrDefault(path).HasValue(key));
            Assert.Equal(0, StockUiSettings.LoadOrDefault(path).GetValue(key));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SessionConstruction_LoadsPersistedNormalOverInvertedUserProfile(bool first, bool x)
    {
        string name = "CameraReload_" + Guid.NewGuid().ToString("N");
        string path = StockUiSettings.GetSettingsPath(name);
        var user = InputProfile.CreateCompact();
        user.GamepadSettings.InvertCameraX = user.GamepadSettings.InvertCameraY = true;
        user.InvertMouseX = user.InvertMouseY = true;
        var settings = new StockUiSettings();
        var key = (first, x) switch
        {
            (true, true) => StockUiSettingKey.FirstPersonInvertX,
            (true, false) => StockUiSettingKey.FirstPersonInvertY,
            (false, true) => StockUiSettingKey.ThirdPersonInvertX,
            _ => StockUiSettingKey.ThirdPersonInvertY,
        };
        settings.SetValue(key, 0);
        settings.SaveToFile(path);
        try
        {
            using var session = new CharacterSession(name, 1001, "fixture",
                new SessionNetworkManager("127.0.0.1", 54230), new InputProfileHub(user));
            var pad = session.Locomotion.Profile.GamepadSettings.Camera(first);
            var mouse = session.Locomotion.Profile.MouseCamera(first);
            Assert.False(x ? pad.InvertX : pad.InvertY);
            Assert.False(x ? mouse.InvertX : mouse.InvertY);
            Assert.True(x ? pad.InvertY : pad.InvertX);
            Assert.True(user.GamepadSettings.InvertCameraX);
            Assert.True(user.GamepadSettings.InvertCameraY);
            Assert.NotSame(settings, session.ActionService.UiSettings);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FirstPersonChoice_PreservesSensitivityBindingsAndUnselectedAxes(bool separate)
    {
        var profile = InputProfile.CreateCompact();
        profile.SeparateFirstPersonMouse = separate;
        profile.MouseSensitivityX = 2f;
        profile.MouseSensitivityY = 3f;
        profile.FirstPersonMouseSensitivityX = 4f;
        profile.FirstPersonMouseSensitivityY = 5f;
        profile.InvertMouseY = true;
        profile.FirstPersonInvertMouseY = false;
        var pad = profile.GamepadSettings;
        pad.SeparateFirstPersonCamera = separate;
        pad.CameraSensitivityX = 1.3f;
        pad.CameraSensitivityY = 1.7f;
        pad.FirstPersonCameraSensitivityX = 2.3f;
        pad.FirstPersonCameraSensitivityY = 2.7f;
        pad.InvertCameraY = true;
        pad.FirstPersonInvertCameraY = false;
        pad.RightStickDeadzone = 0.25f;
        var bindings = profile.GetChords(InputAction.CameraPanRight).ToArray();
        var settings = new StockUiSettings();
        var beforeMouse = profile.MouseCamera(true);
        var beforePad = pad.Camera(true);
        CharacterSession.ApplyCameraSettings(profile, settings);
        Assert.Equal(beforeMouse, profile.MouseCamera(true));
        Assert.Equal(beforePad, pad.Camera(true));
        settings.SetValue(StockUiSettingKey.FirstPersonInvertX, 1);
        CharacterSession.ApplyCameraSettings(profile, settings);
        Assert.Equal(beforeMouse with { InvertX = true }, profile.MouseCamera(true));
        Assert.Equal(beforePad with { InvertX = true }, pad.Camera(true));
        Assert.True(profile.MouseCamera(false).InvertY);
        Assert.True(pad.Camera(false).InvertY);
        Assert.False(profile.MouseCamera(false).InvertX);
        Assert.False(pad.Camera(false).InvertX);
        Assert.Equal(0.25f, pad.RightStickDeadzone);
        Assert.Equal(bindings, profile.GetChords(InputAction.CameraPanRight));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void KeyboardModeSwitch_PreservesCharacterCameraMotionAcrossRefreshAndReload(bool first, bool x)
    {
        string name = "CombinedControls_" + Guid.NewGuid().ToString("N");
        string otherName = "CombinedOther_" + Guid.NewGuid().ToString("N");
        string reloadName = "CombinedReload_" + Guid.NewGuid().ToString("N");
        string path = StockUiSettings.GetSettingsPath(name);
        string otherPath = StockUiSettings.GetSettingsPath(otherName);
        string reloadPath = StockUiSettings.GetSettingsPath(reloadName);
        var hub = new InputProfileHub(InputProfile.CreateCompact());
        CharacterSession Create(string character)
        {
            var session = new CharacterSession(character, 1001, "fixture",
                new SessionNetworkManager("127.0.0.1", 54230), hub);
            session.World.UpsertEntity(new PlayerEntity(1001, 0) { IsSpawned = true });
            session.LocalPlayer.ServerId = 1001;
            return session;
        }
        var axis = (first, x) switch
        {
            (true, true) => StockUiSettingKey.FirstPersonInvertX,
            (true, false) => StockUiSettingKey.FirstPersonInvertY,
            (false, true) => StockUiSettingKey.ThirdPersonInvertX,
            _ => StockUiSettingKey.ThirdPersonInvertY,
        };
        try
        {
            using var session = Create(name);
            using var other = Create(otherName);
            var normal = Motion(session.Locomotion, session.InputState, first, "pad");
            Assert.Equal(first ? 1 : -1, Math.Sign(normal.X));
            Assert.Equal(1, Math.Sign(normal.Y));
            var otherNormal = Motion(other.Locomotion, other.InputState, first, "pad");
            session.ActionService.UiSettings.SetValue(axis, 1);
            other.ActionService.UiSettings.SetValue(StockUiSettingKey.KeyboardSize, 2);
            var expected = new Vector2(x ? -normal.X : normal.X, x ? normal.Y : -normal.Y);
            Assert.NotEqual(0f, x ? normal.X : normal.Y);
            foreach (int size in new[] { 0, 1, 2, 0 })
            {
                session.ActionService.UiSettings.SetValue(StockUiSettingKey.KeyboardSize, size);
                Check(session, size, expected);
                Check(other, 2, otherNormal);
                hub.SetProfile(InputProfile.CreateCompact());
                Check(session, size, expected);
                Check(other, 2, otherNormal);
                Assert.False(hub.Profile.GamepadSettings.Camera(first).InvertX);
                Assert.False(hub.Profile.GamepadSettings.Camera(first).InvertY);

                // Bypass the settings store cache: fresh character, actual persisted bytes, fresh session.
                File.Copy(path, reloadPath, overwrite: true);
                using var reload = Create(reloadName);
                Check(reload, size, expected);
                Assert.Equal(size, StockUiSettings.LoadOrDefault(path).GetValue(StockUiSettingKey.KeyboardSize));
                Assert.Equal(1, StockUiSettings.LoadOrDefault(path).GetValue(axis));
                // Each iteration must have a new cache key as well as new bytes.
                reloadName = "CombinedReload_" + Guid.NewGuid().ToString("N");
                File.Delete(reloadPath);
                reloadPath = StockUiSettings.GetSettingsPath(reloadName);
            }
            void Check(CharacterSession candidate, int size, Vector2 motion)
            {
                candidate.Chat.Input.Cancel();
                Assert.Equal(size == 0, candidate.Chat.TryOpenForTyping(GordianKey.A,
                    InputModifiers.None, candidate.Locomotion.Profile));
                // All three features coexist on each live/restored character and keyboard mode.
                var input = candidate.Chat.Input;
                input.Cancel();
                input.Catalog = Gordian.Engine.Tests.Ui.AutoTranslateInputTests.Catalog();
                input.Open(); input.InsertText("Syn");
                input.HandleKey(GordianKey.Tab, InputModifiers.None);
                Assert.False(input.Completion.IsOpen);
                Assert.True(input.IsOpen); // selection is not submission
                Assert.Single(input.RichText.Phrases);
                Assert.Equal("Synthetic greeting!", input.Text);
                input.HandleKey(GordianKey.Backspace, InputModifiers.None);
                Assert.Empty(input.RichText.Phrases);
                input.Cancel();
                Assert.Equal(motion, Motion(candidate.Locomotion, candidate.InputState, first, "pad"));
                var pad = candidate.Locomotion.Profile.GamepadSettings.Camera(first);
                var mouse = candidate.Locomotion.Profile.MouseCamera(first);
                Assert.Equal(pad.InvertX, mouse.InvertX);
                Assert.Equal(pad.InvertY, mouse.InvertY);
            }
        }
        finally
        {
            File.Delete(path);
            File.Delete(otherPath);
            File.Delete(reloadPath);
        }
    }

    private static Vector2 Motion(PlayerLocomotionController controller, InputState input, bool first, string device)
    {
        controller.CameraMode = first ? CameraMode.FirstPerson : CameraMode.ThirdPersonOrbital;
        controller.CameraYaw = 180f;
        controller.CameraPitch = 10f;
        if (device == "pad") input.SetGamepadState(new GamepadState(true, GamepadButton.None, Vector2.Zero, new Vector2(0.5f, 0.5f), 0, 0));
        else if (device == "mouse") input.AddMouseDelta(5f, 5f);
        else input.SetKeyDown(GordianKey.E); // virtual right stick: compact keyboard pan
        controller.Update(TimeSpan.FromMilliseconds(16));
        if (device == "keys") input.SetKeyUp(GordianKey.E);
        return new Vector2(controller.CameraYaw - 180f, controller.CameraPitch - 10f);
    }
}
