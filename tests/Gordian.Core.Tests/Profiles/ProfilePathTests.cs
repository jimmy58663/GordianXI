// tests/Gordian.Core.Tests/Profiles/ProfilePathTests.cs
using System.Linq;
using Gordian.Core.Profiles;
using Xunit;

namespace Gordian.Core.Tests.Profiles
{
    /// <summary>Profile paths (#638): names unique per launch-list folder, and the <c>--profile</c> lookup.</summary>
    public sealed class ProfilePathTests
    {
        private static readonly AccountProfile Root = new() { Id = "1", ProfileName = "Mule", Folder = "" };
        private static readonly AccountProfile Horizon = new() { Id = "2", ProfileName = "Gordian", Folder = "Horizon" };
        private static readonly AccountProfile Retail = new() { Id = "3", ProfileName = "Gordian", Folder = "Retail" };
        private static readonly AccountProfile Nested = new() { Id = "4", ProfileName = "Gordian", Folder = "Test2/Test3" };
        private static readonly AccountProfile[] All = [Root, Horizon, Retail, Nested];

        [Fact]
        public void Format_JoinsFolderAndName()
        {
            Assert.Equal("Mule", ProfilePath.Format(Root));
            Assert.Equal("Horizon/Gordian", ProfilePath.Format(Horizon));
            Assert.Equal("Test2/Test3/Gordian", ProfilePath.Format(Nested));
            Assert.Equal("A/B/X", ProfilePath.Format(new AccountProfile { ProfileName = "X", Folder = @" /A\ B/ " }));
        }

        [Fact]
        public void Find_ABareNameMatchesEveryFolder_IgnoringCase()
        {
            Assert.Same(Root, Assert.Single(ProfilePath.Find(All, "mule")));
            Assert.Equal(new[] { Horizon, Retail, Nested }, ProfilePath.Find(All, " GORDIAN ").ToArray());
            Assert.Empty(ProfilePath.Find(All, "Nobody"));
            Assert.Empty(ProfilePath.Find(All, "  "));
        }

        [Fact]
        public void Find_APathSplitsAtTheLastSeparator()
        {
            Assert.Same(Horizon, Assert.Single(ProfilePath.Find(All, "horizon/gordian")));
            Assert.Same(Nested, Assert.Single(ProfilePath.Find(All, "Test2/Test3/Gordian")));
            Assert.Same(Nested, Assert.Single(ProfilePath.Find(All, @"Test2\Test3\Gordian")));
            Assert.Same(Root, Assert.Single(ProfilePath.Find(All, "/Mule")));      // a leading '/' is the top level
            Assert.Empty(ProfilePath.Find(All, "Test3/Gordian"));                 // the whole folder path, not its end
            Assert.Empty(ProfilePath.Find(All, "Horizon/Mule"));
        }

        [Fact]
        public void FindNameInFolder_IsScopedToTheFolder_AndSkipsTheEditedProfile()
        {
            Assert.Same(Horizon, ProfilePath.FindNameInFolder(All, "horizon", "gordian"));
            Assert.Null(ProfilePath.FindNameInFolder(All, "Horizon", "Gordian", exceptId: "2"));
            Assert.Null(ProfilePath.FindNameInFolder(All, "", "Gordian"));
            Assert.Same(Root, ProfilePath.FindNameInFolder(All, null, " MULE "));
        }

        [Fact]
        public void FreeNameInFolder_CountsOnlyThatFolder()
        {
            Assert.Equal("Gordian", ProfilePath.FreeNameInFolder(All, "", "Gordian", n => $"Gordian {n}"));
            Assert.Equal("Gordian 2", ProfilePath.FreeNameInFolder(All, "Horizon", "Gordian", n => $"Gordian {n}"));
        }

        [Fact]
        public void HasSeparator_FindsEitherSlash()
        {
            Assert.True(ProfilePath.HasSeparator("A/B"));
            Assert.True(ProfilePath.HasSeparator(@"A\B"));
            Assert.False(ProfilePath.HasSeparator("Gordian"));
            Assert.False(ProfilePath.HasSeparator(null));
        }
    }
}
