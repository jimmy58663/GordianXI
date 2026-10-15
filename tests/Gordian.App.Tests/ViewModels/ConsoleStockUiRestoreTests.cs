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

        [Fact]
        public async Task Console_FollowsTheFocusedPopOut_AndKeepsAHandPickUntilTheNextFocus()
        {
            using var vm = new MainWindowViewModel(_registry);
            var main = Session("Gordian", 1001);
            var popped = Session("Knot", 1002);
            var third = Session("Claude", 1003);
            _registry.RegisterSession(main);
            _registry.RegisterSession(popped);
            _registry.RegisterSession(third);

            // The windows as ViewportWindowManager reports them: the main window shows Gordian, a pop-out shows Knot.
            var tracker = new Gordian.App.Services.WindowFocusTracker();
            vm.AttachViewportFocus(tracker);
            object mainWindow = new(), popOut = new();
            tracker.SetWindowSession(mainWindow, main);
            tracker.SetWindowSession(popOut, popped);

            tracker.Activated(popOut);
            Assert.Same(popped, vm.Console.SelectedSession);

            // /stockui from the console reaches the pop-out's character.
            await popped.ActionService.ExecuteCommandAsync("/stockui hide chat");
            await vm.Console.ExecuteSlashCommandAsync("/stockui show chat");
            Assert.False(popped.ActionService.UiVisibility.IsHidden(StockUiElement.Chat));

            // The player clicks into the main window and picks Claude by hand: it holds while no viewport has the focus.
            tracker.Deactivated(popOut);
            vm.Console.SelectedSession = third;
            tracker.SetAnyWindowActive(false);
            tracker.SetAnyWindowActive(true);
            Assert.Same(third, vm.Console.SelectedSession);

            // Focusing a viewport again follows it, the same pop-out included.
            tracker.Activated(popOut);
            Assert.Same(popped, vm.Console.SelectedSession);
            tracker.Deactivated(popOut);
            tracker.Activated(mainWindow);
            Assert.Same(main, vm.Console.SelectedSession);

            // The focused main window switching character (Ctrl+Tab) is followed too.
            tracker.SetWindowSession(mainWindow, third);
            Assert.Same(third, vm.Console.SelectedSession);
        }
    }
}
