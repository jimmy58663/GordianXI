// tests/Gordian.Core.Tests/Resources/VfsHotReloadTests.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Resources.Vfs;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>
    /// The VFS hot reload for the edits users make (#374): a file added to, replaced in or deleted from a mounted pack,
    /// a pack folder copied in, and <c>vfs.json</c> saved with a pack switched on. Each one re-mounts the packs and
    /// clears the resource caches, so the next read sees the new file. Real file system watchers on a temp folder.
    /// </summary>
    public sealed class VfsHotReloadTests : IDisposable
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
        private readonly string _root;
        private readonly string _game;
        private readonly string _resources;
        private readonly string _dats;

        public VfsHotReloadTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "gordian_vfs_hot_" + Guid.NewGuid().ToString("N"));
            _game = Path.Combine(_root, "game");
            _resources = Path.Combine(_root, "resources");
            _dats = Path.Combine(_resources, "dats");
            Directory.CreateDirectory(Path.Combine(_game, "ROM", "1"));
            Directory.CreateDirectory(_dats);
            Directory.CreateDirectory(Path.Combine(_resources, "assets"));
            File.WriteAllText(Path.Combine(_game, "ROM", "1", "1.DAT"), "base");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void WriteConfig(params (string Id, bool Enabled)[] packs)
        {
            var entries = new System.Text.StringBuilder();
            for (int i = 0; i < packs.Length; i++)
            {
                if (i > 0) entries.Append(',');
                entries.Append($"{{ \"id\": \"{packs[i].Id}\", \"enabled\": {(packs[i].Enabled ? "true" : "false")}, \"priority\": {100 - i} }}");
            }
            File.WriteAllText(Path.Combine(_resources, VirtualFileSystem.ConfigFileName),
                $"{{ \"version\": 1, \"enabled\": true, \"hotReload\": true, \"dats\": [ {entries} ], \"assets\": [] }}");
        }

        private static string ReadResolved(VirtualFileSystem vfs) =>
            vfs.TryResolveDat("ROM/1/1.DAT", out var asset) && asset != null ? asset.ReadAllText() : "<missing>";

        private static async Task<bool> WaitUntilAsync(Func<bool> condition)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < Timeout)
            {
                try
                {
                    if (condition()) return true;
                }
                catch (IOException)
                {
                    // A file still being written; look again.
                }
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
            return condition();
        }

        [Fact]
        public async Task FileAddedReplacedAndDeletedInAMountedPack_IsSeenWithoutARestart()
        {
            string pack = Path.Combine(_dats, "Pack", "ROM", "1");
            Directory.CreateDirectory(Path.Combine(_dats, "Pack", "ROM", "2"));
            File.WriteAllText(Path.Combine(_dats, "Pack", "ROM", "2", "2.DAT"), "other");
            WriteConfig(("Pack", true));

            using var vfs = new VirtualFileSystem(_game, _resources);
            int reloads = 0;
            vfs.OnReloaded += () => Interlocked.Increment(ref reloads);
            Assert.Equal("base", ReadResolved(vfs));

            // Added: the pack's index (built when it was mounted) must learn the new file.
            Directory.CreateDirectory(pack);
            File.WriteAllText(Path.Combine(pack, "1.DAT"), "added");
            Assert.True(await WaitUntilAsync(() => ReadResolved(vfs) == "added"), "added file not resolved");

            // Replaced in place (copy over it): a reload is raised so the caches drop the old bytes.
            int before = Volatile.Read(ref reloads);
            File.Copy(Path.Combine(_dats, "Pack", "ROM", "2", "2.DAT"), Path.Combine(pack, "1.DAT"), overwrite: true);
            Assert.True(await WaitUntilAsync(() => Volatile.Read(ref reloads) > before), "no reload after a replace");
            Assert.Equal("other", ReadResolved(vfs));

            // Deleted: back to the game's own file.
            File.Delete(Path.Combine(pack, "1.DAT"));
            Assert.True(await WaitUntilAsync(() => ReadResolved(vfs) == "base"), "deleted file still resolved");
        }

        [Fact]
        public async Task PackFolderCopiedIn_IsDiscovered_AndSwitchingItOnInVfsJsonTakesEffect()
        {
            WriteConfig();
            using var vfs = new VirtualFileSystem(_game, _resources);
            Assert.Equal("base", ReadResolved(vfs));

            // Copy a whole pack folder in (made elsewhere, then moved in, as a file manager does).
            string staging = Path.Combine(_root, "staging", "NewPack", "ROM", "1");
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "1.DAT"), "new pack");
            Directory.Move(Path.Combine(_root, "staging", "NewPack"), Path.Combine(_dats, "NewPack"));

            // New packs are discovered switched off (vfs.json gains the entry) ...
            string configPath = Path.Combine(_resources, VirtualFileSystem.ConfigFileName);
            Assert.True(await WaitUntilAsync(() => File.ReadAllText(configPath).Contains("NewPack")), "pack not discovered");
            Assert.Equal("base", ReadResolved(vfs));

            // ... and saving vfs.json with it switched on mounts it.
            WriteConfig(("NewPack", true));
            Assert.True(await WaitUntilAsync(() => ReadResolved(vfs) == "new pack"), "switching the pack on in vfs.json had no effect");
            Assert.Contains(vfs.ActiveDatPacks, p => p.Id == "NewPack");
        }

        [Fact]
        public void Reload_AdvancesTheResourceAndTableGenerations()
        {
            WriteConfig();
            var resources = new ResourceManager(_game, _resources);
            int generation = resources.CacheGeneration;
            int tables = ZoneDatLoader.Generation;

            resources.Vfs.Reload(); // what /reloaddats does

            Assert.True(resources.CacheGeneration > generation);
            Assert.True(ZoneDatLoader.Generation > tables);
            resources.Vfs.Dispose();
        }

        [Fact]
        public void ReloadDatsCommand_IsAClientCommand()
        {
            var parsed = Gordian.Core.Network.ChatCommandRouter.Parse("/reloaddats");
            Assert.Equal(Gordian.Core.Network.ChatCommandResultKind.ReloadDats, parsed.Kind);
            Assert.Equal(Gordian.Core.Network.ChatCommandResultKind.ReloadDats, Gordian.Core.Network.ChatCommandRouter.Parse("/reloaddat").Kind);
        }

        [Fact]
        public void ClientMessageTables_AreReadAgainAfterAReload()
        {
            int reads = 0;
            var tables = new ClientMessageTables(_ =>
            {
                Interlocked.Increment(ref reads);
                return null;
            });
            _ = tables.BattleMessages;
            int before = Volatile.Read(ref reads);
            Assert.True(before >= 1);

            // (Other tests may clear caches meanwhile; the point is that a clear always reads again.)
            ZoneDatLoader.Invalidate();
            _ = tables.BattleMessages;
            Assert.True(Volatile.Read(ref reads) > before);
        }
    }
}
