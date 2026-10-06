// tests/Gordian.Core.Tests/Events/EventOpcodeCensusTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Gordian.Core.Events;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Xunit;

namespace Gordian.Core.Tests.Events
{
    /// <summary>
    /// Keeps the event opcode reference (docs/events/) honest. <see cref="Census"/> counts how many retail event
    /// entries use each opcode and sub-case, walking every entry by <see cref="EventOpcodeTable"/> as
    /// <see cref="EventOpcodeCorpusTests"/> does, and rewrites docs/events/opcode-usage.md when
    /// <c>GORDIAN_WRITE_DOCS=1</c> is set. <see cref="OpcodesDoc_HasEveryOpcode"/> needs no game data and fails when
    /// docs/events/opcodes.md lacks a row for an opcode.
    /// </summary>
    public class EventOpcodeCensusTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        /// <summary>The last opcode XiEvents documents (OpCodes/0x00D9).</summary>
        private const int LastOpcode = 0xD9;

        private readonly ITestOutputHelper _output;

        public EventOpcodeCensusTests(ITestOutputHelper output) => _output = output;

        private static string? FindRepoRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "GordianXI.slnx"))) return dir.FullName;
            }
            return null;
        }

        /// <summary>True when the opcode's length depends on the byte after it (a sub-case opcode).</summary>
        private static bool HasSubCases(byte op)
        {
            var probe = new byte[64];
            probe[0] = op;
            int first = EventOpcodeTable.GetLength(probe, 0);
            for (int sub = 1; sub < 256; sub++)
            {
                probe[1] = (byte)sub;
                if (EventOpcodeTable.GetLength(probe, 0) != first) return true;
            }
            return false;
        }

        private sealed class Tally
        {
            public int Occurrences;
            public int Entries;
            public readonly HashSet<int> Zones = new();
        }

        [Fact]
        public void Census()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();

            var ops = new Dictionary<int, Tally>();
            var subs = new Dictionary<(int Op, int Sub), Tally>();
            var subCase = Enumerable.Range(0, 256).Select(op => HasSubCases((byte)op)).ToArray();
            int zones = 0, entries = 0;
            for (int zone = 0; zone < 300; zone++)
            {
                var bytes = rm.LoadDatBytesByFileId(ZoneEventScript.GetFileId(zone));
                if (bytes == null) continue;
                var script = ZoneEventScript.Parse(bytes);
                if (script == null) continue;
                zones++;
                foreach (var block in script.Blocks)
                {
                    for (int i = 0; i < block.Offsets.Count; i++)
                    {
                        int start = block.Offsets[i], end = block.Code.Length;
                        foreach (int offset in block.Offsets)
                        {
                            if (offset > start && offset < end) end = offset;
                        }
                        entries++;
                        var seenOps = new HashSet<int>();
                        var seenSubs = new HashSet<(int, int)>();
                        int pc = start;
                        while (pc < end)
                        {
                            int op = block.Code[pc];
                            Count(ops, op, seenOps.Add(op), zone);
                            if (subCase[op] && pc + 1 < block.Code.Length)
                            {
                                var key = (op, (int)block.Code[pc + 1]);
                                Count(subs, key, seenSubs.Add(key), zone);
                            }
                            int length = EventOpcodeTable.GetLength(block.Code, pc);
                            if (length <= 0) break;
                            pc += length;
                        }
                    }
                }
            }

            string markdown = Render(ops, subs, subCase, zones, entries);
            _output.WriteLine(markdown);
            Assert.True(entries > 0);
            if (Environment.GetEnvironmentVariable("GORDIAN_WRITE_DOCS") == "1" && FindRepoRoot() is string root)
            {
                File.WriteAllText(Path.Combine(root, "docs", "events", "opcode-usage.md"), markdown);
            }
        }

        private static void Count<TKey>(Dictionary<TKey, Tally> tallies, TKey key, bool firstInEntry, int zone) where TKey : notnull
        {
            if (!tallies.TryGetValue(key, out var tally)) tallies[key] = tally = new Tally();
            tally.Occurrences++;
            if (firstInEntry) tally.Entries++;
            tally.Zones.Add(zone);
        }

        private static string Render(Dictionary<int, Tally> ops, Dictionary<(int Op, int Sub), Tally> subs, bool[] subCase, int zones, int entries)
        {
            var sb = new StringBuilder();
            sb.Append("# Event opcode usage\n\n");
            sb.Append("Generated by `EventOpcodeCensusTests.Census` from the retail event DATs; do not edit by hand. Regenerate with\n");
            sb.Append("`GORDIAN_WRITE_DOCS=1 dotnet test tests/Gordian.Core.Tests --filter EventOpcodeCensusTests` on a machine with the game installed.\n\n");
            sb.Append($"Scope: {zones} zones with event DATs, {entries:N0} event entries. Each entry is walked from its start by the opcode length table ");
            sb.Append("(`EventOpcodeTable`) up to the next entry's offset, so the counts are lower bounds: code reached only by a jump past that offset, ");
            sb.Append("and the rest of an entry after an opcode of unknown length, are not counted. The other way, script tables (runs of 16-bit work references, `NN 80`) ");
            sb.Append("that sit after an end inside an entry's range are walked as code, which inflates the opcodes that look like table bytes (0x6D, 0xA6, 0xB2, 0xCB, 0xD7 ");
            sb.Append("and part of 0x80 are mostly such data) and makes every sub-case 0x80 row table data. *Events* is the number of entries that use the opcode at least once. ");
            sb.Append("What each opcode does and what GordianXI does with it is in [opcodes.md](opcodes.md).\n\n");
            sb.Append("## Opcodes\n\n| Op | Events | Uses | Zones |\n|---|---:|---:|---:|\n");
            for (int op = 0; op <= LastOpcode; op++)
            {
                ops.TryGetValue(op, out var t);
                sb.Append($"| 0x{op:X2} | {t?.Entries ?? 0:N0} | {t?.Occurrences ?? 0:N0} | {t?.Zones.Count ?? 0} |\n");
            }
            sb.Append("\n## Sub-cases\n\nOpcodes whose length depends on the byte after them; sub-cases that no entry uses are left out.\n\n");
            sb.Append("| Op | Sub | Events | Uses | Zones |\n|---|---|---:|---:|---:|\n");
            foreach (var ((op, sub), t) in subs.Where(s => s.Key.Op <= LastOpcode && subCase[s.Key.Op]).OrderBy(s => s.Key.Op).ThenBy(s => s.Key.Sub))
            {
                sb.Append($"| 0x{op:X2} | 0x{sub:X2} | {t.Entries:N0} | {t.Occurrences:N0} | {t.Zones.Count} |\n");
            }
            return sb.ToString();
        }

        [Fact]
        public void OpcodesDoc_HasEveryOpcode()
        {
            string? root = FindRepoRoot();
            if (root == null) return;
            string path = Path.Combine(root, "docs", "events", "opcodes.md");
            Assert.True(File.Exists(path), "docs/events/opcodes.md is missing");
            var rows = new HashSet<int>();
            foreach (Match m in Regex.Matches(File.ReadAllText(path), @"^\|\s*0x([0-9A-F]{2})\s*\|", RegexOptions.Multiline))
            {
                rows.Add(Convert.ToInt32(m.Groups[1].Value, 16));
            }
            var missing = Enumerable.Range(0, LastOpcode + 1).Where(op => !rows.Contains(op)).Select(op => $"0x{op:X2}").ToList();
            Assert.True(missing.Count == 0, "docs/events/opcodes.md has no row for " + string.Join(", ", missing));
        }
    }
}
