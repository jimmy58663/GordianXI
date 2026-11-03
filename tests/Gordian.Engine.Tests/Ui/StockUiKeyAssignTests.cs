// tests/Gordian.Engine.Tests/Ui/StockUiKeyAssignTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Input;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>Config -> Misc. 2 -> Key Assignment: the keyboard's key-assignment windows (#337).</summary>
    public class StockUiKeyAssignTests
    {
        private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(16);

        private static UiMenuButton Button(int id, short x, short y, short w, int up, int down) => new()
        {
            ButtonId = (short)id, X = x, Y = y, Width = w, Height = 16, NavUp = (sbyte)up, NavDown = (sbyte)down, NavLeft = (sbyte)id, NavRight = (sbyte)id,
        };

        private static UiMenuDefinition Menu(string name, short width, short height, IEnumerable<UiMenuButton> buttons) => new()
        {
            Name = name,
            Frame = new UiMenuFrame { X = 16, Y = 48, Width = width, Height = height, Anchor = UiAnchor.TopLeft },
            Buttons = buttons.ToList(),
        };

        /// <summary>Misc. 2 with its Movement / Commands / Default buttons, both keyboard windows (fourteen rows) and yes/no.</summary>
        private static (StockUiMenuController Menus, InputProfileHub Hub) Controller()
        {
            var hub = new InputProfileHub(InputProfile.CreateCompact());
            IEnumerable<UiMenuButton> Rows() => Enumerable.Range(1, 14).Select(i => Button(i, 34, (short)(21 + 16 * (i - 1)), 256, i == 1 ? 14 : i - 1, i == 14 ? 1 : i + 1));
            var menus = new StockUiMenuController
            {
                Library = UiResourceLibrary.FromDefinitions(new[]
                {
                    Menu(StockUiConfigPages.Misc2Page, 366, 312, new[] { Button(8, 78, 222, 96, 10, 9), Button(9, 190, 222, 80, 8, 10), Button(10, 286, 222, 64, 9, 8) }),
                    Menu(StockUiMenuController.KeyAssignMovementMenu, 366, 248, Rows()),
                    Menu(StockUiMenuController.KeyAssignCommandsMenu, 366, 248, Rows()),
                    new UiMenuDefinition
                    {
                        Name = StockUiMenuEntries.MessageYesNoMenu,
                        Frame = new UiMenuFrame { X = 130, Y = 208, Width = 112, Height = 42, Anchor = UiAnchor.TopLeft },
                        Buttons = new List<UiMenuButton> { Button(1, 16, 6, 88, 2, 2), Button(2, 16, 22, 88, 1, 1) },
                    },
                }),
                GamepadProfile = () => hub.Profile,
            };
            menus.GamepadProfileEdited = edit => hub.Edit(edit, menus);
            return (menus, hub);
        }

        private static void Keys(InputState input, InputProfile profile, StockUiMenuController menus, params GordianKey[] held)
        {
            foreach (var key in Enum.GetValues<GordianKey>()) if (input.IsKeyHeld(key) && !held.Contains(key)) input.SetKeyUp(key);
            foreach (var key in held) input.SetKeyDown(key);
            input.SuppressKeyboardActions = menus.IsCapturingKeyboard;
            input.Update(profile, Tick);
            menus.ProcessInput(input, Tick);
        }

        private static StockUiOpenMenu OpenMovement(StockUiMenuController menus)
        {
            Assert.True(menus.Open(StockUiConfigPages.Misc2Page));
            menus.Top!.SelectedButtonId = StockUiMenuController.KeyAssignMovementButton;
            menus.Activate();
            var list = menus.Top!;
            Assert.Equal(StockUiListKind.KeyAssign, list.ListKind);
            Assert.Equal(StockUiMenuController.KeyAssignMovementMenu, list.Name);
            return list;
        }

        [Fact]
        public void Movement_ListsRetailFunctionsAndTheirKeys()
        {
            var (menus, _) = Controller();
            var list = OpenMovement(menus);

            Assert.Equal(KeyboardAssignments.Movement.Count, list.Rows.Count);
            Assert.Equal("Move Up", list.Rows[0].Text);
            Assert.Equal("W", list.Rows[0].Value);
            int camera = KeyboardAssignments.Movement.ToList().FindIndex(f => f.TextIndex == 93);
            Assert.Equal("I, Up", list.Rows[camera].Value);
            // A two-context row shows only the keys that drive both: the arrow moves the menu cursor and the camera.
            int both = KeyboardAssignments.Movement.ToList().FindIndex(f => f.TextIndex == 101);
            Assert.Equal("Up", list.Rows[both].Value);
            Assert.True(list.CanScroll);
        }

        [Fact]
        public void Commands_OpensTheSecondWindow()
        {
            var (menus, _) = Controller();
            Assert.True(menus.Open(StockUiConfigPages.Misc2Page));
            menus.Top!.SelectedButtonId = StockUiMenuController.KeyAssignCommandsButton;
            menus.Activate();
            Assert.Equal(StockUiMenuController.KeyAssignCommandsMenu, menus.Top!.Name);
            Assert.Equal("Zoom Out", menus.Top.Rows[0].Text);
            Assert.Equal("Page Down", menus.Top.Rows[0].Value);
        }

        [Fact]
        public void RowTakesTheNextKey_SwapsWithItsOwner_AndTheKeyDoesNotAlsoAct()
        {
            var (menus, hub) = Controller();
            var input = new InputState();
            var list = OpenMovement(menus);                   // Move Up: W
            Keys(input, hub.Profile, menus, GordianKey.Enter); // Confirm: wait for a key
            Keys(input, hub.Profile, menus);
            Assert.True(menus.IsCapturingKeyboard);
            Assert.Equal(StockUiMenuController.KeyCapturePrompt, list.Rows[0].Value);

            Keys(input, hub.Profile, menus, GordianKey.S);     // S belonged to Move Down
            Assert.False(input.IsActionHeld(InputAction.MoveBackward));
            Assert.Equal(new[] { new InputChord(GordianKey.S) }, hub.Profile.GetChords(InputAction.MoveForward));
            Assert.Contains(new InputChord(GordianKey.W), hub.Profile.GetChords(InputAction.MoveBackward));
            Assert.DoesNotContain(new InputChord(GordianKey.S), hub.Profile.GetChords(InputAction.MoveBackward));
            Assert.Equal("S", list.Rows[0].Value);
            Assert.Equal("W", list.Rows[1].Value);
            // The keyboard stays quiet until S is let go.
            Assert.True(menus.IsCapturingKeyboard);
            Keys(input, hub.Profile, menus);
            Assert.False(menus.IsCapturingKeyboard);
        }

        [Fact]
        public void ModifiersAreKept_EscapeGivesUp()
        {
            var (menus, hub) = Controller();
            var input = new InputState();
            var list = OpenMovement(menus);
            Keys(input, hub.Profile, menus, GordianKey.Enter);
            Keys(input, hub.Profile, menus);
            Keys(input, hub.Profile, menus, GordianKey.Escape);
            Assert.Equal("W", list.Rows[0].Value);
            Assert.Same(list, menus.Top); // Escape did not also close the window
            Keys(input, hub.Profile, menus);

            Keys(input, hub.Profile, menus, GordianKey.Enter);
            Keys(input, hub.Profile, menus);
            input.SetKeyDown(GordianKey.LeftCtrl);
            Keys(input, hub.Profile, menus, GordianKey.LeftCtrl, GordianKey.U);
            Assert.Equal("Ctrl+U", list.Rows[0].Value);
        }

        [Fact]
        public void ResetDefaults_PutsTheKeysBackAndKeepsThePad()
        {
            var profile = InputProfile.CreateCompact();
            KeyboardAssignments.Assign(profile, KeyboardAssignments.Movement[0], new InputChord(GordianKey.U));
            var padBefore = profile.GetChords(InputAction.Confirm).Where(c => c.IsGamepadChord).ToList();

            KeyboardAssignments.ResetDefaults(profile);

            Assert.Equal(new[] { new InputChord(GordianKey.W) }, profile.GetChords(InputAction.MoveForward).Where(c => !c.IsGamepadChord));
            Assert.Equal(padBefore, profile.GetChords(InputAction.Confirm).Where(c => c.IsGamepadChord).ToList());
        }

        [Fact]
        public void Default_AsksThenResets()
        {
            var (menus, hub) = Controller();
            var input = new InputState();
            hub.Edit(p => KeyboardAssignments.Assign(p, KeyboardAssignments.Movement[0], new InputChord(GordianKey.U)));
            Assert.True(menus.Open(StockUiConfigPages.Misc2Page));
            menus.Top!.SelectedButtonId = StockUiMenuController.KeyAssignDefaultButton;
            menus.Activate();
            for (int i = 0; i < 100 && menus.Top?.Name != StockUiMenuEntries.MessageYesNoMenu; i++) System.Threading.Thread.Sleep(5);
            Assert.Equal(StockUiMenuEntries.MessageYesNoMenu, menus.Top!.Name);
            menus.Top.SelectedButtonId = 1; // Yes
            menus.Activate();
            for (int i = 0; i < 200 && !hub.Profile.GetChords(InputAction.MoveForward).Contains(new InputChord(GordianKey.W)); i++)
            {
                Keys(input, hub.Profile, menus);
                System.Threading.Thread.Sleep(5);
            }
            Assert.Contains(new InputChord(GordianKey.W), hub.Profile.GetChords(InputAction.MoveForward));
        }
    }
}
