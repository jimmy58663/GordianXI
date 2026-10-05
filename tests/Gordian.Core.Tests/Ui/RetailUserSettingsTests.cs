// tests/Gordian.Core.Tests/Ui/RetailUserSettingsTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    public class RetailUserSettingsTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        /// <summary>A fresh cnf.dat with the Log page's masks as a never-configured character has them.</summary>
        private static byte[] FreshCnf()
        {
            var cnf = StockUiFontColorsTests.FreshCnf();
            BinaryPrimitives.WriteUInt32LittleEndian(cnf.AsSpan(0x290), 0x00000000);
            BinaryPrimitives.WriteUInt32LittleEndian(cnf.AsSpan(0x294), 0xFFFFFFFF);
            BinaryPrimitives.WriteUInt32LittleEndian(cnf.AsSpan(0x298), 0xFFFFFFFF);
            BinaryPrimitives.WriteUInt32LittleEndian(cnf.AsSpan(0x29C), 0x00000000);
            return cnf;
        }

        [Fact]
        public void FreshRouting_IsTheDefault_AndAConfiguredOneMovesChatToWindow2()
        {
            var fresh = RetailCnf.Parse(FreshCnf());
            Assert.Equal(StockUiChatLog.DefaultWindow2Types, fresh.LogWindow2Types);

            // The maintainer's configured characters: 0x298 = ffffc020, 0x29C = 00001ffe.
            var cnf = FreshCnf();
            BinaryPrimitives.WriteUInt32LittleEndian(cnf.AsSpan(0x298), 0xFFFFC020);
            BinaryPrimitives.WriteUInt32LittleEndian(cnf.AsSpan(0x29C), 0x00001FFE);
            uint mask = RetailCnf.Parse(cnf).LogWindow2Types!.Value;
            Assert.NotEqual(0u, mask & StockUiChatLog.Bit(ChatLogType.Say));
            Assert.NotEqual(0u, mask & StockUiChatLog.Bit(ChatLogType.NpcConversation));
            Assert.NotEqual(0u, mask & StockUiChatLog.Bit(ChatLogType.SelfEvade));
            Assert.Equal(0u, mask & StockUiChatLog.Bit(ChatLogType.Yell));
            Assert.Equal(0u, mask & StockUiChatLog.Bit(ChatLogType.BasicSystem));
            Assert.Equal(0u, mask & StockUiChatLog.Bit(ChatLogType.SelfLose));

            Assert.Null(RetailCnf.Parse(cnf.AsSpan(0, 0x100)).LogWindow2Types);
        }

        [Fact]
        public void Apply_OverwritesTheColoursAndTheRouting()
        {
            var cnf = FreshCnf();
            cnf[0x50] = 0x00; cnf[0x51] = 0x00; cnf[0x52] = 0xFF;   // say: B 0, G 0, R 0xFF
            var settings = new StockUiSettings();
            settings.SetValue(StockUiSettingKey.LogWindow2Types, 0);
            var applied = RetailUserSettings.Apply(RetailCnf.Parse(cnf), settings);
            Assert.Equal(new[] { "28 font colors", "log window routing" }, applied);
            Assert.Equal(new StockUiRgb(0xFF, 0, 0), settings.GetFontColor(StockUiFontColorId.Say));
            Assert.True(settings.HasFontColor(StockUiFontColorId.Tell));
            Assert.Equal((int)StockUiChatLog.DefaultWindow2Types, settings.GetValue(StockUiSettingKey.LogWindow2Types));
        }

        [Fact]
        public void ImportCommand_ListsTheFolders_ThenImportsOne_ReadingOnly()
        {
            string game = Path.Combine(Path.GetTempPath(), "gordian_retail_" + Guid.NewGuid().ToString("N"));
            try
            {
                string mine = Path.Combine(game, RetailUserSettings.UserFolderName, "2a");
                string other = Path.Combine(game, RetailUserSettings.UserFolderName, "167dc34");
                Directory.CreateDirectory(mine);
                Directory.CreateDirectory(other);
                var cnf = FreshCnf();
                cnf[0x58] = 0x10;                                      // tell: B 0x10
                string cnfPath = Path.Combine(mine, RetailUserSettings.ConfigFileName);
                File.WriteAllBytes(cnfPath, cnf);
                var before = File.GetLastWriteTimeUtc(cnfPath);

                var settings = new StockUiSettings();
                var chat = new StockUiChat { GameDirectory = () => game, CharacterId = () => 0x2A, Settings = () => settings };
                Assert.False(chat.TryImportRetailCommand("/say hi"));
                Assert.True(chat.TryImportRetailCommand("/importretail"));
                var lines = new List<ChatLogLine>();
                chat.Log.CopyVisible(1, 10, lines);
                Assert.Contains(lines, l => l.Text.Contains("2a") && l.Text.Contains("this character"));
                Assert.Contains(lines, l => l.Text.Contains("167dc34") && l.Text.Contains("no cnf.dat"));

                Assert.True(chat.TryImportRetailCommand("/importretail nope"));
                chat.Log.CopyVisible(1, 1, lines);
                Assert.Equal(ChatLogChannel.Error, lines[0].Channel);

                Assert.True(chat.TryImportRetailCommand("/importretail 2a"));
                chat.Log.CopyVisible(1, 1, lines);
                Assert.Equal("Imported 28 font colors and log window routing from USER/2a.", lines[0].Text);
                Assert.Equal(0x10, settings.GetFontColor(StockUiFontColorId.Tell).B);
                Assert.Equal(before, File.GetLastWriteTimeUtc(cnfPath));  // never written

                Assert.Equal("2a", RetailUserSettings.FolderNameFor(42));
            }
            finally
            {
                if (Directory.Exists(game)) Directory.Delete(game, recursive: true);
            }
        }

        /// <summary>Against the maintainer's install: every cnf.dat parses, and the fresh characters' routing is the default.</summary>
        [Fact]
        public void RetailUserFolders_Parse()
        {
            if (!Directory.Exists(RetailUserSettings.UserDirectory(GameDirectory))) return;
            var folders = RetailUserSettings.ListFolders(GameDirectory);
            foreach (var folder in folders.Where(f => f.HasConfig))
            {
                var cnf = RetailUserSettings.ReadConfig(folder.Path);
                Assert.NotNull(cnf);
                Assert.Equal(StockUiFontColors.Entries.Count, cnf!.FontColors.Count);
                Assert.NotNull(cnf.LogWindow2Types);
                if (folder.Name is "3" or "4" or "5" or "6") Assert.Equal(StockUiChatLog.DefaultWindow2Types, cnf.LogWindow2Types);
            }
        }
    }
}
