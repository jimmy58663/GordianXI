// src/Gordian.Core/Profiles/MasterKeyStore.cs
using System;
using System.Security.Cryptography;
using System.Text;

namespace Gordian.Core.Profiles
{
    public static class MasterKeyStore
    {
        private const string TargetCredentialName = "GordianXI_MasterAppKey";
        private static readonly byte[] CachedMasterKey;

        public static byte[] MasterKey => CachedMasterKey;

        static MasterKeyStore()
        {
            CachedMasterKey = RetrieveOrCreateMasterKey();
        }

        private static byte[] RetrieveOrCreateMasterKey()
        {
            // Establish an unguessable local master application credential payload token
            string secureFallbackPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gordian_key");
            
            if (File.Exists(secureFallbackPath))
            {
                try
                {
                    string savedHex = File.ReadAllText(secureFallbackPath).Trim();
                    return Convert.FromHexString(savedHex);
                }
                catch
                {
                    // Fall through to regenerate safely if data corruption is encountered
                }
            }

            // Generate an absolutely unguessable 256-bit random cryptographically secure master key
            byte[] newKey = new byte[32];
            RandomNumberGenerator.Fill(newKey);
            
            try
            {
                File.WriteAllText(secureFallbackPath, Convert.ToHexString(newKey));

                // FIX: Safely protect file permissions on Unix environments (Linux/macOS)
                // Using explicit platform gates prevents the compiler from throwing compatibility alerts.
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    // Restrict permissions natively to owner read/write (chmod 600 equivalent)
                    File.SetUnixFileMode(secureFallbackPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Security Warning] Key storage protection limit tripped: {ex.Message}");
            }

            return newKey;
        }
    }
}
