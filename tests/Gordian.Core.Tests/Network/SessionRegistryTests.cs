// tests/Gordian.Core.Tests/Network/SessionRegistryTests.cs
using System;
using Gordian.Core.Network;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class SessionRegistryTests : IDisposable
    {
        private readonly SessionRegistry _registry;

        public SessionRegistryTests()
        {
            _registry = new SessionRegistry();
        }

        public void Dispose()
        {
            _registry.Dispose();
        }

        [Fact]
        public void RegisterSession_TracksSessionAndFiresEvent()
        {
            CharacterSession? capturedSession = null;
            _registry.SessionRegistered += (s, e) => capturedSession = e;

            var networkManager = new SessionNetworkManager("127.0.0.1", 54231)
            {
                CurrentState = SessionState.ActiveInWorld
            };
            var session = new CharacterSession("TarutaruHealer", 1001, "my_account", networkManager);

            _registry.RegisterSession(session);

            Assert.NotNull(capturedSession);
            Assert.Equal("TarutaruHealer", capturedSession.CharacterName);
            Assert.True(_registry.IsCharacterActive("TarutaruHealer"));
            Assert.True(_registry.IsCharacterActive("tarutaruhealer")); // Case-insensitive
            Assert.True(_registry.IsAccountActive("my_account"));
            Assert.False(_registry.IsCharacterActive("UnknownChar"));
            Assert.Single(_registry.ActiveSessions);
        }

        [Fact]
        public void CreateAndRegisterSession_FromHandoff_RegistersSuccessfully()
        {
            var handoff = new SessionHandoffArgs
            {
                TargetCharacterName = "ElvaanPaladin",
                CharacterId = 2002,
                ServerIp = "127.0.0.1",
                ServerPort = 54230,
                Base64SessionToken = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })
            };

            var session = _registry.CreateAndRegisterSession(handoff, "tank_account");

            Assert.Equal("ElvaanPaladin", session.CharacterName);
            Assert.Equal(2002u, session.CharacterId);
            Assert.Equal("tank_account", session.AccountUsername);
            Assert.True(_registry.IsCharacterActive("ElvaanPaladin"));
            Assert.True(_registry.IsAccountActive("tank_account"));

            Assert.True(_registry.TryGetSession(session.SessionId, out var found));
            Assert.Same(session, found);

            Assert.True(_registry.TryGetSessionByCharacterName("elvaanpaladin", out var foundByName));
            Assert.Same(session, foundByName);
        }

        [Fact]
        public void UnregisterSession_RemovesSessionAndFiresEvent()
        {
            CharacterSession? unregisteredSession = null;
            _registry.SessionUnregistered += (s, e) => unregisteredSession = e;

            var networkManager = new SessionNetworkManager("127.0.0.1", 54231)
            {
                CurrentState = SessionState.ActiveInWorld
            };
            var session = new CharacterSession("MithraThief", 3003, "thf_account", networkManager);

            _registry.RegisterSession(session);
            Assert.True(_registry.IsCharacterActive("MithraThief"));

            _registry.UnregisterSession(session.SessionId);

            Assert.NotNull(unregisteredSession);
            Assert.Equal("MithraThief", unregisteredSession.CharacterName);
            Assert.False(_registry.IsCharacterActive("MithraThief"));
            Assert.Empty(_registry.ActiveSessions);
        }
    }
}
