// tests/Gordian.Engine.Tests/Ui/ChatLogFileWriterTests.cs
using System;
using System.IO;
using Gordian.Core.Config;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources.Tables;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    [CollectionDefinition("ChatLogFileTests", DisableParallelization = true)]
    public class ChatLogFileTestsCollection { }

    /// <summary>Per-character, per-day chat log files (#565), and the session moving older builds' logs into them (#639).</summary>
    [Collection("ChatLogFileTests")]
    public sealed class ChatLogFileWriterTests : IDisposable
    {
        private readonly string _logs = Path.Combine(Path.GetTempPath(), $"gordian_chatlogs_{Guid.NewGuid():N}");

        public void Dispose()
        {
            GordianLog.Shutdown();
            try { Directory.Delete(_logs, recursive: true); } catch (IOException) { }
        }

        private void Start(bool? chatLog = null) =>
            GordianLog.Initialize(new LogOptions
            {
                LogsDirectory = _logs,
                Settings = new LoggingSettings { ChatLog = chatLog },
            });

        private string ChatFile(string server, string world, string character, DateTime day)
        {
            Assert.True(GordianLog.Flush(TimeSpan.FromSeconds(5)));
            string path = Path.Combine(_logs, GordianLog.ChatFolder, server, world, character, day.ToString("yyyy-MM-dd") + ".log");
            if (!File.Exists(path)) return string.Empty;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        [Fact]
        public void Format_IsTimeChannelAndPlainText()
        {
            var line = new ChatLogLine(ChatLogChannel.Tell, "Gemini>> meet at the\nHome Point", new DateTime(2026, 11, 14, 21, 5, 9));

            Assert.Equal("[21:05:09] [Tell] Gemini>> meet at the Home Point", ChatLogFileWriter.Format(line));
        }

        [Fact]
        public void PlainText_WritesElementIconsAsWordsAndDropsDrawingCodes()
        {
            string text = "Resist " + ElementGlyphs.IconChar(0) + "+20\u0007 done�";

            Assert.Equal("Resist Fire+20 done", ChatLogFileWriter.PlainText(text));
            Assert.Equal(string.Empty, ChatLogFileWriter.PlainText(null));
        }

        [Fact]
        public void EveryLogLine_GoesToTheCharactersFileForThatDay()
        {
            Start();
            var log = new StockUiChatLog();
            using var writer = new ChatLogFileWriter(log, new CharacterFolderKey("127.0.0.1", "Asura", "Gemini"));
            var day = new DateTime(2026, 11, 14, 20, 0, 0);

            log.Add(new ChatLogLine(ChatLogChannel.Say, "Gemini : hello", day));
            log.Add(new ChatLogLine(ChatLogChannel.Combat, "Gemini hits the Wild Rabbit for 12 points of damage.", day.AddSeconds(3)));

            string content = ChatFile("127.0.0.1", "Asura", "Gemini", day);
            Assert.Contains("[20:00:00] [Say] Gemini : hello", content);
            Assert.Contains("[20:00:03] [Combat] Gemini hits the Wild Rabbit", content);
        }

        [Fact]
        public void PlayPastMidnight_MovesToTheNextDaysFile()
        {
            Start();
            var log = new StockUiChatLog();
            using var writer = new ChatLogFileWriter(log, new CharacterFolderKey("Retail", "Asura", "Gemini"));
            var before = new DateTime(2026, 11, 14, 23, 59, 58);
            var after = new DateTime(2026, 11, 15, 0, 0, 2);

            log.Add(new ChatLogLine(ChatLogChannel.Party, "(Claude) ready", before));
            log.Add(new ChatLogLine(ChatLogChannel.Party, "(Claude) go", after));

            Assert.Contains("ready", ChatFile("Retail", "Asura", "Gemini", before));
            Assert.DoesNotContain("go", ChatFile("Retail", "Asura", "Gemini", before));
            Assert.Contains("go", ChatFile("Retail", "Asura", "Gemini", after));
        }

        [Fact]
        public void TwoCharacters_WriteSeparateFiles()
        {
            Start();
            var first = new StockUiChatLog();
            var second = new StockUiChatLog();
            using var a = new ChatLogFileWriter(first, new CharacterFolderKey("Retail", "Asura", "Gemini"));
            using var b = new ChatLogFileWriter(second, new CharacterFolderKey("Retail", "Asura", "Claude"));
            var day = new DateTime(2026, 11, 14, 12, 0, 0);

            first.Add(new ChatLogLine(ChatLogChannel.Say, "from gemini", day));
            second.Add(new ChatLogLine(ChatLogChannel.Say, "from claude", day));

            Assert.Contains("from gemini", ChatFile("Retail", "Asura", "Gemini", day));
            Assert.DoesNotContain("from claude", ChatFile("Retail", "Asura", "Gemini", day));
            Assert.Contains("from claude", ChatFile("Retail", "Asura", "Claude", day));
        }

        [Fact]
        public void ChatLogSettingOff_WritesNothing()
        {
            Start(chatLog: false);
            var log = new StockUiChatLog();
            using var writer = new ChatLogFileWriter(log, new CharacterFolderKey("Retail", "Asura", "Gemini"));
            var day = new DateTime(2026, 11, 14, 12, 0, 0);

            log.Add(new ChatLogLine(ChatLogChannel.Say, "not saved", day));

            Assert.Equal(string.Empty, ChatFile("Retail", "Asura", "Gemini", day));
            Assert.False(Directory.Exists(Path.Combine(_logs, GordianLog.ChatFolder)));
        }

        [Fact]
        public void Session_MovesOlderBuildsChatLogsIntoItsWorldFolder()
        {
            Start();
            string name = "Char" + Guid.NewGuid().ToString("N")[..8];
            string old = Path.Combine(_logs, GordianLog.ChatFolder, "127.0.0.1", name, "2026-11-14.log");
            Directory.CreateDirectory(Path.GetDirectoryName(old)!);
            File.WriteAllText(old, "[20:00:00] [Say] before the update\n");

            using var session = new Gordian.Engine.Sessions.CharacterSession(name, 1001, "account",
                new Gordian.Core.Network.SessionNetworkManager("127.0.0.1", 54230), serverName: "127.0.0.1", worldName: "Gordian");

            Assert.False(File.Exists(old));
            Assert.Contains("before the update", ChatFile("127.0.0.1", "Gordian", name, new DateTime(2026, 11, 14)));
        }

        [Fact]
        public void Dispose_StopsWriting()
        {
            Start();
            var log = new StockUiChatLog();
            var writer = new ChatLogFileWriter(log, new CharacterFolderKey("Retail", "Asura", "Gemini"));
            var day = new DateTime(2026, 11, 14, 12, 0, 0);

            writer.Dispose();
            log.Add(new ChatLogLine(ChatLogChannel.Say, "after logout", day));

            Assert.DoesNotContain("after logout", ChatFile("Retail", "Asura", "Gemini", day));
        }
    }
}
