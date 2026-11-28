// tests/Gordian.Engine.Tests/Network/CharacterConfigMigrationTests.cs
using System;
using System.IO;
using Gordian.Core.Config;
using Gordian.Core.World;
using Gordian.Engine.Macros;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Network
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class CharacterConfigMigrationCollection
    {
        /// <summary>Tests that point <see cref="GordianStorage.RootDataDirectory"/> at a scratch folder run alone.</summary>
        public const string Name = "CharacterConfigMigration";
    }

    /// <summary>
    /// Each per-character store takes an older build's flat file (<c>macros/&lt;name&gt;.json</c> and so on) into the
    /// character's <c>CharConfig/&lt;server&gt;/&lt;world&gt;/&lt;character&gt;/</c> folder when it first loads (#639).
    /// </summary>
    [Collection(CharacterConfigMigrationCollection.Name)]
    public sealed class CharacterConfigMigrationTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "GordianXI_CharConfigStores_" + Guid.NewGuid().ToString("N"));

        public CharacterConfigMigrationTests()
        {
            Directory.CreateDirectory(_root);
            GordianStorage.RootDataDirectory = _root;
        }

        public void Dispose()
        {
            GordianStorage.RootDataDirectory = null!;
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        /// <summary>A name no other test uses, so the stores' process-wide caches start empty for it.</summary>
        private static CharacterFolderKey NewKey() => new("127.0.0.1", "Gordian", "Char" + Guid.NewGuid().ToString("N")[..8]);

        private string Legacy(string folder, CharacterFolderKey key, string json)
        {
            string path = Path.Combine(_root, folder, key.Character + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
            return path;
        }

        /// <summary>Each store's file and how to load it.</summary>
        public static TheoryData<string, CharacterConfigFile, Func<CharacterFolderKey, object>> Stores => new()
        {
            { "macros", MacroStore.ConfigFile, key => MacroStore.GetForCharacter(key) },
            { "equipment sets", EquipmentSetBook.ConfigFile, key => EquipmentSetStore.GetForCharacter(key) },
            { "UI layout", StockUiLayout.ConfigFile, key => StockUiLayoutStore.GetForCharacter(key) },
            { "UI settings", StockUiSettings.ConfigFile, key => StockUiSettingsStore.GetForCharacter(key) },
            { "synthesis history", SynthesisHistory.ConfigFile, key => SynthesisHistoryStore.GetForCharacter(key) },
        };

        [Theory]
        [MemberData(nameof(Stores))]
        public void Store_MovesTheOldFileIntoTheCharacterFolder(string store, CharacterConfigFile file, Func<CharacterFolderKey, object> load)
        {
            var key = NewKey();
            string old = Legacy(file.LegacyFolder, key, "{}");

            Assert.NotNull(load(key));

            Assert.False(File.Exists(old), store);
            Assert.True(File.Exists(Path.Combine(_root, "CharConfig", "127.0.0.1", "Gordian", key.Character!, file.FileName)), store);
        }

        [Fact]
        public void MacroStore_LoadsTheMovedFile()
        {
            var key = NewKey();
            Legacy("macros", key, "{\"Book\":1,\"Set\":1,\"Books\":[{\"Number\":1,\"Title\":\"MigBook\",\"Sets\":[]}]}");

            var library = MacroStore.GetForCharacter(key);

            Assert.Equal("MigBook", library.GetStoredBookTitle(0));
        }
    }
}
