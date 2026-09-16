// tests/Gordian.Core.Tests/Resources/VirtualFileSystemTests.cs
using System;
using System.IO;
using System.Text;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Vfs;
using Gordian.Core.Resources.Vfs.Models;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class VirtualFileSystemTests : IDisposable
    {
        private readonly string _tempRoot;
        private readonly string _baseGameDir;
        private readonly string _resourcesDir;
        private readonly string _datsDir;
        private readonly string _assetsDir;

        public VirtualFileSystemTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "gordian_vfs_test_" + Guid.NewGuid().ToString("N"));
            _baseGameDir = Path.Combine(_tempRoot, "game");
            _resourcesDir = Path.Combine(_tempRoot, "resources");
            _datsDir = Path.Combine(_resourcesDir, "dats");
            _assetsDir = Path.Combine(_resourcesDir, "assets");

            Directory.CreateDirectory(_baseGameDir);
            Directory.CreateDirectory(_datsDir);
            Directory.CreateDirectory(_assetsDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempRoot))
                {
                    Directory.Delete(_tempRoot, recursive: true);
                }
            }
            catch { }
        }

        [Fact]
        public void VfsPathNormalizer_NormalizesSlashesAndUppercase()
        {
            Assert.Equal("ROM/118/106.DAT", VfsPathNormalizer.Normalize("rom\\118\\106.dat"));
            Assert.Equal("ROM/118/106.DAT", VfsPathNormalizer.Normalize("/ROM/118/106.DAT/"));
            Assert.Equal("ROM/2/41.DAT", VfsPathNormalizer.Normalize("./rom/2/41.dat"));
            Assert.Equal("MODELS/BODY.GLB", VfsPathNormalizer.Normalize("models\\body.glb"));
            Assert.Equal(string.Empty, VfsPathNormalizer.Normalize("   "));
            Assert.Equal(string.Empty, VfsPathNormalizer.Normalize(null));
        }

        [Fact]
        public void VirtualFileSystem_PriorityStacking_HigherPriorityWins()
        {
            // 1. Setup base game file
            string baseDatDir = Path.Combine(_baseGameDir, "ROM", "118");
            Directory.CreateDirectory(baseDatDir);
            File.WriteAllText(Path.Combine(baseDatDir, "106.DAT"), "BaseGame");

            // 2. Setup PackA (Priority 100)
            string packADir = Path.Combine(_datsDir, "PackA", "ROM", "118");
            Directory.CreateDirectory(packADir);
            File.WriteAllText(Path.Combine(packADir, "106.DAT"), "PackA_Content");

            // 3. Setup PackB (Priority 50)
            string packBDir = Path.Combine(_datsDir, "PackB", "ROM", "118");
            Directory.CreateDirectory(packBDir);
            File.WriteAllText(Path.Combine(packBDir, "106.DAT"), "PackB_Content");

            // 4. Create vfs.json
            string vfsJson = """
            {
              "version": 1,
              "enabled": true,
              "hotReload": false,
              "dats": [
                { "id": "PackA", "enabled": true, "priority": 100 },
                { "id": "PackB", "enabled": true, "priority": 50 }
              ]
            }
            """;
            File.WriteAllText(Path.Combine(_resourcesDir, "vfs.json"), vfsJson);

            using var vfs = new VirtualFileSystem();
            vfs.Initialize(_baseGameDir, _resourcesDir);

            // Assert PackA is chosen over PackB and BaseGame
            bool resolved = vfs.TryResolveDat("ROM/118/106.DAT", out var asset);
            Assert.True(resolved);
            Assert.NotNull(asset);
            Assert.Equal("PackA", asset.SourcePack);
            Assert.True(asset.IsOverride);
            Assert.Equal("PackA_Content", asset.ReadAllText());
        }

        [Fact]
        public void VirtualFileSystem_PackDisabled_FallsBackToLowerPriorityOrBase()
        {
            // 1. Setup base game file
            string baseDatDir = Path.Combine(_baseGameDir, "ROM", "118");
            Directory.CreateDirectory(baseDatDir);
            File.WriteAllText(Path.Combine(baseDatDir, "106.DAT"), "BaseGame");

            // 2. Setup PackA (disabled) & PackB (enabled)
            string packADir = Path.Combine(_datsDir, "PackA", "ROM", "118");
            Directory.CreateDirectory(packADir);
            File.WriteAllText(Path.Combine(packADir, "106.DAT"), "PackA_Content");

            string packBDir = Path.Combine(_datsDir, "PackB", "ROM", "118");
            Directory.CreateDirectory(packBDir);
            File.WriteAllText(Path.Combine(packBDir, "106.DAT"), "PackB_Content");

            string vfsJson = """
            {
              "version": 1,
              "enabled": true,
              "hotReload": false,
              "dats": [
                { "id": "PackA", "enabled": false, "priority": 100 },
                { "id": "PackB", "enabled": true, "priority": 50 }
              ]
            }
            """;
            File.WriteAllText(Path.Combine(_resourcesDir, "vfs.json"), vfsJson);

            using var vfs = new VirtualFileSystem(_baseGameDir, _resourcesDir);

            // Since PackA is disabled, PackB should resolve
            bool resolvedB = vfs.TryResolveDat("ROM/118/106.DAT", out var assetB);
            Assert.True(resolvedB);
            Assert.NotNull(assetB);
            Assert.Equal("PackB", assetB.SourcePack);
            Assert.Equal("PackB_Content", assetB.ReadAllText());

            // Disable all VFS
            vfs.IsEnabled = false;
            bool resolvedBase = vfs.TryResolveDat("ROM/118/106.DAT", out var assetBase);
            Assert.True(resolvedBase);
            Assert.NotNull(assetBase);
            Assert.Equal("BaseGame", assetBase.SourcePack);
            Assert.False(assetBase.IsOverride);
            Assert.Equal("BaseGame", assetBase.ReadAllText());
        }

        [Fact]
        public void VirtualFileSystem_CrossPlatformCasing_MatchesNormalizedKey()
        {
            // Mod on disk has lowercase folder and filename
            string packDir = Path.Combine(_datsDir, "LowerPack", "rom", "118");
            Directory.CreateDirectory(packDir);
            File.WriteAllText(Path.Combine(packDir, "106.dat"), "CaseInsensitiveData");

            string vfsJson = """
            {
              "version": 1,
              "enabled": true,
              "hotReload": false,
              "dats": [
                { "id": "LowerPack", "enabled": true, "priority": 50 }
              ]
            }
            """;
            File.WriteAllText(Path.Combine(_resourcesDir, "vfs.json"), vfsJson);

            using var vfs = new VirtualFileSystem(_baseGameDir, _resourcesDir);

            // Query using uppercase standard
            Assert.True(vfs.TryResolveDat("ROM/118/106.DAT", out var asset1));
            Assert.Equal("CaseInsensitiveData", asset1!.ReadAllText());

            // Query using mixed case and backslashes
            Assert.True(vfs.TryResolveDat("Rom\\118\\106.dat", out var asset2));
            Assert.Equal("CaseInsensitiveData", asset2!.ReadAllText());
        }

        [Fact]
        public void VirtualFileSystem_ModernAssetPack_DatAlias_InterceptsDatLookup()
        {
            // Create a modern pack with manifest.json declaring a datAlias
            string packDir = Path.Combine(_assetsDir, "ModernPBR");
            string modelsDir = Path.Combine(packDir, "models", "characters");
            Directory.CreateDirectory(modelsDir);

            string glbPath = Path.Combine(modelsDir, "haubergeon.glb");
            File.WriteAllBytes(glbPath, Encoding.UTF8.GetBytes("GLTF_BINARY_MOCK_DATA"));

            string manifestJson = """
            {
              "id": "ModernPBR",
              "name": "Modern PBR Armor",
              "version": "1.0.0",
              "overrides": {
                "datAliases": {
                  "ROM/28/52.DAT": "models/characters/haubergeon.glb"
                },
                "items": {
                  "12502": {
                    "model": "models/characters/haubergeon.glb",
                    "materialVariant": "silver"
                  }
                }
              }
            }
            """;
            File.WriteAllText(Path.Combine(packDir, "manifest.json"), manifestJson);

            string vfsJson = """
            {
              "version": 1,
              "enabled": true,
              "hotReload": false,
              "assets": [
                { "id": "ModernPBR", "enabled": true, "priority": 100 }
              ]
            }
            """;
            File.WriteAllText(Path.Combine(_resourcesDir, "vfs.json"), vfsJson);

            using var vfs = new VirtualFileSystem(_baseGameDir, _resourcesDir);

            // Legacy DAT lookup for ROM/28/52.DAT should be intercepted and resolve the modern .glb model!
            bool resolved = vfs.TryResolveDat("ROM/28/52.DAT", out var asset);
            Assert.True(resolved);
            Assert.NotNull(asset);
            Assert.Equal("ModernPBR", asset.SourcePack);
            Assert.Equal(AssetCategory.Model, asset.Category);
            Assert.True(asset.IsOverride);
            Assert.EndsWith("haubergeon.glb", asset.PhysicalPath);

            // Item override lookup
            bool itemResolved = vfs.TryResolveItemModel(12502, out var itemAsset, out string? variant);
            Assert.True(itemResolved);
            Assert.NotNull(itemAsset);
            Assert.Equal("silver", variant);
            Assert.Equal(AssetCategory.Model, itemAsset.Category);
        }

        [Fact]
        public void VirtualFileSystem_AutoDiscovery_AddsNewPacksAsDisabled()
        {
            // Initial vfs.json with existing pack
            string vfsJson = """
            {
              "version": 1,
              "enabled": true,
              "hotReload": false,
              "dats": [
                { "id": "ExistingPack", "enabled": true, "priority": 100 }
              ]
            }
            """;
            File.WriteAllText(Path.Combine(_resourcesDir, "vfs.json"), vfsJson);
            Directory.CreateDirectory(Path.Combine(_datsDir, "ExistingPack"));

            // Add new pack folder on disk
            Directory.CreateDirectory(Path.Combine(_datsDir, "NewlyDiscoveredPack"));
            Directory.CreateDirectory(Path.Combine(_assetsDir, "NewAssetPack"));

            using var vfs = new VirtualFileSystem(_baseGameDir, _resourcesDir);

            // Reload config from disk to check auto-discovery
            var updatedConfig = VfsConfig.LoadOrCreate(Path.Combine(_resourcesDir, "vfs.json"));

            Assert.Contains(updatedConfig.Dats, d => d.Id == "NewlyDiscoveredPack" && !d.Enabled);
            Assert.Contains(updatedConfig.Assets, a => a.Id == "NewAssetPack" && !a.Enabled);
            Assert.Contains(updatedConfig.Dats, d => d.Id == "ExistingPack" && d.Enabled);
        }

        [Fact]
        public void ResourceManager_IntegratesVfs_ResolvesOverrides()
        {
            // 1. Setup base game FTABLE/VTABLE
            byte[] ftable = new byte[4];
            byte[] vtable = new byte[2];
            vtable[1] = 1; // ROM root, file 0 => ROM/0/0.DAT
            File.WriteAllBytes(Path.Combine(_baseGameDir, "FTABLE.DAT"), ftable);
            File.WriteAllBytes(Path.Combine(_baseGameDir, "VTABLE.DAT"), vtable);

            // Base game ROM/0/0.DAT
            string baseRom0 = Path.Combine(_baseGameDir, "ROM", "0");
            Directory.CreateDirectory(baseRom0);
            File.WriteAllText(Path.Combine(baseRom0, "0.DAT"), "BaseGame_0_DAT");

            // 2. Mod pack overriding ROM/0/0.DAT
            string modRom0 = Path.Combine(_datsDir, "UIPack", "ROM", "0");
            Directory.CreateDirectory(modRom0);
            File.WriteAllText(Path.Combine(modRom0, "0.DAT"), "Modded_0_DAT");

            string vfsJson = """
            {
              "version": 1,
              "enabled": true,
              "hotReload": false,
              "dats": [
                { "id": "UIPack", "enabled": true, "priority": 100 }
              ]
            }
            """;
            File.WriteAllText(Path.Combine(_resourcesDir, "vfs.json"), vfsJson);

            var resourceManager = new ResourceManager(_baseGameDir, _resourcesDir);
            Assert.True(resourceManager.InitializeFileTable());

            // TryResolveFile for FileId 1 should resolve to the mod pack physical path!
            Assert.True(resourceManager.TryResolveFile(1, out string resolvedPath));
            Assert.Contains("UIPack", resolvedPath);
            Assert.Equal("Modded_0_DAT", File.ReadAllText(resolvedPath));
        }
    }
}
