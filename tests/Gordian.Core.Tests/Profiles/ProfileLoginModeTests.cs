// tests/Gordian.Core.Tests/Profiles/ProfileLoginModeTests.cs
using System;
using System.IO;
using Gordian.Core.Profiles;
using Xunit;

namespace Gordian.Core.Tests.Profiles
{
    public class ProfileLoginModeTests
    {
        [Theory]
        [InlineData("", 0, ProfileLoginMode.CharacterSelect)]
        [InlineData("Knot", 0, ProfileLoginMode.AutoLogin)]
        [InlineData("", 3, ProfileLoginMode.AutoLogin)]
        public void OlderProfiles_WithoutAMode_KeepTheirBehaviour(string name, int slot, ProfileLoginMode expected)
        {
            var profile = new AccountProfile { CharacterName = name, CharacterSlot = slot };
            Assert.Null(profile.LoginMode);
            Assert.Equal(expected, profile.EffectiveLoginMode);
        }

        [Fact]
        public void CharacterSelect_WinsEvenWithACharacterNamed()
        {
            var profile = new AccountProfile { CharacterName = "Knot", LoginMode = ProfileLoginMode.CharacterSelect };
            Assert.Equal(ProfileLoginMode.CharacterSelect, profile.EffectiveLoginMode);
        }

        [Fact]
        public void AutoLogin_WithoutACharacter_FallsBackToCharacterSelect()
        {
            var profile = new AccountProfile { LoginMode = ProfileLoginMode.AutoLogin };
            Assert.Equal(ProfileLoginMode.CharacterSelect, profile.EffectiveLoginMode);
        }

        [Fact]
        public void Mode_RoundTripsThroughTheFile_AndOldFilesLoadWithoutIt()
        {
            string dir = Path.Combine(Path.GetTempPath(), "GordianXI_LoginMode_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                new AccountProfile { ProfileName = "Mode", CharacterSlot = 2, LoginMode = ProfileLoginMode.CharacterSelect }.SaveToFile(dir);
                var loaded = AccountProfile.LoadFromFile(Path.Combine(dir, "Mode.json"));
                Assert.Equal(ProfileLoginMode.CharacterSelect, loaded!.LoginMode);
                Assert.Equal(ProfileLoginMode.CharacterSelect, loaded.Clone().LoginMode);

                File.WriteAllText(Path.Combine(dir, "Old.json"), "{\"ProfileName\":\"Old\",\"CharacterName\":\"Knot\"}");
                var old = AccountProfile.LoadFromFile(Path.Combine(dir, "Old.json"));
                Assert.Null(old!.LoginMode);
                Assert.Equal(ProfileLoginMode.AutoLogin, old.EffectiveLoginMode);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
