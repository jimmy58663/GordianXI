// tests/Gordian.App.Tests/Graphics/StockUiWindowPlacementTests.cs
using Gordian.App.Graphics;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>The shared window placement rules (#418): companions at their authored offset, windows on the log's top edge.</summary>
    public class StockUiWindowPlacementTests
    {
        [Fact]
        public void Beside_KeepsTheAuthoredOffsetAtTheParentsScale()
        {
            var parent = new UiMenuFrame { X = 16, Y = 48 };
            var at = StockUiWindowPlacement.Beside(new StockUiPlacement(100, 200, 2, false), parent, 130, 240);
            Assert.Equal(new StockUiPlacement(100 + 114 * 2, 200 + 192 * 2, 2, false), at);
        }

        [Fact]
        public void TopOnLog_SitsOnWindow1_ElseTheScreensBottom_ElseTheFallback()
        {
            // On Window 1's top edge.
            Assert.Equal(700 - 72, StockUiWindowPlacement.TopOnLog(new StockUiScreen(1280, 900, 700), 1, 72, 240));
            // The log hidden: the screen's bottom less the margin, or the window's own place when it has one.
            Assert.Equal(900 - StockUiWindowPlacement.BottomMargin * 2 - 72, StockUiWindowPlacement.TopOnLog(new StockUiScreen(1280, 900, null), 2, 72, 240));
            Assert.Equal(500, StockUiWindowPlacement.TopOnLog(new StockUiScreen(1280, 900, null), 1, 72, 240, hiddenLogTop: 500));
            // No screen: the authored place; never above the top.
            Assert.Equal(240, StockUiWindowPlacement.TopOnLog(default, 1, 72, 240));
            Assert.Equal(0, StockUiWindowPlacement.TopOnLog(new StockUiScreen(1280, 900, 40), 1, 72, 240));
            Assert.False(StockUiWindowPlacement.HasScreen(default));
        }
    }
}
