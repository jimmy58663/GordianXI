// tests/Gordian.Core.Tests/Config/CharacterConfigStorageTests.cs
using System;
using System.IO;
using Gordian.Core.Config;
using Xunit;

namespace Gordian.Core.Tests.Config
{
    /// <summary>The per-character config folders and the move of older builds' flat files into them (#639).</summary>
    public sealed class CharacterConfigStorageTests : IDisposable
    {
        private static readonly CharacterConfigFile Macros = new("macros.json", "macros");

        private readonly string _root = Path.Combine(Path.GetTempPath(), "GordianXI_CharConfig_" + Guid.NewGuid().ToString("N"));

        public CharacterConfigStorageTests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private static CharacterFolderKey Key(string? server, string? world, string? character) => new(server, world, character);

        private string Legacy(string folder, string name, string text = "{}")
        {
            string path = Path.Combine(_root, folder, name + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return path;
        }

        private string Expected(string server, string world, string character, string file) =>
            Path.Combine(_root, CharacterConfigStorage.FolderName, server, world, character, file);

        [Fact]
        public void GetDirectory_IsCharConfigThenServerWorldAndCharacter() =>
            Assert.Equal(Path.Combine(_root, "CharConfig", "127.0.0.1", "Gordian", "Akaden"),
                CharacterConfigStorage.GetDirectory(_root, Key("127.0.0.1", "Gordian", "Akaden")));

        [Fact]
        public void GetDirectory_EmptyNamesBecomeUnknown() =>
            Assert.Equal(Path.Combine(_root, "CharConfig", "Unknown", "Unknown", "Unknown"), CharacterConfigStorage.GetDirectory(_root, Key("", null, " ")));

        [Fact]
        public void GetDirectory_CleansUnsafeNames() =>
            Assert.Equal(Path.Combine(_root, "CharConfig", "host_1", "Asura", "Akaden"),
                CharacterConfigStorage.GetDirectory(_root, Key(" host:1 ", "Asura", "Akaden")));

        [Fact]
        public void GetDirectory_TheSameNameOnTwoWorlds_IsTwoFolders() =>
            Assert.NotEqual(CharacterConfigStorage.GetDirectory(_root, Key("Retail", "Asura", "Akaden")),
                CharacterConfigStorage.GetDirectory(_root, Key("Retail", "Shiva", "Akaden")));

        [Fact]
        public void Resolve_WithNothingSaved_ReturnsTheNewPathAndCreatesNothing()
        {
            string path = CharacterConfigStorage.Resolve(_root, Macros, Key("Retail", "Asura", "Akaden"));

            Assert.Equal(Expected("Retail", "Asura", "Akaden", "macros.json"), path);
            Assert.False(Directory.Exists(Path.Combine(_root, "CharConfig")));
        }

        [Fact]
        public void Resolve_MovesTheOldFileIntoTheCharacterFolder()
        {
            string old = Legacy("macros", "Akaden", "{\"books\":1}");

            string path = CharacterConfigStorage.Resolve(_root, Macros, Key("127.0.0.1", "Gordian", "Akaden"));

            Assert.Equal(Expected("127.0.0.1", "Gordian", "Akaden", "macros.json"), path);
            Assert.Equal("{\"books\":1}", File.ReadAllText(path));
            Assert.False(File.Exists(old));
        }

        [Fact]
        public void Resolve_RemovesTheOldFolderOnceItIsEmpty()
        {
            Legacy("macros", "Akaden");

            CharacterConfigStorage.Resolve(_root, Macros, Key("Retail", "Asura", "Akaden"));

            Assert.False(Directory.Exists(Path.Combine(_root, "macros")));
        }

        [Fact]
        public void Resolve_KeepsTheOldFolderWhileOtherCharactersAreInIt()
        {
            string other = Legacy("macros", "Other");
            Legacy("macros", "Akaden");

            CharacterConfigStorage.Resolve(_root, Macros, Key("Retail", "Asura", "Akaden"));

            Assert.True(File.Exists(other));
        }

        [Fact]
        public void Resolve_LeavesTheOldFileWhenTheCharacterAlreadyHasOne()
        {
            string old = Legacy("macros", "Akaden", "old");
            string current = Expected("Retail", "Asura", "Akaden", "macros.json");
            Directory.CreateDirectory(Path.GetDirectoryName(current)!);
            File.WriteAllText(current, "current");

            string path = CharacterConfigStorage.Resolve(_root, Macros, Key("Retail", "Asura", "Akaden"));

            Assert.Equal(current, path);
            Assert.Equal("current", File.ReadAllText(path));
            Assert.True(File.Exists(old));
        }

        [Fact]
        public void Resolve_TheSameNameElsewhereStartsEmpty()
        {
            Legacy("macros", "Akaden", "first");

            string first = CharacterConfigStorage.Resolve(_root, Macros, Key("Retail", "Asura", "Akaden"));
            string otherWorld = CharacterConfigStorage.Resolve(_root, Macros, Key("Retail", "Shiva", "Akaden"));
            string otherServer = CharacterConfigStorage.Resolve(_root, Macros, Key("127.0.0.1", "Gordian", "Akaden"));

            Assert.Equal("first", File.ReadAllText(first));
            Assert.False(File.Exists(otherWorld));
            Assert.False(File.Exists(otherServer));
        }

        [Fact]
        public void Resolve_WithNoCharacterName_TakesNoOldFile()
        {
            string old = Legacy("macros", "default");

            string path = CharacterConfigStorage.Resolve(_root, Macros, Key("Retail", "Asura", ""));

            Assert.Equal(Expected("Retail", "Asura", "Unknown", "macros.json"), path);
            Assert.True(File.Exists(old));
        }

        [Fact]
        public void Resolve_TrimsTheNameAsOlderBuildsDid()
        {
            Legacy("macros", "Akaden", "trimmed");

            string path = CharacterConfigStorage.Resolve(_root, Macros, Key("Retail", "Asura", " Akaden "));

            Assert.Equal("trimmed", File.ReadAllText(path));
        }

        [Fact]
        public void Resolve_TakesAnOldFileNamedAsTyped()
        {
            // Older builds named a character Con's file "Con.json"; its folder is "Con_" (a Windows device name).
            string old = Legacy("macros", "Con", "con");

            string path = CharacterConfigStorage.Resolve(_root, Macros, Key("Retail", "Asura", "Con"));

            Assert.Equal(Expected("Retail", "Asura", "Con_", "macros.json"), path);
            Assert.Equal("con", File.ReadAllText(path));
            Assert.False(File.Exists(old));
        }

        [Fact]
        public void Resolve_WhenTheMoveFails_KeepsUsingTheOldFile()
        {
            string old = Legacy("macros", "Akaden", "kept");
            // A file where the server folder should be makes creating the character folder fail.
            Directory.CreateDirectory(Path.Combine(_root, "CharConfig"));
            File.WriteAllText(Path.Combine(_root, "CharConfig", "Retail"), "not a folder");

            string path = CharacterConfigStorage.Resolve(_root, Macros, Key("Retail", "Asura", "Akaden"));

            Assert.Equal(old, path);
            Assert.Equal("kept", File.ReadAllText(old));
        }
    }
}
