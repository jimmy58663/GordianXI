// tests/Gordian.Core.Tests/Network/StandardMessagesShopTests.cs
using System;
using Gordian.Core.Network.Packets;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class StandardMessagesShopTests
    {
        [Fact]
        public void SellMessage_NamesTheItemAndCount()
        {
            // LandSandBoat's 0x009 after a vendor sale (0x085): MsgStd::Sell with "Para0 <item> Para1 <count>".
            var one = new SystemMessage(0, 0, StandardMessages.ShopSellMessage, 0, "Para0 65535 Para1 1", DateTime.UtcNow);
            Assert.Equal("You sell Gil.", StandardMessages.FormatMessage(one));
            var many = new SystemMessage(0, 0, StandardMessages.ShopSellToShopMessage, 0, "Para0 65535 Para1 12", DateTime.UtcNow);
            Assert.Equal("You sell 12 Gil to the shop.", StandardMessages.FormatMessage(many));
        }

        [Fact]
        public void TryGetNumbers_ReadsTwoParameters()
        {
            Assert.True(StandardMessages.TryGetNumbers("Para0 639 Para1 12", out uint a, out uint b));
            Assert.Equal((639u, 12u), (a, b));
            Assert.False(StandardMessages.TryGetNumbers("Para0 639", out _, out _));
            Assert.False(StandardMessages.TryGetNumbers(string.Empty, out _, out _));
        }
    }
}
