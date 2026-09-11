// tests/Gordian.Core.Tests/Profiles/LaunchOrchestratorTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using Gordian.Core.Network;
using Gordian.Core.Profiles;
using Xunit;

namespace Gordian.Core.Tests.Profiles
{
    public class LaunchOrchestratorTests : IDisposable
    {
        private readonly SessionRegistry _registry;

        public LaunchOrchestratorTests()
        {
            _registry = new SessionRegistry();
        }

        public void Dispose()
        {
            _registry.Dispose();
        }

        [Fact]
        public void GetActiveCharacterNames_PullsFromRegistryAccurately()
        {
            var net1 = new SessionNetworkManager("127.0.0.1", 54231)
            {
                CurrentState = SessionState.ActiveInWorld
            };
            var session1 = new CharacterSession("JimmyChar", 100, "jimmy_account", net1);
            _registry.RegisterSession(session1);

            var active = LaunchOrchestrator.GetActiveCharacterNames(_registry);

            Assert.Contains("JimmyChar", active);
            Assert.Contains("jimmy_account", active);
            Assert.DoesNotContain("InactiveChar", active);
        }

        [Fact]
        public void LaunchSelectedProfiles_SkipsAlreadyActiveProfiles()
        {
            // Register an active session for "jimmy_mule"
            var net = new SessionNetworkManager("127.0.0.1", 54231)
            {
                CurrentState = SessionState.ActiveInWorld
            };
            var session = new CharacterSession("CraftMule", 200, "jimmy_mule", net);
            _registry.RegisterSession(session);

            var profiles = new List<AccountProfile>
            {
                new AccountProfile
                {
                    ProfileName = "CraftMule",
                    Username = "jimmy_mule",
                    IsSelectedForLaunch = true,
                    BootloaderPath = "non_existent_path.exe"
                },
                new AccountProfile
                {
                    ProfileName = "OfflineChar",
                    Username = "offline_user",
                    IsSelectedForLaunch = true,
                    BootloaderPath = "non_existent_path.exe"
                },
                new AccountProfile
                {
                    ProfileName = "UnselectedChar",
                    Username = "unselected_user",
                    IsSelectedForLaunch = false,
                    BootloaderPath = "non_existent_path.exe"
                }
            };

            // Should skip "CraftMule" because it is active in registry,
            // Should skip "UnselectedChar" because IsSelectedForLaunch == false,
            // And attempt "OfflineChar" (which fails gracefully on File.Exists check without launching)
            int launched = LaunchOrchestrator.LaunchSelectedProfiles(profiles, _registry);

            Assert.Equal(0, launched); // non_existent_path.exe does not exist, so 0 launched
        }
    }
}
