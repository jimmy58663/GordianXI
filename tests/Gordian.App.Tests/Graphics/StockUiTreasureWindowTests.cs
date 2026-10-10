// tests/Gordian.App.Tests/Graphics/StockUiTreasureWindowTests.cs
using System;
using System.Buffers.Binary;
using System.Text;
using System.Threading.Tasks;
using Gordian.App.Graphics;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>The Treasure Pool window (#143) rendered from the retail DATs on D3D11 (GORDIAN_UI_DUMP writes the frame).</summary>
    public class StockUiTreasureWindowTests
    {
        private static byte[] TrophyList(byte slot, ushort itemId, uint startTime, byte entry, ushort localLot, string leader, ushort leaderLot)
        {
            var p = new byte[56];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4, 4), 0x01000200);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(12, 2), itemId);
            p[16] = slot;
            p[17] = entry;
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(20, 4), startTime);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(24, 2), (ushort)(localLot > 0 ? 1 : 0));
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(26, 2), localLot);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(34, 2), leaderLot);
            Encoding.ASCII.GetBytes(leader).CopyTo(p.AsSpan(36));
            return p;
        }

        [Fact]
        public void RendersPoolListWithCountdownInfoAndLotWindow()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;

            var pool = new TreasurePoolState();
            var dispatcher = new PacketDispatcher();
            new TreasurePacketModule(pool, new LocalPlayerState { ServerId = 1 }, null, (_, _) => Task.CompletedTask).Register(dispatcher);
            // A crystal found just now, a cloth a minute ago (lotted 337, Gemini leads with 512), a passed beastcoin.
            uint now = 600_000;
            dispatcher.Dispatch(new PacketHeader(0x0D2, 60, 1), TrophyList(0, 4096, now - 3_000, 0, 0, "", 0));
            dispatcher.Dispatch(new PacketHeader(0x0D2, 60, 1), TrophyList(1, 816, now - 63_000, (byte)TreasureEntryKind.Lot, 337, "Gemini", 512));
            dispatcher.Dispatch(new PacketHeader(0x0D2, 60, 1), TrophyList(2, 880, now - 120_000, (byte)TreasureEntryKind.Pass, 0, "", 0));

            var menus = new StockUiMenuController
            {
                Library = screen.Library,
                TreasurePool = pool,
                ItemLookup = id => screen.Resources.TryGetItem(id, out var record) ? record : null,
            };
            var list = menus.OpenTreasurePool(null)!;
            menus.Move(Gordian.Core.Input.InputAction.MenuDown); // the cloth
            menus.Activate(); // Cast Lot / Pass
            Assert.Equal(2, menus.OpenMenus.Count);

            StockUiPlacement Place(StockUiOpenMenu menu)
            {
                var frame = menu.Menu.Frame;
                return StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
            }
            float window1Top = screen.Height - 150;
            var layout = new StockUiScreen(screen.Width, screen.Height, window1Top);
            // The party window with the roll column for the selected cloth: we lotted 337, the other member has not acted.
            Assert.True(screen.Library.TryGetMenu("ptw2", out var party));
            var partyAt = StockUiLayout.Place(party.Frame.Anchor, party.Frame.X, party.Frame.Y, party.Frame.Width, party.Frame.Height, 1, screen.Width, screen.Height);
            var rolls = new[] { StockUiTreasurePool.RollText(pool.GetMemberEntry(1, 1, 1)), StockUiTreasurePool.RollText(pool.GetMemberEntry(1, 2, 1)) };
            Assert.Equal(new[] { "337", "?" }, rolls);
            var shot = screen.Render(r =>
            {
                foreach (var menu in menus.OpenMenus) StockUiMenuWindow.Draw(r, screen.Library, screen.Font, menu, Place(menu), 0, logFont: screen.LogFont, screen: layout);
                r.DrawMenu(party, partyAt, includeButtons: false);
                StockUiTreasureWindow.DrawRollColumn(r, screen.Font, party, partyAt, rolls, label: true);
            }, "gpu_treasure_pool.png");

            var at = Place(list);
            int px = (int)at.X, py = (int)at.Y;
            // Row names after the icon boxes and the countdowns at the right.
            Assert.True(screen.HasLightPixel(shot, px + 22, px + 120, py + 5 + 8), "no name on the first row");
            Assert.True(screen.HasLightPixel(shot, px + 140, px + 176, py + 5 + 8), "no countdown on the first row");
            // The cloth was lotted: its name is orange-red, not white.
            bool lotted = false;
            for (int x = px + 22; x < px + 120 && !lotted; x++)
            {
                var p = screen.Pixel(shot, x, py + 23 + 8);
                lotted = p.R > 200 && p.G > 90 && p.G < 190 && p.B < 140;
            }
            Assert.True(lotted, "the lotted row is not orange-red");
            // The description alone sits on Window 1's top edge: the name line and a description line.
            Assert.True(screen.HasLightPixel(shot, px + 48, px + 300, (int)window1Top - 56 + 4 + 8), "no item name above the log");
            // The roll column left of the party window: light digits in the first member's box.
            var firstRow = party.Buttons[0];
            Assert.True(screen.HasLightPixel(shot, (int)partyAt.X - 27, (int)partyAt.X - 1, (int)partyAt.Y + firstRow.Y + 6, 170), "no lot beside the party row");
            // The Cast Lot / Pass window right of the list.
            var lot = Place(menus.Top!);
            Assert.True(lot.X >= px + 182);
            // Pass is selected (Cast Lot is greyed after a lot): its glyphs are drawn in the selected orange.
            bool orange = false;
            for (int x = (int)lot.X + 21; x < (int)lot.X + 100 && !orange; x++)
            {
                var p = screen.Pixel(shot, x, (int)lot.Y + 21 + 7);
                orange = p.R > 200 && p.G > 120 && p.B < 120;
            }
            Assert.True(orange, "no selected Pass label");
        }
    }
}
