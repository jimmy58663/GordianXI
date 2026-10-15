// tests/Gordian.Core.Tests/Macros/MacroRunnerTests.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Input;
using Gordian.Core.Macros;
using Xunit;

namespace Gordian.Core.Tests.Macros
{
    public class MacroRunnerTests
    {
        private sealed class Recorder
        {
            public readonly List<string> Events = new();
            public readonly object Sync = new();

            public Task Submit(string line)
            {
                lock (Sync) Events.Add(line);
                return Task.CompletedTask;
            }

            public Task Delay(TimeSpan time, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                lock (Sync) Events.Add($"wait {time.TotalSeconds:0.##}");
                return Task.CompletedTask;
            }
        }

        [Theory]
        [InlineData("/wait 2", true, 2.0)]
        [InlineData("/wait 1.5", true, 1.5)]
        [InlineData("/WAIT", true, 1.0)]
        [InlineData("/waiting", false, 0.0)]
        [InlineData("/ja \"Provoke\" <t>", false, 0.0)]
        public void WaitCommand_IsRecognised(string line, bool isWait, double seconds)
        {
            Assert.Equal(isWait, MacroRunner.TryParseWaitCommand(line, out double parsed));
            if (isWait) Assert.Equal(seconds, parsed, 3);
        }

        [Fact]
        public void ExtractWait_RemovesThePauseFromTheLine()
        {
            Assert.Equal("/lastsynth", MacroRunner.ExtractWait("/lastsynth <wait19>", out double s1));
            Assert.Equal(19, s1, 3);
            Assert.Equal("/p Hello there", MacroRunner.ExtractWait("/p Hello <wait 2> there", out double s2));
            Assert.Equal(2, s2, 3);
            Assert.Equal("/ma \"Cure\" <t>", MacroRunner.ExtractWait("/ma \"Cure\" <t>", out double s3));
            Assert.Equal(0, s3, 3);
        }

        [Fact]
        public async Task Run_SendsEachLineInOrderAndHonoursWaits()
        {
            var recorder = new Recorder();
            var runner = new MacroRunner { SubmitLine = recorder.Submit, Delay = recorder.Delay };
            var macro = new Macro("Test", new[] { "/equip main \"Joyeuse\"", "/wait 1.5", "", "/lastsynth <wait19>", "/echo done", "/wait 999" });
            await runner.Run(macro);
            Assert.Equal(new[] { "/equip main \"Joyeuse\"", "wait 1.5", "/lastsynth", "wait 19", "/echo done", "wait 60" }, recorder.Events);
            Assert.False(runner.IsRunning);
        }

        [Fact]
        public async Task Run_HoldsTheNextLineWhileTheSubTargetCursorIsUp()
        {
            var recorder = new Recorder();
            int polls = 0;
            var runner = new MacroRunner
            {
                SubmitLine = recorder.Submit,
                Delay = recorder.Delay,
                IsSubTargeting = () => ++polls < 4,
            };
            await runner.Run(new Macro("Cure", new[] { "/ma \"Cure\" <st>", "/p Cured!" }));
            Assert.Equal("/ma \"Cure\" <st>", recorder.Events[0]);
            Assert.Equal("/p Cured!", recorder.Events[^1]);
            Assert.Equal(3, recorder.Events.FindAll(e => e.StartsWith("wait", StringComparison.Ordinal)).Count);
        }

        [Fact]
        public async Task Run_ANewMacroStopsTheOneRunning()
        {
            var sent = new List<string>();
            var gate = new TaskCompletionSource();
            var runner = new MacroRunner
            {
                SubmitLine = line =>
                {
                    lock (sent) sent.Add(line);
                    return Task.CompletedTask;
                },
                Delay = async (time, token) => await gate.Task.WaitAsync(token),
            };
            var first = runner.Run(new Macro("A", new[] { "/echo a1", "/wait 5", "/echo a2" }));
            // Let the first reach its wait.
            for (int i = 0; i < 100 && !sent.Contains("/echo a1"); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
            var second = runner.Run(new Macro("B", new[] { "/echo b1" }));
            await second;
            await first;
            Assert.Equal(new[] { "/echo a1", "/echo b1" }, sent);
        }

        [Fact]
        public void Run_EmptyMacroDoesNothing()
        {
            var runner = new MacroRunner { SubmitLine = _ => throw new InvalidOperationException() };
            Assert.True(runner.Run(Macro.Empty).IsCompleted);
            Assert.False(runner.IsRunning);
        }

        [Fact]
        public async Task Controller_KeyboardRunsSwitchesAndPicks()
        {
            var library = new MacroLibrary();
            library.Set(new MacroSlot(0, 0, MacroPalette.Ctrl, 0), new Macro("One", new[] { "/echo one" }));
            library.Set(new MacroSlot(0, 0, MacroPalette.Alt, 2), new Macro("Three", new[] { "/echo three" }));
            library.Set(new MacroSlot(0, 4, MacroPalette.Ctrl, 1), new Macro("Set5", new[] { "/echo set5" }));
            var recorder = new Recorder();
            var controller = new MacroController(library);
            controller.Runner.SubmitLine = recorder.Submit;
            controller.Runner.Delay = recorder.Delay;
            var profile = InputProfile.CreateCompact();
            var input = new InputState();

            void Tick()
            {
                input.Update(profile, TimeSpan.FromMilliseconds(16));
                controller.Update(input, blocked: false);
            }

            // Ctrl held: the Ctrl palette shows; Ctrl+1 runs Ctrl1.
            input.SetKeyDown(GordianKey.LeftCtrl);
            Tick();
            Assert.Equal(MacroPalette.Ctrl, controller.ShownPalette);
            input.SetKeyDown(GordianKey.D1);
            Tick();
            await controller.Runner.Current;
            Assert.Equal(new[] { "/echo one" }, recorder.Events);
            input.SetKeyUp(GordianKey.D1);
            input.SetKeyUp(GordianKey.LeftCtrl);
            Tick();
            Assert.Equal(MacroPalette.None, controller.ShownPalette);

            // Alt+3 runs Alt3.
            input.SetKeyDown(GordianKey.LeftAlt);
            input.SetKeyDown(GordianKey.D3);
            Tick();
            await controller.Runner.Current;
            Assert.Equal("/echo three", recorder.Events[^1]);
            input.SetKeyUp(GordianKey.D3);
            Tick();

            // Alt + Shift + 5 changes to set 5; Alt + Right picks key 2; Alt + Enter runs it.
            input.SetKeyDown(GordianKey.LeftShift);
            input.SetKeyDown(GordianKey.D5);
            Tick();
            Assert.Equal(4, library.CurrentSet);
            input.SetKeyUp(GordianKey.D5);
            input.SetKeyUp(GordianKey.LeftShift);
            input.SetKeyUp(GordianKey.LeftAlt);
            input.SetKeyDown(GordianKey.LeftCtrl);
            Tick();
            input.SetKeyDown(GordianKey.Right);
            Tick();
            Assert.Equal(1, controller.SelectedKey);
            Assert.False(input.IsActionHeld(InputAction.MenuRight)); // Ctrl+Right does not fall back to the plain key
            input.SetKeyUp(GordianKey.Right);
            input.SetKeyDown(GordianKey.Enter);
            Tick();
            await controller.Runner.Current;
            Assert.Equal("/echo set5", recorder.Events[^1]);
            input.SetKeyUp(GordianKey.Enter);

            // Ctrl + Down cycles the set; blocked hides the palette.
            input.SetKeyDown(GordianKey.Down);
            Tick();
            Assert.Equal(5, library.CurrentSet);
            controller.Update(input, blocked: true);
            Assert.Equal(MacroPalette.None, controller.ShownPalette);
        }

        [Fact]
        public async Task Controller_GamepadDPadPicksAndConfirmRuns()
        {
            var library = new MacroLibrary();
            library.Set(new MacroSlot(0, 0, MacroPalette.Alt, 1), new Macro("Two", new[] { "/echo two" }));
            var recorder = new Recorder();
            var controller = new MacroController(library);
            controller.Runner.SubmitLine = recorder.Submit;
            var profile = InputProfile.CreateGamepadDefault();
            var input = new InputState();

            void Press(GamepadButton buttons)
            {
                input.SetGamepadState(new GamepadState(true, buttons, default, default, 0f, 1f));
                input.Update(profile, TimeSpan.FromMilliseconds(16));
                controller.Update(input, blocked: false, profile);
            }

            Press(GamepadButton.None);
            Assert.Equal(MacroPalette.Alt, controller.ShownPalette);
            Assert.True(controller.HeldByGamepad);

            // Y, B, X, LB, RB touch no macro; the d-pad moves as the arrows; Confirm (A) runs the picked one.
            Press(GamepadButton.Y | GamepadButton.B | GamepadButton.X | GamepadButton.LeftShoulder | GamepadButton.RightShoulder);
            Press(GamepadButton.None);
            Assert.Empty(recorder.Events);
            Press(GamepadButton.DPadRight);
            Press(GamepadButton.None);
            Assert.Equal(1, controller.SelectedKey);
            Press(GamepadButton.DPadDown);
            Press(GamepadButton.None);
            Assert.Equal(1, library.CurrentSet);
            Press(GamepadButton.DPadUp);
            Press(GamepadButton.None);
            Assert.Equal(0, library.CurrentSet);
            Press(GamepadButton.A);
            await controller.Runner.Current;
            Assert.Equal(new[] { "/echo two" }, recorder.Events);
            Assert.False(input.IsActionHeld(InputAction.Confirm)); // the palette takes the pad's other buttons
        }
    }
}
