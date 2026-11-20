// tests/Gordian.App.Tests/SampleProfiles.cs
using Gordian.Core.Profiles;

namespace Gordian.App.Tests
{
    /// <summary>
    /// A small launch tree for view-model tests: RetailMain and LP00-QA at the root, CraftMule and AF00 in Test1, and HS00
    /// in Test2/Test3. The app writes no profiles of its own on an empty store, so tests that need a tree write this one
    /// to their scratch profiles folder before building the view model. Credentials are placeholders.
    /// </summary>
    internal static class SampleProfiles
    {
        public static void WriteTo(string profilesDirectory)
        {
            var doc = new ProfileStoreDocument();
            doc.Profiles.Add(new AccountProfile
            {
                ProfileName = "RetailMain",
                CharacterName = "RetailMain",
                ServerType = ProfileServerType.Retail,
                IsSelectedForLaunch = true,
            });
            doc.Profiles.Add(LandSandBoat("CraftMule", "Test1", selected: true));
            doc.Profiles.Add(LandSandBoat("AF00", "Test1", selected: true));
            doc.Profiles.Add(LandSandBoat("HS00", "Test2/Test3", selected: true));
            doc.Profiles.Add(LandSandBoat("LP00-QA", string.Empty, selected: false));
            // Each profile's own file as well as the store, the way the app saves a profile.
            foreach (var profile in doc.Profiles) profile.SaveToFile(profilesDirectory);
            ProfileStorageService.Save(profilesDirectory, doc);
        }

        private static AccountProfile LandSandBoat(string name, string folder, bool selected) => new()
        {
            ProfileName = name,
            CharacterName = name,
            Folder = folder,
            Username = name.ToLowerInvariant() + "_user",
            Password = "placeholder",
            ServerType = ProfileServerType.LandSandBoat,
            ServerHost = "127.0.0.1",
            ServerPort = 54231,
            IsSelectedForLaunch = selected,
        };
    }
}
