// tests/Gordian.App.Tests/Services/AppResourceManagerSwitchTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Gordian.App.Services;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.App.Tests.Services
{
    /// <summary>
    /// Switching FFXI installs without a restart (#665): one install at a time, switched only while no viewport window,
    /// character or launch uses the loaded one. Two fake installs map file id 0 to different DATs, so what the loaded
    /// manager reads shows which install it is.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class InstallSwitchCollection
    {
        public const string Name = "InstallSwitch";
    }

    // AppResourceManager, ItemNameResolver and GameDirectoryDetector are process-wide, and any test that opens a viewport
    // reaches them, so these run alone (a collection without parallelization runs after every other one).
    [Collection(InstallSwitchCollection.Name)]
    public sealed class AppResourceManagerSwitchTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"gordian_installs_{Guid.NewGuid():N}");
        private readonly string _installA;
        private readonly string _installB;
        private string? _usage;
        private readonly List<ResourceManager?> _switchedTo = new();

        public AppResourceManagerSwitchTests()
        {
            _installA = Path.Combine(_root, "Retail", "FINAL FANTASY XI");
            _installB = Path.Combine(_root, "Horizon", "Game");
            WriteInstall(_installA, subDir: 10, fileNum: 3);
            WriteInstall(_installB, subDir: 20, fileNum: 5);

            AppResourceManager.ResetForTests();
            GameDirectoryDetector.DefaultGameDirectory = null;
            AppResourceManager.UsageProbe = () => _usage;
            AppResourceManager.InstallSwitched += rm => _switchedTo.Add(rm);
        }

        public void Dispose()
        {
            AppResourceManager.ResetForTests();
            GameDirectoryDetector.DefaultGameDirectory = null;
            ItemNameResolver.Initialize(null);
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        }

        /// <summary>A one-entry FTABLE/VTABLE pair: file id 0 -> ROM/{subDir}/{fileNum}.DAT, which holds the install's name.</summary>
        private static void WriteInstall(string dir, ushort subDir, byte fileNum)
        {
            Directory.CreateDirectory(Path.Combine(dir, "ROM", subDir.ToString()));
            byte[] ftable = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(ftable, (ushort)((subDir << 7) | fileNum));
            File.WriteAllBytes(Path.Combine(dir, "FTABLE.DAT"), ftable);
            File.WriteAllBytes(Path.Combine(dir, "VTABLE.DAT"), new byte[] { 1 });
            File.WriteAllText(Path.Combine(dir, "ROM", subDir.ToString(), $"{fileNum}.DAT"), dir);
        }

        private static string? ReadFileZero(ResourceManager? rm)
        {
            byte[]? bytes = rm?.LoadDatBytesByFileId(0);
            return bytes == null ? null : System.Text.Encoding.UTF8.GetString(bytes);
        }

        private static AppResourceManager.LaunchHold Launch(string install)
        {
            string? problem = AppResourceManager.SelectForLaunch(install, out var hold);
            Assert.Null(problem);
            Assert.NotNull(hold);
            return hold!;
        }

        [Fact]
        public void LaunchOnAnotherInstall_WhenNothingUsesTheLoadedOne_SwitchesAndReadsTheNewInstallEverywhere()
        {
            Launch(_installA).Dispose();
            var a = AppResourceManager.Instance;
            Assert.Equal(_installA, ReadFileZero(a));
            Assert.Equal(_installA, ItemNameResolver.GameDirectory);
            int generationA = a!.CacheGeneration;
            int zoneDatGeneration = ZoneDatLoader.Generation;

            Launch(_installB).Dispose();

            var b = AppResourceManager.Instance;
            Assert.NotSame(a, b);
            Assert.True(AppResourceManager.SamePath(_installB, b!.GameDirectory));
            Assert.Equal(_installB, ReadFileZero(b));
            // Item names, the zone DAT tables (and the message tables that follow them) and every cache keyed by the
            // generation alone (GPU model keys, stock UI, item icons, footwear) read the new install.
            Assert.Equal(_installB, ItemNameResolver.GameDirectory);
            Assert.NotEqual(generationA, b.CacheGeneration);
            Assert.NotEqual(zoneDatGeneration, ZoneDatLoader.Generation);
            Assert.Equal(new[] { b }, _switchedTo);
        }

        [Fact]
        public void LaunchOnTheSameInstall_WhileItIsInUse_GoesAheadWithoutSwitching()
        {
            using var first = Launch(_installA);
            var a = AppResourceManager.Instance;
            _usage = "a character is logged in";

            using var second = Launch(_installA + Path.DirectorySeparatorChar);

            Assert.Same(a, AppResourceManager.Instance);
            Assert.Empty(_switchedTo);
        }

        [Fact]
        public void LaunchOnAnotherInstall_WhileACharacterOrWindowUsesTheLoadedOne_IsRefusedUntilItEnds()
        {
            Launch(_installA).Dispose();
            var a = AppResourceManager.Instance;
            _usage = "a character is logged in";

            string? problem = AppResourceManager.SelectForLaunch(_installB, out var hold);

            Assert.Null(hold);
            Assert.NotNull(problem);
            Assert.Contains(_installA, problem);
            Assert.Contains(_installB, problem);
            Assert.Contains("a character is logged in", problem);
            Assert.Contains("log out of the characters", problem);
            Assert.DoesNotContain("Restart", problem);
            Assert.Same(a, AppResourceManager.Instance);
            Assert.Equal(_installA, ItemNameResolver.GameDirectory);

            _usage = "a game window is open";
            Assert.NotNull(AppResourceManager.SelectForLaunch(_installB, out _));

            // The session ended and the windows closed: the same launch now goes ahead.
            _usage = null;
            Launch(_installB).Dispose();
            Assert.Equal(_installB, ReadFileZero(AppResourceManager.Instance));
        }

        [Fact]
        public void ALaunchInFlight_HoldsItsInstall_SoALaterProfileOnAnotherInstallWaits()
        {
            var hold = Launch(_installA);

            string? problem = AppResourceManager.SelectForLaunch(_installB, out var refused);
            Assert.Null(refused);
            Assert.Contains("a launch is in progress", problem);
            Assert.Equal(1, AppResourceManager.LaunchHolds);

            hold.Dispose();
            hold.Dispose(); // twice is harmless
            Assert.Equal(0, AppResourceManager.LaunchHolds);
            Launch(_installB).Dispose();
            Assert.Equal(_installB, ReadFileZero(AppResourceManager.Instance));
        }

        [Fact]
        public void EmptyViewport_SwitchesToTheDefaultInstall_OnlyWhenNothingUsesTheLoadedOne()
        {
            GameDirectoryDetector.DefaultGameDirectory = _installA;
            Launch(_installB).Dispose();
            Assert.Equal(_installB, ReadFileZero(AppResourceManager.Instance));

            _usage = "a character is logged in";
            AppResourceManager.SelectDefaultForViewport();
            Assert.Equal(_installB, ReadFileZero(AppResourceManager.Instance));

            _usage = null;
            AppResourceManager.SelectDefaultForViewport();
            Assert.Equal(_installA, ReadFileZero(AppResourceManager.Instance));

            // A profile with no folder of its own plays on the default install too.
            Launch("").Dispose();
            Assert.Equal(_installA, ReadFileZero(AppResourceManager.Instance));
        }

        [Fact]
        public void ChangingTheDefaultInstall_SwitchesNothingUntilAnInstallIsUsed()
        {
            GameDirectoryDetector.DefaultGameDirectory = _installA;
            var a = AppResourceManager.Instance;
            Assert.Equal(_installA, ReadFileZero(a));

            GameDirectoryDetector.DefaultGameDirectory = _installB;
            Assert.Same(a, AppResourceManager.Instance);

            Launch(null!).Dispose();
            Assert.Equal(_installB, ReadFileZero(AppResourceManager.Instance));
        }

        [Fact]
        public void AFailingUsageProbe_CountsAsInUse()
        {
            Launch(_installA).Dispose();
            AppResourceManager.UsageProbe = () => throw new InvalidOperationException("boom");

            Assert.NotNull(AppResourceManager.SelectForLaunch(_installB, out _));
            Assert.Equal(_installA, ReadFileZero(AppResourceManager.Instance));
        }

        [Theory]
        [InlineData(0, false, false, null)]
        [InlineData(1, false, false, "a character is logged in")]
        [InlineData(2, true, true, "2 characters are logged in")]
        [InlineData(0, true, false, "a game window is open")]
        [InlineData(0, false, true, "a game window is open")] // the device (and its GPU caches) outlives the last window by a moment
        public void DescribeUsers_CountsCharactersWindowsAndTheGraphicsDevice(int sessions, bool windows, bool device, string? expected)
        {
            Assert.Equal(expected, AppResourceManager.DescribeUsers(sessions, windows, device));
        }
    }
}
