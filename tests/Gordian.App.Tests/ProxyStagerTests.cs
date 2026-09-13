// tests/Gordian.App.Tests/ProxyStagerTests.cs
using System;
using System.IO;
using Gordian.App.Services;
using Xunit;

namespace Gordian.App.Tests
{
    public class ProxyStagerTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _mockProxyFile;

        public ProxyStagerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "GordianXI_StagerTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            _mockProxyFile = Path.Combine(_tempDir, "MockProxy.dll");
            File.WriteAllText(_mockProxyFile, "MOCK_PROXY_CONTENT");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch { }
        }

        [Fact]
        public void StageProxy_BacksUpOriginalAndPlacesProxy()
        {
            // Arrange
            string originalDll = Path.Combine(_tempDir, ProxyStager.TargetDllName);
            File.WriteAllText(originalDll, "ORIGINAL_FFXI_CONTENT");

            // Act
            bool staged = ProxyStager.StageProxy(_tempDir, _mockProxyFile);

            // Assert
            Assert.True(staged);
            Assert.True(ProxyStager.IsStaged);

            string backupDll = Path.Combine(_tempDir, ProxyStager.BackupDllName);
            Assert.True(File.Exists(backupDll), "Backup file FFXiMain.dll.orig must exist.");
            Assert.Equal("ORIGINAL_FFXI_CONTENT", File.ReadAllText(backupDll));

            Assert.True(File.Exists(originalDll), "Target FFXiMain.dll must exist (proxy).");
            Assert.Equal("MOCK_PROXY_CONTENT", File.ReadAllText(originalDll));
        }

        [Fact]
        public void RestoreOriginal_RestoresBackupAndRemovesProxy()
        {
            // Arrange
            string originalDll = Path.Combine(_tempDir, ProxyStager.TargetDllName);
            File.WriteAllText(originalDll, "ORIGINAL_FFXI_CONTENT");
            ProxyStager.StageProxy(_tempDir, _mockProxyFile);

            // Act
            bool restored = ProxyStager.RestoreOriginal(_tempDir);

            // Assert
            Assert.True(restored);
            Assert.False(ProxyStager.IsStaged);

            string backupDll = Path.Combine(_tempDir, ProxyStager.BackupDllName);
            Assert.False(File.Exists(backupDll), "Backup file FFXiMain.dll.orig should no longer exist.");

            Assert.True(File.Exists(originalDll), "FFXiMain.dll must exist.");
            Assert.Equal("ORIGINAL_FFXI_CONTENT", File.ReadAllText(originalDll));
        }

        [Fact]
        public void SelfHealStartup_RecoversOrphanedBackup()
        {
            // Arrange: simulate a crashed session where FFXiMain.dll.orig is orphaned
            string targetDll = Path.Combine(_tempDir, ProxyStager.TargetDllName);
            string backupDll = Path.Combine(_tempDir, ProxyStager.BackupDllName);

            File.WriteAllText(targetDll, "MOCK_PROXY_LEFTOVER");
            File.WriteAllText(backupDll, "GENUINE_ORIGINAL_CONTENT");

            // Act
            bool healed = ProxyStager.SelfHealStartup(_tempDir);

            // Assert
            Assert.True(healed);
            Assert.False(File.Exists(backupDll));
            Assert.True(File.Exists(targetDll));
            Assert.Equal("GENUINE_ORIGINAL_CONTENT", File.ReadAllText(targetDll));
        }

        [Fact]
        public void StageProxy_FailsGracefullyOnInvalidDirectory()
        {
            bool staged = ProxyStager.StageProxy("Z:\\NonExistentPath_12345", _mockProxyFile);
            Assert.False(staged);
        }

        [Fact]
        public void StageProxy_FailsGracefullyOnMissingProxyFile()
        {
            string originalDll = Path.Combine(_tempDir, ProxyStager.TargetDllName);
            File.WriteAllText(originalDll, "ORIGINAL_CONTENT");

            bool staged = ProxyStager.StageProxy(_tempDir, "Z:\\NonExistentProxy.dll");
            Assert.False(staged);
        }

        [Fact]
        public void DetectGameDirectory_OnWindows_ReturnsValidDirectoryOrNull()
        {
            if (OperatingSystem.IsWindows())
            {
                string? detected = GameDirectoryDetector.DetectGameDirectory();
                if (detected != null)
                {
                    Assert.True(Directory.Exists(detected));
                    string dllPath = Path.Combine(detected, ProxyStager.TargetDllName);
                    string origPath = Path.Combine(detected, ProxyStager.BackupDllName);
                    Assert.True(File.Exists(dllPath) || File.Exists(origPath));
                }
            }
        }
    }
}
