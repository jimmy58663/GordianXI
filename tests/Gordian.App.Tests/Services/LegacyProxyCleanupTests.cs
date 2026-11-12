// tests/Gordian.App.Tests/Services/LegacyProxyCleanupTests.cs
using System;
using System.IO;
using Gordian.App.Services;
using Xunit;

namespace Gordian.App.Tests.Services
{
    /// <summary>The startup check that undoes what the removed proxy launch path could leave in the game folder.</summary>
    public sealed class LegacyProxyCleanupTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "GordianXI_ProxyCleanup_" + Guid.NewGuid().ToString("N"));

        public LegacyProxyCleanupTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        private string ClientDll => Path.Combine(_dir, LegacyProxyCleanup.ClientDllName);
        private string Backup => Path.Combine(_dir, LegacyProxyCleanup.BackupDllName);

        [Fact]
        public void AnOrphanedBackup_ReplacesTheProxy()
        {
            File.WriteAllText(ClientDll, "proxy");
            File.WriteAllText(Backup, "genuine");

            Assert.True(LegacyProxyCleanup.RestoreOrphanedBackup(_dir));
            Assert.Equal("genuine", File.ReadAllText(ClientDll));
            Assert.False(File.Exists(Backup));
        }

        [Fact]
        public void AnOrphanedBackup_IsRestoredWhenTheProxyIsGone()
        {
            File.WriteAllText(Backup, "genuine");

            Assert.True(LegacyProxyCleanup.RestoreOrphanedBackup(_dir));
            Assert.Equal("genuine", File.ReadAllText(ClientDll));
        }

        [Fact]
        public void ACleanInstall_IsLeftAlone()
        {
            File.WriteAllText(ClientDll, "genuine");

            Assert.False(LegacyProxyCleanup.RestoreOrphanedBackup(_dir));
            Assert.Equal("genuine", File.ReadAllText(ClientDll));
            Assert.False(LegacyProxyCleanup.RestoreOrphanedBackup(null));
            Assert.False(LegacyProxyCleanup.RestoreOrphanedBackup(Path.Combine(_dir, "missing")));
        }
    }
}
