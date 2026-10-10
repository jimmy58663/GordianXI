// tests/Gordian.Core.Tests/Network/EquipInspectPacketTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// The player check (#64): S2C 0x0C9 laid out as LandSandBoat sends it (0x0CA, then mode 3 equipment, then the mode 1
    /// general block), the check state, and the check window the controller opens from it.
    /// </summary>
    public class EquipInspectPacketTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const uint CheckedId = 0x01000123;
        private const ushort CheckedIndex = 0x0456;

        private static byte[] Header(int length, byte optionFlag, uint id = CheckedId)
        {
            var p = new byte[length];
            BinaryPrimitives.WriteUInt32LittleEndian(p, id);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4), CheckedIndex);
            p[6] = optionFlag;
            return p;
        }

        /// <summary>A mode-3 packet as LandSandBoat sizes it: 8 bytes of sub-header and count, 28 bytes per item.</summary>
        private static byte[] Equipment(params (ushort Id, EquipSlotId Slot)[] items)
        {
            var p = Header(8 + 28 * Math.Max(1, items.Length), 3);
            p[7] = (byte)items.Length;
            for (int i = 0; i < items.Length; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(8 + i * 28), items[i].Id);
                p[8 + i * 28 + 2] = (byte)items[i].Slot;
                p[8 + i * 28 + 4] = 0x02; // augment type byte in Data: ignored
            }
            return p;
        }

        private static byte[] General(byte mainJob, byte mainLevel, byte subJob, byte subLevel, string linkshell, ushort color)
        {
            var p = Header(80, 1);
            if (linkshell.Length > 0)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(10), 513);
                Assert.True(LinkshellNameCodec.TryEncode(linkshell, p.AsSpan(12, 16)));
                BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(28), color);
            }
            p[30] = mainJob;
            p[31] = subJob;
            p[32] = mainLevel;
            p[33] = subLevel;
            p[34] = mainJob;
            return p;
        }

        [Fact]
        public void Mode3_ReadsItemsUpToTheCount()
        {
            var packet = new S2C_0x0C9_EquipInspect(Equipment((16385, EquipSlotId.Main), (12511, EquipSlotId.Head), (13469, EquipSlotId.Ring2)));
            Assert.True(packet.IsValid);
            Assert.Equal(EquipInspectMode.Equipment, packet.Mode);
            Assert.Equal(CheckedId, packet.ServerId);
            Assert.Equal(CheckedIndex, packet.TargetIndex);
            Span<EquipInspectItem> items = stackalloc EquipInspectItem[8];
            int count = packet.ReadItems(items);
            Assert.Equal(3, count);
            Assert.Equal(new EquipInspectItem(16385, EquipSlotId.Main), items[0]);
            Assert.Equal(new EquipInspectItem(12511, EquipSlotId.Head), items[1]);
            Assert.Equal(new EquipInspectItem(13469, EquipSlotId.Ring2), items[2]);
        }

        [Fact]
        public void Mode3_StopsAtAZeroItemAndSkipsBadSlots()
        {
            var p = Equipment((16385, EquipSlotId.Main), (0, EquipSlotId.Sub), (12511, EquipSlotId.Head));
            Span<EquipInspectItem> items = stackalloc EquipInspectItem[8];
            Assert.Equal(1, new S2C_0x0C9_EquipInspect(p).ReadItems(items));

            p = Equipment((16385, EquipSlotId.Main), (12511, EquipSlotId.Head));
            p[8 + 2] = 16; // a linkshell slot is not a check slot
            Assert.Equal(1, new S2C_0x0C9_EquipInspect(p).ReadItems(items));
            Assert.Equal(EquipSlotId.Head, items[0].Slot);
        }

        [Fact]
        public void Mode2_AndSingleItem_ReadTheOlderLayouts()
        {
            var p = Header(32, 2);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(7), 16385);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(9), 12511);
            p[23] = (byte)EquipSlotId.Main;
            p[24] = (byte)EquipSlotId.Head;
            Span<EquipInspectItem> items = stackalloc EquipInspectItem[8];
            Assert.Equal(2, new S2C_0x0C9_EquipInspect(p).ReadItems(items));
            Assert.Equal(new EquipInspectItem(12511, EquipSlotId.Head), items[1]);

            var single = Header(12, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(single.AsSpan(8), 13469);
            single[10] = (byte)EquipSlotId.Ring1;
            Assert.Equal(1, new S2C_0x0C9_EquipInspect(single).ReadItems(items));
            Assert.Equal(new EquipInspectItem(13469, EquipSlotId.Ring1), items[0]);
        }

        [Fact]
        public void Mode1_DecodesJobsAndLinkshell()
        {
            var packet = new S2C_0x0C9_EquipInspect(General(1, 75, 13, 37, "Gordian", 0x0F3A));
            Assert.True(packet.IsValid);
            Assert.Equal(EquipInspectMode.General, packet.Mode);
            Assert.Equal((1, 75, 13, 37), (packet.MainJob, packet.MainJobLevel, packet.SubJob, packet.SubJobLevel));
            Assert.Equal("Gordian", packet.LinkshellName);
            Assert.Equal(513, packet.LinkshellItemId);
            Assert.Equal(0x0F3A, packet.LinkshellColor);
            var truncated = new byte[79];
            truncated[6] = 1;
            Assert.False(new S2C_0x0C9_EquipInspect(truncated).IsValid);
            Assert.False(new S2C_0x0C9_EquipInspect(new byte[7]).IsValid);
        }

        private sealed class Fixture
        {
            public PlayerCommandState State { get; } = new();
            public WorldState World { get; } = new();
            public PacketDispatcher Dispatcher { get; } = new();

            public Fixture()
            {
                var module = new PlayerCommandPacketModule(State, World, (_, _) => Task.CompletedTask);
                module.Register(Dispatcher);
            }

            public void Receive(ushort id, byte[] payload) => Assert.True(Dispatcher.Dispatch(new PacketHeader(id, payload.Length + 4, 1), payload));
        }

        private static byte[] InspectMessage(string name, string message, bool bazaar)
        {
            var p = new byte[144];
            Encoding.ASCII.GetBytes(message).CopyTo(p, 0);
            p[123] = (byte)((bazaar ? 0x01 : 0) | (1 << 2));
            Encoding.ASCII.GetBytes(name).CopyTo(p, 124);
            return p;
        }

        [Fact]
        public void Module_CollectsEquipmentAndCompletesOnTheGeneralBlock()
        {
            var f = new Fixture();
            var completed = new List<EquipInspectInfo>();
            f.State.Equipment.Completed += completed.Add;

            // LandSandBoat's order: 0x0CA, then 0x0C9 mode 3 (eight items a packet), then mode 1.
            f.Receive(0x0CA, InspectMessage("Cybin", "Selling crystals", bazaar: true));
            f.Receive(0x0C9, Equipment((16385, EquipSlotId.Main), (12511, EquipSlotId.Head), (12638, EquipSlotId.Body), (14117, EquipSlotId.Feet),
                (13056, EquipSlotId.Neck), (13470, EquipSlotId.Ring1), (13469, EquipSlotId.Ring2), (13606, EquipSlotId.Back)));
            f.Receive(0x0C9, Equipment((17284, EquipSlotId.Sub), (13176, EquipSlotId.Waist)));
            Assert.Empty(completed);
            f.Receive(0x0C9, General(1, 75, 13, 37, "Gordian", 0x0F3A));

            var info = Assert.Single(completed);
            Assert.Same(info, f.State.Equipment.Last);
            Assert.Equal(CheckedId, info.ServerId);
            Assert.Equal(10, info.Equipment.Count);
            Assert.Equal(16385, info.ItemIn(EquipSlotId.Main));
            Assert.Equal(17284, info.ItemIn(EquipSlotId.Sub));
            Assert.Equal(0, info.ItemIn(EquipSlotId.Ammo));
            Assert.Equal("Gordian", info.LinkshellName);
            Assert.True(info.HasLinkshell);
            Assert.Equal("Cybin", info.Message?.Name);
            Assert.Equal("Lv.75 Warrior / Lv.37 Ninja", StockUiCheck.FormatJobs(info));
            Assert.Equal(LinkshellRank.Leader, info.LinkshellRank); // item 513

            // The next check starts from an empty set.
            f.Receive(0x0C9, General(0, 0, 0, 0, string.Empty, 0));
            Assert.Equal(2, completed.Count);
            Assert.Empty(completed[1].Equipment);
            Assert.True(completed[1].IsAnonymous);
            Assert.Equal("???", StockUiCheck.FormatJobs(completed[1])); // retail screenshot, 2026-10-07
            Assert.Equal(LinkshellRank.None, completed[1].LinkshellRank);
            Assert.False(completed[1].HasLinkshell);
        }

        [Fact]
        public void State_ItemsForAnotherCharacterStartANewCheck()
        {
            var state = new EquipInspectState();
            state.AddItems(1, new[] { new EquipInspectItem(100, EquipSlotId.Main) });
            state.AddItems(2, new[] { new EquipInspectItem(200, EquipSlotId.Head) });
            var info = state.Complete(2, 5, 1, 1, 0, 0, 1, 0, 0, 0, string.Empty, 0, null);
            Assert.Equal(0, info.ItemIn(EquipSlotId.Main));
            Assert.Equal(200, info.ItemIn(EquipSlotId.Head));
        }

        [Fact]
        public void LinkshellColour_WidensLikeTheEntityUpdates()
        {
            var state = new EquipInspectState();
            // r = 0xA, g = 0x3, b = 0xF: (c << 4) + 15 as LandSandBoat's entity updates carry it.
            var info = state.Complete(1, 1, 1, 1, 0, 0, 1, 0, 0, 513, "Shell", 0x0F3A, null);
            Assert.Equal(((byte)0xAF, (byte)0x3F, (byte)0xFF), info.LinkshellRgb);
        }

        [Theory]
        [InlineData(1, EquipSlotId.Main)]
        [InlineData(2, EquipSlotId.Head)]
        [InlineData(4, EquipSlotId.Back)]
        [InlineData(5, EquipSlotId.Sub)]
        [InlineData(10, EquipSlotId.Ear1)]
        [InlineData(11, EquipSlotId.Ring1)]
        [InlineData(14, EquipSlotId.Ear2)]
        [InlineData(16, EquipSlotId.Feet)]
        public void GridButtons_MapToSlotsByTheirLabels(int button, EquipSlotId slot)
        {
            Assert.True(StockUiCheck.TryGetSlot(button, out var mapped));
            Assert.Equal(slot, mapped);
            Assert.Equal(button, StockUiCheck.ButtonOf(slot));
            Assert.False(StockUiCheck.TryGetSlot(StockUiCheck.ViewWaresButton, out _));
        }

        [Fact]
        public void FormatJobs_ShowsTheMasteryLevelWhenUnlocked()
        {
            var info = new EquipInspectState().Complete(1, 1, 1, 99, 0, 0, 1, 12, 0x01, 0, string.Empty, 0, null);
            Assert.Equal("Lv.12 Warrior", StockUiCheck.FormatJobs(info));
            Assert.Equal("Lv.12 Fighter", StockUiCheck.FormatJobs(info, job => job == 1 ? "Fighter" : null));
        }

        private static UiMenuDefinition InspectMenu()
        {
            var buttons = new List<UiMenuButton>();
            for (int i = 1; i <= 16; i++)
            {
                int column = (i - 1) / 4, row = (i - 1) % 4;
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)i, X = (short)(24 + 34 * column), Y = (short)(11 + 34 * row), Width = 32, Height = 32,
                    NavUp = (sbyte)(row == 0 ? 17 : i - 1), NavDown = (sbyte)(row == 3 ? 17 : i + 1),
                    NavLeft = (sbyte)(column == 0 ? i + 12 : i - 4), NavRight = (sbyte)(column == 3 ? i - 12 : i + 4),
                });
            }
            buttons.Add(new UiMenuButton { ButtonId = 17, X = 48, Y = 148, Width = 88, Height = 16, NavUp = 4, NavDown = 1, NavLeft = 17, NavRight = 17 });
            return new UiMenuDefinition
            {
                Category = "menu", Name = StockUiCheck.MenuName,
                Frame = new UiMenuFrame { X = 16, Y = 48, Width = 182, Height = 190 },
                Buttons = buttons,
            };
        }

        private static StockUiCheckData Data(bool bazaar)
        {
            var state = new EquipInspectState();
            state.AddItems(CheckedId, new[] { new EquipInspectItem(16385, EquipSlotId.Main), new EquipInspectItem(23407, EquipSlotId.Head) });
            // LandSandBoat sets 0x0CA's BazaarFlag on every packet; whether there is a bazaar comes from the entity update.
            var message = new InspectMessageInfo("Cybin", "Selling crystals\nCheap!", true, false, 1, 0);
            return new StockUiCheckData("Cybin", state.Complete(CheckedId, CheckedIndex, 10, 99, 3, 49, 10, 0, 0, 0, string.Empty, 0, message), bazaar);
        }

        /// <summary>Bihu Roundlet +3 as the item DAT has it (ten description lines; the retail screenshots show 8 + 2).</summary>
        private static ItemRecord Bihu() => new()
        {
            ItemId = 23407, Name = "Bihu Roundlet +3", LogName = "bihu roundlet +3", Level = 99, ItemLevel = 119,
            EquipSlotsMask = 0x10, RacesMask = 0x1FE, JobsMask = 0x400,
            Description = "DEF:115 HP+56 MP+52 STR+21\nDEX+24 VIT+28 AGI+24\nINT+29 MND+27 CHR+40\nAccuracy+37 Attack+62\nMagic Accuracy+51\n" +
                          "Evasion+58 Magic Evasion+95\n\"Magic Def. Bonus\"+7\nSinging skill +18\nHaste+6% Enmity-9\nPhysical Damage taken-6%",
        };

        private static StockUiMenuController Controller() => new()
        {
            Library = UiResourceLibrary.FromDefinitions(new[] { InspectMenu() }),
            ItemLookup = id => id == 23407 ? Bihu() : new ItemRecord
            {
                ItemId = id, Name = "Cesti", LogName = "cesti", Description = "DMG:+1 Delay:+48 Accuracy+3", Skill = 1, EquipSlotsMask = 1,
                RacesMask = 0x1FE, Level = 1, JobsMask = 0x80BE6,
            },
        };

        [Fact]
        public void Controller_OpensWithTheCursorOnViewWaresAndTheHelpBarJobs()
        {
            var controller = Controller();
            var menu = controller.OpenCheck(Data(bazaar: true));
            Assert.NotNull(menu);
            Assert.Same(menu, controller.Top);
            Assert.Single(controller.OpenMenus);
            Assert.True(menu!.IsCheck);
            Assert.True(controller.IsCheckOpen);
            // Retail (2026-10-07 screenshot): the cursor starts on View Wares, where the bazaar comment shows.
            Assert.Equal(StockUiCheck.ViewWaresButton, menu.SelectedButtonId);
            Assert.Equal(0, menu.SelectedCheckItem);
            Assert.False(menu.IsGreyed(StockUiCheck.ViewWaresButton));
            Assert.Equal(new[] { "Selling crystals", "Cheap!" }, menu.Check!.CommentLines);
            Assert.Equal("Lv.99 Bard / Lv.49 White Mage", menu.Check.JobText);

            controller.Move(Gordian.Core.Input.InputAction.MenuDown); // View Wares -> Main
            Assert.Equal(1, menu.SelectedButtonId);
            Assert.Equal(16385, menu.SelectedCheckItem);
            controller.Move(Gordian.Core.Input.InputAction.MenuDown);
            Assert.Equal(23407, menu.SelectedCheckItem); // Head
            controller.Move(Gordian.Core.Input.InputAction.MenuDown);
            Assert.Equal(0, menu.SelectedCheckItem); // Body: empty

            // A second check replaces the window; nothing for sale greys View Wares.
            var again = controller.OpenCheck(Data(bazaar: false));
            Assert.Single(controller.OpenMenus);
            Assert.True(again!.IsGreyed(StockUiCheck.ViewWaresButton));
            Assert.Equal(StockUiCheck.ViewWaresButton, again.SelectedButtonId); // the cursor can still rest there
        }

        [Fact]
        public void Controller_GreyedViewWaresDoesNothingAndCancelCloses()
        {
            var controller = Controller();
            var notices = new List<string>();
            controller.NoticePosted += notices.Add;
            var menu = controller.OpenCheck(Data(bazaar: false))!;
            controller.Activate();
            Assert.Empty(notices);
            Assert.Same(menu, controller.Top);

            controller.OpenCheck(Data(bazaar: true));
            controller.Activate();
            Assert.Equal("View Wares is not available yet.", Assert.Single(notices));

            controller.Top!.SelectedButtonId = 1;
            controller.Activate(); // a slot does nothing
            Assert.Single(notices);

            controller.CloseTop();
            Assert.False(controller.IsOpen);
            Assert.False(controller.IsCheckOpen);
        }

        [Fact]
        public void Description_PagesAsRetailShowsBihuRoundlet()
        {
            var pages = StockUiItemDescription.Pages(Bihu());
            Assert.Equal(2, pages.Count);
            // Page 1: name, slot line, eight description lines, the level line, the footer: twelve lines.
            Assert.Equal("Bihu roundlet +3", pages[0].Lines[0]);
            Assert.Equal("[Head]All Races", pages[0].Lines[1]);
            Assert.Equal("Singing skill +18", pages[0].Lines[9]);
            Assert.Equal("Lv.99 BRD", pages[0].Lines[10]);
            Assert.Equal("<Item Level:119>", pages[0].Footer);
            Assert.True(pages[0].HasMore);
            Assert.Equal(12, pages[0].LineCount);
            Assert.Equal("item12in", StockUiItemDescription.FrameFor(pages[0].LineCount));
            // Page 2: name, slot line, the two remaining lines, the footer.
            Assert.Equal(new[] { "Bihu roundlet +3", "[Head]All Races", "Haste+6% Enmity-9", "Physical Damage taken-6%" }, pages[1].Lines);
            Assert.False(pages[1].HasMore);
            Assert.Equal("item5inf", StockUiItemDescription.FrameFor(pages[1].LineCount));
        }

        [Fact]
        public void Description_WeaponsRacesAndJobs()
        {
            var cesti = new ItemRecord
            {
                Name = "Behem. Cesti +1", LogName = "behemoth cesti +1", Skill = 1, EquipSlotsMask = 1, RacesMask = 0x1FE, Level = 70,
                JobsMask = 0x80BE6, Description = "DMG:+8 Delay:+40 HP+17 Accuracy+4",
            };
            var page = Assert.Single(StockUiItemDescription.Pages(cesti));
            Assert.Equal("Behemoth cesti +1", page.Lines[0]);
            Assert.Equal("(Hand-to-Hand)All Races", page.Lines[1]);
            Assert.Equal("Lv.70 WAR MNK RDM THF PLD DRK BST RNG DNC", page.Lines[3]);
            Assert.Equal(string.Empty, page.Footer);
            Assert.Equal(4, page.LineCount);
            Assert.Equal("item4inf", StockUiItemDescription.FrameFor(page.LineCount));
            Assert.Equal("Hume Male Elvaan", StockUiItemDescription.Races(0x02 | 0x08 | 0x10));
            Assert.Equal("Royal Archer's cesti", StockUiItemDescription.DisplayName(new ItemRecord { Name = "Ryl.Arc. Cesti", LogName = "Royal Archer's cesti" }));
            Assert.Equal("iteminfo", StockUiItemDescription.FrameFor(2));
        }

        [Fact]
        public void Controller_PagesTheDescriptionAndResetsOnMove()
        {
            var controller = Controller();
            var menu = controller.OpenCheck(Data(bazaar: true))!;
            Assert.False(controller.NextCheckPage()); // on View Wares: nothing to page
            menu.SelectedButtonId = 2; // Head: Bihu Roundlet +3
            Assert.Equal(2, menu.SelectedCheckPages.Count);
            Assert.True(controller.NextCheckPage());
            Assert.Equal(1, menu.CheckPage);
            Assert.True(controller.NextCheckPage());
            Assert.Equal(0, menu.CheckPage); // wraps
            controller.NextCheckPage();
            controller.Move(Gordian.Core.Input.InputAction.MenuUp); // to Main
            controller.Move(Gordian.Core.Input.InputAction.MenuDown); // back to Head
            Assert.Equal(0, menu.CheckPage);
            menu.SelectedButtonId = 1; // Cesti: one page
            Assert.False(controller.NextCheckPage());
        }

        [Fact]
        public void RetailMenus_InspectWindowHasTheGridAndViewWares()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var ui = UiResourceLibrary.Load(rm);
            if (ui == null) return;

            Assert.True(ui.TryGetMenu(StockUiCheck.MenuName, out var inspect));
            Assert.Equal((16, 48, 182, 190, UiAnchor.TopLeft), (inspect.Frame.X, inspect.Frame.Y, inspect.Frame.Width, inspect.Frame.Height, inspect.Frame.Anchor));
            Assert.Equal(17, inspect.Buttons.Count);
            // The grid and its labels are the equipment window's: button n carries the same label image in both.
            Assert.True(ui.TryGetMenu("equip", out var equip));
            for (int id = 1; id <= 16; id++)
            {
                var a = inspect.FindButton(id)!;
                var b = equip.FindButton(id)!;
                Assert.Equal((a.X, a.Y), (b.X, b.Y));
                Assert.Equal(a.Shapes[0].ImageIndex, b.Shapes[0].ImageIndex);
            }
            var wares = inspect.FindButton(StockUiCheck.ViewWaresButton)!;
            Assert.Equal((48, 148), ((int)wares.X, (int)wares.Y));
            Assert.Contains(wares.Shapes, s => s.Kind == 4); // the greyed look
            Assert.True(ui.TryGetMenu(StockUiCheck.CommentMenu, out var comment));
            Assert.Equal((16, 240, 366, 56), (comment.Frame.X, comment.Frame.Y, comment.Frame.Width, comment.Frame.Height));
            for (int n = 3; n <= StockUiItemDescription.MaxLines; n++)
            {
                // The item info windows grow 16 px a line: 8 + 16 n.
                Assert.True(ui.TryGetMenu(StockUiItemDescription.FrameFor(n), out var info), StockUiItemDescription.FrameFor(n));
                Assert.Equal((366, 8 + 16 * n), ((int)info.Frame.Width, (int)info.Frame.Height));
            }
            Assert.True(ui.TryGetMenu(StockUiCheck.TitleMenu, out _));
            Assert.True(ui.TryGetMenu(StockUiCheck.HelpMenu, out _));
        }
    }
}
