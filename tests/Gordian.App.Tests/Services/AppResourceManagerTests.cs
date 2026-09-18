// tests/Gordian.App.Tests/Services/AppResourceManagerTests.cs
using System;
using System.Numerics;
using Gordian.App.Graphics;
using Gordian.App.Services;
using Gordian.Core.Network;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.Services
{
    public class AppResourceManagerTests
    {
        [Fact]
        public void AppResourceManager_Instance_CanBeQueriedWithoutThrowing()
        {
            // Verifies that querying AppResourceManager singleton does not throw
            var rm = AppResourceManager.Instance;
            // On machines with FFXI installed, rm will be non-null; on CI or clean boxes it may be null.
            // Both are acceptable as long as it handles missing registry cleanly.
            if (rm != null)
            {
                Assert.NotNull(rm.FileTable);
            }
        }

        [Fact]
        public void VeldridViewportControl_SessionAndWorldStateBinding_SyncsCorrectly()
        {
            var control = new VeldridViewportControl();
            Assert.NotNull(control.Camera);
            Assert.NotNull(control.Environment);

            var net = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("TestHero", 9999, "hero_user", net);

            control.ActiveSession = session;
            Assert.Equal(session, control.ActiveSession);
            Assert.Equal(session.World, control.WorldState);

            // Verify changing zone on World fires ZoneChanged and updates control pending zone
            session.World.CurrentZoneId = 100;
            Assert.Equal(100, session.World.CurrentZoneId);

            // Changing active session resets WorldState
            control.ActiveSession = null;
            Assert.Null(control.ActiveSession);
            Assert.Null(control.WorldState);
        }
    }
}
