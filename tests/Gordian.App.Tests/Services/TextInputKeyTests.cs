using Avalonia.Input;
using Gordian.App.Services;
using Xunit;

namespace Gordian.App.Tests.Services;

public class TextInputKeyTests
{
    [Fact]
    public void MissingX11SymbolsDoNotSuppressPrintableKeyText()
    {
        foreach (var key in new[] { Key.A, Key.D0, Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6,
            Key.D7, Key.D8, Key.D9, Key.OemQuestion, Key.OemQuotes, Key.OemPlus, Key.Space, Key.NumPad1 })
        {
            Assert.True(AvaloniaInputMapper.IsTextInputKey(key, null, KeyModifiers.Shift));
            Assert.True(AvaloniaInputMapper.IsTextInputKey(key, "", KeyModifiers.None));
        }
    }

    [Fact]
    public void EditingAndShortcutKeysRemainHandled()
    {
        foreach (var key in new[] { Key.Return, Key.Tab, Key.Escape, Key.Back, Key.Delete,
            Key.Left, Key.Home, Key.F1, Key.LeftShift, Key.None })
            Assert.False(AvaloniaInputMapper.IsTextInputKey(key, null, KeyModifiers.None));
        foreach (var mods in new[] { KeyModifiers.Control, KeyModifiers.Alt, KeyModifiers.Control | KeyModifiers.Shift })
            Assert.False(AvaloniaInputMapper.IsTextInputKey(Key.D1, "1", mods));
    }

    [Fact]
    public void PlatformSymbolsRetainExistingWindowsAndLayoutBehavior()
    {
        Assert.True(AvaloniaInputMapper.IsTextInputKey(Key.A, "A", KeyModifiers.None));
        Assert.True(AvaloniaInputMapper.IsTextInputKey(Key.D2, "\"", KeyModifiers.Shift));
        Assert.True(AvaloniaInputMapper.IsTextInputKey(Key.None, "!", KeyModifiers.Shift));
        Assert.False(AvaloniaInputMapper.IsTextInputKey(Key.A, "\n", KeyModifiers.None));
    }
}
