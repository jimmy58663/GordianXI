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
using System.Reflection;
using System.Runtime.InteropServices;

// Synthetic events only, on the runner's fresh authenticated display. Never start
// the game App, connect the session, or attach to an owner's display. Optional art
// validation reads an explicitly supplied install without copying any resources.
if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("GORDIAN_VIEWPORT_PROBE") != "1") return 77;
GordianStorage.RootDataDirectory = Path.Combine(Path.GetTempPath(), "input-data");
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
    window.AddHandler(InputElement.KeyDownEvent, (_, e) => { if (e.PhysicalKey.ToString().StartsWith("NumPad")) Console.WriteLine($"KEYPAD: key={e.Key} physical={e.PhysicalKey}"); }, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
    bool sawMissingShiftSymbol = false, missingSymbolHandled = false;
    window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
    {
        if (e.Key == Key.D1 && e.KeyModifiers == KeyModifiers.Shift && string.IsNullOrEmpty(e.KeySymbol))
        { sawMissingShiftSymbol = true; missingSymbolHandled |= e.Handled; }
    }, Avalonia.Interactivity.RoutingStrategies.Bubble, true);

    Require(!session.ActionService.UiSettings.HasValue(Gordian.Engine.Ui.StockUiSettingKey.KeyboardSize), "fresh character has no saved size");
    Tap("a");
    Require(session.Chat.Input.IsOpen && session.Chat.Input.Text == "a", "displayed Full default types before any toggle");
    Tap("Escape");
    // Exercise the actual dashboard commands against the same shared hub/session, not factory helpers.
    var controls = new ControlsInputViewModel(Path.Combine(GordianStorage.UserSettingsDirectory, "preset-fixture.json"), InputProfileHub.Default);
    controls.SetSession(session);
    foreach (int selectedMode in new[] { 0, 1, 2, 0 })
    {
        (selectedMode == 0 ? controls.LoadFullNumpadPresetCommand : selectedMode == 1 ? controls.LoadCompact1PresetCommand : controls.LoadCompactPresetCommand).Execute(null);
        Require(session.ActionService.UiSettings.GetValue(Gordian.Engine.Ui.StockUiSettingKey.KeyboardSize) == selectedMode, "preset UI changes character mode");
        for (int numLock = 0; numLock < 2; numLock++)
        {
            // Assert the real selected mode's gameplay action with either Num Lock state.
            if (NumLockOn() != (numLock == 1)) Tap("Num_Lock");
            Held([selectedMode == 0 ? "KP_8" : "w"], () => Require(session.InputState.IsActionHeld(InputAction.MoveForward), "preset UI resolved movement in both Num Lock states"));
            Require(!session.InputState.IsActionHeld(InputAction.MoveForward), "preset movement released");
        }
        Tap("Control_L", "p");
        Require(session.Chat.Input.IsOpen && session.Chat.Input.Mode == Gordian.Engine.Ui.ChatInputMode.Party, "preset Ctrl+P opens party draft");
        Tap("Escape");
        session.Chat.OnChatMessage(new Gordian.Core.Network.Packets.ChatMessage(
            Gordian.Core.Network.Packets.ChatMessageType.Tell, "Incoming", "fixture", 0, false, 0, 0, DateTime.UtcNow, false));
        session.Chat.Input.TellTarget = "Outgoing";
        Tap("Alt_L", "r");
        Require(session.Chat.Input.Text == "/tell Incoming ", "native reply uses received teller, not outgoing partner");
        Tap("Escape");
        session.World.UpsertEntity(new Gordian.Core.World.PlayerEntity(1, 1) { Name = "Self", IsSpawned = true, Hpp = 100 });
        session.World.UpsertEntity(new Gordian.Core.World.PlayerEntity(2, 2) { Name = "Other", IsSpawned = true, Hpp = 100 });
        session.World.UpsertEntity(new Gordian.Core.World.WorldEntity(3, 3, Gordian.Core.World.EntityType.Npc) { Name = "Npc", IsSpawned = true });
        session.Party.UpsertMember(new Gordian.Core.World.PartyMember { ServerId = 1, Name = "Self", PartyNumber = 0 });
        foreach (byte party in new byte[] { 1, 2 })
        {
            uint id = (uint)(party + 3);
            session.Party.UpsertMember(new Gordian.Core.World.PartyMember { ServerId = id, Name = "Ally" + id, PartyNumber = party });
            session.World.UpsertEntity(new Gordian.Core.World.PlayerEntity(id, (ushort)id) { Name = "Ally" + id, IsSpawned = true, Hpp = 100, Position = new System.Numerics.Vector3(10, 0, 0) });
        }
        foreach (var (key, id) in new[] { ("F7", 1u), ("F8", 3u), ("F9", 2u), ("F10", 1u), ("F11", 4u), ("F12", 5u) })
        {
            Tap(key);
            Require(session.ActionService.CurrentTarget?.ServerId == id, "native function key selects real target " + key);
        }
        session.ActionService.ClearTarget();
        var menus = session.ActionService.Menus;
        menus.Library = Gordian.Core.Resources.Ui.UiResourceLibrary.FromDefinitions(new[] {
            new Gordian.Core.Resources.Ui.UiMenuDefinition { Name = Gordian.Engine.Ui.StockUiActionMenu.AbilityListMenu,
                Frame = new Gordian.Core.Resources.Ui.UiMenuFrame { Width = 200, Height = 200 },
                Buttons = new[] { new Gordian.Core.Resources.Ui.UiMenuButton { ButtonId = 1, Width = 80, Height = 16 } } } });
        menus.ActionRowsProvider = (_, _, _) => Array.Empty<Gordian.Engine.Ui.StockUiActionRow>();
        Tap("Control_L", "j");
        Require(menus.Top?.ActionList == Gordian.Engine.Ui.StockUiActionList.JobAbilities, "native Ctrl+J opens real job ability list");
        Tap("Escape");
        menus.Library = null;
        var visibility = session.ActionService.UiVisibility;
        visibility.Hide("probe-addon", Gordian.Engine.Ui.StockUiElement.Party);
        if (selectedMode == 2 && layout == "de") Tap("ISO_Level3_Shift", "9");
        else Tap(selectedMode == 2 ? "bracketright" : "Scroll_Lock");
        Require(visibility.Hidden == Gordian.Engine.Ui.StockUiElement.All, "native windows shortcut hides stock UI");
        if (selectedMode == 2 && layout == "de") Tap("ISO_Level3_Shift", "9");
        else Tap(selectedMode == 2 ? "bracketright" : "Scroll_Lock");
        Require(visibility.Hidden == Gordian.Engine.Ui.StockUiElement.Party, "native windows restore preserves independent visibility");
        visibility.Release("probe-addon");
        if (selectedMode == 2 && layout == "de")
        {
            InputProfileHub.Default.Edit(p => p.KeyboardLayout!.Overrides[nameof(InputAction.CycleLogWindow)] = new() { "D9" });
            Tap("ISO_Level3_Shift", "9");
            Require(session.Chat.IsLogSelected && visibility.Hidden == Gordian.Engine.Ui.StockUiElement.None,
                "explicit German digit override wins over inherited symbol shortcut");
            session.Chat.ReleaseLogWindow();
            InputProfileHub.Default.Edit(p => p.KeyboardLayout!.Overrides.Remove(nameof(InputAction.CycleLogWindow)));
        }
        var screenshotHandler = session.ActionService.ScreenshotHandler;
        int screenshots = 0;
        session.ActionService.ScreenshotHandler = _ => { screenshots++; return true; };
        if (selectedMode == 2 && layout == "de") Tap("ISO_Level3_Shift", "8");
        else Tap(selectedMode == 2 ? "bracketleft" : "Print");
        Require(screenshots == 1, "native screenshot shortcut reaches existing handler once");
        Require(!session.InputState.IsKeyHeld(GordianKey.OemOpenBracket), "translated screenshot key released");
        session.ActionService.ScreenshotHandler = screenshotHandler;
        session.Party.ClearParty();
        session.ActionService.ClearTarget();
        foreach (uint id in new[] { 1u, 2u, 3u, 4u, 5u }) session.World.RemoveEntity(id);
    }
    if (NumLockOn()) Tap("Num_Lock");
    Console.WriteLine("PASS: actual preset UI Full/Compact1/Compact2/Full movement and chat shortcuts");
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
        Held(["Tab"], () => Require(!session.InputState.WasActionTriggered(InputAction.TargetNearest), "Tab does not target while chat open"));
        Held(["Shift_L", "Tab"], () => Require(!session.InputState.WasActionTriggered(InputAction.TargetPrevious), "Shift+Tab does not target while chat open"));
        Require(session.Chat.Input.Completion.IsOpen, "native Tab opens completion");
        Tap("Escape");
        Require(session.Chat.Input.IsOpen, "Escape dismisses menu without draft loss");
        Require(session.Chat.Input.Text == before, "unmatched Tab preserves draft");
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
    // Keyboard Size is a character setting, independent of the dashboard preset.
    session.Locomotion.Profile = InputProfile.CreateCompact();
    session.ActionService.UiSettings.SetValue(Gordian.Engine.Ui.StockUiSettingKey.KeyboardSize, 0);
    Held(["w"], () =>
    {
        Require(session.Chat.Input.IsOpen && session.Chat.Input.Text == "w", "selected Full starts chat with native first letter once");
        Require(!session.InputState.IsKeyHeld(GordianKey.W), "initiating letter never enters gameplay");
    });
    Tap("Escape");
    Tap("Shift_L", "1");
    Require(session.Chat.Input.IsOpen && session.Chat.Input.Text == "!", "empty-symbol shifted first character uses native text once");
    Tap("Escape");
    Tap("Caps_Lock"); Tap("a"); Tap("Caps_Lock");
    Require(session.Chat.Input.Text == "A", "Caps Lock initiating character uses native case once");
    Tap("Escape");
    foreach (string modifier in new[] { "Control_L", "Alt_L", "Super_L" })
    {
        Tap(modifier, "w");
        Require(!session.Chat.Input.IsOpen, "modified shortcut never auto-opens chat");
    }
    session.Locomotion.Profile = InputProfile.CreateFullNumpad();
    Held(["KP_8"], () =>
    {
        Require(!session.Chat.Input.IsOpen, "Full keypad remains gameplay");
        Require(session.InputState.IsKeyHeld(GordianKey.NumPad8), "Full keypad reaches native game input");
    });
    // Verify physical identity independently of presets: modes must never rewrite custom chords.
    var originalProfile = session.Locomotion.Profile;
    session.Locomotion.Profile = new InputProfile("Synthetic keypad identity", "No actions");
    session.ActionService.UiVisibility.Hide("keypad-fixture", Gordian.Engine.Ui.StockUiElement.Chat);
    foreach (int mode in new[] { 0, 1, 2 })
    {
        session.ActionService.UiSettings.SetValue(Gordian.Engine.Ui.StockUiSettingKey.KeyboardSize, mode);
        foreach (bool numLock in new[] { false, true })
        {
            // Each mode starts OFF; toggle ON for its second pass and OFF after it.
            if (numLock) Tap("Num_Lock");
            Require(X.XkbGetNamedIndicator(display, X.XInternAtom(display, "Num Lock", false), IntPtr.Zero, out int state, IntPtr.Zero, IntPtr.Zero) != 0
                && (state != 0) == numLock, "verified native Num Lock state");
            foreach (var (symbol, key) in new[] {
                ("KP_0", GordianKey.NumPad0), ("KP_1", GordianKey.NumPad1),
                ("KP_2", GordianKey.NumPad2), ("KP_3", GordianKey.NumPad3),
                ("KP_4", GordianKey.NumPad4), ("KP_5", GordianKey.NumPad5),
                ("KP_6", GordianKey.NumPad6), ("KP_7", GordianKey.NumPad7),
                ("KP_8", GordianKey.NumPad8), ("KP_9", GordianKey.NumPad9),
                ("KP_Delete", GordianKey.NumPadDecimal), ("KP_Divide", GordianKey.NumPadDivide),
                ("KP_Multiply", GordianKey.NumPadMultiply), ("KP_Subtract", GordianKey.NumPadSubtract),
                ("KP_Add", GordianKey.NumPadAdd), ("KP_Enter", GordianKey.NumPadEnter) })
            {
                Held([symbol], () => Require(session.InputState.IsKeyHeld(key), $"mode={mode} numLock={numLock} physical {key} ingress"));
                Require(!session.InputState.IsKeyHeld(key), $"{key} release");
            }
            Held(["Up"], () => Require(session.InputState.IsKeyHeld(GordianKey.Up) && !session.InputState.IsKeyHeld(GordianKey.NumPad8), "dedicated Up stays distinct"));
            if (numLock) Tap("Num_Lock");
        }
    }
    // A Num Lock change while held must not strand the physical gameplay key.
    Held(["KP_8"], () => Tap("Num_Lock"));
    Require(!session.InputState.IsKeyHeld(GordianKey.NumPad8), "release across Num Lock transition");
    Tap("Num_Lock");
    session.ActionService.UiVisibility.ReleaseAll();
    session.Locomotion.Profile = originalProfile;
    session.ActionService.UiSettings.SetValue(Gordian.Engine.Ui.StockUiSettingKey.KeyboardSize, 0);
    Held(["KP_8"], () => Require(session.InputState.IsActionHeld(InputAction.MoveForward), "Full preset movement resolves with Num Lock OFF"));
    Tap("Num_Lock");
    Held(["KP_8"], () => Require(session.InputState.IsActionHeld(InputAction.MoveForward), "Full preset movement resolves with Num Lock ON"));
    Tap("Num_Lock");
    Tap("Return"); Tap("a"); Tap("b"); Tap("KP_Left"); Tap("x");
    Require(session.Chat.Input.Text == "axb", "Num Lock OFF preserves logical editing navigation");
    Tap("Num_Lock"); Tap("KP_4"); Tap("Num_Lock");
    Require(session.Chat.Input.Text == "ax4b", "Num Lock ON preserves platform keypad text");
    Tap("Escape");
    Console.WriteLine("PASS: 3 modes x 2 Num Lock states x 16 keypad keys; distinct navigation and transition release");
    Held(["F1"], () => Require(!session.Chat.Input.IsOpen && session.InputState.IsKeyHeld(GordianKey.F1), "Full function key remains gameplay"));
    session.ActionService.ClearTarget(); // F1 now has a synthetic self to select; Space opens only outside Confirm context.
    session.Locomotion.Profile = InputProfile.CreateCompact();
    foreach (int compact in new[] { 1, 2 })
    {
        session.ActionService.UiSettings.SetValue(Gordian.Engine.Ui.StockUiSettingKey.KeyboardSize, compact);
        Held(["w"], () =>
        {
            Require(!session.Chat.Input.IsOpen, "selected Compact does not type-to-chat");
            Require(session.InputState.IsKeyHeld(GordianKey.W), "Compact movement binding preserved");
        });
        Tap("space");
        Require(session.Chat.Input.IsOpen && session.Chat.Input.Text == "", $"Compact Space opens empty chat: open={session.Chat.Input.IsOpen} text={session.Chat.Input.Text} target={session.ActionService.CurrentTarget?.ServerId} selecting={session.Chat.IsSelecting} menus={session.ActionService.Menus.IsOpen}");
        Tap("Escape");
    }
    Console.WriteLine("PASS: selected Keyboard Size Full/Compact 1/Compact 2 routes first letter without gameplay leakage");
    session.ActionService.UiSettings.SetValue(Gordian.Engine.Ui.StockUiSettingKey.KeyboardSize, 0);
    session.ActionService.UiVisibility.Hide("fixture", Gordian.Engine.Ui.StockUiElement.Chat);
    Held(["w"], () => Require(!session.Chat.Input.IsOpen && session.InputState.IsKeyHeld(GordianKey.W), "hidden stock chat does not capture typing"));
    session.ActionService.UiVisibility.ReleaseAll();
    // Synthetic catalog, real XTEST selection; never connected or sent to a server.
    var dictionary = new byte[101];
    dictionary[0] = dictionary[1] = dictionary[76] = dictionary[77] = 2;
    dictionary[2] = dictionary[78] = 1; dictionary[79] = 5; dictionary[80] = 20;
    System.Text.Encoding.ASCII.GetBytes("Fixture").CopyTo(dictionary, 4);
    System.Text.Encoding.ASCII.GetBytes("Synthetic greeting!").CopyTo(dictionary, 81);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(dictionary.AsSpan(68), 1);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(dictionary.AsSpan(72), 25);
    session.Chat.Input.Catalog = Gordian.Core.Resources.AutoTranslateCatalog.Parse(dictionary);
    int submitted = 0;
    void CountSubmission(Gordian.Core.Network.ChatText _, Gordian.Engine.Ui.ChatInputMode __) => submitted++;
    session.Chat.Input.RichSubmitted += CountSubmission;
    Tap("Return"); Tap("s"); Tap("y"); Tap("n"); Tap("Tab");
    Require(!session.Chat.Input.Completion.IsOpen, "unique native Tab closes completion immediately");
    Require(session.Chat.Input.IsOpen && session.Chat.Input.RichText.Phrases.Count == 1 && submitted == 0, "Tab selects identity without send");
    Require(session.Chat.Input.Text == "Synthetic greeting!", "native candidate inserted");
    using (var switchNetwork = new SessionNetworkManager("127.0.0.1", 54230))
    using (var switchSession = new CharacterSession("CompletionSwitch", 99, "fixture", switchNetwork))
    {
        var original = vm.ActiveTab;
        var next = vm.AddSession(switchSession);
        vm.ActiveTab = original;
        Tap("Control_L", "Tab");
        Require(vm.ActiveTab == next, "Ctrl+Tab switches the actual active character");
        Require(!switchSession.Chat.Input.IsOpen && !switchSession.Chat.Input.Completion.IsOpen, "switch shortcut does not open another character's chat");
        Tap("Control_L", "Tab");
        Require(vm.ActiveTab == original, "Ctrl+Tab returns to original character");
        Require(!session.Chat.Input.Completion.IsOpen && session.Chat.Input.RichText.Phrases.Count == 1, "switch preserves the draft phrase without completion");
        vm.RemoveSession(switchSession);
    }
    Tap("BackSpace"); Require(session.Chat.Input.RichText.Phrases.Count == 0, "native edit de-tokenizes");
    Tap("Escape"); Tap("Return"); Tap("Tab");
    Require(session.Chat.Input.Completion.Rows[0].Key == 0, "empty browse category");
    Tap("Return"); Require(session.Chat.Input.Completion.Rows[0].Key != 0, "category opens phrases");
    CompletionRenderProbe.Run(session.Chat.Input);
    Tap("Return");
    Require(session.Chat.Input.IsOpen && session.Chat.Input.RichText.Phrases.Count == 1 && submitted == 0, "category Enter selects without sending");
    Tap("Escape");
    session.Chat.Input.RichSubmitted -= CountSubmission;

    // Isolate native command submission at the dispatcher seam: no session/network action executes.
    var executeBefore = session.Chat.Execute;
    var richBefore = session.Chat.ExecuteRich;
    int commands = 0, richSends = 0;
    session.Chat.Execute = (line, _) =>
    {
        Require(line == "/shutdown", "native submitted canonical shutdown"); commands++;
        return System.Threading.Tasks.Task.FromResult(Gordian.Engine.Actions.PlayerActionResult.Ok("fixture only"));
    };
    session.Chat.ExecuteRich = (_, _) =>
    {
        richSends++;
        return System.Threading.Tasks.Task.FromResult(Gordian.Engine.Actions.PlayerActionResult.Ok("fixture only"));
    };
    try
    {
        var commandDictionary = new byte[91];
        commandDictionary[0] = commandDictionary[1] = commandDictionary[76] = commandDictionary[77] = 2;
        commandDictionary[2] = commandDictionary[78] = 13;
        commandDictionary[79] = 1; commandDictionary[80] = 10;
        System.Text.Encoding.ASCII.GetBytes("Fixture").CopyTo(commandDictionary, 4);
        System.Text.Encoding.ASCII.GetBytes("/shutdown").CopyTo(commandDictionary, 81);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(commandDictionary.AsSpan(68), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(commandDictionary.AsSpan(72), 15);
        session.Chat.Input.Catalog = Gordian.Core.Resources.AutoTranslateCatalog.Parse(commandDictionary);
        Tap("Return"); Tap(layout == "de" ? new[] { "Shift_L", "7" } : new[] { "slash" }); Tap("s"); Tap("h"); Tap("u"); Tap("t"); Tap("d"); Tap("o"); Tap("w"); Tap("n");
        Require(session.Chat.Input.Text == "/shutdown" && session.Chat.Input.RichText.Phrases.Count == 0, "plain native shutdown typed exactly");
        Tap("Return"); Require(commands == 1 && richSends == 0, "plain shutdown reaches dispatcher once");
        Tap("Return"); Tap(layout == "de" ? new[] { "Shift_L", "7" } : new[] { "slash" }); Tap("s"); Tap("h"); Tap("u"); Tap("Tab");
        Require(session.Chat.Input.Text == "/shutdown" && session.Chat.Input.RichText.Phrases.Count == 1 && commands == 1, "single Tab accepts shutdown without executing");
        Tap("Return"); Require(commands == 2 && richSends == 0, "explicit Enter routes command phrase once");
        Tap("Return"); Tap("x"); Tap("Tab"); Tap("Return");
        Require(session.Chat.Input.IsOpen && commands == 2 && richSends == 0, "no-match menu Enter never submits unknown draft");
        Tap("Escape"); Tap("Escape");
    }
    finally { session.Chat.Execute = executeBefore; session.Chat.ExecuteRich = richBefore; }
    Console.WriteLine("PASS: native plain/phrase shutdown dispatch, single-Tab no-send, no-match Enter no-send (fixture sinks only)");
    Held(["Tab"], () => Require(session.InputState.WasActionTriggered(InputAction.TargetNearest), "outside-chat target binding retained"));
    session.Chat.Input.OpenNumber(2, _ => { }); Tap("Tab");
    Require(!session.Chat.Input.Completion.IsOpen, "number field has no completion"); Tap("Escape");
    Console.WriteLine("PASS: native phrase prefix/category/confirm/cancel/edit/number/outside-chat routing");
    // The same routing owns stock macro/name fields. No persisted macro or name is changed.
    var field = session.ActionService.Menus.MacroField;
    field.Open("", 60, (_, _) => { }, escapeKeepsText: false);
    Tap("Shift_L", "1");
    Require(field.Text == "!", "stock editor receives shifted text");
    // This standalone field has no owning menu; the synthetic locomotion tick closes orphaned fields.
    // Reopen it before the next native route assertion (a real macro menu keeps its field alive).
    field.Open("!", 60, (_, _) => { }, escapeKeepsText: false);
    Held(["w"], () =>
    {
        Require(field.Text == "!w", "stock editor text actual=" + field.Text);
        Require(!session.Chat.Input.IsOpen, "stock editor wins over Full type-to-chat");
        Require(!session.InputState.IsKeyHeld(GordianKey.W), "stock editor suppresses movement ingress");
    });
    field.Open("!w", 60, (_, _) => { }, escapeKeepsText: false);
    Tap("Tab"); Require(!session.Chat.Input.Completion.IsOpen, "macro field Tab does not open chat completion");
    Tap("Escape");
    Require(!field.IsOpen, "stock editor Escape preserved");
    Require(sawMissingShiftSymbol && !missingSymbolHandled, "X11 missing-symbol press remains unhandled for platform text");
    Console.WriteLine("PASS: real X11 missing-symbol seam and stock text editor; no connections or owner profiles");
    // A genuinely new session reads a saved character setting, not the SettingsStore's existing cached object.
    foreach (int size in new[] { -1, 0, 1, 2 })
    {
        string name = "ReloadedKeyboard" + size;
        var saved = new Gordian.Engine.Ui.StockUiSettings();
        if (size >= 0) saved.SetValue(Gordian.Engine.Ui.StockUiSettingKey.KeyboardSize, size);
        saved.SaveToFile(Gordian.Engine.Ui.StockUiSettings.GetSettingsPath(name));
        var hub = new InputProfileHub(InputProfile.CreateCompact());
        using var freshNetwork = new SessionNetworkManager("127.0.0.1", 54230);
        using var fresh = new CharacterSession(name, (uint)(size + 2), "fixture", freshNetwork, hub);
        var original = vm.ActiveTab;
        var freshTab = vm.AddSession(fresh);
        vm.ActiveTab = freshTab;
        Tap("a");
        Require(fresh.Chat.Input.IsOpen == (size <= 0), "new session uses persisted Keyboard Size via native input");
        if (size <= 0) Require(fresh.Chat.Input.Text == "a", "restored Full keeps initiating character once");
        Tap("Escape");
        hub.Edit(p => p.MouseSensitivityX = 1.25f);
        Tap("a");
        Require(fresh.Chat.Input.IsOpen == (size <= 0), "shared profile refresh does not discard selected Keyboard Size");
        Tap("Escape");
        vm.ActiveTab = original;
        vm.RemoveSession(fresh);
    }
    Console.WriteLine("PASS: fresh sessions restore all three Keyboard Size values; shared-profile refresh preserves selection");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
finally
{
    window.Close();
    // No GPU/native viewport was initialized by this keyboard-only fixture.
    X.XCloseDisplay(display);
}
bool NumLockOn()
{
    Require(X.XkbGetNamedIndicator(display, X.XInternAtom(display, "Num Lock", false), IntPtr.Zero, out int state, IntPtr.Zero, IntPtr.Zero) != 0, "Num Lock query");
    return state != 0;
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
static object? Field(object o, string n) => o.GetType().GetField(n, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o);
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
    [DllImport("libX11.so.6")] internal static extern ulong XInternAtom(IntPtr d, string name, bool onlyIfExists);
    [DllImport("libX11.so.6")] internal static extern int XkbGetNamedIndicator(IntPtr d, ulong atom, IntPtr index, out int state, IntPtr map, IntPtr real);
    [DllImport("libX11.so.6")] internal static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] internal static extern int XCloseDisplay(IntPtr d);
    [DllImport("libX11.so.6")] internal static extern int XSetInputFocus(IntPtr d, IntPtr w, int revert, ulong time);
    [DllImport("libX11.so.6")] internal static extern int XSync(IntPtr d, bool discard);
    [DllImport("libX11.so.6")] internal static extern ulong XStringToKeysym(string name);
    [DllImport("libX11.so.6")] internal static extern byte XKeysymToKeycode(IntPtr d, ulong symbol);
    [DllImport("libXtst.so.6")] internal static extern int XTestFakeKeyEvent(IntPtr d, uint key, bool down, ulong delay);
}
