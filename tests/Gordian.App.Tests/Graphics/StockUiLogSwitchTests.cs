// tests/Gordian.App.Tests/Graphics/StockUiLogSwitchTests.cs
using System;
using Gordian.App.Graphics;
using Gordian.Core.Network;
using Gordian.Engine.Sessions;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// #377: one HUD draws whichever character the viewport shows. Each character keeps its own log window sizing, and the
    /// characters not on screen keep theirs up to date, so a switch neither reads the other log's lines as new ones (the
    /// window grew line by line, as if the log replayed) nor waits for the window to catch up.
    /// </summary>
    public class StockUiLogSwitchTests
    {
        private static CharacterSession Session(string prefix) =>
            new($"{prefix}{Guid.NewGuid():N}"[..15], 1, "acct", new SessionNetworkManager("127.0.0.1", 54231));

        [Fact]
        public void LogSizing_IsPerCharacter_AndAHiddenLogKeepsUp()
        {
            var hud = new StockUiHud();
            var shown = Session("Shown");
            var hidden = Session("Hidden");
            for (int i = 0; i < 20; i++) hidden.Chat.Log.Add(ChatLogChannel.System, $"Line {i}");

            hud.TickHiddenLog(shown);
            hud.TickHiddenLog(hidden);
            // Lines already there when a log is first sized are not new: a reactive window (retail default, minimum 0) stays shut.
            Assert.Equal(0, hud.LogSizing(hidden, 1).Lines);
            Assert.NotSame(hud.LogSizing(shown, 1), hud.LogSizing(hidden, 1));

            shown.Chat.Log.Add(ChatLogChannel.System, "New for the shown character");
            shown.Chat.Log.Add(ChatLogChannel.System, "And another");
            hud.TickHiddenLog(shown);
            hud.TickHiddenLog(hidden);
            Assert.Equal(2, hud.LogSizing(shown, 1).Lines);
            Assert.Equal(0, hud.LogSizing(hidden, 1).Lines);

            // A line arriving while the character is not on screen sizes its own window in the background.
            hidden.Chat.Log.Add(ChatLogChannel.System, "Arrived while hidden");
            hud.TickHiddenLog(hidden);
            Assert.Equal(1, hud.LogSizing(hidden, 1).Lines);
            Assert.Equal(2, hud.LogSizing(shown, 1).Lines);
        }
    }
}
