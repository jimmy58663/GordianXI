// tests/Gordian.Core.Tests/Ui/StockUiGamepadAssignTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gordian.Core.Input;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    /// <summary>Config -> Gamepad: the gamepad key-assignment list (#21).</summary>
    public class StockUiGamepadAssignTests
    {
        private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(16);

        private static UiMenuButton Button(int id, short x, short y, short w, int up, int down) => new()
        {
            ButtonId = (short)id, X = x, Y = y, Width = w, Height = 16, NavUp = (sbyte)up, NavDown = (sbyte)down, NavLeft = (sbyte)id, NavRight = (sbyte)id,
        };

        private static UiMenuDefinition Menu(string name, short x, short y, short width, short height, IEnumerable<UiMenuButton> buttons) => new()
        {
            Name = name,
            Frame = new UiMenuFrame { X = x, Y = y, Width = width, Height = height, Anchor = UiAnchor.TopLeft },
            Buttons = buttons.ToList(),
        };

        /// <summary>The config list's Gamepad entry (button 13), the list frame (conf11s: fourteen rows, scrolling at the ends) and yes/no.</summary>
        private static (StockUiMenuController Menus, InputProfileHub Hub) Controller()
        {
            var hub = new InputProfileHub(InputProfile.CreateCompact());
            var menus = new StockUiMenuController
            {
                Library = UiResourceLibrary.FromDefinitions(new[]
                {
                    Menu(StockUiMenuEntries.ConfigMenu, 384, 48, 112, 218, new[] { Button(13, 16, 198, 88, 13, 13) }),
                    Menu(StockUiConfigPages.GamepadAssignMenu, 16, 48, 366, 248,
                        Enumerable.Range(1, 14).Select(i => Button(i, 34, (short)(21 + 16 * (i - 1)), 256, i == 1 ? 16 : i - 1, i == 14 ? 26 : i + 1))),
                    Menu(StockUiMenuEntries.MessageYesNoMenu, 130, 208, 112, 42, new[] { Button(1, 16, 6, 88, 2, 2), Button(2, 16, 22, 88, 1, 1) }),
                }),
                GamepadProfile = () => hub.Profile,
            };
            menus.GamepadProfileEdited = edit => hub.Edit(edit, menus);
            return (menus, hub);
        }

        private static void Pad(InputState input, InputProfile profile, StockUiMenuController menus, GamepadButton buttons)
        {
            input.SetGamepadState(new GamepadState(true, buttons, Vector2.Zero, Vector2.Zero, 0f, 0f, 1));
            input.SuppressGamepadActions = menus.IsCapturingGamepad;
            input.Update(profile, Tick);
            menus.ProcessInput(input, Tick);
        }

        private static StockUiOpenMenu OpenList(StockUiMenuController menus)
        {
            Assert.True(menus.Open(StockUiMenuEntries.ConfigMenu));
            menus.Activate(); // Gamepad
            var list = menus.Top!;
            Assert.Equal(StockUiListKind.GamepadAssign, list.ListKind);
            return list;
        }

        [Fact]
        public void GamepadEntry_OpensTheListOfFunctionsAndTheirButtons()
        {
            var (menus, _) = Controller();
            var list = OpenList(menus);

            Assert.Equal(GamepadAssignments.Functions.Count + 1, list.Rows.Count);
            Assert.Equal("Confirm", list.Rows[0].Text);
            Assert.Equal("A", list.Rows[0].Value);
            Assert.Equal(StockUiMenuController.GamepadResetLabel, list.Rows[^1].Text);
            Assert.True(list.CanScroll);
            int lockOn = GamepadAssignments.Functions.ToList().FindIndex(f => f.Actions[0] == InputAction.ToggleLockOn);
            Assert.Equal("L3", list.Rows[lockOn].Value);
        }

        [Fact]
        public void RowTakesTheNextPadButton_WhichDoesNotAlsoAct()
        {
            var (menus, hub) = Controller();
            var input = new InputState();
            var list = OpenList(menus);
            menus.Move(InputAction.MenuDown);                   // Cancel
            Pad(input, hub.Profile, menus, GamepadButton.A);    // A confirms the row: wait for a button
            Assert.True(menus.IsCapturingGamepad);
            Assert.Equal(1, menus.GamepadCaptureEntry);
            Assert.Equal(StockUiMenuController.GamepadCapturePrompt, list.Rows[1].Value);

            Pad(input, hub.Profile, menus, GamepadButton.A);    // still held: not a new press
            Assert.True(menus.IsCapturingGamepad);
            Pad(input, hub.Profile, menus, GamepadButton.None);
            Pad(input, hub.Profile, menus, GamepadButton.Y);    // Y becomes Cancel
            Assert.Equal(-1, menus.GamepadCaptureEntry);
            Assert.Same(list, menus.Top);                        // the list stays open
            Assert.True(input.SuppressGamepadActions);
            Assert.False(input.IsActionHeld(InputAction.CycleLogWindow)); // Y drove nothing while it was being assigned
            Assert.True(hub.Profile.TryGetAction(new InputChord(GamepadButton.Y), out var y));
            Assert.Equal(InputAction.Cancel, y);
            Assert.Equal("Y", list.Rows[1].Value);
            Assert.Equal("B", list.Rows[3].Value);              // Select Window took Cancel's old B

            Pad(input, hub.Profile, menus, GamepadButton.Y);    // held after the assignment: still quiet
            Assert.True(menus.IsCapturingGamepad);
            Pad(input, hub.Profile, menus, GamepadButton.None); // let go
            Assert.False(menus.IsCapturingGamepad);
        }

        [Fact]
        public void KeyboardCancel_OrTheTimeout_GiveUp()
        {
            var (menus, hub) = Controller();
            var input = new InputState();
            var list = OpenList(menus);
            menus.Activate();                                    // Confirm row (from the keyboard)
            Assert.True(menus.IsCapturingGamepad);
            input.SetKeyDown(GordianKey.Escape);
            input.Update(hub.Profile, Tick);
            menus.ProcessInput(input, Tick);
            Assert.False(menus.IsCapturingGamepad);
            Assert.Same(list, menus.Top);                        // Escape did not also close the list
            Assert.Equal("A", list.Rows[0].Value);
            input.SetKeyUp(GordianKey.Escape);

            menus.Activate();
            Assert.True(menus.IsCapturingGamepad);
            input.Update(hub.Profile, StockUiMenuController.GamepadCaptureTimeout);
            menus.ProcessInput(input, StockUiMenuController.GamepadCaptureTimeout);
            Assert.False(menus.IsCapturingGamepad);
        }

        [Fact]
        public async System.Threading.Tasks.Task ResetRow_AsksThenRestoresTheDefaults()
        {
            var (menus, hub) = Controller();
            hub.Edit(p => GamepadAssignments.Assign(p, GamepadAssignments.Functions[0], GamepadButton.X));
            var list = OpenList(menus);
            Assert.Equal("X", list.Rows[0].Value);
            menus.Move(InputAction.MenuUp);                      // wraps to the last row
            Assert.Equal(list.Rows.Count - 1, list.EntryIndex(list.SelectedButtonId));
            menus.Activate();
            Assert.Equal(StockUiMenuEntries.MessageYesNoMenu, menus.Top!.Name);
            Assert.Equal(StockUiMenuController.GamepadResetPrompt, menus.Top.Message);
            menus.Move(InputAction.MenuUp);                      // Yes (No is the default)
            menus.Activate();
            await System.Threading.Tasks.Task.Delay(50, TestContext.Current.CancellationToken);
            Assert.Equal("X", list.Rows[0].Value);               // applied on the next input tick, not from the answer's thread
            menus.ProcessInput(new InputState(), Tick);
            Assert.Equal("A", list.Rows[0].Value);
            Assert.True(hub.Profile.TryGetAction(new InputChord(GamepadButton.A), out var a));
            Assert.Equal(InputAction.Confirm, a);
        }
    }
}
