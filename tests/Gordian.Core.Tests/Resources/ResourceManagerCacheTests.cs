// tests/Gordian.Core.Tests/Resources/ResourceManagerCacheTests.cs
using System;
using System.Buffers.Binary;
using System.IO;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Vfs;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class ResourceManagerCacheTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "gordian-rm-cache-" + Guid.NewGuid().ToString("N"));
        private readonly string _gameDir;
        private readonly string _resourcesDir;

        public ResourceManagerCacheTests()
        {
            _gameDir = Path.Combine(_root, "game");
            _resourcesDir = Path.Combine(_root, "resources");
            Directory.CreateDirectory(_gameDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        }

        /// <summary>Writes a one-entry FTABLE/VTABLE pair: file id 0 -> ROM/{subDir}/{fileNum}.DAT.</summary>
        private void WriteFileTable(ushort subDir, byte fileNum)
        {
            byte[] ftable = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(ftable, (ushort)((subDir << 7) | fileNum));
            File.WriteAllBytes(Path.Combine(_gameDir, "FTABLE.DAT"), ftable);
            File.WriteAllBytes(Path.Combine(_gameDir, "VTABLE.DAT"), new byte[] { 1 });
        }

        [Fact]
        public void ClearCache_AdvancesCacheGeneration()
        {
            var rm = new ResourceManager(_gameDir, _resourcesDir);
            int before = rm.CacheGeneration;

            rm.ClearCache();
            int afterOne = rm.CacheGeneration;
            rm.ClearCache();

            Assert.NotEqual(before, afterOne);
            Assert.NotEqual(afterOne, rm.CacheGeneration);
        }

        [Fact]
        public void ClearCache_RebuildsALoadedFileTable()
        {
            WriteFileTable(subDir: 10, fileNum: 3);
            var rm = new ResourceManager(_gameDir, _resourcesDir);
            Assert.True(rm.InitializeFileTable());

            WriteFileTable(subDir: 20, fileNum: 5);
            rm.ClearCache();

            Assert.True(rm.FileTable.TryResolve(0, out string path));
            Assert.Equal(Path.Combine("ROM", "20", "5.DAT"), path);
        }

        [Fact]
        public void ClearCache_KeepsTheFileTableWhenItCannotBeRebuilt()
        {
            WriteFileTable(subDir: 10, fileNum: 3);
            var rm = new ResourceManager(_gameDir, _resourcesDir);
            Assert.True(rm.InitializeFileTable());

            File.Delete(Path.Combine(_gameDir, "FTABLE.DAT"));
            rm.ClearCache();

            Assert.True(rm.FileTable.TryResolve(0, out string path));
            Assert.Equal(Path.Combine("ROM", "10", "3.DAT"), path);
        }

        [Fact]
        public void VfsReload_ClearsTheResourceCaches()
        {
            var vfs = new VirtualFileSystem(_gameDir, _resourcesDir);
            try
            {
                var rm = new ResourceManager(_gameDir, _resourcesDir, vfs);
                int before = rm.CacheGeneration;

                vfs.Reload();

                Assert.NotEqual(before, rm.CacheGeneration);
            }
            finally
            {
                vfs.Dispose();
            }
        }
    }
}
