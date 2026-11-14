// tests/Gordian.App.Tests/AvaloniaLogSinkTests.cs
using Gordian.App.Services;
using Xunit;

namespace Gordian.App.Tests
{
    /// <summary>The Avalonia log adapter fills message-template holes in order (#564).</summary>
    public sealed class AvaloniaLogSinkTests
    {
        [Fact]
        public void Fill_ReplacesHolesInOrder()
        {
            string message = AvaloniaLogSink.Fill("An error occurred binding {Property} to {Expression}: {Message}",
                new object?[] { "Text", "Inventory.SelectedItem", "Value is null." });

            Assert.Equal("An error occurred binding Text to Inventory.SelectedItem: Value is null.", message);
        }

        [Fact]
        public void Fill_LeavesExtraHolesAndNoValuesAlone()
        {
            Assert.Equal("a {B}", AvaloniaLogSink.Fill("a {B}", null));
            Assert.Equal("x {B}", AvaloniaLogSink.Fill("{A} {B}", new object?[] { "x" }));
        }
    }
}
