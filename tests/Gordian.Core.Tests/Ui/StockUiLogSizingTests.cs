// tests/Gordian.Core.Tests/Ui/StockUiLogSizingTests.cs
using System;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    public class StockUiLogSizingTests
    {
        [Fact]
        public void Reactive_GrowsWithNewLines_ThenGivesBackOneLinePerResizeTime()
        {
            var sizing = new StockUiLogSizing();
            Assert.Equal(0, sizing.Update(0, 10, reactive: true, minLines: 0, maxLines: 8, resizeSeconds: 2, expanded: false));
            Assert.Equal(3, sizing.Update(0.1, 13, true, 0, 8, 2, false));     // three new lines
            Assert.Equal(8, sizing.Update(0.2, 30, true, 0, 8, 2, false));     // capped at the maximum
            Assert.Equal(8, sizing.Update(2.1, 30, true, 0, 8, 2, false));     // not yet quiet for one resize time
            Assert.Equal(7, sizing.Update(2.25, 30, true, 0, 8, 2, false));    // one line goes...
            Assert.Equal(7, sizing.Update(4.2, 30, true, 0, 8, 2, false));     // ...then it waits again
            Assert.Equal(6, sizing.Update(4.3, 30, true, 0, 8, 2, false));
            for (double t = 4.4; t < 20; t += 0.1) sizing.Update(t, 30, true, 0, 8, 2, false);
            Assert.Equal(0, sizing.Lines);                                     // down to the minimum, 0: closed
            Assert.Equal(1, sizing.Update(20.1, 31, true, 0, 8, 2, false));    // a new line opens it again
        }

        [Fact]
        public void DisplayLines_FollowTheLineCountSmoothly()
        {
            var sizing = new StockUiLogSizing();
            sizing.Update(0, 0, true, 0, 8, 2, false);
            Assert.Equal(0f, sizing.DisplayLines);
            sizing.Update(0.01, 4, true, 0, 8, 2, false);
            Assert.InRange(sizing.DisplayLines, 0.01f, 0.2f);                  // starts moving
            float last = sizing.DisplayLines;
            for (double t = 0.02; t < 1.0; t += 1 / 60.0)
            {
                sizing.Update(t, 4, true, 0, 8, 2, false);
                Assert.True(sizing.DisplayLines >= last);                     // never overshoots back
                last = sizing.DisplayLines;
            }
            Assert.Equal(4f, sizing.DisplayLines);
        }

        [Fact]
        public void NotReactive_OrExpanded_ShowsTheMaximum()
        {
            var sizing = new StockUiLogSizing();
            Assert.Equal(6, sizing.Update(0, 0, reactive: false, minLines: 0, maxLines: 6, resizeSeconds: 1, expanded: false));
            Assert.Equal(6f, sizing.DisplayLines);
            var reactive = new StockUiLogSizing();
            Assert.Equal(0, reactive.Update(0, 0, true, 0, 6, 1, false));
            Assert.Equal(6, reactive.Update(0.1, 0, true, 0, 6, 1, expanded: true));   // e.g. the input line opened
            Assert.Equal(6, reactive.Update(0.5, 0, true, 0, 6, 1, false));            // waits a resize time
            Assert.Equal(5, reactive.Update(1.2, 0, true, 0, 6, 1, false));
        }

        [Fact]
        public void Defaults_AreZeroToEightLines_AndTheResizeTimeIsTwentiethsOfTheSlider()
        {
            var settings = new StockUiSettings();
            Assert.Equal(0, settings.GetValue(StockUiSettingKey.Window1MinLines));
            Assert.Equal(0, settings.GetValue(StockUiSettingKey.Window2MinLines));
            Assert.Equal(8, settings.GetValue(StockUiSettingKey.Window1MaxLines));
            Assert.Equal(8, settings.GetValue(StockUiSettingKey.Window2MaxLines));
            Assert.Equal(2.5, StockUiLogSizing.ResizeSeconds(50));
            var sizing = new StockUiLogSizing();
            Assert.Equal(4, sizing.Update(0, 0, true, 8, 4, 5, false));        // a minimum above the maximum is the maximum
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
