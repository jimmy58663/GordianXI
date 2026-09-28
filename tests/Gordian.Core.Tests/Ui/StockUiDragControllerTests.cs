// tests/Gordian.Core.Tests/Ui/StockUiDragControllerTests.cs
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    public class StockUiDragControllerTests
    {
        // The retail solo party window (112 x 34 at (384, 398), bottom-right) and log window (366 x 134 at (16, 298), bottom-left).
        private static readonly UiMenuFrame Party = new() { X = 384, Y = 398, Width = 112, Height = 34, Anchor = UiAnchor.BottomRight };
        private static readonly UiMenuFrame Log = new() { X = 16, Y = 298, Width = 366, Height = 134, Anchor = UiAnchor.BottomLeft };
        private const float ScreenWidth = 1920, ScreenHeight = 1080;

        private static (StockUiDragController Drag, StockUiLayout Layout, StockUiPlacement Party, StockUiPlacement Log) Frame(bool unlocked)
        {
            var layout = new StockUiLayout { Unlocked = unlocked };
            var drag = new StockUiDragController();
            drag.BeginFrame(layout, ScreenWidth, ScreenHeight);
            var log = layout.Resolve(StockUiWindowIds.Log, Log, ScreenWidth, ScreenHeight);
            var party = layout.Resolve(StockUiWindowIds.Party, Party, ScreenWidth, ScreenHeight);
            // The log window is drawn with a 16-px title band above its frame and stretched to the party window.
            drag.Register(StockUiWindowIds.Log, Log, log, log.X, log.Y - 16, party.X - 2 - log.X, Log.Height + 16);
            drag.Register(StockUiWindowIds.Party, Party, party);
            drag.RegisterButton(StockUiDragController.ResetPositionsButton, 900, 6, 120, 18, 1);
            drag.EndFrame();
            return (drag, layout, party, log);
        }

        [Fact]
        public void Locked_IgnoresTheMouse()
        {
            var (drag, layout, party, _) = Frame(unlocked: false);
            Assert.False(drag.Unlocked);
            Assert.False(drag.OnMouseDown(party.X + 5, party.Y + 5));
            Assert.False(drag.OnMouseMove(100, 100));
            Assert.False(drag.OnMouseUp(100, 100));
            Assert.Null(drag.HoveredWindow);
            Assert.Empty(layout.Windows);
        }

        [Fact]
        public void PressOutsideEveryWindow_IsNotADrag()
        {
            var (drag, _, _, _) = Frame(unlocked: true);
            Assert.False(drag.OnMouseDown(960, 100));
            Assert.Null(drag.DraggingWindow);
        }

        [Fact]
        public void Drag_KeepsTheGrabPoint_ClampsToTheScreen_AndWritesTheLayoutOnRelease()
        {
            var (drag, layout, party, _) = Frame(unlocked: true);
            int changes = 0;
            layout.Changed += () => changes++;

            Assert.True(drag.OnMouseDown(party.X + 10, party.Y + 8));
            Assert.Equal(StockUiWindowIds.Party, drag.DraggingWindow);
            Assert.Equal(StockUiWindowIds.Party, drag.HoveredWindow);

            // Moving the pointer moves the window by the same amount, without touching the layout yet.
            Assert.True(drag.OnMouseMove(party.X + 10 - 700, party.Y + 8 - 500));
            Assert.True(drag.TryGetDragPosition(StockUiWindowIds.Party, out float x, out float y));
            Assert.Equal((party.X - 700, party.Y - 500), (x, y));
            Assert.Equal(0, changes);
            Assert.Empty(layout.Windows);

            // Dragged past the top-left corner the window stops at the screen edge.
            drag.OnMouseMove(-50, -50);
            drag.TryGetDragPosition(StockUiWindowIds.Party, out x, out y);
            Assert.Equal((0f, 0f), (x, y));

            Assert.True(drag.OnMouseUp(-50, -50));
            Assert.Null(drag.DraggingWindow);
            Assert.False(drag.TryGetDragPosition(StockUiWindowIds.Party, out _, out _));
            Assert.Equal(1, changes);
            var moved = layout.Resolve(StockUiWindowIds.Party, Party, ScreenWidth, ScreenHeight);
            Assert.Equal((0f, 0f), (moved.X, moved.Y));
            Assert.Equal(UiAnchor.TopLeft, layout.Windows[StockUiWindowIds.Party].Anchor);
        }

        [Fact]
        public void HitRectangleAboveTheFrame_MovesTheFrameOrigin()
        {
            var (drag, layout, _, log) = Frame(unlocked: true);
            // Grab the log window by its title band (above the frame's top).
            Assert.True(drag.OnMouseDown(log.X + 40, log.Y - 8));
            drag.OnMouseMove(log.X + 40 + 100, log.Y - 8 - 300);
            Assert.True(drag.TryGetDragPosition(StockUiWindowIds.Log, out float x, out float y));
            Assert.Equal((log.X + 100, log.Y - 300), (x, y));
            drag.OnMouseUp(log.X + 140, log.Y - 308);
            var moved = layout.Resolve(StockUiWindowIds.Log, Log, ScreenWidth, ScreenHeight);
            Assert.Equal(log.X + 100, moved.X, 3);
            Assert.Equal(log.Y - 300, moved.Y, 3);
        }

        [Fact]
        public void TopmostRegistration_WinsWhereWindowsOverlap()
        {
            var layout = new StockUiLayout { Unlocked = true };
            var drag = new StockUiDragController();
            drag.BeginFrame(layout, ScreenWidth, ScreenHeight);
            var below = new StockUiPlacement(100, 100, 1, false);
            var above = new StockUiPlacement(150, 150, 1, false);
            drag.Register(StockUiWindowIds.Log, Log, below);     // 366 x 134 from (100, 100)
            drag.Register(StockUiWindowIds.Party, Party, above); // 112 x 34 from (150, 150), drawn later
            drag.EndFrame();

            drag.OnMouseMove(160, 160);
            Assert.Equal(StockUiWindowIds.Party, drag.HoveredWindow);
            drag.OnMouseMove(110, 110);
            Assert.Equal(StockUiWindowIds.Log, drag.HoveredWindow);
            Assert.True(drag.OnMouseDown(160, 160));
            Assert.Equal(StockUiWindowIds.Party, drag.DraggingWindow);
        }

        [Fact]
        public void Locking_CancelsADrag()
        {
            var (drag, layout, party, _) = Frame(unlocked: true);
            Assert.True(drag.OnMouseDown(party.X + 1, party.Y + 1));
            layout.SetUnlocked(false);
            Assert.Null(drag.DraggingWindow);
            Assert.False(drag.OnMouseUp(0, 0));
            Assert.Empty(layout.Windows);
        }

        [Fact]
        public void DefaultPositionsButton_ResetsEveryWindowPosition_KeepingOtherOverrides()
        {
            var (drag, layout, party, _) = Frame(unlocked: true);
            layout.MoveTo(StockUiWindowIds.Party, Party, 10, 10, ScreenWidth, ScreenHeight);
            layout.SetWindowScale(StockUiWindowIds.Log, 0.75f);
            layout.SetHidden(StockUiWindowIds.Target, true);

            Assert.Equal(StockUiDragController.ResetPositionsButton, drag.HitTest(950, 10));
            Assert.True(drag.OnMouseDown(950, 10)); // a click, not a drag
            Assert.Null(drag.DraggingWindow);

            Assert.False(layout.Windows.ContainsKey(StockUiWindowIds.Party)); // back at retail, override dropped
            Assert.Equal(0.75f, layout.Windows[StockUiWindowIds.Log].Scale);
            Assert.True(layout.Windows[StockUiWindowIds.Target].Hidden);
            Assert.True(layout.Unlocked); // still editing
            var reset = layout.Resolve(StockUiWindowIds.Party, Party, ScreenWidth, ScreenHeight);
            Assert.Equal((party.X, party.Y), (reset.X, reset.Y));
        }
    }
}
