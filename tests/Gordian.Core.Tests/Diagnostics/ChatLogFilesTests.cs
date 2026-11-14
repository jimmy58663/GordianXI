// tests/Gordian.Core.Tests/Diagnostics/ChatLogFilesTests.cs
using System;
using System.IO;
using Gordian.Core.Diagnostics;
using Gordian.Core.Profiles;
using Xunit;

namespace Gordian.Core.Tests.Diagnostics
{
    /// <summary>Chat log folder naming (#565).</summary>
    public sealed class ChatLogFilesTests
    {
        [Theory]
        [InlineData("Gemini", "Gemini")]
        [InlineData("myserver.example.com", "myserver.example.com")]
        [InlineData("  Spaced  ", "Spaced")]
        [InlineData("a/b\\c:d*e?f\"g<h>i|j", "a_b_c_d_e_f_g_h_i_j")]
        [InlineData("", "Unknown")]
        [InlineData(null, "Unknown")]
        [InlineData("..", "Unknown")]
        [InlineData("name.", "name")]
        public void FolderName_IsSafeAndKeepsCasing(string? name, string expected) =>
            Assert.Equal(expected, ChatLogFiles.FolderName(name));

        [Fact]
        public void PathFor_IsServerCharacterAndDay()
        {
            string path = ChatLogFiles.PathFor("logs", "Retail", "Gemini", new DateTime(2026, 11, 14, 23, 0, 0));

            Assert.Equal(Path.Combine("logs", "chat", "Retail", "Gemini", "2026-11-14.log"), path);
        }

        [Fact]
        public void ChatLogServerName_IsRetailOrTheServerHost()
        {
            Assert.Equal("Retail", new AccountProfile { ServerType = ProfileServerType.Retail, ServerHost = "ignored" }.ChatLogServerName);
            Assert.Equal("127.0.0.1", new AccountProfile { ServerType = ProfileServerType.LandSandBoat, ServerHost = " 127.0.0.1 " }.ChatLogServerName);
        }
    }
}
