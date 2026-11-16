using System.Numerics;
using System.Threading.Tasks;
using Gordian.Core.World;
using Gordian.Engine.Actions;
using Xunit;

namespace Gordian.Engine.Tests.Actions
{
    /// <summary>The sub-target cursor's range colour (#589).</summary>
    public class SubTargetRangeTests
    {
        private static readonly WorldEntity Me = new(1, 1, EntityType.Player) { Position = Vector3.Zero };

        private static SubTargetSelection Cursor(WorldEntity on, ActionKind kind, ushort id) =>
            new("test", ActionTargetFlags.Enemy | ActionTargetFlags.Self, on, _ => Task.FromResult(PlayerActionResult.Ok("done"))) { Kind = kind, Id = id };

        private static WorldEntity MobAt(float x) =>
            new(2, 2, EntityType.Monster) { Position = new Vector3(x, 0, 0) };

        [Fact]
        public void RangeIndexes_MapToLandSandBoatsYalms()
        {
            Assert.Equal(3f, SubTargetRange.RangeYalms(2));
            Assert.Equal(4f, SubTargetRange.RangeYalms(3));
            Assert.Equal(5f, SubTargetRange.RangeYalms(4));
            Assert.Equal(8f, SubTargetRange.RangeYalms(7));
            Assert.Equal(10f, SubTargetRange.RangeYalms(8));
            Assert.Equal(12f, SubTargetRange.RangeYalms(9));
            Assert.Equal(14f, SubTargetRange.RangeYalms(10));
            Assert.Equal(16f, SubTargetRange.RangeYalms(11));
            Assert.Equal(20f, SubTargetRange.RangeYalms(12));
            Assert.Null(SubTargetRange.RangeYalms(SubTargetRange.SelfOnlyIndex));
            Assert.Null(SubTargetRange.RangeYalms(0));
            Assert.Null(SubTargetRange.RangeYalms(16));
        }

        [Fact]
        public void Classify_BlueInRange_YellowWithinAYalm_RedBeyond()
        {
            // Retail on local LSB (2026-11-15): Cure, 20 yalms: violet to 20.2, yellow 20.25-21.2, red from 21.25.
            Assert.Equal(SubTargetRangeBand.InRange, SubTargetRange.Classify(0, 20));
            Assert.Equal(SubTargetRangeBand.InRange, SubTargetRange.Classify(20.2f, 20));
            Assert.Equal(SubTargetRangeBand.JustOutOfRange, SubTargetRange.Classify(20.25f, 20));
            Assert.Equal(SubTargetRangeBand.JustOutOfRange, SubTargetRange.Classify(21.2f, 20));
            Assert.Equal(SubTargetRangeBand.OutOfRange, SubTargetRange.Classify(21.25f, 20));
            // Divine Waltz, 10 yalms: the same offsets.
            Assert.Equal(SubTargetRangeBand.InRange, SubTargetRange.Classify(10.2f, 10));
            Assert.Equal(SubTargetRangeBand.JustOutOfRange, SubTargetRange.Classify(10.25f, 10));
            Assert.Equal(SubTargetRangeBand.JustOutOfRange, SubTargetRange.Classify(11.2f, 10));
            Assert.Equal(SubTargetRangeBand.OutOfRange, SubTargetRange.Classify(11.25f, 10));
        }

        [Fact]
        public void For_ReadsTheActionsRangeFromTheTables()
        {
            var catalog = TestCatalog.Build();
            // Cure (index 12, 20 yalms).
            Assert.Equal(SubTargetRangeBand.InRange, SubTargetRange.For(Cursor(MobAt(19), ActionKind.Spell, TestCatalog.Cure), catalog, Me));
            Assert.Equal(SubTargetRangeBand.JustOutOfRange, SubTargetRange.For(Cursor(MobAt(20.7f), ActionKind.Spell, TestCatalog.Cure), catalog, Me));
            Assert.Equal(SubTargetRangeBand.OutOfRange, SubTargetRange.For(Cursor(MobAt(30), ActionKind.Spell, TestCatalog.Cure), catalog, Me));
            // Provoke (index 9, 12 yalms), Fast Blade (index 2, melee reach 3 yalms).
            Assert.Equal(SubTargetRangeBand.OutOfRange, SubTargetRange.For(Cursor(MobAt(15), ActionKind.JobAbility, TestCatalog.Provoke), catalog, Me));
            Assert.Equal(SubTargetRangeBand.JustOutOfRange, SubTargetRange.For(Cursor(MobAt(3.7f), ActionKind.WeaponSkill, TestCatalog.FastBlade), catalog, Me));
            // Items: 20 yalms (PROVISIONAL).
            Assert.Equal(SubTargetRangeBand.OutOfRange, SubTargetRange.For(Cursor(MobAt(25), ActionKind.Item, 4112), catalog, Me));
            // Yourself, an action with no range in the tables (Fire here), and no catalog: in range.
            Assert.Equal(SubTargetRangeBand.InRange, SubTargetRange.For(Cursor(Me, ActionKind.Spell, TestCatalog.Cure), catalog, Me));
            Assert.Equal(SubTargetRangeBand.InRange, SubTargetRange.For(Cursor(MobAt(50), ActionKind.Spell, TestCatalog.Fire), catalog, Me));
            Assert.Equal(SubTargetRangeBand.InRange, SubTargetRange.For(Cursor(MobAt(50), ActionKind.Spell, TestCatalog.Cure), null, Me));
        }

        [Fact]
        public void Distance_IsCentreToCentre()
        {
            Assert.Equal(20f, SubTargetRange.DistanceTo(Vector3.Zero, MobAt(20)), 3);
            Assert.Equal(5f, SubTargetRange.DistanceTo(Vector3.Zero, new WorldEntity(3, 3, EntityType.Player) { Position = new Vector3(3, 0, 4) }), 3);
        }
    }
}
