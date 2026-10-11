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
        int RecastQuarters, IReadOnlyList<int> JobLevels, int MenuIndex, int Icon, int Icon2 = 0xFFFF)
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
    /// <param name="RecastId">
    /// The recast timer the ability shares (+0x08, u16): the <c>TimerId</c> S2C 0x119 reports its recast under.
    /// <b>Beyond xi-tools:</b> xi-tools leaves +0x08 unnamed; on the retail table (2026-10-11) it equals LandSandBoat's
    /// <c>abilities.recastId</c> for every ability checked (Mighty Strikes 0, Berserk 1, Warcry 2, Defender 3, Provoke 5,
    /// Chakra 15, Boost 16, Steal 60, Flee 62, Hide 63; the waltzes 217). Weapon skills carry an unrelated value there.
    /// </param>
    /// <param name="TpCost">TP the command costs (+0x0C, i16; -1 for none: weapon skills read -1 and take 1000 TP by rule).</param>
    public sealed record CommandRecord(int Id, int Type, int Icon, int TargetFlags, int Range, int RecastId = 0, int TpCost = -1, int Icon2 = -1)
    {
        /// <summary>The <see cref="Type"/> of a weapon skill.</summary>
        public const int WeaponSkillType = 3;

        /// <summary>The <see cref="Type"/> of a job trait (listed, not used).</summary>
        public const int TraitType = 4;

        /// <summary>Command ids from here up are job abilities (512 + the ability id the server knows).</summary>
        public const int JobAbilityBase = 512;
    }

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
    /// enfeebling, dark, 19 MP, BLM 20 / RDM 25). Of the command fields, id, type, icon, targets, range, the recast id
    /// (+0x08, see <see cref="CommandRecord.RecastId"/>) and the TP cost (+0x0C, Curing Waltz 200) were checked; the rest
    /// are not exposed (the level byte xi-tools names at +0x0F reads 0 for job abilities).
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
                Icon: BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(0x40)),
                Icon2: BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(0x42)));
        }

        private static CommandRecord ReadCommand(ReadOnlySpan<byte> r) => new(
            Id: BinaryPrimitives.ReadUInt16LittleEndian(r),
            Type: r[0x02],
            Icon: r[0x03],
            TargetFlags: BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(0x0A)),
            Range: r[0x10],
            RecastId: BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(0x08)),
            TpCost: BinaryPrimitives.ReadInt16LittleEndian(r.Slice(0x0C)),
            Icon2: BinaryPrimitives.ReadInt16LittleEndian(r.Slice(0x04)));
    }
}
