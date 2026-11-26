using Gordian.App.Graphics;
using Gordian.Engine.Ui;
using Xunit;
namespace Gordian.App.Tests.Graphics;
public class AutoTranslateMenuTests
{
    [Fact]
    public void VisibleRowsFollowSelectionAndExplainNoMatches()
    {
        var rows = Enumerable.Range(0, 20).Select(i => new StockUiChatInput.CompletionRow("Candidate " + i, (uint)i + 1)).ToArray();
        var view = new StockUiChatInput.CompletionView(true, rows, 12);
        var lines = StockUiChatWindow.CompletionLines(view);
        Assert.Contains("> Candidate 12", lines);
        Assert.Equal(9, lines.Count);
        Assert.Contains("No matching phrases", StockUiChatWindow.CompletionLines(new(true, [], 0)));
        Assert.Empty(StockUiChatWindow.CompletionLines(new(false, [], 0)));
    }
}
