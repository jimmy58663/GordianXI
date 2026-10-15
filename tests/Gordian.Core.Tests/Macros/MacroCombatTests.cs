// tests/Gordian.Core.Tests/Macros/MacroCombatTests.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Gordian.Core.Actions;
using Gordian.Core.Config;
using Gordian.Core.Input;
using Gordian.Core.Macros;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Tests.Ui;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Macros
{
    /// <summary>
    /// Macros while engaged (#339, round 2 A22): the whole path from the key or trigger through the locomotion tick to the
    /// macro runner, with the character engaged, locked on and the target command menu up, as in combat.
    /// </summary>
    public class MacroCombatTests
    {
        private readonly WorldState _world = new();
        private readonly LocalPlayerState _player = new() { ServerId = 0x01020304 };
        private readonly CombatState _combat = new();
        private readonly PlayerActionService _service;
        private readonly InputState _input = new();
        private readonly PlayerLocomotionController _locomotion;
        private readonly MacroController _macros;
        private readonly List<string> _ran = new();
        private readonly StockUiChat _chat = new();
        private readonly List<ushort> _icons = new() { 56, 13 };
        private readonly List<ushort> _cancelled = new();

        public MacroCombatTests()
        {
            var profile = new SessionProfile();
            Task Capture(ReadOnlyMemory<byte> mem, bool urgent) => Task.CompletedTask;
            _service = new PlayerActionService(profile, _world, _player,
                new CombatPacketModule(_combat, _player, Capture), new ChatPacketModule(Capture),
                new PartyPacketModule(new PartyState(), Capture), new EntityPacketModule(_world, _player, Capture),
                new LifecyclePacketModule(profile, Capture), Capture);
            _service.Menus.Library = StockUiMenuControllerTests.SyntheticLibrary();
            _world.UpsertEntity(new WorldEntity(0x01020304, 10, EntityType.Player) { Name = "Tester", Position = new Vector3(0, 0, 0) });
            _world.UpsertEntity(new WorldEntity(0x01000022, 34, EntityType.Monster) { Name = "Wild Rabbit", Position = new Vector3(3, 0, 0), Hpp = 100 });

            var library = new MacroLibrary();
            library.Set(new MacroSlot(0, 0, MacroPalette.Ctrl, 0), new Macro("Ctrl1", new[] { "/echo ctrl1" }));
            library.Set(new MacroSlot(0, 0, MacroPalette.Alt, 0), new Macro("Alt1", new[] { "/echo alt1" }));
            _macros = new MacroController(library);
            _macros.Runner.SubmitLine = line =>
            {
                lock (_ran) _ran.Add(line);
                return Task.CompletedTask;
            };
            var inputProfile = InputProfile.CreateGamepadDefault();
            _chat.StatusIds = () => _icons;
            _chat.CancelStatus = id => _cancelled.Add(id);
            _locomotion = new PlayerLocomotionController(_input, inputProfile, _world, _player, _service)
            {
                Macros = _macros,
                Chat = _chat,
            };
        }

        private void Tick() => _locomotion.Update(TimeSpan.FromMilliseconds(16));

        /// <summary>Engaged with the target selected, locked on and the battle command menu up, as after Attack in retail.</summary>
        private void Engage()
        {
            Assert.True(_service.SetTargetByServerId(0x01000022));
            _combat.Engage(0x01000022, 34);
            _service.SetLockOn(true);
            Assert.True(_service.OpenTargetCommandMenu());
            Assert.True(_service.Menus.Top!.IsCommandMenu);
            Tick();
        }

        /// <summary>
        /// The retail flow with the retail menu DAT (when installed): target, Confirm opens the command menu, Attack sends the
        /// attack and re-opens the menu (the engaged list), the server engages; then every gate is checked.
        /// </summary>
        [Fact]
        public async Task RetailFlow_AttackFromTheCommandMenuThenMacros()
        {
            const string game = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(game)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(game);
            rm.InitializeFileTable();
            var ui = Gordian.Core.Resources.Ui.UiResourceLibrary.Load(rm);
            if (ui == null) return;
            _service.Menus.Library = ui;
            Assert.True(_service.SetTargetByServerId(0x01000022));
            Assert.True(_service.OpenTargetCommandMenu());
            _service.Menus.Activate(); // Attack (the first row)
            for (int i = 0; i < 100 && !_service.Menus.IsOpen; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
            _combat.Engage(0x01000022, 34);
            Assert.True(_service.Menus.Top!.IsCommandMenu);
            Assert.Single(_service.Menus.OpenMenus);
            Tick();

            _input.SetKeyDown(GordianKey.LeftCtrl);
            _input.SetModifiers(InputModifiers.Control);
            Tick();
            Assert.Equal(MacroPalette.Ctrl, _macros.ShownPalette);
            Assert.False(MacroController.IsBlockedBy(_service.Menus, null));
            _input.SetKeyDown(GordianKey.D1);
            Tick();
            await _macros.Runner.Current;
            Assert.Equal(new[] { "/echo ctrl1" }, _ran);
        }

        [Fact]
        public async Task Keyboard_CtrlDigitRunsWhileEngaged()
        {
            Engage();
            _input.SetKeyDown(GordianKey.LeftCtrl);
            _input.SetModifiers(InputModifiers.Control);
            Tick();
            Assert.Equal(MacroPalette.Ctrl, _macros.ShownPalette);
            _input.SetKeyDown(GordianKey.D1);
            Tick();
            await _macros.Runner.Current;
            Assert.Equal(new[] { "/echo ctrl1" }, _ran);
        }

        [Fact]
        public async Task Gamepad_TriggerAndConfirmRunWhileEngaged()
        {
            Engage();
            _input.SetGamepadState(new GamepadState(true, GamepadButton.None, default, default, 0f, 1f));
            Tick();
            Assert.Equal(MacroPalette.Alt, _macros.ShownPalette);
            _input.SetGamepadState(new GamepadState(true, GamepadButton.A, default, default, 0f, 1f));
            Tick();
            await _macros.Runner.Current;
            Assert.Equal(new[] { "/echo alt1" }, _ran);
            // The command menu did not take the A press.
            Assert.True(_service.Menus.Top!.IsCommandMenu);
        }

        /// <summary>A status icon selection whose icons all went away ends; the next icon (gained in combat) does not revive it.</summary>
        [Fact]
        public void StaleStatusSelection_DoesNotBlockTheMacrosWhenAnIconComesBack()
        {
            var icons = new List<ushort> { 56 };
            var chat = new StockUiChat { StatusIds = () => icons };
            chat.CycleLogWindow(); // the log
            chat.CycleLogWindow(); // the status icons
            Assert.Equal(0, chat.SelectedStatusIcon);
            Assert.NotNull(MacroController.BlockReason(null, chat));
            icons.Clear(); // the effect wears off
            Assert.Equal(-1, chat.SelectedStatusIcon);
            icons.Add(13); // an effect gained in combat
            Assert.Equal(-1, chat.SelectedStatusIcon);
            Assert.False(chat.IsSelecting);
            Assert.Null(MacroController.BlockReason(null, chat));
        }

        private void Pad(GamepadButton buttons)
        {
            _input.SetGamepadState(new GamepadState(true, buttons, default, default, 0f, 0f));
            Tick();
        }

        /// <summary>
        /// Round 4: while engaged (the target command menu always up) Y / keypad + still select the log, then the status icons,
        /// and the selection takes the keys: Confirm cancels the buff under the cursor; the command menu is left alone.
        /// </summary>
        [Fact]
        public void Engaged_YSelectsTheLogAndTheStatusIcons()
        {
            Engage();
            var menu = _service.Menus.Top;
            Pad(GamepadButton.Y);
            Pad(GamepadButton.None);
            Assert.Equal(1, _chat.SelectedLogWindow);
            Pad(GamepadButton.Y);
            Pad(GamepadButton.None);
            Assert.Equal(0, _chat.SelectedLogWindow);
            Assert.Equal(0, _chat.SelectedStatusIcon);
            Pad(GamepadButton.DPadRight);
            Pad(GamepadButton.None);
            Assert.Equal(1, _chat.SelectedStatusIcon);
            Pad(GamepadButton.A);
            Pad(GamepadButton.None);
            Assert.Equal(new ushort[] { 13 }, _cancelled);
            Pad(GamepadButton.B);
            Pad(GamepadButton.None);
            Assert.False(_chat.IsSelecting);
            Assert.Same(menu, _service.Menus.Top); // the command menu took none of it
            Assert.Single(_service.Menus.OpenMenus);
        }

        /// <summary>Any other open menu keeps Y / keypad + for itself (item options, Button / Line Edit): the log is not selected.</summary>
        [Fact]
        public void OtherMenuOpen_YDoesNotSelectTheLog()
        {
            Assert.True(_service.Menus.Open(StockUiMenuEntries.ConfigMenu));
            Pad(GamepadButton.Y);
            Pad(GamepadButton.None);
            Assert.False(_chat.IsSelecting);
        }
    }
}
