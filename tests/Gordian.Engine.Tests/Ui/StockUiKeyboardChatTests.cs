using Gordian.Engine.Input;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui;

public class StockUiKeyboardChatTests
{
    [Theory]
    [InlineData(0, false, true)]
    [InlineData(0, true, true)]
    [InlineData(1, false, false)]
    [InlineData(1, true, false)]
    [InlineData(2, false, false)]
    [InlineData(2, true, false)]
    public void ExplicitKeyboardSize_WinsOverDashboardPreset(int size, bool fullPreset, bool opens)
    {
        var settings = new StockUiSettings();
        settings.SetValue(StockUiSettingKey.KeyboardSize, size);
        var profile = fullPreset ? InputProfile.CreateFullNumpad() : InputProfile.CreateCompact();
        string before = profile.SaveToJson();
        var chat = new StockUiChat { Settings = () => settings };
        Assert.Equal(opens, chat.TryOpenForTyping(GordianKey.W, InputModifiers.None, profile));
        Assert.Equal(opens, chat.Input.IsOpen);
        Assert.Empty(chat.Input.Text); // Only platform TextInput may supply the initial character.
        Assert.Equal(before, profile.SaveToJson()); // No preset replacement or lost custom bindings.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UntouchedAndResetSettings_UseDisplayedFullDefault(bool full)
    {
        var settings = new StockUiSettings();
        var chat = new StockUiChat { Settings = () => settings };
        var profile = full ? InputProfile.CreateFullNumpad() : InputProfile.CreateCompact();
        Assert.True(chat.TryOpenForTyping(GordianKey.A, InputModifiers.Shift, profile));
        chat.Input.Cancel();
        settings.SetValue(StockUiSettingKey.KeyboardSize, full ? 1 : 0);
        Assert.Equal(!full, chat.TryOpenForTyping(GordianKey.A, InputModifiers.Shift, profile));
        chat.Input.Cancel();
        settings.ResetAll();
        Assert.True(chat.TryOpenForTyping(GordianKey.A, InputModifiers.Shift, profile));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SavedKeyboardSize_IsUsedAfterReload(int size)
    {
        string path = Path.Combine(Path.GetTempPath(), $"keyboard-chat-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new StockUiSettings();
            settings.SetValue(StockUiSettingKey.KeyboardSize, size);
            settings.SaveToFile(path);
            var restored = StockUiSettings.LoadOrDefault(path);
            var chat = new StockUiChat { Settings = () => restored };
            Assert.True(restored.HasValue(StockUiSettingKey.KeyboardSize));
            Assert.Equal(size == 0, chat.TryOpenForTyping(GordianKey.A, InputModifiers.None, InputProfile.CreateFullNumpad()));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(GordianKey.A, InputModifiers.Control)]
    [InlineData(GordianKey.A, InputModifiers.Alt)]
    [InlineData(GordianKey.A, InputModifiers.Super)]
    [InlineData(GordianKey.D1, InputModifiers.Control | InputModifiers.Shift)]
    [InlineData(GordianKey.F1, InputModifiers.None)]
    [InlineData(GordianKey.NumPad8, InputModifiers.None)]
    [InlineData(GordianKey.NumPadMultiply, InputModifiers.None)]
    [InlineData(GordianKey.NumPadAdd, InputModifiers.None)]
    [InlineData(GordianKey.NumPadDivide, InputModifiers.None)]
    [InlineData(GordianKey.Tab, InputModifiers.None)]
    [InlineData(GordianKey.Up, InputModifiers.None)]
    [InlineData(GordianKey.Enter, InputModifiers.None)]
    [InlineData(GordianKey.Space, InputModifiers.None)]
    [InlineData(GordianKey.Escape, InputModifiers.None)]
    public void FullSize_DoesNotTreatGameplayOrShortcutsAsTyping(GordianKey key, InputModifiers modifiers)
    {
        var settings = new StockUiSettings();
        settings.SetValue(StockUiSettingKey.KeyboardSize, 0);
        var chat = new StockUiChat { Settings = () => settings };
        Assert.False(chat.TryOpenForTyping(key, modifiers, InputProfile.CreateFullNumpad()));
        Assert.False(chat.Input.IsOpen);
    }

    [Fact]
    public void DifferentCharacters_KeepIndependentSelections()
    {
        var first = new StockUiSettings();
        var second = new StockUiSettings();
        first.SetValue(StockUiSettingKey.KeyboardSize, 0);
        second.SetValue(StockUiSettingKey.KeyboardSize, 2);
        var profile = InputProfile.CreateCompact();
        Assert.True(new StockUiChat { Settings = () => first }.TryOpenForTyping(GordianKey.A, InputModifiers.None, profile));
        Assert.False(new StockUiChat { Settings = () => second }.TryOpenForTyping(GordianKey.A, InputModifiers.None, profile));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void CompactSpace_OpensChatUnlessConfirmIsTaken(bool menu, bool confirm, bool opens)
    {
        var chat = new StockUiChat();
        Assert.Equal(opens, chat.TryOpen(GordianKey.Space, InputModifiers.None, InputProfile.CreateCompact(), menu, " ", confirm));
        Assert.Empty(chat.Input.Text);
        if (opens)
        {
            chat.Input.InsertText(" ");
            Assert.Empty(chat.Input.Text);
            chat.Input.InsertText("a");
            Assert.Equal("a", chat.Input.Text);
        }
    }
}
