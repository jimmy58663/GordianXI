using Gordian.Engine.Input;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui;

public class StockUiKeyboardChatTests
{
    [Theory]
    [InlineData(KeyboardMode.Full, 2, true)]
    [InlineData(KeyboardMode.Compact1, 0, false)]
    [InlineData(KeyboardMode.Compact2, 0, false)]
    public void SharedKeyboardMode_IgnoresStaleCharacterSelection(KeyboardMode mode, int stale, bool opens)
    {
        var settings = new StockUiSettings();
        settings.SetValue(StockUiSettingKey.KeyboardSize, stale);
        var profile = InputProfile.CreateDefault();
        profile.KeyboardLayout!.Mode = mode;
        profile = KeyboardLayout.Resolve(profile, mode);
        string before = profile.SaveToJson();
        var chat = new StockUiChat { Settings = () => settings };
        Assert.Equal(opens, chat.TryOpenForTyping(GordianKey.W, InputModifiers.None, profile));
        Assert.Equal(opens, chat.Input.IsOpen);
        Assert.Empty(chat.Input.Text);
        Assert.Equal(before, profile.SaveToJson());
        chat.Input.Cancel();
        settings.ResetAll();
        Assert.Equal(opens, chat.TryOpenForTyping(GordianKey.W, InputModifiers.Shift, profile));
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
        var chat = new StockUiChat { Settings = () => new StockUiSettings() };
        Assert.False(chat.TryOpenForTyping(key, modifiers, KeyboardLayout.Resolve(InputProfile.CreateDefault(), KeyboardMode.Full)));
        Assert.False(chat.Input.IsOpen);
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
