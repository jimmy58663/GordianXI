// src/Gordian.Core/Resources/Tables/SpellCommandTables.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Containers;

namespace Gordian.Core.Resources.Tables
{
    /// <summary>
    /// One spell of the client's spell table (section 0x49 <c>mgc_</c>), decoded. Ids index the d_msg spell names
    /// (ROM/181/73) and the server's spell ids.
    /// </summary>
    /// <param name="Kind">1 white, 2 black, 3 summoning, 4 ninjutsu, 5 song, 6 blue, 7 geomancy, 8 trust.</param>
    /// <param name="Element">0 fire ... 7 dark, 15 none.</param>
    /// <param name="Skill">The skill id (33 healing magic, 36 elemental magic ...).</param>
    /// <param name="CastQuarters">Cast time in quarter seconds (Cure: 8 = 2 s).</param>
    /// <param name="RecastQuarters">Recast in quarter seconds (Cure: 20 = 5 s).</param>
    /// <param name="JobLevels">Level per job in job order (0 none, 1 WAR ... 22 RUN, 23 MON); <see cref="SpellRecord.CannotLearn"/> when the job cannot learn it.</param>
    /// <param name="MenuIndex">The slot the known-spell list sorts the spell into.</param>
    public sealed record SpellRecord(int Id, int Kind, int Element, int TargetFlags, int Skill, int MpCost, int CastQuarters,
        int RecastQuarters, IReadOnlyList<int> JobLevels, int MenuIndex, int Icon)
    {
        /// <summary>The <see cref="JobLevels"/> value of a job that cannot learn the spell.</summary>
        public const int CannotLearn = 0xFFFF;

        /// <summary>The level at which <paramref name="job"/> learns the spell, or null when it cannot.</summary>
        public int? LevelFor(int job) => job >= 0 && job < JobLevels.Count && JobLevels[job] != CannotLearn ? JobLevels[job] : null;
    }

    /// <summary>
    /// One record of the client's command table (section 0x53 <c>comm</c>): weapon skills at their id, job abilities at
    /// 512 + id. Only the fields checked against the retail data are named.
    /// </summary>
    /// <param name="Type">1 job ability, 2 pet ability, 3 weapon skill, 4 job trait, 6 Blood Pact: Rage, 8 Corsair's Roll, ... (xi-tools).</param>
    public sealed record CommandRecord(int Id, int Type, int Icon, int TargetFlags, int Range);

    /// <summary>
    /// The client's spell and command tables: file id 81 (<c>ROM/118/114</c>, a "menu" container) holds section 0x49
    /// <c>mgc_</c> (1024 spell records of 0x64 bytes) and section 0x53 <c>comm</c> (2816 command records of 0x30 bytes),
    /// beside three 0x04 tables (<c>mnc2</c>, <c>mon_</c>, <c>levc</c>, not read).
    /// <para>
    /// Every record is scrambled on its own: bytes 0x02, 0x0B and 0x0C are stored plain, and the other bytes are rotated
    /// right by an amount the three plain bytes pick, so decoding rotates them left by
    /// <c>{1, 7, 2, 6, 3}[|popcount(b02) + popcount(b0C) - popcount(b0B)| mod 5]</c>. The scheme, the record sizes and
    /// the field offsets are referenced from xi-tools (https://github.com/vekien/xi-tools, <c>docs/dats/ROM_118_114.md</c>);
    /// the spell fields were checked against the retail table on 2026-10-10 (Cure: white, light, healing magic, 8 MP,
    /// cast 2 s, recast 5 s, WHM 1 / RDM 3 / PLD 5; Fire: black, fire, elemental, 7 MP, BLM 13 / RDM 19; Sleep (253):
    /// enfeebling, dark, 19 MP, BLM 20 / RDM 25). The command fields beyond id, type, icon, targets and range are not
    /// checked (the level byte xi-tools names at +0x0F reads 0 for job abilities), so they are not exposed.
    /// </para>
    /// </summary>
    public sealed class SpellCommandTables
    {
        /// <summary>The English file id (resolves to ROM/118/114).</summary>
        public const int FileId = 81;

        public const int SpellRecordSize = 0x64;
        public const int CommandRecordSize = 0x30;

        private static readonly int[] Rotations = { 1, 7, 2, 6, 3 };

        private SpellCommandTables(IReadOnlyList<SpellRecord?> spells, IReadOnlyList<CommandRecord?> commands)
        {
            Spells = spells;
            Commands = commands;
        }

        /// <summary>Spell records by spell id; null for an empty (all-zero) record.</summary>
        public IReadOnlyList<SpellRecord?> Spells { get; }

        /// <summary>Command records by command id; null for an empty record.</summary>
        public IReadOnlyList<CommandRecord?> Commands { get; }

        /// <summary>The spell with this id, or null.</summary>
        public SpellRecord? GetSpell(int id) => id >= 0 && id < Spells.Count ? Spells[id] : null;

        /// <summary>The command with this id, or null.</summary>
        public CommandRecord? GetCommand(int id) => id >= 0 && id < Commands.Count ? Commands[id] : null;

        /// <summary>Decodes the tables; null when the buffer has neither section.</summary>
        public static SpellCommandTables? Parse(ReadOnlySpan<byte> file)
        {
            var spells = new List<SpellRecord?>();
            var commands = new List<CommandRecord?>();
            bool found = false;
            Span<byte> record = stackalloc byte[SpellRecordSize];
            foreach (var header in DatSectionWalker.ReadHeaders(file))
            {
                if (header.DataOffset + header.DataSizeBytes > file.Length) continue;
                var payload = file.Slice(header.DataOffset, header.DataSizeBytes);
                if (header.TypeCode == DatSectionType.SpellList)
                {
                    found = true;
                    for (int at = 0; at + SpellRecordSize <= payload.Length; at += SpellRecordSize)
                    {
                        var r = record.Slice(0, SpellRecordSize);
                        payload.Slice(at, SpellRecordSize).CopyTo(r);
                        spells.Add(IsEmpty(r) ? null : ReadSpell(Unscramble(r)));
                    }
                }
                else if (header.TypeCode == DatSectionType.AbilityList)
                {
                    found = true;
                    for (int at = 0; at + CommandRecordSize <= payload.Length; at += CommandRecordSize)
                    {
                        var r = record.Slice(0, CommandRecordSize);
                        payload.Slice(at, CommandRecordSize).CopyTo(r);
                        commands.Add(IsEmpty(r) ? null : ReadCommand(Unscramble(r)));
                    }
                }
            }
            return found ? new SpellCommandTables(spells, commands) : null;
        }

        /// <summary>Undoes a record's rotation in place (the bytes at 0x02, 0x0B and 0x0C stay as they are) and returns it.</summary>
        public static Span<byte> Unscramble(Span<byte> record)
        {
            if (record.Length <= 0x0C) return record;
            int shift = Rotations[Math.Abs(BitOperations.PopCount(record[0x02]) + BitOperations.PopCount(record[0x0C])
                - BitOperations.PopCount(record[0x0B])) % Rotations.Length];
            for (int i = 0; i < record.Length; i++)
            {
                if (i is 0x02 or 0x0B or 0x0C) continue;
                byte b = record[i];
                record[i] = (byte)((b << shift) | (b >> (8 - shift)));
            }
            return record;
        }

        /// <summary>Scrambles a decoded record in place (the inverse of <see cref="Unscramble"/>), for tests.</summary>
        public static Span<byte> Scramble(Span<byte> record)
        {
            if (record.Length <= 0x0C) return record;
            int shift = Rotations[Math.Abs(BitOperations.PopCount(record[0x02]) + BitOperations.PopCount(record[0x0C])
                - BitOperations.PopCount(record[0x0B])) % Rotations.Length];
            for (int i = 0; i < record.Length; i++)
            {
                if (i is 0x02 or 0x0B or 0x0C) continue;
                byte b = record[i];
                record[i] = (byte)((b >> shift) | (b << (8 - shift)));
            }
            return record;
        }

        private static bool IsEmpty(ReadOnlySpan<byte> record) => record.IndexOfAnyExcept((byte)0) < 0;

        private static SpellRecord ReadSpell(ReadOnlySpan<byte> r)
        {
            var levels = new int[24];
            for (int j = 0; j < levels.Length; j++) levels[j] = BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(0x0E + j * 2));
            return new SpellRecord(
                Id: BinaryPrimitives.ReadUInt16LittleEndian(r),
                Kind: BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(0x02)),
                Element: BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(0x04)),
                TargetFlags: BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(0x06)),
                Skill: BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(0x08)),
                MpCost: BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(0x0A)),
                CastQuarters: r[0x0C],
                RecastQuarters: r[0x0D],
                JobLevels: levels,
                MenuIndex: BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(0x3E)),
                Icon: BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(0x40)));
        }

        private static CommandRecord ReadCommand(ReadOnlySpan<byte> r) => new(
            Id: BinaryPrimitives.ReadUInt16LittleEndian(r),
            Type: r[0x02],
            Icon: r[0x03],
            TargetFlags: BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(0x0A)),
            Range: r[0x10]);
    }
}
