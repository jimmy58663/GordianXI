// tests/Gordian.App.Tests/ViewModels/ConsoleStockUiRestoreTests.cs
using System;
using System.IO;
using System.Threading.Tasks;
using Gordian.App.ViewModels;
using Gordian.Core.Config;
using Gordian.Core.Network;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.App.Tests.ViewModels
{
    /// <summary>
    /// Getting a hidden stock chat back from the main window's Command Console tab (#29, round 3 of the in-game test):
    /// the console must act on the character the viewport shows. It stayed on the first registered character, so with
    /// two logged in, <c>/stockui show chat</c> answered "shown" for the other one and the visible chat stayed hidden.
    /// </summary>
    [Collection("StorageTests")]
    public class ConsoleStockUiRestoreTests : IDisposable
    {
        private readonly string _profiles = Path.Combine(Path.GetTempPath(), "GordianConsoleRestore_" + Guid.NewGuid().ToString("N"));
        private readonly SessionRegistry _registry = new();

        public ConsoleStockUiRestoreTests()
        {
            Directory.CreateDirectory(_profiles);
            GordianStorage.ProfilesDirectory = _profiles;
            CommandConsoleViewModel.UiDispatcher = a => a();
        }

        public void Dispose()
        {
            CommandConsoleViewModel.UiDispatcher = null;
            _registry.Dispose();
            GordianStorage.ProfilesDirectory = null!;
            if (Directory.Exists(_profiles)) Directory.Delete(_profiles, true);
        }

        private static CharacterSession Session(string name, uint id) =>
            new(name, id, "acct", new SessionNetworkManager("127.0.0.1", 54231) { CurrentState = SessionState.ActiveInWorld });

        [Fact]
        public async Task ConsoleRestore_ReachesTheCharacterOnScreen()
        {
            using var vm = new MainWindowViewModel(_registry);
            var first = Session("Gordian", 1001);
            var shown = Session("Knot", 1002);
            _registry.RegisterSession(first);
            _registry.RegisterSession(shown);
            Assert.Same(first, vm.Console.SelectedSession); // the console starts on the first character

            // The viewport shows the second character (Ctrl+Tab), whose chat the player hid in-game.
            vm.Viewport.AddSession(first);
            var tab = vm.Viewport.AddSession(shown);
            vm.Viewport.ActiveTab = tab;
            Assert.True((await shown.ActionService.ExecuteCommandAsync("/stockui hide chat")).Success);
            Assert.True(shown.ActionService.UiVisibility.IsHidden(StockUiElement.Chat));

            // The Command Console tab, as the player uses it: type the command and run it.
            Assert.Same(shown, vm.Console.SelectedSession);
            vm.Console.InputCommand = "/stockui show chat";
            await vm.Console.ExecuteInputAsync();
            Assert.False(shown.ActionService.UiVisibility.IsHidden(StockUiElement.Chat));

            // /stockui reset the same way.
            await shown.ActionService.ExecuteCommandAsync("/stockui hide chat menus");
            await vm.Console.ExecuteSlashCommandAsync("/stockui reset");
            Assert.Equal(StockUiElement.None, shown.ActionService.UiVisibility.Hidden);
            Assert.Equal(StockUiElement.None, first.ActionService.UiVisibility.Hidden);
        }
    }
}
