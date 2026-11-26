using Gordian.Engine.Input;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Input;

public class SharedKeyboardMigrationTests
{
    [Fact]
    public void LegacyCompact_DefaultCharacterSettings_WWalksInsteadOfOpeningChat()
    {
        var profile = InputProfile.FromJson(InputProfile.CreateCompact().SaveToJson());
        var chat = new StockUiChat { Settings = () => new StockUiSettings() };
        Assert.False(chat.TryOpenForTyping(GordianKey.W, InputModifiers.None, profile));
        Assert.Contains(new InputChord(GordianKey.W), profile.GetChords(InputAction.MoveForward));
    }
}
