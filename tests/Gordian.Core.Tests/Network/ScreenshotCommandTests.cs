// tests/Gordian.Core.Tests/Network/ScreenshotCommandTests.cs
using Gordian.Core.Network;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class ScreenshotCommandTests
    {
        [Theory]
        [InlineData("/screenshot", 0, "")]
        [InlineData("/screenshot hide", 1, "")]
        [InlineData("/screenshot HIDE valkurm_noon", 1, "valkurm_noon")]
        [InlineData("/screenshot valkurm_noon hide", 1, "valkurm_noon")]
        [InlineData("/screenshot shot-1.png", 0, "shot-1")]
        public void ParsesHideAndName(string line, int hide, string name)
        {
            var result = ChatCommandRouter.Parse(line);

            Assert.Equal(ChatCommandResultKind.Screenshot, result.Kind);
            Assert.Equal(hide, result.ActionParam);
            Assert.Equal(name, result.Message);
        }

        [Theory]
        [InlineData("/screenshot ../escape")]
        [InlineData("/screenshot ..")]
        [InlineData("/screenshot folder/name")]
        [InlineData("/screenshot folder\\name")]
        [InlineData("/screenshot C:name")]
        [InlineData("/screenshot one two")]
        public void RefusesNamesThatCouldLeaveTheFolder(string line)
        {
            var result = ChatCommandRouter.Parse(line);

            Assert.Equal(ChatCommandResultKind.LocalNotice, result.Kind);
            Assert.StartsWith("Usage: /screenshot", result.Message);
        }
    }
}
