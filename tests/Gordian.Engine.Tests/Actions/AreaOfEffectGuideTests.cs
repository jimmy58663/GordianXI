// tests/Gordian.Engine.Tests/Actions/AreaOfEffectGuideTests.cs
using System;
using System.Buffers.Binary;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Gordian.Core.Config;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Tables;
using Gordian.Core.World;
using Gordian.Engine.Actions;
using Xunit;

namespace Gordian.Engine.Tests.Actions
{
    /// <summary>The area-of-effect range guide's state (#356): which cursor gets one, where and how big.</summary>
    public class AreaOfEffectGuideTests
    {
        private const ushort Cure = 1, Curaga = 7, Diaga = 33, HordeLullaby = 376, Breath = 536;
        private const ushort CircleBlade = 38, FightersRoll = 98;

        private static ActionCatalog Catalog()
        {
            var spells = new byte[SpellCommandTables.SpellRecordSize * 1024];
            Spell(spells, Cure, radius: 0, shape: 0);
            Spell(spells, Curaga, radius: 8, shape: 1);
            Spell(spells, HordeLullaby, radius: 3, shape: 1);
            Spell(spells, Diaga, radius: 8, shape: 1);
            Spell(spells, Breath, radius: 4, shape: 2);
            var commands = new byte[SpellCommandTables.CommandRecordSize * 2816];
            Command(commands, CircleBlade, type: 3, radius: 3, shape: 1);
            Command(commands, 512 + FightersRoll, type: 8, radius: 7, shape: 1);
            var file = TestCatalog.Section("mgc_", DatSectionType.SpellList, spells)
                .Concat(TestCatalog.Section("comm", DatSectionType.AbilityList, commands)).ToArray();
            return new ActionCatalog(SpellCommandTables.Parse(file)!, _ => null, _ => null);
        }

        private static void Spell(byte[] table, int id, int radius, int shape)
        {
            var r = table.AsSpan(id * SpellCommandTables.SpellRecordSize, SpellCommandTables.SpellRecordSize);
            BinaryPrimitives.WriteUInt16LittleEndian(r, (ushort)id);
            BinaryPrimitives.WriteUInt16LittleEndian(r.Slice(0x02), 1);
            r[0x45] = 12;
            r[0x46] = (byte)radius;
            r[0x47] = (byte)shape;
            SpellCommandTables.Scramble(r);
        }

        private static void Command(byte[] table, int id, int type, int radius, int shape)
        {
            var r = table.AsSpan(id * SpellCommandTables.CommandRecordSize, SpellCommandTables.CommandRecordSize);
            BinaryPrimitives.WriteUInt16LittleEndian(r, (ushort)id);
            r[0x02] = (byte)type;
            r[0x10] = 2;
            r[0x11] = (byte)radius;
            r[0x12] = (byte)shape;
            SpellCommandTables.Scramble(r);
        }

        private static SubTargetSelection Cursor(ActionKind kind, ushort id, WorldEntity on) =>
            new("test", ActionTargetFlags.Self, on, _ => Task.FromResult(PlayerActionResult.Ok("")) ) { Kind = kind, Id = id };

        private static readonly WorldEntity Self = new(0x01020304, 10, EntityType.Player) { Name = "Self" };

        [Fact]
        public void Tables_DecodeTheRadiusAndShape()
        {
            var catalog = Catalog();
            Assert.Equal((12, 8, AreaOfEffectShape.AroundTarget), (catalog.GetSpell(Curaga)!.Range, catalog.GetSpell(Curaga)!.AoeRadius, catalog.GetSpell(Curaga)!.AoeShape));
            Assert.Equal(AreaOfEffectShape.Cone, catalog.GetSpell(Breath)!.AoeShape);
            Assert.Equal((7, AreaOfEffectShape.AroundTarget), (catalog.GetJobAbility(FightersRoll)!.AoeRadius, catalog.GetJobAbility(FightersRoll)!.AoeShape));
        }

        [Fact]
        public void Guide_FollowsTheEntityUnderTheCursor_AtTheCalibratedRadius()
        {
            var catalog = Catalog();
            var npc = new WorldEntity(0x01000010, 16, EntityType.Npc) { Name = "Field Manual" };
            var cursor = Cursor(ActionKind.Spell, Curaga, Self);

            var guide = AreaOfEffectGuide.For(cursor, catalog)!.Value;
            Assert.Same(Self, guide.Center);
            Assert.Equal(8, guide.RadiusIndex);
            Assert.Equal(17.6f, guide.Radius, 0.05f); // retail: band at ~17.6 yalms for index 8

            cursor.Current = npc;
            Assert.Same(npc, AreaOfEffectGuide.For(cursor, catalog)!.Value.Center);

            Assert.Equal(9.0f, AreaOfEffectGuide.For(Cursor(ActionKind.Spell, HordeLullaby, Self), catalog)!.Value.Radius, 0.05f); // retail: ~9
        }

        [Fact]
        public void Guide_CoversCommandsByTheirOwnRecords()
        {
            var catalog = Catalog();
            Assert.Equal(3, AreaOfEffectGuide.For(Cursor(ActionKind.WeaponSkill, CircleBlade, Self), catalog)!.Value.RadiusIndex);
            Assert.Equal(7, AreaOfEffectGuide.For(Cursor(ActionKind.JobAbility, FightersRoll, Self), catalog)!.Value.RadiusIndex);
        }

        [Fact]
        public void NoGuide_ForSingleTargetConesItemsOrNoCursor()
        {
            var catalog = Catalog();
            Assert.Null(AreaOfEffectGuide.For(Cursor(ActionKind.Spell, Cure, Self), catalog)); // retail: Cure <st> shows none
            Assert.Null(AreaOfEffectGuide.For(Cursor(ActionKind.Spell, Breath, Self), catalog));
            Assert.Null(AreaOfEffectGuide.For(Cursor(ActionKind.Item, Curaga, Self), catalog));
            Assert.Null(AreaOfEffectGuide.For(null, catalog));
            Assert.Null(AreaOfEffectGuide.For(Cursor(ActionKind.Spell, Curaga, Self), null));
        }

        private const uint SelfId = 0x01020304;

        /// <summary>A session with yourself at the origin, a party of three around you and a stranger and a monster in range.</summary>
        private static (PlayerActionService Service, WorldState World) Session()
        {
            var world = new WorldState();
            var local = new LocalPlayerState { ServerId = SelfId };
            var party = new PartyState();
            var profile = new SessionProfile();
            Task Capture(ReadOnlyMemory<byte> mem, bool urgent) => Task.CompletedTask;
            var service = new PlayerActionService(profile, world, local,
                new CombatPacketModule(new CombatState(), local, Capture), new ChatPacketModule(Capture),
                new PartyPacketModule(party, Capture), new EntityPacketModule(world, local, Capture),
                new LifecyclePacketModule(profile, Capture), Capture)
            { Catalog = Catalog() };
            void Add(uint id, ushort index, EntityType type, string name, Vector3 at, bool member)
            {
                world.UpsertEntity(new WorldEntity(id, index, type) { Name = name, Position = at, Hpp = 100 });
                if (member) party.UpsertMember(new PartyMember { ServerId = id, Name = name, MemberNumber = (byte)(index - 10) });
            }
            Add(SelfId, 10, EntityType.Player, "Gemini", Vector3.Zero, true);
            Add(0x0A, 11, EntityType.Player, "Near", new Vector3(10, 0, 0), true);
            Add(0x0B, 12, EntityType.Player, "Upstairs", new Vector3(0, 6, 12), true);   // higher up, horizontally inside
            Add(0x0C, 13, EntityType.Player, "Far", new Vector3(20, 0, 0), true);        // outside 17.6
            Add(0x0D, 14, EntityType.Player, "Stranger", new Vector3(5, 0, 0), false);   // not in the party
            Add(0x0E, 15, EntityType.Monster, "Hare", new Vector3(3, 0, 3), false);
            return (service, world);
        }

        [Fact]
        public void AffectedCharacters_AreYourPartyInsideTheCircle()
        {
            var (service, _) = Session();
            service.BeginSubTarget(ActionKind.Spell, Curaga, "Curaga", ActionTargetFlags.Self | ActionTargetFlags.Party);
            Assert.Equal(SelfId, service.SubTargeting!.Current.ServerId);
            var affected = service.AreaOfEffectTargets().Select(e => e.Name).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "Gemini", "Near", "Upstairs" }, affected);
        }

        [Fact]
        public void AffectedCharacters_NoneForEnemyActionsOrSingleTargets()
        {
            var (service, _) = Session();
            service.BeginSubTarget(ActionKind.Spell, Diaga, "Diaga", ActionTargetFlags.Enemy);
            Assert.NotNull(service.SubTargeting);
            Assert.Empty(service.AreaOfEffectTargets());

            service.CancelSubTarget();
            service.BeginSubTarget(ActionKind.Spell, Cure, "Cure", ActionTargetFlags.Self | ActionTargetFlags.Party);
            Assert.Null(service.AreaOfEffect);
            Assert.Empty(service.AreaOfEffectTargets());
        }
    }
}
