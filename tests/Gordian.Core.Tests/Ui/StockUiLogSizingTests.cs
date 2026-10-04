// tests/Gordian.Core.Tests/Ui/StockUiLogSizingTests.cs
using System;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    public class StockUiLogSizingTests
    {
        [Fact]
        public void Reactive_StartsAtTheMinimum_GrowsWithNewLines_ThenShrinksAfterTheResizeTime()
        {
            var sizing = new StockUiLogSizing();
            Assert.Equal(2, sizing.Update(0, 10, reactive: true, minLines: 2, maxLines: 8, resizeSeconds: 5, expanded: false));
            Assert.Equal(5, sizing.Update(0.1, 13, true, 2, 8, 5, false));     // three new lines
            Assert.Equal(8, sizing.Update(0.2, 30, true, 2, 8, 5, false));     // capped at the maximum
            Assert.Equal(8, sizing.Update(5.1, 30, true, 2, 8, 5, false));     // not yet quiet for five seconds
            Assert.Equal(7, sizing.Update(5.3, 30, true, 2, 8, 5, false));     // shrinks a line a step
            Assert.Equal(7, sizing.Update(5.35, 30, true, 2, 8, 5, false));
            Assert.Equal(6, sizing.Update(5.45, 30, true, 2, 8, 5, false));
            for (double t = 5.6; t < 7; t += 0.1) sizing.Update(t, 30, true, 2, 8, 5, false);
            Assert.Equal(2, sizing.Lines);
            Assert.Equal(3, sizing.Update(7.1, 31, true, 2, 8, 5, false));     // a new line grows it again
        }

        [Fact]
        public void NotReactive_OrExpanded_ShowsTheMaximum()
        {
            var sizing = new StockUiLogSizing();
            Assert.Equal(6, sizing.Update(0, 0, reactive: false, minLines: 1, maxLines: 6, resizeSeconds: 1, expanded: false));
            Assert.Equal(6, sizing.Update(10, 0, false, 1, 6, 1, false));
            var reactive = new StockUiLogSizing();
            Assert.Equal(1, reactive.Update(0, 0, true, 1, 6, 1, false));
            Assert.Equal(6, reactive.Update(0.1, 0, true, 1, 6, 1, expanded: true));   // e.g. the input line opened
            Assert.Equal(6, reactive.Update(0.5, 0, true, 1, 6, 1, false));            // stays until the resize time passes
            Assert.Equal(5, reactive.Update(1.2, 0, true, 1, 6, 1, false));
        }

        [Fact]
        public void MinimumAboveTheMaximum_IsTheMaximum_AndTheResizeTimeIsTenthsOfTheSlider()
        {
            var sizing = new StockUiLogSizing();
            Assert.Equal(4, sizing.Update(0, 0, true, 8, 4, 5, false));
            Assert.Equal(5.0, StockUiLogSizing.ResizeSeconds(50));
            Assert.Equal(0.0, StockUiLogSizing.ResizeSeconds(0));
        }

        [Fact]
        public void Log_CountsArrivedLinesPerWindow()
        {
            var log = new StockUiChatLog();
            log.Add(ChatLogChannel.Say, "a");
            log.Add(new ChatLogLine(ChatLogChannel.Combat, "hit", DateTime.Now));
            Assert.Equal(2, log.AddedCount(1));     // one window: both lines
            Assert.Equal(0, log.AddedCount(2));
            log.MultiWindow = true;
            Assert.Equal(1, log.AddedCount(1));
            Assert.Equal(1, log.AddedCount(2));
        }
    }
}
