using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Gordian.App;
using Gordian.App.Graphics;
using Gordian.App.ViewModels;
using Gordian.Core.Config;
using Gordian.Core.Network;
using Gordian.Engine.Input;
using Gordian.Engine.Macros;
using Gordian.Engine.Sessions;
using System.Diagnostics;
using System.Runtime.InteropServices;

// Synthetic events only, on the runner's fresh authenticated display. Never start
// the game App, connect the session, load game data, or attach to an owner's display.
if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("GORDIAN_VIEWPORT_PROBE") != "1") return 77;
GordianStorage.UserSettingsDirectory = Path.Combine(Path.GetTempPath(), "input-settings");
Directory.CreateDirectory(GordianStorage.UserSettingsDirectory);
AppBuilder.Configure<Application>().UsePlatformDetect()
    .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software], EnableIme = false,
        EnableSessionManagement = false, UseDBusMenu = false, UseDBusFilePicker = false }).SetupWithoutStarting();
Application.Current!.Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
using var network = new SessionNetworkManager("127.0.0.1", 54230); // Never connected or started.
using var session = new CharacterSession("Synthetic", 1, "fixture", network);
session.Macros.Library = new MacroLibrary();
session.Macros.Library.Set(new MacroSlot(0, 0, MacroPalette.Ctrl, 0), new Macro("fixture", []));
int macros = 0;
session.Macros.MacroStarted += _ => macros++;
var vm = new ViewportViewModel(enableAutoSave: false);
vm.AddSession(session);
var window = new ViewportWindow { DataContext = vm };
var control = window.FindControl<VeldridViewportControl>("ViewportControl")!;
// Input routing needs the production window, not the unrelated GPU viewport.
// Keep its native child detached so this fixture runs on upstream graphics unchanged.
control.IsVisible = false;
var display = X.XOpenDisplay(IntPtr.Zero);
Require(display != IntPtr.Zero, "isolated X11 connection");
string layout = Environment.GetEnvironmentVariable("GORDIAN_INPUT_LAYOUT") ?? "us";
// German Shift+3 is non-ASCII §, intentionally filtered by the existing stock font policy.
string shifted = layout == "de" ? "!\"$%&/()=" : "!@#$%^&*()";
try
{
    window.Show();
    Require(window.TryGetPlatformHandle()?.Handle != IntPtr.Zero, "real native window exists");
    window.Focus();
    X.XSetInputFocus(display, window.TryGetPlatformHandle()!.Handle, 1, 0);
    X.XSync(display, false);
    // Assert the exact native seam before/after the fix without collecting arbitrary keystrokes.
    bool sawMissingShiftSymbol = false, missingSymbolHandled = false;
    window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
    {
        if (e.Key == Key.D1 && e.KeyModifiers == KeyModifiers.Shift && string.IsNullOrEmpty(e.KeySymbol))
        { sawMissingShiftSymbol = true; missingSymbolHandled |= e.Handled; }
    }, Avalonia.Interactivity.RoutingStrategies.Bubble, true);

    foreach (bool full in new[] { false, true })
    {
        session.Locomotion.Profile = full ? InputProfile.CreateFullNumpad() : InputProfile.CreateCompact();
        Tap(full ? "space" : "Return");
        Pump(() => session.Chat.Input.IsOpen, "native opening key opens chat");
        Require(session.Chat.Input.Text == "", "opening Space is not inserted");
        Tap("a"); Tap("Shift_L", "a"); Tap("Caps_Lock"); Tap("a"); Tap("Shift_L", "a"); Tap("Caps_Lock");
        Require(session.Chat.Input.Text == "aAAa", "case/Caps Lock without duplicates");
        foreach (string key in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" }) Tap("Shift_L", key);
        Require(session.Chat.Input.Text == "aAAa" + shifted, "layout-specific shifted digits entered exactly once; actual=" + session.Chat.Input.Text);
        string before = session.Chat.Input.Text;
        Held(["Tab"], () => Require(session.InputState.WasActionTriggered(InputAction.TargetNearest), "Tab retains target-right binding while chat open"));
        Held(["Shift_L", "Tab"], () => Require(session.InputState.WasActionTriggered(InputAction.TargetPrevious), "Shift+Tab retains target-left binding while chat open"));
        Require(session.Chat.Input.Text == before, "Tab does not modify text or autocomplete");
        int previousMacros = macros;
        Held(["Control_L", "1"], () =>
        {
            Require(!session.InputState.WasActionTriggered(InputAction.MacroCtrl1), "open chat suppresses macro key ingress");
            Require(macros == previousMacros, "open chat does not start macro");
        });
        Require(session.Chat.Input.Text == before, "Ctrl+1 inserts no text");
        Tap("BackSpace");
        Require(session.Chat.Input.Text == before[..^1], "Backspace edits rather than text");
        Tap("Escape");
        Require(!session.Chat.Input.IsOpen, "Escape closes chat");
        Held(["Control_L", "1"], () => Require(macros == previousMacros + 1, "closed-chat Ctrl+1 starts exactly one synthetic empty macro"));
        if (layout == "us")
        {
            Tap("slash");
            Require(session.Chat.Input.IsOpen && session.Chat.Input.Text == "/", "opening slash entered exactly once");
            Tap("Escape");
            Console.WriteLine("PASS: US opening slash de-duplicated");
        }
        Console.WriteLine($"PASS: {layout}/{(full ? "Full" : "Compact")}: native case, shifted digits, Tab bindings, macro isolation, editing");
    }
    // The same routing owns stock macro/name fields. No persisted macro or name is changed.
    var field = session.ActionService.Menus.MacroField;
    field.Open("", 60, (_, _) => { }, escapeKeepsText: false);
    Tap("Shift_L", "1");
    Require(field.Text == "!", "stock editor receives shifted text");
    Tap("Escape");
    Require(!field.IsOpen, "stock editor Escape preserved");
    Require(sawMissingShiftSymbol && !missingSymbolHandled, "X11 missing-symbol press remains unhandled for platform text");
    Console.WriteLine("PASS: real X11 missing-symbol seam and stock text editor; no connections/game data");
    Console.WriteLine("PASS: input fixture complete");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
finally
{
    window.Close();
    // No GPU/native viewport was initialized by this keyboard-only fixture.
    X.XCloseDisplay(display);
}
void Tap(params string[] keys) => Held(keys, () => { });
void Held(string[] keys, Action check)
{
    try
    {
        foreach (var key in keys) Send(key, true);
        Drain();
        // The disconnected fixture has no network/game tick. Evaluate the same production
        // locomotion path once while the real platform events' keys remain held.
        session.Locomotion.Update(TimeSpan.FromMilliseconds(16));
        check();
    }
    finally
    {
        foreach (var key in keys.Reverse()) Send(key, false);
        Drain();
        session.Locomotion.Update(TimeSpan.FromMilliseconds(16));
    }
}
void Send(string name, bool down)
{
    byte key = X.XKeysymToKeycode(display, X.XStringToKeysym(name));
    Require(key != 0, "native key available: " + name);
    Require(X.XTestFakeKeyEvent(display, key, down, 0) != 0, "XTEST accepted synthetic key");
}
void Drain()
{
    X.XSync(display, false);
    var clock = Stopwatch.StartNew();
    Pump(() => clock.ElapsedMilliseconds > 60, "synthetic key dispatch");
}
static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static void Pump(Func<bool> condition, string message)
{
    using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
    timer.Tick += (_, _) => { if (condition()) cancel.Cancel(); };
    timer.Start();
    try { Dispatcher.UIThread.MainLoop(cancel.Token); }
    finally { timer.Stop(); }
    Require(condition(), message);
}
static class X
{
    [DllImport("libX11.so.6")] internal static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] internal static extern int XCloseDisplay(IntPtr d);
    [DllImport("libX11.so.6")] internal static extern int XSetInputFocus(IntPtr d, IntPtr w, int revert, ulong time);
    [DllImport("libX11.so.6")] internal static extern int XSync(IntPtr d, bool discard);
    [DllImport("libX11.so.6")] internal static extern ulong XStringToKeysym(string name);
    [DllImport("libX11.so.6")] internal static extern byte XKeysymToKeycode(IntPtr d, ulong symbol);
    [DllImport("libXtst.so.6")] internal static extern int XTestFakeKeyEvent(IntPtr d, uint key, bool down, ulong delay);
}
