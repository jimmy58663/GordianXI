using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Gordian.App.ViewModels;
using Gordian.Core.Network;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Input;
using Gordian.Engine.Sessions;
using Gordian.Engine.Ui;

namespace Gordian.App.Tests.Services;

[CollectionDefinition("Viewport keyboard routing", DisableParallelization = true)]
public sealed class ViewportKeyboardRoutingCollection;

/// <summary>
/// Headless integration through the real window's registered tunnelling handlers and session.
/// Routed key/text events exercise production ordering without a native renderer, game or server.
/// These tests do not claim native platform event generation or compositor coverage.
/// </summary>
[Collection("Viewport keyboard routing")]
public class ViewportKeyboardRoutingTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(16);

    [Theory]
    [InlineData(Key.B, PhysicalKey.B, "b", KeyModifiers.None, GordianKey.B)]
    [InlineData(Key.P, PhysicalKey.P, "p", KeyModifiers.Control, GordianKey.P)]
    [InlineData(Key.P, PhysicalKey.P, "p", KeyModifiers.Alt, GordianKey.P)]
    public Task FullAssignmentCapture_OwnsTypingAndChatShortcutsUntilRelease(
        Key key, PhysicalKey physical, string text, KeyModifiers modifiers, GordianKey gordianKey) => Run(() =>
    {
        var hub = new InputProfileHub(InputProfile.CreateDefault());
        using var session = Session(hub);
        using var shell = new Shell(session);
        var window = shell.Window;
        var menus = session.ActionService.Menus;
        OpenCapture(menus);

        Down(window, key, physical, text, modifiers);
        Text(window, text); // also inject the text a backend might deliver after the press
        Assert.False(session.Chat.Input.IsOpen);
        Assert.Equal(string.Empty, session.Chat.Input.Text);
        Pump(session);
        Assert.Equal(new[] { new InputChord(gordianKey, Gordian.App.Services.AvaloniaInputMapper.ToInputModifiers(modifiers)) },
            session.Locomotion.Profile.GetChords(InputAction.MoveForward));
        Assert.True(menus.IsCapturingKeyboard); // assignment remains suppressed until release
        Assert.False(session.InputState.IsActionHeld(InputAction.MoveForward));

        Down(window, key, physical, text, modifiers); // native repeat after capture committed
        Text(window, text);
        Pump(session);
        Assert.False(session.Chat.Input.IsOpen);
        Assert.False(session.InputState.IsActionHeld(InputAction.MoveForward));
        Up(window, key, physical);
        Pump(session);
        Assert.False(menus.IsCapturingKeyboard);
        Assert.False(session.InputState.IsKeyHeld(gordianKey));

        Down(window, key, physical, text, modifiers);
        Text(window, text);
        Pump(session);
        Assert.False(session.Chat.Input.IsOpen);
        Assert.True(session.InputState.IsActionHeld(InputAction.MoveForward));
        Up(window, key, physical);
        Pump(session);

        // A different, fresh Full typing key must still open a draft and insert only TextInput.
        var fresh = Down(window, Key.X, PhysicalKey.X, "x");
        Assert.False(fresh.Handled);
        Assert.True(session.Chat.Input.IsOpen);
        Assert.Equal(string.Empty, session.Chat.Input.Text);
        Text(window, "x");
        Assert.Equal("x", session.Chat.Input.Text);
        Up(window, Key.X, PhysicalKey.X);
    });

    [Fact]
    public Task SharedFullChange_DoesNotReinterpretHeldWUntilReleaseAndFreshPress() => Run(() =>
    {
        var profile = InputProfile.CreateDefault();
        profile.KeyboardLayout!.Mode = KeyboardMode.Compact2;
        var hub = new InputProfileHub(profile);
        using var session = Session(hub);
        using var sibling = Session(hub);
        using var shell = new Shell(session);
        var window = shell.Window;

        Down(window, Key.W, PhysicalKey.W, "w");
        Text(window, "w");
        Pump(session);
        Assert.True(session.InputState.IsKeyHeld(GordianKey.W));
        Assert.True(session.InputState.IsActionHeld(InputAction.MoveForward));
        Assert.False(session.Chat.Input.IsOpen);

        // Exercise the actual shared notification, not a direct call to BlockHeldKeysUntilRelease.
        sibling.ActionService.Menus.SetSetting(StockUiSettingKey.KeyboardSize, 0);
        Assert.Equal(KeyboardMode.Full, session.Locomotion.Profile.KeyboardLayout!.Mode);
        for (int repeat = 0; repeat < 3; repeat++)
        {
            Down(window, Key.W, PhysicalKey.W, "w");
            Text(window, "w");
            Pump(session);
            Assert.False(session.Chat.Input.IsOpen);
            Assert.Equal(string.Empty, session.Chat.Input.Text);
            Assert.True(session.InputState.IsKeyHeld(GordianKey.W));
            Assert.False(session.InputState.IsActionHeld(InputAction.MoveForward));
        }

        Up(window, Key.W, PhysicalKey.W);
        Pump(session);
        Assert.False(session.InputState.IsKeyHeld(GordianKey.W));
        Assert.False(session.Chat.Input.IsOpen);
        var fresh = Down(window, Key.W, PhysicalKey.W, "w");
        Assert.False(fresh.Handled); // Windows must be allowed to produce the ensuing TextInput.
        Assert.True(session.Chat.Input.IsOpen);
        Assert.Equal(string.Empty, session.Chat.Input.Text);
        Text(window, "w");
        Assert.Equal("w", session.Chat.Input.Text);
        Down(window, Key.W, PhysicalKey.W, "w");
        Text(window, "w");
        Assert.Equal("ww", session.Chat.Input.Text); // normal repeat typing in an editor remains valid
        Up(window, Key.W, PhysicalKey.W);
    });

    private static CharacterSession Session(InputProfileHub hub) => new(
        "Routing_" + Guid.NewGuid().ToString("N"), 1, "fixture",
        new SessionNetworkManager("127.0.0.1", 54230), hub);

    private static async Task Run(Action test)
    {
        await using var app = HeadlessUnitTestSession.StartNew(typeof(Application));
        await app.Dispatch(test, TestContext.Current.CancellationToken);
    }

    private sealed class Shell : IDisposable
    {
        public ViewportWindow Window { get; }
        public Shell(CharacterSession session)
        {
            var vm = new ViewportViewModel();
            vm.ActiveTab = vm.AddSession(session);
            Window = new ViewportWindow("KeyboardRouting_" + Guid.NewGuid().ToString("N"));
            Window.FindControl<Gordian.App.Graphics.VeldridViewportControl>("ViewportControl")!.IsVisible = false;
            Window.DataContext = vm;
        }
        public void Dispose() => Window.Close();
    }

    private static KeyEventArgs Down(ViewportWindow window, Key key, PhysicalKey physical, string symbol,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent,
            Key = key, PhysicalKey = physical, KeySymbol = symbol, KeyModifiers = modifiers };
        window.RaiseEvent(e);
        return e;
    }

    private static void Up(ViewportWindow window, Key key, PhysicalKey physical) =>
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, PhysicalKey = physical });

    private static void Text(ViewportWindow window, string text) =>
        window.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });

    private static void Pump(CharacterSession session)
    {
        session.InputState.SuppressKeyboardActions = session.ActionService.Menus.IsCapturingKeyboard;
        session.InputState.Update(session.Locomotion.Profile, Tick);
        session.ActionService.Menus.ProcessInput(session.InputState, Tick);
    }

    private static void OpenCapture(StockUiMenuController menus)
    {
        UiMenuButton Button(int id) => new() { ButtonId = (short)id, Width = 256, Height = 16 };
        menus.Library = UiResourceLibrary.FromDefinitions(new[]
        {
            new UiMenuDefinition { Name = StockUiConfigPages.Misc2Page,
                Frame = new UiMenuFrame { Width = 366, Height = 312 },
                Buttons = new List<UiMenuButton> { Button(StockUiMenuController.KeyAssignMovementButton) } },
            new UiMenuDefinition { Name = StockUiMenuController.KeyAssignMovementMenu,
                Frame = new UiMenuFrame { Width = 366, Height = 248 },
                Buttons = Enumerable.Range(1, 14).Select(Button).ToList() },
        });
        Assert.True(menus.Open(StockUiConfigPages.Misc2Page));
        menus.Top!.SelectedButtonId = StockUiMenuController.KeyAssignMovementButton;
        menus.Activate();
        menus.Top!.SelectedButtonId = 1;
        menus.Activate();
        Assert.True(menus.IsCapturingKeyboard);
        Assert.Equal(StockUiMenuController.KeyCapturePrompt, menus.Top.Rows[0].Value);
    }
}
