// tests/Gordian.App.Tests/Services/DevLaunchOptionsTests.cs
using Gordian.App.Services;
using Xunit;

namespace Gordian.App.Tests.Services
{
    public class DevLaunchOptionsTests
    {
        [Fact]
        public void ReadsBothOptions()
        {
            var options = DevLaunchOptions.Parse(["--profiles-dir", "shared", "--profile", "Claude"]);

            Assert.Equal("shared", options.ProfilesDirectory);
            Assert.Equal("Claude", options.ProfileName);
        }

        [Fact]
        public void NoOptionsLeavesBothUnset()
        {
            var options = DevLaunchOptions.Parse([]);

            Assert.Null(options.ProfilesDirectory);
            Assert.Null(options.ProfileName);
        }

        [Fact]
        public void IgnoresOtherArgumentsAndAMissingValue()
        {
            var options = DevLaunchOptions.Parse(["--other", "value", "--profile"]);

            Assert.Null(options.ProfilesDirectory);
            Assert.Null(options.ProfileName);
        }
    }
}
