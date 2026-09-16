// tests/Gordian.App.Tests/ViewModels/CommandConsoleViewModelTests.cs
using System;
using System.Threading.Tasks;
using Gordian.App.ViewModels;
using Gordian.Core.Config;
using Gordian.Core.Network;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.ViewModels
{
    public sealed class CommandConsoleViewModelTests : IDisposable
    {
        private readonly SessionRegistry _registry;
        private readonly CommandConsoleViewModel _vm;

        public CommandConsoleViewModelTests()
        {
            CommandConsoleViewModel.UiDispatcher = a => a();
            _registry = new SessionRegistry();
            _vm = new CommandConsoleViewModel(_registry);
        }

        public void Dispose()
        {
            _vm.Dispose();
            CommandConsoleViewModel.UiDispatcher = null;
        }

        [Fact]
        public void InitialState_WithoutSession_HasSafeDefaults()
        {
            Assert.False(_vm.HasActiveSession);
            Assert.Null(_vm.SelectedSession);
            Assert.NotEmpty(_vm.Entries); // Has welcome banner
            Assert.True(_vm.AutoScroll);
            Assert.Equal("None", _vm.CurrentTargetName);
            Assert.Contains("No active session", _vm.SessionHeaderTitle);
        }

        [Fact]
        public void RegisterSession_AutoSelectsSession()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 0x01020304, "user1", netManager);

            _registry.RegisterSession(session);

            Assert.True(_vm.HasActiveSession);
            Assert.Same(session, _vm.SelectedSession);
            Assert.Contains("Cybin", _vm.SessionHeaderTitle);
        }

        [Fact]
        public async Task ExecuteInput_WithoutSession_LogsError()
        {
            _vm.InputCommand = "/pos";
            await _vm.ExecuteInputAsync();

            Assert.Empty(_vm.InputCommand);
            var lastEntry = _vm.Entries[^1];
            Assert.Equal(ConsoleEntryKind.Error, lastEntry.Kind);
            Assert.Contains("No active character session", lastEntry.Message);
        }

        [Fact]
        public async Task ExecuteInput_WithSession_LogsInputAndSuccess()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 0x01020304, "user1", netManager);
            _registry.RegisterSession(session);

            _vm.InputCommand = "/pos";
            await _vm.ExecuteInputAsync();

            Assert.Empty(_vm.InputCommand);
            // Verify echo and output were logged
            Assert.True(_vm.Entries.Count >= 2);
            var inputEcho = _vm.Entries[^2];
            var resultEntry = _vm.Entries[^1];

            Assert.Equal(ConsoleEntryKind.Input, inputEcho.Kind);
            Assert.Equal("/pos", inputEcho.Message);

            Assert.Equal(ConsoleEntryKind.Info, resultEntry.Kind);
            Assert.Contains("Position", resultEntry.Message);
        }

        [Fact]
        public void History_UpAndDown_TraversesCommandHistory()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 0x01020304, "user1", netManager);
            _registry.RegisterSession(session);

            _vm.InputCommand = "/pos";
            _ = _vm.ExecuteInputAsync();

            _vm.InputCommand = "/vitals";
            _ = _vm.ExecuteInputAsync();

            // Up arrow should give last command "/vitals"
            _vm.HistoryPrevious();
            Assert.Equal("/vitals", _vm.InputCommand);

            // Up arrow again should give previous command "/pos"
            _vm.HistoryPrevious();
            Assert.Equal("/pos", _vm.InputCommand);

            // Down arrow should return to "/vitals"
            _vm.HistoryNext();
            Assert.Equal("/vitals", _vm.InputCommand);

            // Down arrow again should clear to empty line
            _vm.HistoryNext();
            Assert.Equal(string.Empty, _vm.InputCommand);
        }

        [Fact]
        public void ClearConsole_ClearsBufferAndAddsNotice()
        {
            _vm.ClearConsole();

            Assert.Single(_vm.Entries);
            Assert.Equal(ConsoleEntryKind.System, _vm.Entries[0].Kind);
            Assert.Contains("cleared", _vm.Entries[0].Message);
        }

        [Fact]
        public async Task QuickAction_Vitals_ExecutesSuccessfully()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 0x01020304, "user1", netManager);
            _registry.RegisterSession(session);

            await _vm.ExecuteSlashCommandAsync("/vitals");

            var lastEntry = _vm.Entries[^1];
            Assert.Equal(ConsoleEntryKind.Info, lastEntry.Kind);
            Assert.Contains("Vitals", lastEntry.Message);
        }

        [Fact]
        public void UnregisterSession_SwitchesToRemainingOrNull()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 0x01020304, "user1", netManager);
            _registry.RegisterSession(session);

            Assert.True(_vm.HasActiveSession);

            _registry.UnregisterSession(session.SessionId);

            Assert.False(_vm.HasActiveSession);
            Assert.Null(_vm.SelectedSession);
        }

        [Fact]
        public async Task ExecuteCommand_Help_AddsInfoEntryWithCommands()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 0x01020304, "user1", netManager);
            _registry.RegisterSession(session);

            await _vm.ExecuteSlashCommandAsync("/help");

            var lastEntry = _vm.Entries[^1];
            Assert.Equal(ConsoleEntryKind.Info, lastEntry.Kind);
            Assert.Contains("Available Client Commands", lastEntry.Message);
        }

        [Fact]
        public async Task ExecuteCommand_GmHelp_WhenNotGm_AddsWarningEntry()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 0x01020304, "user1", netManager);
            _registry.RegisterSession(session);

            await _vm.ExecuteSlashCommandAsync("/gmhelp");

            var lastEntry = _vm.Entries[^1];
            Assert.Equal(ConsoleEntryKind.Warning, lastEntry.Kind);
            Assert.Equal("You are not a GM.", lastEntry.Message);
        }
    }
}
