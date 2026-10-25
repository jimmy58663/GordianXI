// tests/Gordian.Engine.Tests/Ui/StockUiStatusTests.cs
using System.Collections.Generic;
using System.Linq;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>The Status window's data (#23): the job rows, the skill lines from S2C 0x062's words, and the level box's numbers.</summary>
    public class StockUiStatusTests
    {
        [Fact]
        public void JobRows_LeftColumnOddIds_RightColumnEvenIds()
        {
            Assert.Equal(1, StockUiStatus.JobOfRow(1));   // Warrior
            Assert.Equal(3, StockUiStatus.JobOfRow(2));   // White Mage
            Assert.Equal(21, StockUiStatus.JobOfRow(11)); // Geomancer
            Assert.Equal(2, StockUiStatus.JobOfRow(12));  // Monk
            Assert.Equal(18, StockUiStatus.JobOfRow(20)); // Puppetmaster
            Assert.Equal(22, StockUiStatus.JobOfRow(22)); // Rune Fencer
            Assert.Equal(0, StockUiStatus.JobOfRow(23));
        }

        [Fact]
        public void SkillLines_LeaveOutUntrainedSkills_AndReadTheCapBit()
        {
            var raw = new ushort[64];
            raw[1] = 424 | 0x8000; // Hand-to-Hand, capped
            raw[3] = 144;          // Sword
            raw[22] = 300;         // the automaton's melee skill is not a Combat Skills line
            var lines = StockUiStatus.SkillLines(StockUiStatus.CombatSkills, raw);
            Assert.Equal(new[] { "Hand-to-Hand", "Sword" }, lines.Select(l => l.Name));
            Assert.Equal(424, lines[0].Level);
            Assert.True(lines[0].Capped);
            Assert.False(lines[1].Capped);
        }

        [Fact]
        public void CraftLines_ListEveryCraft_WithRankAndLevel()
        {
            var raw = new ushort[64];
            raw[49] = (ushort)(60 * 0x20 + 6); // Woodworking 60, Craftsman
            raw[48] = 0xFFFF;                  // unused entry
            var lines = StockUiStatus.CraftLines(raw);
            Assert.Equal(10, lines.Count);
            Assert.Equal(("Fishing", 0, "Amateur"), (lines[0].Name, lines[0].Level, lines[0].Rank));
            Assert.Equal(("Woodworking", 60, "Craftsman"), (lines[1].Name, lines[1].Level, lines[1].Rank));
            Assert.Equal("Synergy", lines[^1].Name);
        }

        [Fact]
        public void Numbers_NamesAndPages()
        {
            Assert.Equal("55,999", StockUiStatus.Number(55999));
            Assert.Equal("San d'Oria", StockUiStatus.NationName(0));
            Assert.Equal("Tarutaru", StockUiStatus.RaceName(6));
            Assert.Equal(StockUiStatus.MagicSkillMenu, StockUiStatus.MenuOf(StockUiStatusPage.CraftSkills));
            Assert.Equal(StockUiStatus.ProfileMenu, StockUiStatus.MenuOf(StockUiStatusPage.Profile));
        }

        /// <summary>
        /// The stats side's item level (round 1 of the 2026-10-24 test): the average of the main weapon and the five armour
        /// slots, with the sub weapon but never a shield; "---" (null) without item-level gear.
        /// </summary>
        [Fact]
        public void ItemLevel_AveragesMainSubWeaponAndArmour_LeavingOutShields()
        {
            var items = new Dictionary<ushort, ItemRecord>
            {
                [1] = new() { ItemId = 1, Level = 99, ItemLevel = 119, Damage = 150 },  // main
                [2] = new() { ItemId = 2, Level = 99, ItemLevel = 119 },               // armour
                [3] = new() { ItemId = 3, Level = 99, ItemLevel = 119, ShieldSize = 5 }, // Aegis-like shield
                [4] = new() { ItemId = 4, Level = 99, Damage = 40 },                   // a level-99 sub weapon, no item level
                [5] = new() { ItemId = 5, Level = 75 },                                // old armour
            };
            ItemRecord? Lookup(ushort id) => items.TryGetValue(id, out var r) ? r : null;
            var inventory = new InventoryState();
            void Wear(EquipSlotId slot, byte bag, ushort item)
            {
                inventory.SetItem(ContainerId.Inventory, bag, item, 1, ItemLockFlag.Normal);
                inventory.SetEquip(slot, ContainerId.Inventory, bag);
            }
            Wear(EquipSlotId.Main, 1, 1);
            byte bagSlot = 2;
            foreach (var slot in new[] { EquipSlotId.Head, EquipSlotId.Body, EquipSlotId.Hands, EquipSlotId.Legs, EquipSlotId.Feet }) Wear(slot, bagSlot++, 2);
            Wear(EquipSlotId.Sub, 10, 3);
            Assert.Equal(119, StockUiStatus.ItemLevel(inventory, Lookup)); // the shield is left out

            Wear(EquipSlotId.Sub, 11, 4);
            Assert.Equal((119 * 6 + 99) / 7, StockUiStatus.ItemLevel(inventory, Lookup)); // a sub weapon counts

            var old = new InventoryState();
            old.SetItem(ContainerId.Inventory, 1, 5, 1, ItemLockFlag.Normal);
            old.SetEquip(EquipSlotId.Body, ContainerId.Inventory, 1);
            Assert.Null(StockUiStatus.ItemLevel(old, Lookup));
        }

        /// <summary>The pages fit their rows (retail border to border 19 + 14 a row; round 1 captures, 2026-10-25).</summary>
        [Fact]
        public void PageHeight_FitsTheRows()
        {
            Assert.Equal((short)(22 + 11 * 14), StockUiStatus.PageHeight(StockUiStatusPage.JobLevels, null));
            Assert.Equal((short)(22 + 10 * 14), StockUiStatus.PageHeight(StockUiStatusPage.CraftSkills, null));
            var magic = new[] { new StockUiSkillLine("Divine", 150, true), new StockUiSkillLine("Healing", 150, true), new StockUiSkillLine("Enhancing", 139, false) };
            var info = new StockUiStatusInfo(new StockUiCharacterStats("A", "", "", "", 0, 0, 0, 0, 0, new int[7], new int[7]), 1, 0, 0, "", "", "", "", 1, 0, 1,
                new byte[24], 1, magic, magic, magic);
            Assert.Equal((short)(22 + 2 * 14), StockUiStatus.PageHeight(StockUiStatusPage.MagicSkills, info));
            Assert.Null(StockUiStatus.PageHeight(StockUiStatusPage.Profile, info));
            Assert.Equal("HCG", StockUiStatus.GladiatorRankName(2));
            Assert.Equal("Gla", StockUiStatus.GladiatorRankName(0));
            Assert.Equal(1, StockUiStatus.MasteryRankShown(11));
            Assert.Equal(8, StockUiStatus.MasteryRankShown(8));
        }

        [Fact]
        public void MainMenuStatus_OpensTheStatusList()
        {
            Assert.True(StockUiMenuEntries.TryGet(StockUiMenuEntries.MainMenu, 1, out var status));
            Assert.Equal(StockUiStatus.CommandMenu, status.Opens);
            Assert.True(StockUiMenuEntries.TryGet(StockUiStatus.CommandMenu, StockUiStatus.CraftSkillsButton, out var craft));
            Assert.Equal(StockUiMenuCommand.StatusPage, craft.Command);
            Assert.Equal((int)StockUiStatusPage.CraftSkills, craft.Argument);
        }
    }
}
