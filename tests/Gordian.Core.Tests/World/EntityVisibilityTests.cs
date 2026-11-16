// tests/Gordian.Core.Tests/World/EntityVisibilityTests.cs
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.World
{
    /// <summary>
    /// Who draws an invisible character (#577 round 1). Retail, checked with capture-retail on local LandSandBoat
    /// (2026-11-15, West Ronfaure): another client draws a character under Invisible not at all (no body, shadow or name
    /// plate); the character's own client draws it at half alpha with its shadow and no plate.
    /// </summary>
    public class EntityVisibilityTests
    {
        [Fact]
        public void InvisibleCharacter_IsDrawnOnlyToItself()
        {
            var player = new PlayerEntity(0x01000002, 0x401) { Name = "Claude", IsSpawned = true };
            Assert.True(player.IsDrawn);
            Assert.True(player.IsDrawnToOthers);

            player.IsInvisible = true;
            Assert.True(player.IsDrawn);
            Assert.False(player.IsDrawnToOthers);
        }

        [Fact]
        public void InvisibleEventParticipant_IsStillDrawn()
        {
            var npc = new WorldEntity(0x010E6040, 0x40, EntityType.Npc) { Name = "Balasiel", IsSpawned = true, IsInvisible = true };
            Assert.False(npc.IsDrawnToOthers);
            npc.IsInEvent = true;
            Assert.True(npc.IsDrawnToOthers);
        }

        [Fact]
        public void HiddenEntity_IsDrawnToNobody()
        {
            var npc = new WorldEntity(0x010E6041, 0x41, EntityType.Npc) { Name = "Stand-in", IsSpawned = true, IsHidden = true };
            Assert.False(npc.IsDrawn);
            Assert.False(npc.IsDrawnToOthers);
        }
    }
}
