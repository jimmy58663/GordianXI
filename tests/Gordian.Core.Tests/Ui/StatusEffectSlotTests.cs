// tests/Gordian.Core.Tests/Ui/StatusEffectSlotTests.cs
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    public class StatusEffectSlotTests
    {
        [Fact]
        public void NewPlayer_HasNoStatusEffects()
        {
            Assert.Empty(new LocalPlayerState().GetStatusEffectIds());
        }

        [Fact]
        public void StatusIds_CombineLowBytesWithTwoHighBitsPerSlot()
        {
            var player = new LocalPlayerState();
            player.BuffIcons[0] = 40;                // Protect
            player.BuffIcons[1] = 0x20;              // + high bits 1 -> 0x120 (288)
            player.BuffStatusBits[0] = 0b0000_0100;  // slot 1: bits 2-3 = 1
            player.BuffIcons[5] = 0xFF;              // empty slot in the middle
            player.BuffIcons[6] = 0xFF;              // 0xFF with high bits 1 -> 511, not empty
            player.BuffStatusBits[1] = 0b0001_0000;  // slot 6: bits 4-5 of byte 1 = 1

            Assert.Equal(new ushort[] { 40, 0x120, 0x1FF }, player.GetStatusEffectIds());
        }
    }
}
