// tests/Gordian.Core.Tests/Actions/ActionCatalogTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Gordian.Engine.Actions;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Actions
{
    /// <summary>A small spell and command table with names, scrambled as the retail file is (#240, #353).</summary>
    internal static class TestCatalog
    {
        public const ushort Cure = 1, Fire = 144, Shantotto = 896;
        public const ushort Berserk = 31, Provoke = 35, CuringWaltz = 190;
        public const ushort FastBlade = 32;
        public const ushort Fight = 69, MagicDefBonus = 6;

        public static readonly Dictionary<int, string> SpellNames = new()
        {
            [Cure] = "Cure", [Fire] = "Fire", [Shantotto] = "Shantotto",
        };

        public static readonly Dictionary<int, string> CommandNames = new()
        {
            [FastBlade] = "Fast Blade", [512 + Berserk] = "Berserk", [512 + Provoke] = "Provoke", [512 + CuringWaltz] = "Curing Waltz",
            [512 + 1] = "Dual Wield", // a job trait (type 4): never an ability
            [512 + Fight] = "Fight", [512 + 91] = "Blood Pact: Rage", [1025] = "Poison Nails", [1024] = "Healing Ruby", [1536 + MagicDefBonus] = "Magic Def. Bonus",
        };

        public static ActionCatalog Build()
        {
            var spells = new byte[SpellCommandTables.SpellRecordSize * 1024];
            // Cure: white, MP 8, anyone (0x3F), WHM 1 / RDM 3, menu slot 1. Fire: black, MP 7, enemies, BLM 13 / RDM 19.
            Spell(spells, Cure, kind: 1, flags: 0x3F, mp: 8, menu: 1, (3, 1), (5, 3));
            SetSpellIcon(spells, Cure, 6);
            Spell(spells, Fire, kind: 2, flags: 0x20, mp: 7, menu: 197, (4, 13), (5, 19));
            Spell(spells, Shantotto, kind: 8, flags: 0x01, mp: 0, menu: 918, Enumerable.Range(1, 22).Select(j => (j, 1)).ToArray());
            var commands = new byte[SpellCommandTables.CommandRecordSize * 2816];
            Command(commands, FastBlade, type: 3, flags: 0x20, recastId: 900, tp: -1);
            Command(commands, 512 + Berserk, type: 1, flags: 0x01, recastId: 1, tp: -1, icon: 40, icon2: 361);
            Command(commands, 512 + Provoke, type: 1, flags: 0x20, recastId: 5, tp: -1);
            Command(commands, 512 + CuringWaltz, type: 12, flags: 0x3F, recastId: 217, tp: 200);
            Command(commands, 512 + 1, type: 4, flags: 0x01, recastId: 0, tp: -1);
            Command(commands, 512 + Fight, type: 2, flags: 0x20, recastId: 100, tp: -1);
            Command(commands, 1024, type: 10, flags: 0x05, recastId: 174, tp: -1);
            Command(commands, 512 + 91, type: 2, flags: 0x01, recastId: 173, tp: -1);
            Command(commands, 1025, type: 6, flags: 0x20, recastId: 173, tp: -1);
            Command(commands, 1536 + MagicDefBonus, type: 4, flags: 0x01, recastId: 0, tp: -1, icon: 46, icon2: 46);
            var file = Section("mgc_", DatSectionType.SpellList, spells).Concat(Section("comm", DatSectionType.AbilityList, commands)).ToArray();
            var tables = SpellCommandTables.Parse(file)!;
            return new ActionCatalog(tables, id => SpellNames.GetValueOrDefault(id), id => CommandNames.GetValueOrDefault(id));
        }

        private static void Spell(byte[] table, int id, int kind, int flags, int mp, int menu, params (int Job, int Level)[] levels)
        {
            var r = table.AsSpan(id * SpellCommandTables.SpellRecordSize, SpellCommandTables.SpellRecordSize);
            BinaryPrimitives.WriteUInt16LittleEndian(r, (ushort)id);
            BinaryPrimitives.WriteUInt16LittleEndian(r.Slice(0x02), (ushort)kind);
            BinaryPrimitives.WriteUInt16LittleEndian(r.Slice(0x06), (ushort)flags);
            BinaryPrimitives.WriteUInt16LittleEndian(r.Slice(0x0A), (ushort)mp);
            for (int j = 0; j < 24; j++) BinaryPrimitives.WriteUInt16LittleEndian(r.Slice(0x0E + j * 2), 0xFFFF);
            foreach (var (job, level) in levels) BinaryPrimitives.WriteUInt16LittleEndian(r.Slice(0x0E + job * 2), (ushort)level);
            BinaryPrimitives.WriteUInt16LittleEndian(r.Slice(0x3E), (ushort)menu);
            SpellCommandTables.Scramble(r);
        }

        private static void SetSpellIcon(byte[] table, int id, int icon)
        {
            var r = table.AsSpan(id * SpellCommandTables.SpellRecordSize, SpellCommandTables.SpellRecordSize);
            SpellCommandTables.Unscramble(r);
            BinaryPrimitives.WriteUInt16LittleEndian(r.Slice(0x40), (ushort)icon);
            BinaryPrimitives.WriteUInt16LittleEndian(r.Slice(0x42), 0xFFFF);
            SpellCommandTables.Scramble(r);
        }

        private static void Command(byte[] table, int id, int type, int flags, int recastId, int tp, int icon = 46, int icon2 = -1)
        {
            var r = table.AsSpan(id * SpellCommandTables.CommandRecordSize, SpellCommandTables.CommandRecordSize);
            BinaryPrimitives.WriteUInt16LittleEndian(r, (ushort)id);
            r[0x02] = (byte)type;
            r[0x03] = (byte)icon;
            BinaryPrimitives.WriteInt16LittleEndian(r.Slice(0x04), (short)icon2);
            BinaryPrimitives.WriteUInt16LittleEndian(r.Slice(0x08), (ushort)recastId);
            BinaryPrimitives.WriteUInt16LittleEndian(r.Slice(0x0A), (ushort)flags);
            BinaryPrimitives.WriteInt16LittleEndian(r.Slice(0x0C), (short)tp);
            SpellCommandTables.Scramble(r);
        }

        private static byte[] Section(string tag, DatSectionType type, byte[] payload)
        {
            int units = (16 + payload.Length + 15) / 16;
            var section = new byte[units * 16];
            Encoding.ASCII.GetBytes(tag).CopyTo(section, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(4), (uint)type | (uint)(units << 7));
            payload.CopyTo(section, 16);
            return section;
        }
    }

    public class ActionCatalogTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [Fact]
        public void Names_AreFoundCaseInsensitivelyAndByKind()
        {
            var catalog = TestCatalog.Build();
            Assert.True(catalog.TryFindSpell("cure", out var cure));
            Assert.Equal(TestCatalog.Cure, cure.Id);
            Assert.True(catalog.TryFindSpell("  SHANTOTTO ", out var trust));
            Assert.Equal(TestCatalog.Shantotto, trust.Id);
            Assert.False(catalog.TryFindSpell("Cure IV", out _));

            Assert.True(catalog.TryFindJobAbility("berserk", out ushort berserk, out var record));
            Assert.Equal(TestCatalog.Berserk, berserk);
            Assert.Equal(1, record.RecastId);
            Assert.True(catalog.TryFindWeaponSkill("fast   blade", out ushort ws, out _));
            Assert.Equal(TestCatalog.FastBlade, ws);

            // A weapon skill is not a job ability, a job trait is neither, and a spell name is not a command.
            Assert.False(catalog.TryFindJobAbility("Fast Blade", out _, out _));
            Assert.False(catalog.TryFindJobAbility("Dual Wield", out _, out _));
            Assert.False(catalog.TryFindWeaponSkill("Berserk", out _, out _));
            Assert.Null(catalog.Find(ActionKind.JobAbility, "Cure"));
            Assert.Equal(TestCatalog.Provoke, catalog.Find(ActionKind.JobAbility, "\"Provoke\""));
            Assert.Equal("Curing Waltz", catalog.JobAbilityName(TestCatalog.CuringWaltz));
            Assert.Equal(200, catalog.GetJobAbility(TestCatalog.CuringWaltz)!.TpCost);
            Assert.Equal("Cure IV", ActionCatalog.Normalize("  Cure   IV "));
        }

        /// <summary>The retail tables and names (file id 81, ROM/181/72-73). Skipped without the game install.</summary>
        [Fact]
        public void RetailCatalog_FindsSpellsAbilitiesAndWeaponSkillsByName()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var catalog = ActionCatalog.Load(rm)!;

            Assert.True(catalog.TryFindSpell("cure", out var cure));
            Assert.Equal(1, cure.Id);
            Assert.True(catalog.TryFindSpell("Shantotto", out var shantotto));
            Assert.Equal((896, 8, 0x01), (shantotto.Id, shantotto.Kind, shantotto.TargetFlags));
            Assert.True(catalog.TryFindSpell("Fire", out var fire));
            Assert.Equal((144, 0x20), (fire.Id, fire.TargetFlags));

            // Job abilities: LandSandBoat's ability ids and recast ids (sql/abilities.sql).
            Assert.True(catalog.TryFindJobAbility("Berserk", out ushort berserk, out var berserkRecord));
            Assert.Equal((31, 1, 0x01), (berserk, berserkRecord.RecastId, berserkRecord.TargetFlags));
            Assert.True(catalog.TryFindJobAbility("provoke", out ushort provoke, out var provokeRecord));
            Assert.Equal((35, 5, 0x20), (provoke, provokeRecord.RecastId, provokeRecord.TargetFlags));
            Assert.True(catalog.TryFindJobAbility("Mighty Strikes", out ushort mighty, out var mightyRecord));
            Assert.Equal((16, 0), (mighty, mightyRecord.RecastId));
            foreach (var (name, recastId) in new[] { ("Warcry", 2), ("Defender", 3), ("Chakra", 15), ("Boost", 16), ("Steal", 60), ("Flee", 62), ("Hide", 63) })
            {
                Assert.True(catalog.TryFindJobAbility(name, out _, out var r), name);
                Assert.Equal(recastId, r.RecastId);
            }
            Assert.True(catalog.TryFindWeaponSkill("Fast Blade", out ushort fastBlade, out var fastBladeRecord));
            Assert.Equal((32, 0x20), (fastBlade, fastBladeRecord.TargetFlags));
            Assert.False(catalog.TryFindJobAbility("Fast Blade", out _, out _));
        }
    }
}
