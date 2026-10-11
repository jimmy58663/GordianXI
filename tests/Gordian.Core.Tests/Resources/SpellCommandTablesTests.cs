// tests/Gordian.Core.Tests/Resources/SpellCommandTablesTests.cs
using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>The spell (0x49 <c>mgc_</c>) and command (0x53 <c>comm</c>) tables of ROM/118/114.</summary>
    public class SpellCommandTablesTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private static byte[] Section(string tag, DatSectionType type, byte[] payload)
        {
            int units = (16 + payload.Length + 15) / 16;
            var section = new byte[units * 16];
            Encoding.ASCII.GetBytes(tag).CopyTo(section, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(4), (uint)type | (uint)(units << 7));
            payload.CopyTo(section, 16);
            return section;
        }

        [Fact]
        public void Scramble_IsUndoneByUnscramble()
        {
            var rng = new Random(7);
            for (int n = 0; n < 50; n++)
            {
                var record = new byte[SpellCommandTables.SpellRecordSize];
                rng.NextBytes(record);
                var copy = (byte[])record.Clone();
                SpellCommandTables.Scramble(copy);
                SpellCommandTables.Unscramble(copy);
                Assert.Equal(record, copy);
            }
        }

        [Fact]
        public void Parse_DecodesScrambledSpellAndCommandRecords()
        {
            var spell = new byte[SpellCommandTables.SpellRecordSize];
            BinaryPrimitives.WriteUInt16LittleEndian(spell.AsSpan(0x00), 1);   // id
            BinaryPrimitives.WriteUInt16LittleEndian(spell.AsSpan(0x02), 1);   // white
            BinaryPrimitives.WriteUInt16LittleEndian(spell.AsSpan(0x04), 6);   // light
            BinaryPrimitives.WriteUInt16LittleEndian(spell.AsSpan(0x08), 33);  // healing magic
            BinaryPrimitives.WriteUInt16LittleEndian(spell.AsSpan(0x0A), 8);   // MP
            spell[0x0C] = 8;
            spell[0x0D] = 20;
            for (int j = 0; j < 24; j++) BinaryPrimitives.WriteUInt16LittleEndian(spell.AsSpan(0x0E + j * 2), 0xFFFF);
            BinaryPrimitives.WriteUInt16LittleEndian(spell.AsSpan(0x0E + 3 * 2), 1); // WHM 1
            SpellCommandTables.Scramble(spell);

            var command = new byte[SpellCommandTables.CommandRecordSize];
            BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(0x00), 1);
            command[0x02] = 3; // weapon skill
            command[0x10] = 2;
            SpellCommandTables.Scramble(command);

            var spells = new byte[SpellCommandTables.SpellRecordSize * 2]; // record 0 empty
            spell.CopyTo(spells, SpellCommandTables.SpellRecordSize);
            var commands = new byte[SpellCommandTables.CommandRecordSize * 2];
            command.CopyTo(commands, SpellCommandTables.CommandRecordSize);
            var file = Section("mgc_", DatSectionType.SpellList, spells).Concat(Section("comm", DatSectionType.AbilityList, commands)).ToArray();

            var tables = SpellCommandTables.Parse(file);

            Assert.NotNull(tables);
            Assert.Null(tables!.GetSpell(0));
            var cure = tables.GetSpell(1)!;
            Assert.Equal((1, 6, 33, 8, 8, 20), (cure.Kind, cure.Element, cure.Skill, cure.MpCost, cure.CastQuarters, cure.RecastQuarters));
            Assert.Equal(1, cure.LevelFor(3));
            Assert.Null(cure.LevelFor(4));
            Assert.Equal((1, 3, 2), (tables.GetCommand(1)!.Id, tables.GetCommand(1)!.Type, tables.GetCommand(1)!.Range));
            Assert.Null(SpellCommandTables.Parse(Section("tim0", DatSectionType.Texture, new byte[16])));
        }

        /// <summary>The retail tables (file id 81): Cure, Fire and Sleep as retail lists them. Skipped without the game install.</summary>
        [Fact]
        public void RetailTables_HoldTheKnownSpells()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var bytes = rm.LoadDatBytesByFileId(SpellCommandTables.FileId)!;
            Assert.Equal(rm.LoadDatBytes(Path.Combine("ROM", "118", "114.DAT")), bytes);
            var tables = SpellCommandTables.Parse(bytes)!;
            Assert.Equal(1024, tables.Spells.Count);
            Assert.Equal(2816, tables.Commands.Count);

            var cure = tables.GetSpell(1)!;
            Assert.Equal((1, 1, 6, 33, 8, 8, 20), (cure.Id, cure.Kind, cure.Element, cure.Skill, cure.MpCost, cure.CastQuarters, cure.RecastQuarters));
            Assert.Equal((1, 3, 5), (cure.LevelFor(3), cure.LevelFor(5), cure.LevelFor(7)));
            var fire = tables.GetSpell(144)!;
            Assert.Equal((2, 0, 36, 7, 13, 19), (fire.Kind, fire.Element, fire.Skill, fire.MpCost, fire.LevelFor(4), fire.LevelFor(5)));
            var sleep = tables.GetSpell(253)!;
            Assert.Equal((35, 19, 20, 25), (sleep.Skill, sleep.MpCost, sleep.LevelFor(4), sleep.LevelFor(5)));
            // Every record's id equals its index.
            Assert.All(tables.Spells.Select((s, i) => (s, i)).Where(p => p.s != null), p => Assert.Equal(p.i, p.s!.Id));
            Assert.All(tables.Commands.Select((c, i) => (c, i)).Where(p => p.c != null), p => Assert.Equal(p.i, p.c!.Id));
            Assert.Equal(3, tables.GetCommand(1)!.Type);   // weapon skill Combo
            Assert.Equal(1, tables.GetCommand(528)!.Type); // job ability (512 + 16)
        }
    }
}
