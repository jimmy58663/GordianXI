using Gordian.Engine.Input;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Input;

public class SharedKeyboardMigrationTests
{
    [Theory]
    [InlineData(false, KeyboardMode.Compact2)]
    [InlineData(true, KeyboardMode.Full)]
    public void LegacyLoad_AutomaticallyArchivesAndPreservesBindings(bool full, KeyboardMode mode)
    {
        var legacy = full ? InputProfile.CreateFullNumpad() : InputProfile.CreateCompact();
        legacy.Bindings[InputAction.MoveForward] = [new InputChord(GordianKey.B)];
        legacy.Bindings[InputAction.ToggleCameraMode] = [];
        string original = legacy.SaveToJson();
        var loaded = InputProfile.FromJson(original);
        Assert.NotNull(loaded.KeyboardLayout);
        Assert.Equal(mode, loaded.KeyboardLayout.Mode);
        Assert.Equal(original, loaded.KeyboardLayout.LegacySnapshot);
        Assert.Equal(new[] { "B" }, loaded.KeyboardLayout.Overrides["MoveForward"]);
        Assert.Empty(loaded.KeyboardLayout.Overrides["ToggleCameraMode"]);
        var resolved = KeyboardLayout.Resolve(loaded, mode);
        Assert.Equal(legacy.GetChords(InputAction.MoveForward), resolved.GetChords(InputAction.MoveForward));
        Assert.Empty(resolved.GetChords(InputAction.ToggleCameraMode));
        foreach (var selected in System.Enum.GetValues<KeyboardMode>())
        {
            var effective = KeyboardLayout.Resolve(loaded, selected);
            foreach (var action in System.Enum.GetValues<InputAction>())
                Assert.Equal(legacy.GetChords(action).Where(KeyboardLayout.IsKeyboard), effective.GetChords(action).Where(KeyboardLayout.IsKeyboard));
        }
        Assert.Equal(loaded.SaveToJson(), InputProfile.FromJson(loaded.SaveToJson()).SaveToJson());
    }

    [Theory]
    [InlineData(KeyboardMode.Full)]
    [InlineData(KeyboardMode.Compact1)]
    [InlineData(KeyboardMode.Compact2)]
    public void MenuSelection_IsSharedAcrossCharactersAndFreshHub(KeyboardMode mode)
    {
        var hub = new InputProfileHub(InputProfile.CreateDefault());
        using var a = Session(hub);
        using var b = Session(hub);
        a.ActionService.UiSettings.SetValue(StockUiSettingKey.KeyboardSize, 2); // stale character value
        b.ActionService.UiSettings.SetValue(StockUiSettingKey.MusicVolume, 25);
        a.ActionService.Menus.SetSetting(StockUiSettingKey.KeyboardSize, (int)mode);
        Assert.Equal(mode, hub.Profile.KeyboardLayout!.Mode);
        Assert.Equal((int)mode, b.ActionService.Menus.GetSetting(StockUiSettingKey.KeyboardSize));
        var key = mode == KeyboardMode.Full ? GordianKey.NumPad8 : GordianKey.W;
        Assert.Contains(new InputChord(key), a.Locomotion.Profile.GetChords(InputAction.MoveForward));
        Assert.Contains(new InputChord(key), b.Locomotion.Profile.GetChords(InputAction.MoveForward));
        Assert.Equal(25, b.ActionService.UiSettings.GetValue(StockUiSettingKey.MusicVolume));
        Assert.Equal(100, a.ActionService.UiSettings.GetValue(StockUiSettingKey.MusicVolume));
        var fresh = new InputProfileHub(InputProfile.FromJson(hub.Profile.SaveToJson()));
        using var restarted = Session(fresh);
        Assert.Equal((int)mode, restarted.ActionService.Menus.GetSetting(StockUiSettingKey.KeyboardSize));
        Assert.Contains(new InputChord(key), restarted.Locomotion.Profile.GetChords(InputAction.MoveForward));
    }

    private static Gordian.Engine.Sessions.CharacterSession Session(InputProfileHub hub) => new(
        "SharedKeyboard_" + System.Guid.NewGuid().ToString("N"), 1001, "fixture",
        new Gordian.Core.Network.SessionNetworkManager("127.0.0.1", 54230), hub);

    [Fact]
    public void LoadOrCreate_PersistsMigrationOnceWithExactOriginalArchive()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid() + ".json");
        string original = InputProfile.CreateCompact().SaveToJson();
        try
        {
            System.IO.File.WriteAllText(path, original);
            var loaded = InputProfile.LoadOrCreate(path);
            string migrated = System.IO.File.ReadAllText(path);
            Assert.NotEqual(original, migrated);
            Assert.Equal(original, loaded.KeyboardLayout!.LegacySnapshot);
            var fresh = new InputProfileHub(InputProfile.LoadOrCreate(path));
            Assert.Equal(migrated, System.IO.File.ReadAllText(path));
            Assert.Equal(original, fresh.Profile.KeyboardLayout!.LegacySnapshot);
        }
        finally { System.IO.File.Delete(path); }
    }

    [Theory]
    [InlineData("""{"version":2,"bindings":{"FutureEmpty":[],"MoveForward":["B"]},"future":{"x":7}}""")]
    [InlineData("""{"version":1,"keyboardLayout":{"revision":9,"mode":0,"overrides":{"MoveForward":["FutureKey"]},"future":7},"bindings":{"MoveForward":["B"]}}""")]
    [InlineData("""{"version":1,"keyboardLayout":{"revision":1,"mode":99,"overrides":{}},"bindings":{"MoveForward":["B"]}}""")]
    public void FutureSchemas_AreNotMigratedOrReinterpreted(string json)
    {
        var loaded = InputProfile.FromJson(json);
        Assert.Single(loaded.Bindings);
        Assert.Equal(new[] { new InputChord(GordianKey.B) }, loaded.GetChords(InputAction.MoveForward));
        Assert.Null(loaded.KeyboardLayout?.LegacySnapshot);
        string before = loaded.SaveToJson();
        var hub = new InputProfileHub(loaded);
        using var session = Session(hub);
        session.ActionService.Menus.SetSetting(StockUiSettingKey.KeyboardSize, 2);
        Assert.Equal(before, loaded.SaveToJson());
        Assert.Contains(new InputChord(GordianKey.B), session.Locomotion.Profile.GetChords(InputAction.MoveForward));
    }

    [Fact]
    public void LegacyCompact_DefaultCharacterSettings_WWalksInsteadOfOpeningChat()
    {
        var profile = InputProfile.FromJson(InputProfile.CreateCompact().SaveToJson());
        var chat = new StockUiChat { Settings = () => new StockUiSettings() };
        Assert.False(chat.TryOpenForTyping(GordianKey.W, InputModifiers.None, profile));
        Assert.Contains(new InputChord(GordianKey.W), profile.GetChords(InputAction.MoveForward));
    }
}
