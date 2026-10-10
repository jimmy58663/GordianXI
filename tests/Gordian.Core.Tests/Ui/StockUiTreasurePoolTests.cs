// tests/Gordian.Core.Tests/Ui/StockUiTreasurePoolTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Input;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    /// <summary>The Treasure Pool window (#143): rows, countdown, Cast Lot / Pass, the command menus' Treasure entry.</summary>
    public class StockUiTreasurePoolTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const uint LocalId = 0x01000001;

        /// <summary>An S2C 0x0D2 payload as LandSandBoat lays it out (see TreasurePacketTests).</summary>
        private static byte[] TrophyList(byte slot, ushort itemId, uint startTime, byte entry = 0, ushort localLot = 0, string leader = "", ushort leaderLot = 0)
        {
            var p = new byte[56];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4, 4), 0x01000200);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(12, 2), itemId);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(14, 2), 0x200);
            p[16] = slot;
            p[17] = entry;
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(20, 4), startTime);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(24, 2), (ushort)(localLot > 0 ? 1 : 0));
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(26, 2), localLot);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(34, 2), leaderLot);
            Encoding.ASCII.GetBytes(leader).CopyTo(p.AsSpan(36));
            return p;
        }

        private sealed class Fixture
        {
            public TreasurePoolState Pool { get; } = new();
            public PacketDispatcher Dispatcher { get; } = new();
            public long Now { get; set; } = 1_000_000;

            public Fixture()
            {
                Pool.LocalMilliseconds = () => Now;
                var module = new TreasurePacketModule(Pool, new LocalPlayerState { ServerId = LocalId }, null, (_, _) => Task.CompletedTask);
                module.Register(Dispatcher);
            }

            public void Found(byte slot, ushort itemId, uint startTime, byte entry = 0, ushort localLot = 0, string leader = "", ushort leaderLot = 0) =>
                Assert.True(Dispatcher.Dispatch(new PacketHeader(0x0D2, 60, 1), TrophyList(slot, itemId, startTime, entry, localLot, leader, leaderLot)));
        }

        [Fact]
        public void Countdown_FollowsTheServerClockOfTheNewestItem()
        {
            var f = new Fixture();
            // Server time 500 000 ms at local 1 000 000: a new item (stamped 3 s early, as LandSandBoat does).
            f.Found(0, 4096, 497_000);
            var slot = f.Pool.GetSlot(0)!;
            Assert.Equal(TimeSpan.FromSeconds(300), f.Pool.GetRemaining(slot)); // the first item sets the estimate
            f.Now += 60_000;
            Assert.Equal(TimeSpan.FromSeconds(240), f.Pool.GetRemaining(slot));

            // A newer item moves the estimate up to it; an older one (re-sent) does not move it back.
            f.Found(1, 4097, 557_000 + 2_000);
            f.Found(2, 4098, 400_000);
            Assert.Equal(TimeSpan.FromSeconds(238), f.Pool.GetRemaining(slot));
            Assert.Equal(TimeSpan.FromSeconds(141), f.Pool.GetRemaining(f.Pool.GetSlot(2)!)); // found 159 s before now
            f.Now += 10 * 60_000;
            Assert.Equal(TimeSpan.Zero, f.Pool.GetRemaining(slot)); // never negative

            Assert.Equal("5:00", StockUiTreasurePool.FormatTimeLeft(TimeSpan.FromSeconds(300)));
            Assert.Equal("4:59", StockUiTreasurePool.FormatTimeLeft(TimeSpan.FromSeconds(298.2)));
            Assert.Equal("0:01", StockUiTreasurePool.FormatTimeLeft(TimeSpan.FromMilliseconds(10)));
            Assert.Equal("0:00", StockUiTreasurePool.FormatTimeLeft(TimeSpan.Zero));
        }

        [Fact]
        public void Row_LotAndPassRules()
        {
            var row = new StockUiTreasureRow(0, 4096, "Fire Crystal", 1, TreasureEntryKind.None, 0, string.Empty, 0);
            Assert.True(row.CanLot);
            row = row with { Entry = TreasureEntryKind.Lot, LocalLot = 337 };
            Assert.False(row.CanLot);
            Assert.True(row.CanPass);
            row = row with { Entry = TreasureEntryKind.Pass };
            Assert.False(row.CanPass);
        }

        [Fact]
        public void MemberEntries_FollowTheLotsAndPassesForTheRollColumn()
        {
            var f = new Fixture();
            const uint Knot = 0x01000002;
            f.Found(0, 4096, 497_000);
            Assert.Equal("?", StockUiTreasurePool.RollText(f.Pool.GetMemberEntry(0, LocalId, LocalId)));
            Assert.Equal("?", StockUiTreasurePool.RollText(f.Pool.GetMemberEntry(0, Knot, LocalId)));

            // 0x0D3 progress: we lot 408, Knot passes (the retail screenshots' "408" and "---").
            var lot = new byte[56];
            BinaryPrimitives.WriteUInt32LittleEndian(lot.AsSpan(0, 4), LocalId);
            BinaryPrimitives.WriteUInt32LittleEndian(lot.AsSpan(4, 4), LocalId);
            BinaryPrimitives.WriteInt16LittleEndian(lot.AsSpan(10, 2), 408);
            BinaryPrimitives.WriteUInt16LittleEndian(lot.AsSpan(12, 2), 0x8000);
            BinaryPrimitives.WriteInt16LittleEndian(lot.AsSpan(14, 2), 408);
            Assert.True(f.Dispatcher.Dispatch(new PacketHeader(0x0D3, 60, 1), lot));
            var pass = new byte[56];
            BinaryPrimitives.WriteUInt32LittleEndian(pass.AsSpan(0, 4), LocalId);
            BinaryPrimitives.WriteUInt32LittleEndian(pass.AsSpan(4, 4), Knot);
            BinaryPrimitives.WriteInt16LittleEndian(pass.AsSpan(10, 2), 408);
            Assert.True(f.Dispatcher.Dispatch(new PacketHeader(0x0D3, 60, 1), pass));
            Assert.Equal("408", StockUiTreasurePool.RollText(f.Pool.GetMemberEntry(0, LocalId, LocalId)));
            Assert.Equal("---", StockUiTreasurePool.RollText(f.Pool.GetMemberEntry(0, Knot, LocalId)));

            // A re-sent 0x0D2 (after a zone change) carries our own lot on the slot.
            f.Found(1, 4097, 497_000, entry: (byte)TreasureEntryKind.Lot, localLot: 120);
            Assert.Equal("120", StockUiTreasurePool.RollText(f.Pool.GetMemberEntry(1, LocalId, LocalId)));

            // A judgement forgets the slot's entries.
            var judge = new byte[56];
            judge[17] = (byte)TreasureJudge.Win;
            Assert.True(f.Dispatcher.Dispatch(new PacketHeader(0x0D3, 60, 1), judge));
            Assert.Null(f.Pool.GetMemberEntry(0, Knot, LocalId));
        }

        /// <summary>
        /// S2C 0x0D3 as LandSandBoat builds it (0x0d3_trophy_solution.cpp, treasure_pool.cpp): the highest lotter at 0,
        /// the entry's id at 4, the highest lot at 10, the entry's index at 12 with EntryFlg never set, the entry's roll at
        /// 14 (0xFFFF for a pass), the slot at 16, judge 0.
        /// </summary>
        private static byte[] LsbEntry(byte slot, uint entryId, ushort entryIndex, short entryPoint, uint leaderId, short leaderLot)
        {
            var p = new byte[56];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), leaderId);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4, 4), entryId);
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(10, 2), leaderLot);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(12, 2), entryIndex); // bit 15 (EntryFlg) clear, as LSB sends it
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(14, 2), entryPoint);
            p[16] = slot;
            return p;
        }

        [Fact]
        public void LandSandBoatLotAndPass_ColourTheRowsAndFillTheRollColumn()
        {
            var (f, menus, _, _) = Setup();
            const uint Knot = 0x01000002;
            f.Found(0, 4096, 497_000);
            f.Found(1, 4097, 497_000);
            var list = menus.OpenTreasurePool(null)!;

            // Our lot of 408 on slot 0: EntryFlg clear, the roll in EntryPoint.
            var lot = LsbEntry(0, LocalId, 0x0400, 408, LocalId, 408);
            var decoded = new S2C_0x0D3_TrophySolution(lot);
            Assert.True(decoded.EntryIsLot);
            Assert.Equal(408, decoded.EntryLot);
            Assert.True(f.Dispatcher.Dispatch(new PacketHeader(0x0D3, 60, 1), lot));
            Assert.Equal(TreasureEntryKind.Lot, list.TreasureRows[0].Entry); // orange-red, not the pass grey
            Assert.Equal(408, list.TreasureRows[0].LocalLot);
            Assert.Equal("408", StockUiTreasurePool.RollText(f.Pool.GetMemberEntry(0, LocalId, LocalId)));
            Assert.Equal("?", StockUiTreasurePool.RollText(f.Pool.GetMemberEntry(0, Knot, LocalId)));

            // Knot passes on slot 0 (EntryPoint 0xFFFF), we pass on slot 1.
            var knotPass = LsbEntry(0, Knot, 0x0401, -1, LocalId, 408);
            Assert.False(new S2C_0x0D3_TrophySolution(knotPass).EntryIsLot);
            Assert.True(f.Dispatcher.Dispatch(new PacketHeader(0x0D3, 60, 1), knotPass));
            Assert.True(f.Dispatcher.Dispatch(new PacketHeader(0x0D3, 60, 1), LsbEntry(1, LocalId, 0x0400, -1, 0, 0)));
            Assert.Equal("---", StockUiTreasurePool.RollText(f.Pool.GetMemberEntry(0, Knot, LocalId)));
            Assert.Equal("408", StockUiTreasurePool.RollText(f.Pool.GetMemberEntry(0, LocalId, LocalId))); // Knot's pass leaves ours
            Assert.Equal(TreasureEntryKind.Lot, list.TreasureRows[0].Entry);
            Assert.Equal(TreasureEntryKind.Pass, list.TreasureRows[1].Entry); // grey
            Assert.Equal("---", StockUiTreasurePool.RollText(f.Pool.GetMemberEntry(1, LocalId, LocalId)));

            // XiPackets' layout (EntryFlg set on a lot) still reads as a lot.
            var retail = LsbEntry(1, Knot, 0x8401, 77, Knot, 77);
            Assert.True(new S2C_0x0D3_TrophySolution(retail).EntryIsLot);
        }

        [Fact]
        public void Done_PassesEveryItemNotLottedAndTogglesWithPlus()
        {
            var (f, menus, sent, _) = Setup();
            f.Found(0, 4096, 497_000);
            f.Found(1, 4097, 497_000, entry: (byte)TreasureEntryKind.Lot, localLot: 300);
            f.Found(2, 4098, 497_000, entry: (byte)TreasureEntryKind.Pass);
            f.Found(3, 4099, 497_000);
            var list = menus.OpenTreasurePool(null)!;

            // + / Y: the cursor to Done and back.
            Assert.True(menus.ToggleTreasureDone());
            Assert.True(menus.Top!.IsTreasureDone);
            Assert.True(menus.Top.IsTreasureWindow);
            Assert.True(menus.ToggleTreasureDone());
            Assert.Same(list, menus.Top);

            Assert.True(menus.ToggleTreasureDone());
            menus.Activate(); // Done
            Assert.Equal(new[] { ("pass", (byte)0), ("pass", (byte)3) }, sent.OrderBy(x => x.Item2));
            Assert.Same(list, menus.Top);

            // Outside the list + does nothing here.
            menus.CloseAll();
            Assert.False(menus.ToggleTreasureDone());
        }

        private static UiMenuDefinition LootMenu()
        {
            var buttons = new List<UiMenuButton>();
            for (int i = 1; i <= 10; i++)
            {
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)i, X = 0, Y = (short)(5 + 18 * (i - 1)), Width = 132, Height = 16,
                    NavUp = (sbyte)Math.Max(1, i - 1), NavDown = (sbyte)Math.Min(10, i + 1), NavLeft = (sbyte)i, NavRight = (sbyte)i,
                });
            }
            return new UiMenuDefinition { Category = "menu", Name = StockUiTreasurePool.ListMenu, Frame = new UiMenuFrame { X = 16, Y = 48, Width = 182, Height = 190 }, Buttons = buttons };
        }

        private static UiMenuDefinition LotMenu() => new()
        {
            Category = "menu",
            Name = StockUiTreasurePool.ActionMenu,
            Frame = new UiMenuFrame { X = 0, Y = 0, Width = 112, Height = 40 },
            Buttons = new[]
            {
                new UiMenuButton { ButtonId = 1, X = 21, Y = 5, Width = 88, Height = 16, NavUp = 2, NavDown = 2, NavLeft = 1, NavRight = 1 },
                new UiMenuButton { ButtonId = 2, X = 21, Y = 21, Width = 88, Height = 16, NavUp = 1, NavDown = 1, NavLeft = 2, NavRight = 2 },
            },
        };

        private static UiMenuDefinition DoneMenu() => new()
        {
            Category = "menu",
            Name = StockUiTreasurePool.DoneMenu,
            Frame = new UiMenuFrame { X = 384, Y = 48, Width = 112, Height = 45, Anchor = UiAnchor.TopRight },
            Buttons = new[] { new UiMenuButton { ButtonId = 1, X = 16, Y = 22, Width = 88, Height = 16, NavUp = -1, NavDown = -1, NavLeft = -1, NavRight = -1 } },
        };

        private static (Fixture F, StockUiMenuController Menus, List<(string, byte)> Sent, List<string> Notices) Setup()
        {
            var f = new Fixture();
            var sent = new List<(string, byte)>();
            var notices = new List<string>();
            var menus = new StockUiMenuController
            {
                Library = UiResourceLibrary.FromDefinitions(new[] { LootMenu(), LotMenu(), DoneMenu() }),
                TreasurePool = f.Pool,
                ItemLookup = id => new ItemRecord { ItemId = id, Name = $"Item {id}", LogName = $"item {id}" },
                TreasureLot = slot => { sent.Add(("lot", slot)); return Task.CompletedTask; },
                TreasurePass = slot => { sent.Add(("pass", slot)); return Task.CompletedTask; },
            };
            menus.NoticePosted += notices.Add;
            f.Pool.Changed += menus.OnTreasureChanged;
            return (f, menus, sent, notices);
        }

        [Fact]
        public void List_ShowsThePoolAndCastsLotsFromTheActionWindow()
        {
            var (f, menus, sent, _) = Setup();
            f.Found(3, 4096, 497_000);
            f.Found(7, 4097, 497_500);

            var list = menus.OpenTreasurePool(null);
            Assert.NotNull(list);
            Assert.True(list!.IsTreasureList);
            Assert.Equal(new byte[] { 3, 7 }, list.TreasureRows.Select(r => r.Slot));
            Assert.Equal("Item 4096", list.TreasureRows[0].Name);

            // Only rows with an item take the cursor.
            menus.Move(InputAction.MenuDown);
            Assert.Equal(2, list.SelectedButtonId);
            menus.Move(InputAction.MenuDown);
            Assert.Equal(2, list.SelectedButtonId);

            // Confirm opens Cast Lot / Pass beside the row: right of the list, level with the row.
            menus.Activate();
            var action = menus.Top!;
            Assert.Equal((byte)7, action.TreasureActionSlot);
            Assert.Equal((16 + 182 + StockUiTreasurePool.ActionWindowGap, 48 + 23 - 5), ((int)action.Menu.Frame.X, (int)action.Menu.Frame.Y));
            Assert.False(action.OverlapsAuthored(list)); // the list stays drawn
            Assert.Equal(StockUiTreasurePool.LotButton, action.SelectedButtonId);
            menus.Activate();
            Assert.Equal(("lot", (byte)7), Assert.Single(sent));
            Assert.Same(list, menus.Top);
        }

        [Fact]
        public void ActionWindow_GreysWhatYouHaveDone_AndPassStaysAfterALot()
        {
            var (f, menus, sent, notices) = Setup();
            f.Found(0, 4096, 497_000, entry: (byte)TreasureEntryKind.Lot, localLot: 337);
            menus.OpenTreasurePool(null);
            menus.Activate();
            var action = menus.Top!;
            Assert.True(action.IsGreyed(StockUiTreasurePool.LotButton));
            Assert.False(action.IsGreyed(StockUiTreasurePool.PassButton));
            Assert.Equal(StockUiTreasurePool.PassButton, action.SelectedButtonId);

            action.SelectedButtonId = StockUiTreasurePool.LotButton;
            menus.Activate();
            Assert.Empty(sent);
            Assert.Single(notices);
            Assert.Same(action, menus.Top);

            action.SelectedButtonId = StockUiTreasurePool.PassButton;
            menus.Activate();
            Assert.Equal(("pass", (byte)0), Assert.Single(sent));
        }

        [Fact]
        public void PoolChanges_RefreshTheListAndCloseItWhenEmpty()
        {
            var (f, menus, _, _) = Setup();
            f.Found(2, 4096, 497_000);
            f.Found(5, 4097, 497_000);
            var list = menus.OpenTreasurePool(null)!;
            menus.Move(InputAction.MenuDown); // slot 5
            menus.Activate();
            Assert.Equal((byte)5, menus.Top!.TreasureActionSlot);

            // Slot 2 is given out (a judgement): the cursor follows slot 5 to the first row.
            var judge = new byte[56];
            judge[16] = 2;
            judge[17] = (byte)TreasureJudge.Win;
            Assert.True(f.Dispatcher.Dispatch(new PacketHeader(0x0D3, 60, 1), judge));
            Assert.Single(list.TreasureRows);
            Assert.Equal(1, list.SelectedButtonId);
            Assert.Equal((byte)5, menus.Top!.TreasureActionSlot);

            // Slot 5 goes too: the action window and the empty list close.
            judge[16] = 5;
            Assert.True(f.Dispatcher.Dispatch(new PacketHeader(0x0D3, 60, 1), judge));
            Assert.False(menus.IsOpen);
        }

        [Fact]
        public void EmptyPool_PostsANoticeInsteadOfOpening()
        {
            var (_, menus, _, notices) = Setup();
            Assert.Null(menus.OpenTreasurePool(null));
            Assert.Equal("There is nothing in the treasure pool.", Assert.Single(notices));
        }

        [Theory]
        [InlineData(StockUiTargetKind.Self, false, false)]
        [InlineData(StockUiTargetKind.Player, false, false)]
        [InlineData(StockUiTargetKind.Monster, false, false)]
        [InlineData(StockUiTargetKind.Monster, true, true)]
        public void CommandMenu_OffersTreasureBeforeCheckWhileThePoolHasItems(StockUiTargetKind kind, bool engaged, bool withTarget)
        {
            var context = new StockUiTargetContext(kind, 1, "X", engaged, withTarget);
            Assert.DoesNotContain(StockUiCommandMenu.Compose(context), r => r.Entry.Command == StockUiMenuCommand.TreasurePool);
            var rows = StockUiCommandMenu.Compose(context with { HasTreasure = true });
            int treasure = rows.ToList().FindIndex(r => r.Entry.Command == StockUiMenuCommand.TreasurePool);
            Assert.True(treasure >= 0);
            Assert.Equal(StockUiMenuCommand.Check, rows[treasure + 1].Entry.Command);
            Assert.Equal("Treasure", rows[treasure].Label.Text);
            Assert.DoesNotContain(StockUiCommandMenu.Compose(new StockUiTargetContext(StockUiTargetKind.Pet, 1, "X", HasTreasure: true)),
                r => r.Entry.Command == StockUiMenuCommand.TreasurePool);
        }

        [Fact]
        public void RetailMenus_LootWindowsResolve()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var ui = UiResourceLibrary.Load(rm);
            if (ui == null) return;

            Assert.True(ui.TryGetMenu(StockUiTreasurePool.ListMenu, out var loot));
            Assert.Equal((16, 48, 182, 190, UiAnchor.TopLeft), (loot.Frame.X, loot.Frame.Y, loot.Frame.Width, loot.Frame.Height, loot.Frame.Anchor));
            Assert.Equal(73, loot.Frame.TitleTextId);
            Assert.Equal(10, loot.Buttons.Count);
            for (int i = 1; i <= 10; i++) Assert.Equal((0, 5 + 18 * (i - 1)), ((int)loot.FindButton(i)!.X, (int)loot.FindButton(i)!.Y));

            Assert.True(ui.TryGetMenu(StockUiTreasurePool.ActionMenu, out var lootope));
            foreach (int id in new[] { StockUiTreasurePool.LotButton, StockUiTreasurePool.PassButton })
            {
                var button = lootope.FindButton(id)!;
                Assert.Contains(button.Shapes, s => s.Kind == 0 && ui.TryGetImage(s, out _));
                Assert.Contains(button.Shapes, s => s.Kind == 4 && ui.TryGetImage(s, out _)); // greyed
            }
            Assert.True(ui.TryGetMenu(StockUiTreasurePool.DoneMenu, out var done));
            Assert.Equal((384, 48, UiAnchor.TopRight), ((int)done.Frame.X, (int)done.Frame.Y, done.Frame.Anchor));
            Assert.NotNull(done.FindButton(StockUiTreasurePool.DoneButton));
            // The command menus' Treasure label resolves.
            Assert.True(ui.TryGetMenu(StockUiCommandMenu.Treasure.Menu, out var playermo));
            Assert.NotNull(playermo.FindButton(StockUiCommandMenu.Treasure.Button));
        }
    }
}
