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

        [Theory]
        [InlineData("macros", "macros.json")]
        [InlineData("equipment_sets", "equipment_sets.json")]
        [InlineData("ui_layouts", "ui_layout.json")]
        [InlineData("ui_settings", "ui_settings.json")]
        [InlineData("synthesis_history", "synthesis_history.json")]
        public void Store_MovesTheOldFileIntoTheCharacterFolder(string legacyFolder, string fileName)
        {
            var key = NewKey();
            string old = Legacy(legacyFolder, key, "{}");

            _ = legacyFolder switch
            {
                "macros" => (object)MacroStore.GetForCharacter(key),
                "equipment_sets" => EquipmentSetStore.GetForCharacter(key),
                "ui_layouts" => StockUiLayoutStore.GetForCharacter(key),
                "ui_settings" => StockUiSettingsStore.GetForCharacter(key),
                _ => SynthesisHistoryStore.GetForCharacter(key),
            };

            Assert.False(File.Exists(old));
            Assert.True(File.Exists(Path.Combine(_root, "CharConfig", "127.0.0.1", "Gordian", key.Character!, fileName)));
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
