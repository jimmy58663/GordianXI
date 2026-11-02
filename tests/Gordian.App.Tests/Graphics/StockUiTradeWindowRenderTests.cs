// tests/Gordian.App.Tests/Graphics/StockUiTradeWindowRenderTests.cs
using System;
using Gordian.App.Graphics;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>Offscreen D3D11 render of the player trade window (#486) on the retail DAT frames (GORDIAN_UI_DUMP writes gpu_trade_window.png).</summary>
    public class StockUiTradeWindowRenderTests
    {
        [Fact]
        public void RendersBothOffers_TheGil_ThePartnersNameAndTheirLitOk()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;

            var inventory = new InventoryState();
            inventory.SetContainerSizes(ContainerId.Inventory, 30, 30);
            inventory.SetItem(ContainerId.Inventory, 0, InventoryState.GilItemId, 50000, ItemLockFlag.Normal);
            inventory.SetItem(ContainerId.Inventory, 1, 4096, 9, ItemLockFlag.Normal);   // Fire Crystal x9
            inventory.SetItem(ContainerId.Inventory, 2, 642, 9, ItemLockFlag.Normal);    // Zinc Ore x9
            var menus = new StockUiMenuController
            {
                Library = screen.Library,
                Inventory = inventory,
                ItemLookup = id => screen.Resources.TryGetItem(id, out var record) ? record : null,
                TradeTargetOf = id => new StockUiTradeTarget(id ?? 0, 0x405, "Cybin", StockUiTradeTargetKind.Player),
                TradeOwnName = () => "Gemini",
            };
            inventory.TradeResultReceived += menus.OnTradeResult;
            inventory.TradeChanged += menus.OnTradeChanged;
            inventory.SetTradeStatus(TradeResultKind.Start, 0x01020304, 0x405);
            inventory.SetMyTradeItem(1, 642, 3, 2);
            inventory.SetMyTradeItem(0, InventoryState.GilItemId, 1500, 0);
            inventory.SetPartnerTradeItem(1, 4096, 4, ReadOnlySpan<byte>.Empty, 1);
            inventory.SetTradeStatus(TradeResultKind.Make, 0x01020304, 0x405);
            var window = menus.TradeWindow;
            Assert.NotNull(window);
            window!.SelectedButtonId = StockUiTrade.PartnerSlotBase; // the partner's first slot: its crystal is described

            var frame = window.Menu.Frame;
            var placement = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
            int gx = (int)placement.X, gy = (int)placement.Y;
            var layout = new StockUiScreen(screen.Width, screen.Height, null);
            byte[] pixels = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, window, placement, 0, logFont: screen.LogFont, screen: layout), "gpu_trade_window.png");

            // Our slot 1 (7,22) holds the ore; slot 6 (41,56) is the empty slot art.
            var icon = screen.Pixel(pixels, gx + 7 + 16, gy + 22 + 16);
            var empty = screen.Pixel(pixels, gx + 41 + 16, gy + 56 + 16);
            Assert.True(Math.Abs(icon.R - empty.R) + Math.Abs(icon.G - empty.G) + Math.Abs(icon.B - empty.B) > 30, $"no icon in slot 1: {icon} vs {empty}");
            // "1,500" in the gil field (76..156, y 7..23).
            Assert.True(screen.HasLightPixel(pixels, gx + 110, gx + 156, gy + 15), "no gil in the field");
            // The partner's window under ours: their crystal in slot 1 and their name in the top band.
            int py = gy + (StockUiTrade.PartnerY - frame.Y);
            var partnerIcon = screen.Pixel(pixels, gx + 7 + 16, py + 22 + 16);
            var partnerEmpty = screen.Pixel(pixels, gx + 41 + 16, py + 56 + 16);
            Assert.True(Math.Abs(partnerIcon.R - partnerEmpty.R) + Math.Abs(partnerIcon.G - partnerEmpty.G) + Math.Abs(partnerIcon.B - partnerEmpty.B) > 30, "no icon in the partner's slot 1");
            // Our name in our band (white: we have not pressed OK), theirs in orange (they have, round 1 image-24).
            Assert.True(screen.HasLightPixel(pixels, gx + 8, gx + 60, gy + 11), "no own name");
            bool orange = false;
            for (int x = gx + 8; x < gx + 60 && !orange; x++)
            {
                var p = screen.Pixel(pixels, x, py + 11);
                orange = p.R > 150 && p.R > p.B + 60;
            }
            Assert.True(orange, "no orange partner name");
            Assert.True(window.PartnerAccepted);
        }
    }
}
