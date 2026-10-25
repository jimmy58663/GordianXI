// tests/Gordian.Engine.Tests/Ui/StockUiBlueMagicTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Actions;
using Gordian.Engine.Tests.Actions;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>Blue Magic's Set Spells window (#363): slots, points, the learned list, setting, removing and Remove All.</summary>
    public class StockUiBlueMagicTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const int Blu = (int)JobId.BlueMage;
        private const ushort VenomShell = 513, Maelstrom = 515, MetallicBody = 517, Sandspin = 524, Pollen = 549;

        private static readonly Dictionary<int, string> Names = new()
        {
            [VenomShell] = "Venom Shell", [Maelstrom] = "Maelstrom", [MetallicBody] = "Metallic Body", [Sandspin] = "Sandspin", [Pollen] = "Pollen",
        };

        /// <summary>Five blue spells with their retail levels (mgc_) and menu order.</summary>
        private static ActionCatalog Catalog()
        {
            var spells = new byte[SpellCommandTables.SpellRecordSize * 1024];
            TestCatalog.Spell(spells, Pollen, kind: 6, flags: 0x3F, mp: 8, menu: 1, (Blu, 1));
            TestCatalog.Spell(spells, Sandspin, kind: 6, flags: 0x20, mp: 10, menu: 2, (Blu, 1));
            TestCatalog.Spell(spells, MetallicBody, kind: 6, flags: 0x01, mp: 19, menu: 3, (Blu, 8));
            TestCatalog.Spell(spells, VenomShell, kind: 6, flags: 0x20, mp: 86, menu: 4, (Blu, 42));
            TestCatalog.Spell(spells, Maelstrom, kind: 6, flags: 0x20, mp: 162, menu: 5, (Blu, 61));
            var tables = SpellCommandTables.Parse(TestCatalog.Section("mgc_", DatSectionType.SpellList, spells))!;
            return new ActionCatalog(tables, id => Names.GetValueOrDefault(id), _ => null, id => id == Pollen ? "Restores HP." : null);
        }

        private static UiMenuDefinition List(string name, short x, short help, int rows)
        {
            var buttons = new List<UiMenuButton>();
            for (int i = 1; i <= rows; i++)
            {
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)i, X = 3, Y = (short)(6 + 18 * (i - 1)), Width = 132, Height = 16,
                    NavUp = (sbyte)(i == 1 ? 1 : i - 1), NavDown = (sbyte)(i == rows ? i : i + 1), NavLeft = (sbyte)i, NavRight = (sbyte)i,
                });
            }
            return new UiMenuDefinition { Name = name, Frame = new UiMenuFrame { X = x, Y = 48, Width = 182, Height = 190, HelpTextId = help }, Buttons = buttons };
        }

        private static UiResourceLibrary Library() => UiResourceLibrary.FromDefinitions(new[]
        {
            List(StockUiBlueMagic.SlotsMenu, 130, StockUiBlueMagic.HelpSetSpells, 10),
            List(StockUiBlueMagic.LearnedMenu, 314, StockUiBlueMagic.HelpLearned, 10),
            List(StockUiBlueMagic.OptionsMenu, 384, -1, 3),
            List(StockUiMenuEntries.MessageYesNoMenu, 130, -1, 2),
        });

        private sealed class Harness
        {
            public readonly StockUiMenuController Menus = new() { Library = Library() };
            public readonly byte[] Slots = new byte[20];
            public readonly HashSet<ushort> Learned = new() { Pollen, Sandspin, MetallicBody, VenomShell, Maelstrom };
            public readonly List<string> Sent = new();
            public readonly List<string> Notices = new();
            public int Level = 40;
            public int Bonus;
            public bool SubJob;

            public Harness()
            {
                var catalog = Catalog();
                Menus.BlueMagicCatalog = () => catalog;
                Menus.BlueMagicState = () => new StockUiBlueMagicState(Level, SubJob, Slots, Bonus, id => Learned.Contains(id));
                Menus.NoticePosted += Notices.Add;
                Menus.BlueSpellSet = (slot, spell, sub) => Record($"set {slot} {spell} {sub}");
                Menus.BlueSpellRemove = (slot, spell, sub) => Record($"remove {slot} {spell} {sub}");
            }

            private Task Record(string what)
            {
                lock (Sent) Sent.Add(what);
                return Task.CompletedTask;
            }

            public bool HasSent(string what)
            {
                lock (Sent) return Sent.Contains(what);
            }

            /// <summary>The server's S2C 0x044 answer to a set: the slot takes the spell and the windows refresh.</summary>
            public void Server(int slot, ushort spell)
            {
                Slots[slot] = spell == 0 ? (byte)0 : (byte)(spell - 512);
                Menus.RefreshBlueMagic();
            }
        }

        private static void SpinUntil(Func<bool> condition) =>
            Assert.True(System.Threading.SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(10)));

        [Theory]
        [InlineData(1, 6, 10)]
        [InlineData(11, 8, 15)]
        [InlineData(40, 12, 25)]
        [InlineData(75, 20, 45)]
        [InlineData(99, 20, 55)]
        public void SlotsAndPoints_FollowTheServerRules(int level, int slots, int points)
        {
            Assert.Equal(slots, StockUiBlueMagic.SlotsAt(level));
            Assert.Equal(points, StockUiBlueMagic.BasePointsAt(level));
        }

        [Fact]
        public void UnlockLevels_MatchTheSlotCounts()
        {
            for (int slot = 1; slot <= 20; slot++)
            {
                int level = StockUiBlueMagic.UnlockLevel(slot);
                Assert.True(StockUiBlueMagic.SlotsAt(level) >= slot, $"slot {slot} at {level}");
                if (level > 1) Assert.True(StockUiBlueMagic.SlotsAt(level - 1) < slot, $"slot {slot} before {level}");
            }
        }

        [Fact]
        public void PointsTable_HasTheServerValues()
        {
            Assert.Equal(3, StockUiBlueMagic.PointsOf(VenomShell));
            Assert.Equal(5, StockUiBlueMagic.PointsOf(Maelstrom));
            Assert.Equal(1, StockUiBlueMagic.PointsOf(MetallicBody));
            Assert.Equal(2, StockUiBlueMagic.PointsOf(Sandspin));
            Assert.Equal(1, StockUiBlueMagic.PointsOf(Pollen));
            Assert.Equal(0, StockUiBlueMagic.PointsOf(512));
            Assert.Equal(0, StockUiBlueMagic.PointsOf(1000));
        }

        [Fact]
        public void OptionsWindow_IsBlusortwOnlyForABlueMage()
        {
            var h = new Harness();
            Assert.Equal(StockUiBlueMagic.OptionsMenu, h.Menus.OptionsMenuFor(StockUiActionMenu.BlueMagicKind));
            Assert.Equal(StockUiActionMenu.OptionsMenu, h.Menus.OptionsMenuFor(1));
            h.Menus.BlueMagicState = () => null;
            Assert.Equal(StockUiActionMenu.BlueOptionsMenu, h.Menus.OptionsMenuFor(StockUiActionMenu.BlueMagicKind));
        }

        [Fact]
        public void SetSpells_ListsTheSlots_ThePoints_AndTheLearnedSpellsGreyedByLevel()
        {
            var h = new Harness();
            h.Slots[0] = (byte)(Sandspin - 512);
            var slots = h.Menus.OpenSetSpells(null)!;
            Assert.Equal(StockUiBlueMagicView.Slots, slots.BlueMagicView);
            Assert.Equal(20, slots.BlueSlots.Count);
            Assert.Equal((Sandspin, "Sandspin"), (slots.BlueSlots[0].SpellId, slots.BlueSlots[0].Name));
            Assert.False(slots.BlueSlots[11].Locked); // 12 slots at 40
            Assert.True(slots.BlueSlots[12].Locked);
            Assert.Equal((2, 25), slots.BluePoints);

            var learned = slots.BlueSpells;
            Assert.True(learned[0].RemoveAll);
            Assert.Equal(new[] { Pollen, Sandspin, MetallicBody, VenomShell, Maelstrom }, learned.Skip(1).Select(r => r.SpellId));
            Assert.True(learned[2].IsSet);
            Assert.Null(learned[3].GreyedReason); // Metallic Body, level 8
            Assert.Equal("You cannot set Venom Shell at your current level.", learned[4].GreyedReason); // level 42
            Assert.Equal("You cannot set Maelstrom at your current level.", learned[5].GreyedReason);
            Assert.Equal(Sandspin, slots.BlueDescribedSpell);
        }

        [Fact]
        public void ConfirmOnAnEmptySlot_PicksASpell_AndSendsTheSet()
        {
            var h = new Harness();
            h.Level = 50;
            var slots = h.Menus.OpenSetSpells(null)!;
            slots.SelectedButtonId = 1;
            h.Menus.Activate();
            var learned = h.Menus.Top!;
            Assert.Equal(StockUiBlueMagicView.Learned, learned.BlueMagicView);
            Assert.True(slots.BlueLearnedOpen);
            Assert.Equal(Pollen, learned.BlueDescribedSpell); // past "(Remove All)"

            learned.SelectedButtonId = 5; // Venom Shell
            h.Menus.Activate();
            SpinUntil(() => h.HasSent($"set 0 {VenomShell - 512} False"));
            Assert.Same(slots, h.Menus.Top);
            Assert.False(slots.BlueLearnedOpen);
            Assert.Equal(2, slots.SelectedButtonId); // the next empty slot

            h.Server(0, VenomShell);
            Assert.Equal(VenomShell, slots.BlueSlots[0].SpellId);
            Assert.Equal((3, 30), slots.BluePoints);
        }

        [Fact]
        public void ASpellThatDoesNotFit_IsGreyed_AndSaysWhy()
        {
            var h = new Harness();
            h.Level = 1; // 10 points, 6 slots
            for (int i = 0; i < 5; i++) h.Slots[i] = (byte)(Sandspin - 512);
            var slots = h.Menus.OpenSetSpells(null)!;
            Assert.Equal((10, 10), slots.BluePoints);
            slots.SelectedButtonId = 6;
            h.Menus.Activate();
            var learned = h.Menus.Top!;
            Assert.Equal("You do not have enough Blue Magic Points to set Pollen.", learned.BlueSpells[1].GreyedReason);
            learned.SelectedButtonId = 2;
            h.Menus.Activate();
            Assert.Contains("You do not have enough Blue Magic Points to set Pollen.", h.Notices);

            // The merit and job point bonus the server reports raises the cap.
            h.Bonus = 5;
            h.Menus.RefreshBlueMagic();
            Assert.Null(learned.BlueSpells[1].GreyedReason);
        }

        [Fact]
        public void ConfirmOnASetSlot_RemovesIt_AndALockedSlotSaysWhenItOpens()
        {
            var h = new Harness();
            h.SubJob = true;
            h.Level = 20;
            h.Slots[1] = (byte)(Pollen - 512);
            var slots = h.Menus.OpenSetSpells(null)!;
            slots.SelectedButtonId = 2;
            h.Menus.Activate();
            SpinUntil(() => h.HasSent($"remove 1 {Pollen - 512} True"));

            slots.SelectedButtonId = 10; // 8 slots at level 20
            h.Menus.Activate();
            Assert.Contains("Set 10 opens at Blue Mage level 21.", h.Notices);
        }

        [Fact]
        public void RemoveAll_AsksFirst_ThenRemovesEverySetSpell()
        {
            var h = new Harness();
            h.Level = 60;
            h.Slots[0] = (byte)(Pollen - 512);
            h.Slots[3] = (byte)(VenomShell - 512);
            var slots = h.Menus.OpenSetSpells(null)!;
            slots.SelectedButtonId = 2;
            h.Menus.Activate();
            var learned = h.Menus.Top!;
            learned.FirstRow = 0;
            learned.SelectedButtonId = 1; // (Remove All)
            h.Menus.Activate();
            Assert.Equal(StockUiMenuEntries.MessageYesNoMenu, h.Menus.Top!.Name);
            h.Menus.Top.SelectedButtonId = 1;
            h.Menus.Activate();
            SpinUntil(() => h.HasSent($"remove 0 {Pollen - 512} False") && h.HasSent($"remove 3 {VenomShell - 512} False"));
        }

        [Fact]
        public void CancelOnTheLearnedList_GivesTheListBackToTheSlots()
        {
            var h = new Harness();
            var slots = h.Menus.OpenSetSpells(null)!;
            h.Menus.Activate();
            Assert.True(slots.BlueLearnedOpen);
            h.Menus.CloseTop();
            Assert.Same(slots, h.Menus.Top);
            Assert.False(slots.BlueLearnedOpen);
        }

        [Fact]
        public void NotABlueMage_CannotOpenSetSpells()
        {
            var h = new Harness();
            h.Level = 0;
            Assert.Null(h.Menus.OpenSetSpells(null));
            Assert.Contains("You cannot set blue magic spells on your current job.", h.Notices);
        }

        /// <summary>The retail Set Spells windows and texts (skipped without the install).</summary>
        [Fact]
        public void RetailMenus_SetSpellsWindowsResolve()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            if (library == null) return;
            var help = rm.GetDMsgTable(DMsgCategory.MenuHelp)!;
            var messages = rm.GetDMsgTable(DMsgCategory.MenuMessages)!;

            Assert.True(library.TryGetMenu(StockUiBlueMagic.OptionsMenu, out var options));
            var setSpells = options.FindButton(StockUiBlueMagic.OptionsSetSpellsButton)!;
            Assert.Equal("Set blue magic spells.", help.Records[setSpells.HelpTextId].PrimaryText);
            Assert.True(library.TryGetMenu(StockUiBlueMagic.SlotsMenu, out var slots));
            Assert.Equal((130, 48, 10), (slots.Frame.X, slots.Frame.Y, slots.Buttons.Count(StockUiMenuController.IsSelectable)));
            Assert.Equal(StockUiBlueMagic.HelpSetSpells, slots.Frame.HelpTextId);
            Assert.True(library.TryGetMenu(StockUiBlueMagic.LearnedMenu, out var learned));
            Assert.Equal(StockUiBlueMagic.HelpLearned, learned.Frame.HelpTextId);
            Assert.True(library.TryGetMenu(StockUiBlueMagic.PointsMenu, out _));
            Assert.True(library.TryGetMenu(StockUiBlueMagic.DetailMenu, out _));
            Assert.Equal("(Remove All)", messages.Records[StockUiBlueMagic.MessageRemoveAllLabel].PrimaryText);
            Assert.Equal("Remove all set blue magic spells?", messages.Records[StockUiBlueMagic.MessageRemoveAllPrompt].PrimaryText);

            // The retail spell table: the levels this window greys by.
            var catalog = ActionCatalog.Load(rm)!;
            Assert.Equal(42, catalog.GetSpell(VenomShell)!.LevelFor(Blu));
            Assert.Equal(1, catalog.GetSpell(Pollen)!.LevelFor(Blu));
        }
    }
}
