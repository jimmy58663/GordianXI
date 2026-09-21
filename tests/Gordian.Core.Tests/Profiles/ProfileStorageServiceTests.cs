// tests/Gordian.Core.Tests/Profiles/ProfileStorageServiceTests.cs
using System;
using System.IO;
using System.Linq;
using Gordian.Core.Profiles;
using Xunit;

namespace Gordian.Core.Tests.Profiles
{
    public sealed class ProfileStorageServiceTests : IDisposable
    {
        private readonly string _tempDirectory;

        public ProfileStorageServiceTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "GordianXI_StoreTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                try
                {
                    Directory.Delete(_tempDirectory, true);
                }
                catch
                {
                    // Clean up best effort
                }
            }
        }

        [Fact]
        public void AccountProfile_Clone_CreatesExactCopy()
        {
            var original = new AccountProfile
            {
                ProfileName = "WarriorMain",
                CharacterName = "Conan",
                Username = "conan_user",
                Password = "SecretPassword123",
                OtpSeed = "HXDMVJECJJWSRB3D",
                Folder = "Endgame/Dynamis",
                ServerHost = "127.0.0.1",
                ServerPort = 54231,
                IsSelectedForLaunch = true
            };

            var cloned = original.Clone("WarriorMain - Copy");

            Assert.Equal("WarriorMain - Copy", cloned.ProfileName);
            Assert.Equal("Conan", cloned.CharacterName);
            Assert.Equal("conan_user", cloned.Username);
            Assert.Equal("SecretPassword123", cloned.Password);
            Assert.Equal("HXDMVJECJJWSRB3D", cloned.OtpSeed);
            Assert.Equal("Endgame/Dynamis", cloned.Folder);
            Assert.Equal("127.0.0.1", cloned.ServerHost);
            Assert.Equal(54231, cloned.ServerPort);
            Assert.True(cloned.IsSelectedForLaunch);

            // Verify mutating cloned does not affect original
            cloned.CharacterName = "Arnold";
            Assert.Equal("Conan", original.CharacterName);
        }

        [Fact]
        public void SaveAndLoad_RoundTripsProfilesAndFolderStructureAndPreservesOrder()
        {
            var doc = new ProfileStoreDocument();
            doc.Folders.Add(new ProfileFolderEntry("FolderA", isExpanded: true));
            doc.Folders.Add(new ProfileFolderEntry("FolderB/SubB", isExpanded: false));

            // Add profiles in a specific custom order
            doc.Profiles.Add(new AccountProfile
            {
                ProfileName = "SecondMule",
                Folder = "FolderA",
                Password = "Pw2",
                IsSelectedForLaunch = false
            });
            doc.Profiles.Add(new AccountProfile
            {
                ProfileName = "FirstLeader",
                Folder = "",
                Password = "Pw1",
                IsSelectedForLaunch = true
            });
            doc.Profiles.Add(new AccountProfile
            {
                ProfileName = "ThirdSupport",
                Folder = "FolderB/SubB",
                Password = "Pw3",
                IsSelectedForLaunch = true
            });

            ProfileStorageService.Save(_tempDirectory, doc);

            string storeFile = Path.Combine(_tempDirectory, ProfileStorageService.StoreFileName);
            Assert.True(File.Exists(storeFile));

            // Load back
            var loaded = ProfileStorageService.Load(_tempDirectory);
            Assert.NotNull(loaded);
            Assert.Equal(2, loaded.Folders.Count);
            Assert.Equal("FolderA", loaded.Folders[0].Path);
            Assert.True(loaded.Folders[0].IsExpanded);
            Assert.Equal("FolderB/SubB", loaded.Folders[1].Path);
            Assert.False(loaded.Folders[1].IsExpanded);

            Assert.Equal(3, loaded.Profiles.Count);
            // Verify custom order was preserved exactly
            Assert.Equal("SecondMule", loaded.Profiles[0].ProfileName);
            Assert.Equal("Pw2", loaded.Profiles[0].Password);
            Assert.False(loaded.Profiles[0].IsSelectedForLaunch);

            Assert.Equal("FirstLeader", loaded.Profiles[1].ProfileName);
            Assert.Equal("Pw1", loaded.Profiles[1].Password);
            Assert.True(loaded.Profiles[1].IsSelectedForLaunch);

            Assert.Equal("ThirdSupport", loaded.Profiles[2].ProfileName);
            Assert.Equal("Pw3", loaded.Profiles[2].Password);
            Assert.True(loaded.Profiles[2].IsSelectedForLaunch);
        }

        [Fact]
        public void Save_CreatesBackupFileOnSubsequentSaves()
        {
            var doc1 = new ProfileStoreDocument();
            doc1.Profiles.Add(new AccountProfile { ProfileName = "InitialProfile" });
            ProfileStorageService.Save(_tempDirectory, doc1);

            string backupFile = Path.Combine(_tempDirectory, ProfileStorageService.BackupFileName);
            Assert.False(File.Exists(backupFile)); // No backup on initial save

            var doc2 = new ProfileStoreDocument();
            doc2.Profiles.Add(new AccountProfile { ProfileName = "UpdatedProfile" });
            ProfileStorageService.Save(_tempDirectory, doc2);

            Assert.True(File.Exists(backupFile)); // Backup exists on subsequent save
            string backupJson = File.ReadAllText(backupFile);
            Assert.Contains("InitialProfile", backupJson);
        }

        [Fact]
        public void MigrateFromLegacyFiles_ImportsIndividualJsonFiles()
        {
            // Simulate legacy structure on disk
            var legacyProfile1 = new AccountProfile
            {
                ProfileName = "RootLegacyChar",
                Folder = "",
                Password = "LegacyPass1"
            };
            legacyProfile1.SaveToFile(_tempDirectory);

            var legacyProfile2 = new AccountProfile
            {
                ProfileName = "FolderLegacyChar",
                Folder = "Party1/Dps",
                Password = "LegacyPass2"
            };
            legacyProfile2.SaveToFile(_tempDirectory);

            // Load should auto-detect and migrate into profiles.json
            var loaded = ProfileStorageService.Load(_tempDirectory);

            Assert.True(File.Exists(Path.Combine(_tempDirectory, ProfileStorageService.StoreFileName)));
            Assert.Equal(2, loaded.Profiles.Count);
            Assert.Contains(loaded.Profiles, p => p.ProfileName == "RootLegacyChar" && p.Password == "LegacyPass1");
            Assert.Contains(loaded.Profiles, p => p.ProfileName == "FolderLegacyChar" && p.Folder == "Party1/Dps");

            Assert.Contains(loaded.Folders, f => f.Path == "Party1");
            Assert.Contains(loaded.Folders, f => f.Path == "Party1/Dps");
        }
    }
}
