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

        private string Legacy(string folder, string name, string text = "{}")
        {
            string path = Path.Combine(_root, folder, name + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return path;
        }

        private string Expected(string server, string character, string file) =>
            Path.Combine(_root, CharacterConfigStorage.FolderName, server, character, file);

        [Fact]
        public void GetDirectory_IsCharConfigThenServerThenCharacter() =>
            Assert.Equal(Path.Combine(_root, "CharConfig", "127.0.0.1", "Akaden"), CharacterConfigStorage.GetDirectory(_root, "127.0.0.1", "Akaden"));

        [Fact]
        public void GetDirectory_EmptyNamesBecomeUnknown() =>
            Assert.Equal(Path.Combine(_root, "CharConfig", "Unknown", "Unknown"), CharacterConfigStorage.GetDirectory(_root, "", null));

        [Fact]
        public void GetDirectory_CleansUnsafeNames() =>
            Assert.Equal(Path.Combine(_root, "CharConfig", "host_1", "Akaden"), CharacterConfigStorage.GetDirectory(_root, " host:1 ", "Akaden"));

        [Fact]
        public void Resolve_WithNothingSaved_ReturnsTheNewPathAndCreatesNothing()
        {
            string path = CharacterConfigStorage.Resolve(_root, Macros, "Retail", "Akaden");

            Assert.Equal(Expected("Retail", "Akaden", "macros.json"), path);
            Assert.False(Directory.Exists(Path.Combine(_root, "CharConfig")));
        }

        [Fact]
        public void Resolve_MovesTheOldFileIntoTheCharacterFolder()
        {
            string old = Legacy("macros", "Akaden", "{\"books\":1}");

            string path = CharacterConfigStorage.Resolve(_root, Macros, "127.0.0.1", "Akaden");

            Assert.Equal(Expected("127.0.0.1", "Akaden", "macros.json"), path);
            Assert.Equal("{\"books\":1}", File.ReadAllText(path));
            Assert.False(File.Exists(old));
        }

        [Fact]
        public void Resolve_RemovesTheOldFolderOnceItIsEmpty()
        {
            Legacy("macros", "Akaden");

            CharacterConfigStorage.Resolve(_root, Macros, "Retail", "Akaden");

            Assert.False(Directory.Exists(Path.Combine(_root, "macros")));
        }

        [Fact]
        public void Resolve_KeepsTheOldFolderWhileOtherCharactersAreInIt()
        {
            string other = Legacy("macros", "Other");
            Legacy("macros", "Akaden");

            CharacterConfigStorage.Resolve(_root, Macros, "Retail", "Akaden");

            Assert.True(File.Exists(other));
        }

        [Fact]
        public void Resolve_LeavesTheOldFileWhenTheCharacterAlreadyHasOne()
        {
            string old = Legacy("macros", "Akaden", "old");
            string current = Expected("Retail", "Akaden", "macros.json");
            Directory.CreateDirectory(Path.GetDirectoryName(current)!);
            File.WriteAllText(current, "current");

            string path = CharacterConfigStorage.Resolve(_root, Macros, "Retail", "Akaden");

            Assert.Equal(current, path);
            Assert.Equal("current", File.ReadAllText(path));
            Assert.True(File.Exists(old));
        }

        [Fact]
        public void Resolve_TheSameNameOnASecondServerStartsEmpty()
        {
            Legacy("macros", "Akaden", "first");

            string first = CharacterConfigStorage.Resolve(_root, Macros, "Retail", "Akaden");
            string second = CharacterConfigStorage.Resolve(_root, Macros, "127.0.0.1", "Akaden");

            Assert.Equal("first", File.ReadAllText(first));
            Assert.Equal(Expected("127.0.0.1", "Akaden", "macros.json"), second);
            Assert.False(File.Exists(second));
        }

        [Fact]
        public void Resolve_WithNoCharacterName_TakesNoOldFile()
        {
            string old = Legacy("macros", "default");

            string path = CharacterConfigStorage.Resolve(_root, Macros, "Retail", "");

            Assert.Equal(Expected("Retail", "Unknown", "macros.json"), path);
            Assert.True(File.Exists(old));
        }

        [Fact]
        public void Resolve_TrimsTheNameAsOlderBuildsDid()
        {
            Legacy("macros", "Akaden", "trimmed");

            string path = CharacterConfigStorage.Resolve(_root, Macros, "Retail", " Akaden ");

            Assert.Equal("trimmed", File.ReadAllText(path));
        }

        [Fact]
        public void Resolve_WhenTheMoveFails_KeepsUsingTheOldFile()
        {
            string old = Legacy("macros", "Akaden", "kept");
            // A file where the server folder should be makes creating the character folder fail.
            Directory.CreateDirectory(Path.Combine(_root, "CharConfig"));
            File.WriteAllText(Path.Combine(_root, "CharConfig", "Retail"), "not a folder");

            string path = CharacterConfigStorage.Resolve(_root, Macros, "Retail", "Akaden");

            Assert.Equal(old, path);
            Assert.Equal("kept", File.ReadAllText(old));
        }
    }
}
