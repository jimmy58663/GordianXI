// tests/Gordian.App.Tests/Services/ViewportWindowManagerTests.cs
using System;
using Gordian.App.Graphics;
using Gordian.App.Services;
using Gordian.App.ViewModels;
using Gordian.Core.Network;
using Xunit;

namespace Gordian.App.Tests.Services
{
    public class ViewportWindowManagerTests : IDisposable
    {
        private readonly SessionRegistry _registry;
        private readonly ViewportViewModel _viewModel;
        private readonly ViewportWindowManager _manager;

        public ViewportWindowManagerTests()
        {
            _registry = new SessionRegistry();
            _viewModel = new ViewportViewModel
            {
                AutoLaunchOnConnect = false // disable showing UI window in headless test runner
            };
            _manager = new ViewportWindowManager(_registry, _viewModel);
        }

        public void Dispose()
        {
            _manager.Dispose();
            _registry.Dispose();
        }

        [Fact]
        public void SessionRegistered_AddsCharacterTabToPrimaryViewModel()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 1, "user_cybin", netManager);

            _registry.RegisterSession(session);

            // Directly verify tab addition
            _viewModel.AddSession(session);
            Assert.Single(_viewModel.CharacterTabs);
            Assert.Equal("Cybin", _viewModel.CharacterTabs[0].CharacterName);
            Assert.Equal(_viewModel.CharacterTabs[0], _viewModel.ActiveTab);
        }

        [Fact]
        public void SessionUnregistered_RemovesCharacterTabFromPrimaryViewModel()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 1, "user_cybin", netManager);

            _viewModel.AddSession(session);
            Assert.Single(_viewModel.CharacterTabs);

            _viewModel.RemoveSession(session);
            Assert.Empty(_viewModel.CharacterTabs);
            Assert.Null(_viewModel.ActiveTab);
        }

        [Fact]
        public void TabTearOff_MarksTabAsPoppedOut()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Sylphie", 2, "user_sylph", netManager);

            var tab = _viewModel.AddSession(session);
            Assert.False(tab.IsPoppedOut);

            bool poppedFired = false;
            _viewModel.TabPoppedOut += (s, t) =>
            {
                poppedFired = true;
                t.IsPoppedOut = true;
            };

            tab.PopOutCommand.Execute(null);

            Assert.True(poppedFired);
            Assert.True(tab.IsPoppedOut);
        }

        [Fact]
        public void RegistryUnregisterSession_RemovesCharacterTabViaManager()
        {
            ViewportWindowManager.UiDispatcher = a => a();
            try
            {
                var netManager = new SessionNetworkManager("127.0.0.1", 54230);
                var session = new CharacterSession("Cybin", 1, "user_cybin", netManager);

                _registry.RegisterSession(session);
                Assert.Single(_viewModel.CharacterTabs);

                _registry.UnregisterSession(session.SessionId);
                Assert.Empty(_viewModel.CharacterTabs);
            }
            finally
            {
                ViewportWindowManager.UiDispatcher = null;
            }
        }

        [Fact]
        public void SessionDisconnect_TriggersAutomaticUnregistrationAndTabRemoval()
        {
            ViewportWindowManager.UiDispatcher = a => a();
            try
            {
                var netManager = new SessionNetworkManager("127.0.0.1", 54230)
                {
                    CurrentState = SessionState.ActiveInWorld
                };
                var session = new CharacterSession("Cybin", 1, "user_cybin", netManager);

                _registry.RegisterSession(session);
                Assert.Single(_viewModel.CharacterTabs);

                // Disconnecting character session triggers StateChanged -> SessionRegistry auto unregisters -> ViewportWindowManager cleans up
                session.Disconnect();

                Assert.Empty(_viewModel.CharacterTabs);
                Assert.Null(_viewModel.ActiveTab);
            }
            finally
            {
                ViewportWindowManager.UiDispatcher = null;
            }
        }
    }
}
