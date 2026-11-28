// tests/Gordian.Core.Tests/Diagnostics/ChatLogFilesTests.cs
using System;
using System.IO;
using Gordian.Core.Config;
using Gordian.Core.Diagnostics;
using Gordian.Core.Profiles;
using Xunit;

namespace Gordian.Core.Tests.Diagnostics
{
    /// <summary>Chat log folder naming (#565) and the move of older builds' chat logs into the world folders (#639).</summary>
    public sealed class ChatLogFilesTests : IDisposable
    {
        private static readonly CharacterFolderKey Akaden = new("10.0.0.5", "Gordian", "Akaden");

        private readonly string _logs = Path.Combine(Path.GetTempPath(), "GordianXI_ChatLogs_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_logs)) Directory.Delete(_logs, true);
        }

        private string Write(string text, params string[] parts)
        {
            string path = Path.Combine(_logs, "chat", Path.Combine(parts));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return path;
        }

        private string New(string day) => Path.Combine(_logs, "chat", "10.0.0.5", "Gordian", "Akaden", day);

        [Fact]
        public void PathFor_IsServerWorldCharacterAndDay()
        {
            string path = ChatLogFiles.PathFor("logs", new CharacterFolderKey("Retail", "Asura", "Gemini"), new DateTime(2026, 11, 14, 23, 0, 0));

            Assert.Equal(Path.Combine("logs", "chat", "Retail", "Asura", "Gemini", "2026-11-14.log"), path);
        }

        [Fact]
        public void PathFor_AnEmptyWorldIsUnknown() =>
            Assert.Equal(Path.Combine("logs", "chat", "Retail", "Unknown", "Gemini", "2026-11-14.log"),
                ChatLogFiles.PathFor("logs", new CharacterFolderKey("Retail", "", "Gemini"), new DateTime(2026, 11, 14)));

        [Fact]
        public void ServerFolderName_IsRetailOrTheServerHost()
        {
            Assert.Equal("Retail", new AccountProfile { ServerType = ProfileServerType.Retail, ServerHost = "ignored" }.ServerFolderName);
            Assert.Equal("127.0.0.1", new AccountProfile { ServerType = ProfileServerType.LandSandBoat, ServerHost = " 127.0.0.1 " }.ServerFolderName);
            Assert.Equal("play.example.com", new AccountProfile { ServerType = ProfileServerType.LandSandBoat, ServerHost = "Play.Example.COM" }.ServerFolderName);
        }

        [Fact]
        public void MoveLegacy_MovesTheDayFilesIntoTheWorldFolder()
        {
            string old = Write("a", "10.0.0.5", "Akaden", "2026-11-14.log");
            Write("b", "10.0.0.5", "Akaden", "2026-11-15.log");

            Assert.Equal(2, ChatLogFiles.MoveLegacy(_logs, Akaden));

            Assert.Equal("a", File.ReadAllText(New("2026-11-14.log")));
            Assert.Equal("b", File.ReadAllText(New("2026-11-15.log")));
            Assert.False(Directory.Exists(Path.GetDirectoryName(old)));
        }

        [Fact]
        public void MoveLegacy_FindsAMixedCaseHostFolder_AndRemovesIt()
        {
            var key = new CharacterFolderKey("play.example.com", "Gordian", "Akaden");
            Write("a", "Play.Example.com", "Akaden", "2026-11-14.log");

            Assert.Equal(1, ChatLogFiles.MoveLegacy(_logs, key));

            Assert.True(File.Exists(Path.Combine(_logs, "chat", "play.example.com", "Gordian", "Akaden", "2026-11-14.log")));
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
                Assert.False(Directory.Exists(Path.Combine(_logs, "chat", "Play.Example.com")));
        }

        [Fact]
        public void MoveLegacy_AppendsToADayTheNewFolderAlreadyHas()
        {
            Write("new\n", "10.0.0.5", "Gordian", "Akaden", "2026-11-14.log");
            Write("old\n", "10.0.0.5", "Akaden", "2026-11-14.log");

            ChatLogFiles.MoveLegacy(_logs, Akaden);

            Assert.Equal("new\nold\n", File.ReadAllText(New("2026-11-14.log")));
        }

        [Fact]
        public void MoveLegacy_LeavesOtherCharactersAndServers()
        {
            string other = Write("x", "10.0.0.5", "Other", "2026-11-14.log");
            string retail = Write("y", "Retail", "Akaden", "2026-11-14.log");
            Write("a", "10.0.0.5", "Akaden", "2026-11-14.log");

            ChatLogFiles.MoveLegacy(_logs, Akaden);

            Assert.True(File.Exists(other));
            Assert.True(File.Exists(retail));
        }

        [Fact]
        public void MoveLegacy_IgnoresAWorldFolderNamedLikeTheCharacter()
        {
            // New layout: logs/chat/Retail/Shiva/<character>/ for the world Shiva; a character named Shiva has no old files.
            string current = Write("current", "Retail", "Shiva", "Other", "2026-11-14.log");

            Assert.Equal(0, ChatLogFiles.MoveLegacy(_logs, new CharacterFolderKey("Retail", "Asura", "Shiva")));

            Assert.True(File.Exists(current));
        }

        [Fact]
        public void MoveLegacy_WithNothingToMove_DoesNothing()
        {
            Assert.Equal(0, ChatLogFiles.MoveLegacy(_logs, Akaden));

            Assert.False(Directory.Exists(Path.Combine(_logs, "chat")));
        }
    }
}
