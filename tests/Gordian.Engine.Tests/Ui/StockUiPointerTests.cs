// tests/Gordian.Engine.Tests/Ui/StockUiPointerTests.cs
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>Cancel with no menu open hides the mouse cursor until the mouse moves (#605).</summary>
    public class StockUiPointerTests
    {
        [Fact]
        public void HideUntilMoved_HidesThePointer_UntilAMoveToANewPlace()
        {
            var pointer = new StockUiPointer();
            int requests = 0;
            pointer.HideRequested += () => requests++;
            pointer.MoveTo(100, 50);
            Assert.True(pointer.TryGetPosition(out _, out _));

            pointer.HideUntilMoved();
            Assert.True(pointer.Hidden);
            Assert.Equal(1, requests);
            Assert.False(pointer.TryGetPosition(out _, out _)); // no hover pointer either
            pointer.HideUntilMoved();
            Assert.Equal(1, requests); // already hidden

            pointer.MoveTo(100, 50); // a repeated message at the same place is no move
            Assert.True(pointer.Hidden);
            pointer.MoveTo(101, 50);
            Assert.False(pointer.Hidden);
            Assert.True(pointer.TryGetPosition(out float x, out _));
            Assert.Equal(101, x);
        }

        [Fact]
        public void APointerOutsideTheViewport_ShowsAgainWhenItComesBack()
        {
            var pointer = new StockUiPointer();
            pointer.MoveTo(10, 10);
            pointer.Leave();
            pointer.HideUntilMoved();
            pointer.MoveTo(10, 10);
            Assert.False(pointer.Hidden);
        }
    }
}
