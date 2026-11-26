using System.Buffers.Binary;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Gordian.App.ViewModels;
using Gordian.Core.Network;
using Gordian.Core.Resources;
using Gordian.Engine.Input;
using Gordian.Engine.Sessions;

namespace Gordian.App.Tests.Services;

[CollectionDefinition("Auto-translate viewport routing", DisableParallelization = true)]
public sealed class AutoTranslateRoutingCollection;

/// <summary>
/// Real viewport tunnelling handlers and session on a headless dispatcher: no renderer,
/// game resources or server connection. This does not test native OS event generation.
/// </summary>
[Collection("Auto-translate viewport routing")]
public class AutoTranslateKeyRoutingTests
{
    [Theory]
    [InlineData(KeyModifiers.None, InputAction.TargetNearest)]
    [InlineData(KeyModifiers.Shift, InputAction.TargetPrevious)]
    public Task OpenChat_TabCompletesWithoutFeedingTargetCycle(KeyModifiers modifiers, InputAction targetAction) => Run(() =>
    {
        using var session = Session();
        using var shell = new Shell(session);
        var input = session.Chat.Input;
        input.Catalog = Catalog();
        int sends = 0;
        input.Submitted += (_, _) => sends++;
        input.Open();
        input.InsertText("/p Syn");

        Assert.True(Down(shell.Window, Key.Tab, modifiers).Handled);
        Assert.Equal("/p Synthetic greeting!", input.Text);
        Assert.Single(input.RichText.Phrases);
        Assert.False(input.Completion.IsOpen);
        Assert.True(input.IsOpen);
        Assert.Equal(0, sends);
        session.InputState.Update(session.Locomotion.Profile, TimeSpan.FromMilliseconds(16));
        Assert.False(session.InputState.IsKeyHeld(GordianKey.Tab));
        Assert.False(session.InputState.IsActionHeld(InputAction.TargetNearest));
        Assert.False(session.InputState.IsActionHeld(InputAction.TargetPrevious));
        Up(shell.Window, Key.Tab);

        // The same production route must retain gameplay targeting after routed cancellation.
        Assert.True(Down(shell.Window, Key.Escape).Handled);
        Assert.False(input.IsOpen);
        Up(shell.Window, Key.Escape);
        Assert.True(Down(shell.Window, Key.Tab, modifiers).Handled);
        session.InputState.Update(session.Locomotion.Profile, TimeSpan.FromMilliseconds(16));
        Assert.True(session.InputState.IsKeyHeld(GordianKey.Tab));
        Assert.True(session.InputState.IsActionHeld(targetAction));
        Up(shell.Window, Key.Tab);
        Assert.False(session.InputState.IsKeyHeld(GordianKey.Tab));
        Assert.Equal(0, sends);
    });

    [Fact]
    public Task Escape_CancelsCompletionBeforeClosingDraft() => Run(() =>
    {
        using var session = Session();
        using var shell = new Shell(session);
        var input = session.Chat.Input;
        input.Catalog = Catalog();
        input.Open();
        input.InsertText("/p ");
        Assert.True(Down(shell.Window, Key.Tab).Handled);
        Assert.True(input.Completion.IsOpen);
        Assert.False(session.InputState.IsKeyHeld(GordianKey.Tab));
        Up(shell.Window, Key.Tab);
        Assert.True(Down(shell.Window, Key.Escape).Handled);
        Assert.False(input.Completion.IsOpen);
        Assert.True(input.IsOpen);
        Assert.Equal("/p ", input.Text);
        Assert.Empty(input.RichText.Phrases);
        Up(shell.Window, Key.Escape);
        Assert.True(Down(shell.Window, Key.Escape).Handled);
        Assert.False(input.IsOpen);
        Up(shell.Window, Key.Escape);
    });

    private static CharacterSession Session() => new(
        "AutoRouting_" + Guid.NewGuid().ToString("N"), 1, "fixture",
        new SessionNetworkManager("127.0.0.1", 54230), new InputProfileHub(InputProfile.CreateCompact()));

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
            Window = new ViewportWindow("AutoRouting_" + Guid.NewGuid().ToString("N"));
            Window.FindControl<Gordian.App.Graphics.VeldridViewportControl>("ViewportControl")!.IsVisible = false;
            Window.DataContext = vm;
        }
        public void Dispose() => Window.Close();
    }

    private static KeyEventArgs Down(ViewportWindow window, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers };
        window.RaiseEvent(e);
        return e;
    }

    private static void Up(ViewportWindow window, Key key) =>
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key });

    private static AutoTranslateCatalog Catalog()
    {
        var bytes = new byte[101];
        bytes[0] = bytes[1] = bytes[76] = bytes[77] = 2;
        bytes[2] = bytes[78] = 1;
        bytes[79] = 5;
        bytes[80] = 20;
        Encoding.ASCII.GetBytes("Fixture").CopyTo(bytes, 4);
        Encoding.ASCII.GetBytes("Synthetic greeting!").CopyTo(bytes, 81);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(68), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(72), 25);
        return AutoTranslateCatalog.Parse(bytes);
    }
}
