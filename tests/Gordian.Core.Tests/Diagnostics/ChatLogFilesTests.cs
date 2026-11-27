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
        [Fact]
        public void PathFor_IsServerCharacterAndDay()
        {
            string path = ChatLogFiles.PathFor("logs", "Retail", "Gemini", new DateTime(2026, 11, 14, 23, 0, 0));

            Assert.Equal(Path.Combine("logs", "chat", "Retail", "Gemini", "2026-11-14.log"), path);
        }

        [Fact]
        public void ServerFolderName_IsRetailOrTheServerHost()
        {
            Assert.Equal("Retail", new AccountProfile { ServerType = ProfileServerType.Retail, ServerHost = "ignored" }.ServerFolderName);
            Assert.Equal("127.0.0.1", new AccountProfile { ServerType = ProfileServerType.LandSandBoat, ServerHost = " 127.0.0.1 " }.ServerFolderName);
            Assert.Equal("play.example.com", new AccountProfile { ServerType = ProfileServerType.LandSandBoat, ServerHost = "Play.Example.COM" }.ServerFolderName);
        }
    }
}
