using System;
using System.Linq;
using Gordian.Engine.Input;
using Xunit;

namespace Gordian.Engine.Tests.Input;

public class KeyboardLayoutTests
{
    [Fact]
    public void LegacyLoad_DoesNotInventMissingOrExplicitlyUnboundKeyboardKeys()
    {
        var saved = new InputProfile("Compact custom", "frozen");
        saved.Bind(InputAction.MoveForward, new InputChord(GordianKey.B));
        saved.Bindings[InputAction.ToggleLockOn] = new();
        var loaded = InputProfile.FromJson(saved.SaveToJson());
        Assert.Empty(loaded.GetChords(InputAction.ToggleLockOn));
        Assert.Equal(new[] { new InputChord(GordianKey.B) }, loaded.Bindings.Values.SelectMany(c => c).Where(Keyboard));
    }

    [Fact]
    public void LayeredSession_FullSelectionImmediatelyResolvesKeypadAndKeepsSharedMap()
    {
        var profile = InputProfile.FromJson("""{"keyboardLayout":{"revision":1,"mode":2,"overrides":{}},"bindings":{}}""");
        var hub = new InputProfileHub(profile);
        using var session = new Gordian.Engine.Sessions.CharacterSession("Preset_" + Guid.NewGuid().ToString("N"), 1001, "fixture",
            new Gordian.Core.Network.SessionNetworkManager("127.0.0.1", 54230), hub);
        Assert.True(session.Locomotion.Profile.TryGetAction(new InputChord(GordianKey.NumPad8), out var action));
        Assert.Equal(InputAction.MoveForward, action);
        Assert.Empty(hub.Profile.GetChords(InputAction.MoveForward));
        session.ActionService.UiSettings.SetValue(Gordian.Engine.Ui.StockUiSettingKey.KeyboardSize, 2);
        Assert.True(session.Locomotion.Profile.TryGetAction(new InputChord(GordianKey.W), out action));
        Assert.Equal(InputAction.MoveForward, action);
    }

    [Theory]
    [InlineData(KeyboardMode.Full)]
    [InlineData(KeyboardMode.Compact1)]
    [InlineData(KeyboardMode.Compact2)]
    public void Overrides_ReplaceUnbindAndSuppressOnlySameContext(KeyboardMode mode)
    {
        var profile = InputProfile.FromJson("""{"keyboardLayout":{"revision":1,"mode":0,"overrides":{"MoveForward":["Up"],"ToggleCameraMode":[],"FutureAction":["FutureKey"],"Heal":["FutureChord"]}},"bindings":{}}""");
        var effective = KeyboardLayout.Resolve(profile, mode);
        Assert.Equal(new[] { new InputChord(GordianKey.Up) }, effective.GetChords(InputAction.MoveForward));
        Assert.Empty(effective.GetChords(InputAction.ToggleCameraMode));
        Assert.Empty(effective.GetChords(InputAction.Heal));
        Assert.True(effective.TryGetAction(new InputChord(GordianKey.Up), false, out var game));
        Assert.Equal(InputAction.MoveForward, game);
        Assert.True(effective.TryGetAction(new InputChord(GordianKey.Up), true, out var menu));
        Assert.Equal(InputAction.MenuUp, menu);
        Assert.Contains("FutureKey", InputProfile.FromJson(profile.SaveToJson()).Clone().SaveToJson());
    }

    [Fact]
    public void Assignment_RecordsBothSidesOfSwapAsOverrides_AndResetRestoresInheritedKeys()
    {
        var shared = InputProfile.FromJson("""{"keyboardLayout":{"revision":1,"mode":2,"overrides":{"FutureAction":["FutureKey"]}},"bindings":{}}""");
        var effective = KeyboardLayout.Resolve(shared, KeyboardMode.Compact2);
        KeyboardAssignments.Assign(effective, KeyboardAssignments.Movement[0], new InputChord(GordianKey.S));
        Assert.Equal(new[] { "S" }, effective.KeyboardLayout!.Overrides["MoveForward"]);
        Assert.Equal(new[] { "W" }, effective.KeyboardLayout.Overrides["MoveBackward"]);
        KeyboardAssignments.ResetDefaults(effective);
        Assert.False(effective.KeyboardLayout.Overrides.ContainsKey("MoveForward"));
        Assert.Contains("FutureAction", effective.KeyboardLayout.Overrides.Keys);
        Assert.Contains(new InputChord(GordianKey.W), KeyboardLayout.Resolve(effective, KeyboardMode.Compact2).GetChords(InputAction.MoveForward));
    }

    [Fact]
    public void ModeChange_DoesNotTriggerAnAlreadyHeldKeyUnderTheNewMap()
    {
        var hub = new InputProfileHub(InputProfile.CreateDefault());
        using var session = new Gordian.Engine.Sessions.CharacterSession("Held_" + Guid.NewGuid().ToString("N"), 1001, "fixture",
            new Gordian.Core.Network.SessionNetworkManager("127.0.0.1", 54230), hub);
        session.ActionService.UiSettings.SetValue(Gordian.Engine.Ui.StockUiSettingKey.KeyboardSize, 2);
        session.InputState.SetKeyDown(GordianKey.NumPad7);
        session.InputState.Update(session.Locomotion.Profile, TimeSpan.FromMilliseconds(16));
        session.ActionService.UiSettings.SetValue(Gordian.Engine.Ui.StockUiSettingKey.KeyboardSize, 0);
        session.InputState.Update(session.Locomotion.Profile, TimeSpan.FromMilliseconds(16));
        Assert.False(session.InputState.WasActionTriggered(InputAction.ToggleAutorun));
        Assert.False(session.InputState.IsActionHeld(InputAction.ToggleAutorun));
        session.InputState.SetKeyDown(GordianKey.NumPad7); // native repeat must remain blocked
        session.InputState.Update(session.Locomotion.Profile, TimeSpan.FromMilliseconds(16));
        Assert.False(session.InputState.IsActionHeld(InputAction.ToggleAutorun));
        session.InputState.SetKeyUp(GordianKey.NumPad7);
        session.InputState.SetKeyDown(GordianKey.NumPad7);
        session.InputState.Update(session.Locomotion.Profile, TimeSpan.FromMilliseconds(16));
        Assert.True(session.InputState.WasActionTriggered(InputAction.ToggleAutorun));
    }

    [Fact]
    public void FullTyping_DoesNotStealExplicitGameplayOverride()
    {
        var shared = InputProfile.CreateDefault();
        shared.KeyboardLayout!.Overrides["MoveForward"] = new() { "B" };
        var chat = new Gordian.Engine.Ui.StockUiChat { Settings = () => new Gordian.Engine.Ui.StockUiSettings() };
        Assert.False(chat.TryOpenForTyping(GordianKey.B, InputModifiers.None, KeyboardLayout.Resolve(shared, KeyboardMode.Full)));
        Assert.True(chat.TryOpenForTyping(GordianKey.C, InputModifiers.None, KeyboardLayout.Resolve(shared, KeyboardMode.Full)));
    }

    [Theory]
    [InlineData(KeyboardMode.Full)]
    [InlineData(KeyboardMode.Compact1)]
    [InlineData(KeyboardMode.Compact2)]
    public void ChatShortcut_OpensPartyDraftWithoutSending(KeyboardMode mode)
    {
        var chat = new Gordian.Engine.Ui.StockUiChat();
        var profile = KeyboardLayout.Resolve(InputProfile.CreateDefault(), mode);
        Assert.True(chat.TryOpen(GordianKey.P, InputModifiers.Control, profile, false));
        Assert.Equal(Gordian.Engine.Ui.ChatInputMode.Party, chat.Input.Mode);
        Assert.True(chat.Input.IsOpen);
    }

    [Fact]
    public void UnknownEmptyAndFutureChords_SurviveConvertRestoreReload()
    {
        var p = InputProfile.FromJson("""{"futureSetting":{"x":7},"bindings":{"FutureEmpty":[],"FutureAction":["FutureKey"],"MoveForward":["FutureMove"]}}""");
        Assert.Contains("FutureEmpty", p.SaveToJson());
        KeyboardLayout.ConvertLegacy(p, KeyboardMode.Full);
        KeyboardAssignments.ResetDefaults(p);
        var loaded = InputProfile.FromJson(p.SaveToJson()).Clone();
        Assert.Contains("futureSetting", loaded.SaveToJson());
        Assert.Contains("FutureEmpty", loaded.SaveToJson());
        Assert.Contains("FutureMove", loaded.KeyboardLayout!.Overrides["MoveForward"]);
        Assert.Empty(KeyboardLayout.Resolve(loaded, KeyboardMode.Full).GetChords(InputAction.MoveForward));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void EveryMode_ReloadsSessionLocally_PreservesOtherCharacterAndDraft(int mode)
    {
        var hub = new InputProfileHub(InputProfile.CreateDefault());
        string name = "ReloadPreset_" + Guid.NewGuid().ToString("N");
        using var a = new Gordian.Engine.Sessions.CharacterSession(name, 1001, "fixture", new Gordian.Core.Network.SessionNetworkManager("127.0.0.1", 54230), hub);
        using var b = new Gordian.Engine.Sessions.CharacterSession(name + "B", 1002, "fixture", new Gordian.Core.Network.SessionNetworkManager("127.0.0.1", 54230), hub);
        a.Chat.Input.Open(); a.Chat.Input.InsertText("unsent draft");
        a.ActionService.UiSettings.SetValue(Gordian.Engine.Ui.StockUiSettingKey.KeyboardSize, mode);
        hub.SetProfile(InputProfile.FromJson(hub.Profile.SaveToJson()));
        Assert.Equal("unsent draft", a.Chat.Input.Text);
        Assert.Contains(new InputChord(mode == 0 ? GordianKey.NumPad8 : GordianKey.W), a.Locomotion.Profile.GetChords(InputAction.MoveForward));
        Assert.Contains(new InputChord(GordianKey.NumPad8), b.Locomotion.Profile.GetChords(InputAction.MoveForward));
        var settings = Gordian.Engine.Ui.StockUiSettings.LoadOrDefault(Gordian.Engine.Ui.StockUiSettings.GetSettingsPath(name));
        Assert.Equal(mode, settings.GetValue(Gordian.Engine.Ui.StockUiSettingKey.KeyboardSize));
        Assert.Empty(hub.Profile.GetChords(InputAction.MoveForward));
    }

    [Theory]
    [InlineData(GordianKey.S, Gordian.Engine.Ui.ChatInputMode.Say)]
    [InlineData(GordianKey.L, Gordian.Engine.Ui.ChatInputMode.Linkshell)]
    [InlineData(GordianKey.T, Gordian.Engine.Ui.ChatInputMode.Tell)]
    public void OtherChatShortcuts_OpenOnlyDraft(GordianKey key, Gordian.Engine.Ui.ChatInputMode mode)
    {
        foreach (var modifier in new[] { InputModifiers.Control, InputModifiers.Alt })
        {
            var chat = new Gordian.Engine.Ui.StockUiChat();
            Assert.True(chat.TryOpen(key, modifier, KeyboardLayout.Resolve(InputProfile.CreateDefault(), KeyboardMode.Full), false));
            Assert.Equal(mode, chat.Input.Mode);
            Assert.Equal(key == GordianKey.T ? "/tell " : "", chat.Input.Text);
        }
    }

    [Fact]
    public void ExplicitUnboundChat_DoesNotReappearThroughImplicitSpaceOpening()
    {
        var p = InputProfile.CreateDefault();
        p.KeyboardLayout!.Overrides["OpenChat"] = new();
        var chat = new Gordian.Engine.Ui.StockUiChat();
        Assert.False(chat.TryOpen(GordianKey.Space, InputModifiers.None, KeyboardLayout.Resolve(p, KeyboardMode.Full), false));
        Assert.False(chat.TryOpen(GordianKey.Enter, InputModifiers.None, KeyboardLayout.Resolve(p, KeyboardMode.Full), false));
    }

    [Fact]
    public void KeyboardResolution_DoesNotReorderExistingControllerCollisions()
    {
        var p = new InputProfile { KeyboardLayout = new KeyboardLayout() };
        p.Bind(InputAction.ToggleAutorun, new InputChord(GamepadButton.A));
        p.Bind(InputAction.MoveForward, new InputChord(GamepadButton.A));
        Assert.True(KeyboardLayout.Resolve(p, KeyboardMode.Full).TryGetAction(new InputChord(GamepadButton.A), out var action));
        Assert.Equal(InputAction.ToggleAutorun, action);
    }

    private static bool Keyboard(InputChord c) => c.Key != GordianKey.None && !c.IsMouseChord && !c.IsGamepadChord;
}
