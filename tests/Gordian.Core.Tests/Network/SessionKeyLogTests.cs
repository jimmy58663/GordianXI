// tests/Gordian.Core.Tests/Network/SessionKeyLogTests.cs
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Crypto;
using Gordian.Core.Network.LandSandBoat;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// #640: session keys never reach a log file at any level. Logs are shared in issues and test rounds, so the crypto
    /// suite names a key only by its fingerprint and counter word (<see cref="SessionKeyLog"/>).
    /// </summary>
    [Collection("LogTests")]
    public sealed class SessionKeyLogTests : IDisposable
    {
        private readonly string _logs = Path.Combine(Path.GetTempPath(), $"gordian_keylog_{Guid.NewGuid():N}");

        public void Dispose()
        {
            GordianLog.Shutdown();
            try { Directory.Delete(_logs, recursive: true); } catch (IOException) { }
        }

        private static byte[] TestKey()
        {
            var key = new byte[20];
            for (int i = 0; i < key.Length; i++) key[i] = (byte)(0xA1 + i * 7);
            return key;
        }

        [Fact]
        public void InitializingAndAdvancingTheKey_LogsNoKeyBytesAtAnyLevel()
        {
            GordianLog.Initialize(new LogOptions
            {
                LogsDirectory = _logs,
                Settings = new LoggingSettings { DefaultLevel = LogLevel.Trace },
                DebugByDefault = true,
                NetworkFileOverride = true,
            });
            Assert.True(GordianLog.IsEnabled(LogLevel.Debug, LogCategories.Crypto));

            byte[] key = TestKey();
            byte[] advanced = (byte[])key.Clone();
            BitConverter.TryWriteBytes(advanced.AsSpan(16, 4), BitConverter.ToUInt32(key, 16) + 2);

            using var suite = new LegacyBlowfishCryptoSuite();
            suite.InitializeKey(key);
            Assert.True(suite.AdvanceZoneKey());

            Assert.True(GordianLog.Flush(TimeSpan.FromSeconds(5)));
            string all = string.Concat(Directory.GetFiles(_logs, "*.log", SearchOption.AllDirectories).Select(ReadShared));

            Assert.Contains("Advancing session Blowfish key", all);
            Assert.Contains(SessionKeyLog.Describe(advanced), all);
            foreach (byte[] secret in new[] { key, advanced, MD5.HashData(key), MD5.HashData(advanced) })
            {
                // The whole key, and any 8-byte run of it, in either hex case.
                string hex = Convert.ToHexString(secret);
                for (int start = 0; start + 16 <= hex.Length; start += 2)
                {
                    string run = hex.Substring(start, 16);
                    Assert.DoesNotContain(run, all, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        [Fact]
        public void Describe_NamesTheMapKeyByFingerprintAndCounter()
        {
            byte[] key = TestKey();
            string label = SessionKeyLog.Describe(key);

            Assert.Equal($"key value {LsbSessionKeys.Fingerprint(key):X8}, key counter {BitConverter.ToUInt32(key, 16):X8}", label);
            Assert.DoesNotContain(Convert.ToHexString(key.AsSpan(0, 8)), label);
            Assert.Equal("no key", SessionKeyLog.Describe(ReadOnlySpan<byte>.Empty));
            Assert.EndsWith(", 16 bytes", SessionKeyLog.Describe(new byte[16]));
        }

        private static string ReadShared(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
