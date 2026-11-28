using System;
using System.Numerics;
using Gordian.Core.World;
using Gordian.Engine.Graphics;
using Gordian.Engine.Input;
using Gordian.Engine.Sessions;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Input;

public class CameraConfigMotionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ConfigAxis_ReversesOnlySelectedGamepadAxis(bool firstPerson, bool xAxis)
    {
        var profile = InputProfile.CreateCompact();
        var settings = new StockUiSettings();
        var key = Key(firstPerson, xAxis);
        settings.SetValue(key, 0);
        CharacterSession.ApplyCameraSettings(profile, settings);
        var normal = Motion(profile, firstPerson);
        settings.SetValue(key, 1);
        CharacterSession.ApplyCameraSettings(profile, settings);
        var inverted = Motion(profile, firstPerson);
        Assert.NotEqual(0f, normal.X);
        Assert.NotEqual(0f, normal.Y);
        Assert.Equal(xAxis ? -normal.X : normal.X, inverted.X, 4);
        Assert.Equal(xAxis ? normal.Y : -normal.Y, inverted.Y, 4);
    }

    // Direction oracle: owner's explicit retail controller comparison for #648,
    // not an assumption that Normal means the same thing in both views.
    [Theory]
    [InlineData(false, false, "Left")]
    [InlineData(false, false, "Right")]
    [InlineData(false, false, "Up")]
    [InlineData(false, false, "Down")]
    [InlineData(false, true, "Left")]
    [InlineData(false, true, "Right")]
    [InlineData(false, true, "Up")]
    [InlineData(false, true, "Down")]
    [InlineData(true, false, "Left")]
    [InlineData(true, false, "Right")]
    [InlineData(true, false, "Up")]
    [InlineData(true, false, "Down")]
    [InlineData(true, true, "Left")]
    [InlineData(true, true, "Right")]
    [InlineData(true, true, "Up")]
    [InlineData(true, true, "Down")]
    public void RetailNormalAndInverted_PhysicalStickDirection_ReachesRenderedView(
        bool first, bool inverted, string direction)
    {
        var profile = InputProfile.CreateCompact();
        var settings = new StockUiSettings();
        foreach (bool view in new[] { false, true })
            foreach (bool axis in new[] { false, true }) settings.SetValue(Key(view, axis), 0);
        bool x = direction is "Left" or "Right";
        settings.SetValue(Key(first, x), inverted ? 1 : 0);
        CharacterSession.ApplyCameraSettings(profile, settings);
        var input = new InputState();
        var world = new WorldState();
        var player = new LocalPlayerState { ServerId = 1001 };
        world.UpsertEntity(new PlayerEntity(1001, 0) { IsSpawned = true });
        var controller = new PlayerLocomotionController(input, profile, world, player)
        {
            CameraMode = first ? CameraMode.FirstPerson : CameraMode.ThirdPersonOrbital,
            CameraYaw = 180f, CameraPitch = 10f,
        };
        // SDL RIGHTX: left negative/right positive; SDL RIGHTY: up negative/down positive.
        // SdlGamepadDriver negates Y on entry to GamepadState (XInput already has up positive).
        var thumb = direction switch
        {
            "Left" => new Vector2(-0.5f, 0), "Right" => new Vector2(0.5f, 0),
            "Up" => new Vector2(0, 0.5f), _ => new Vector2(0, -0.5f),
        };
        var viewCamera = new ViewportCamera { Mode = controller.CameraMode };
        viewCamera.Update(Vector3.Zero, 10f, 180f, 6f, 16f / 9f);
        var forward = viewCamera.Forward;
        var right = viewCamera.Right;
        input.SetGamepadState(new GamepadState(true, GamepadButton.None, Vector2.Zero, thumb, 0, 0));
        controller.Update(TimeSpan.FromMilliseconds(16));
        // Same public angles / display-space camera used by the viewport, not merely a toggle check.
        viewCamera.Update(Vector3.Zero, controller.CameraPitch, controller.CameraYaw, 6f, 16f / 9f);
        float motion = x ? Vector3.Dot(viewCamera.Forward - forward, right)
                         : viewCamera.Forward.Y - forward.Y;
        int physicalSign = direction is "Right" or "Up" ? 1 : -1;
        int expected = physicalSign * (first ? 1 : -1) * (inverted ? -1 : 1);
        Assert.Equal(expected, Math.Sign(motion));
        Assert.Equal(x ? 10f : 180f, x ? controller.CameraPitch : controller.CameraYaw);
    }

    private static StockUiSettingKey Key(bool first, bool x) => (first, x) switch
    {
        (true, true) => StockUiSettingKey.FirstPersonInvertX,
        (true, false) => StockUiSettingKey.FirstPersonInvertY,
        (false, true) => StockUiSettingKey.ThirdPersonInvertX,
        _ => StockUiSettingKey.ThirdPersonInvertY,
    };

    private static Vector2 Motion(InputProfile profile, bool firstPerson)
    {
        var input = new InputState();
        var world = new WorldState();
        var player = new LocalPlayerState { ServerId = 1001 };
        world.UpsertEntity(new PlayerEntity(1001, 0) { IsSpawned = true });
        var controller = new PlayerLocomotionController(input, profile, world, player)
        {
            CameraMode = firstPerson ? CameraMode.FirstPerson : CameraMode.ThirdPersonOrbital,
            CameraYaw = 180f,
            CameraPitch = 10f,
        };
        input.SetGamepadState(new GamepadState(true, GamepadButton.None, Vector2.Zero, new Vector2(0.5f, 0.5f), 0, 0));
        controller.Update(TimeSpan.FromMilliseconds(16));
        return new Vector2(controller.CameraYaw - 180f, controller.CameraPitch - 10f);
    }
}
