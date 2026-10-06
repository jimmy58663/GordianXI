// tests/Gordian.Core.Tests/Events/EventOpcodeCorpusTests.cs
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gordian.Core.Events;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Xunit;

namespace Gordian.Core.Tests.Events
{
    /// <summary>
    /// Walks retail event DATs with <see cref="EventOpcodeTable"/> when the install is present: each event entry is
    /// stepped from its start by the table's lengths and must land exactly on the next event's offset (or the end of
    /// the block's code). A wrong length throws the walk off for the rest of the entry, so the landing rate measures
    /// the table (#73).
    /// </summary>
    public class EventOpcodeCorpusTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public EventOpcodeCorpusTests(ITestOutputHelper output) => _output = output;

        private static ResourceManager? Open()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return rm;
        }

        private static ZoneEventScript? LoadZone(ResourceManager rm, int zone)
        {
            var bytes = rm.LoadDatBytesByFileId(ZoneEventScript.GetFileId(zone));
            return bytes == null ? null : ZoneEventScript.Parse(bytes);
        }

        /// <summary>Steps one entry by the table; returns true when it lands exactly on <paramref name="end"/>.</summary>
        private static bool Walk(byte[] code, int start, int end, out int stoppedAt)
        {
            int pc = start;
            while (pc < end)
            {
                int length = EventOpcodeTable.GetLength(code, pc);
                if (length <= 0) break;
                pc += length;
            }
            stoppedAt = pc;
            return pc == end;
        }

        private static IEnumerable<(EventBlock Block, int Index, int Start, int End)> Entries(ZoneEventScript script)
        {
            foreach (var block in script.Blocks)
            {
                for (int i = 0; i < block.Offsets.Count; i++)
                {
                    int start = block.Offsets[i];
                    int end = block.Code.Length;
                    foreach (int offset in block.Offsets)
                    {
                        if (offset > start && offset < end) end = offset;
                    }
                    yield return (block, i, start, end);
                }
            }
        }

        private (int Entries, int Landed) WalkZone(ZoneEventScript script, int zone, bool log)
        {
            int entries = 0, landed = 0;
            foreach (var (block, index, start, end) in Entries(script))
            {
                entries++;
                if (Walk(block.Code, start, end, out int stoppedAt)) landed++;
                else if (log) _output.WriteLine($"zone {zone} actor {block.ActorId:X8} event {block.EventIds[index]}: {start}-{end} stopped at {stoppedAt} (op {(stoppedAt < block.Code.Length ? block.Code[stoppedAt] : 0):X2} {(stoppedAt + 1 < block.Code.Length ? block.Code[stoppedAt + 1] : 0):X2})");
            }
            return (entries, landed);
        }

        [Fact]
        public void AllZones_EventEntriesLandOnTheNextOffset()
        {
            var rm = Open();
            if (rm == null) return;
            int zones = 0, entries = 0, landed = 0;
            for (int zone = 0; zone < 300; zone++)
            {
                var script = LoadZone(rm, zone);
                if (script == null) continue;
                zones++;
                var (e, l) = WalkZone(script, zone, log: false);
                entries += e;
                landed += l;
            }
            _output.WriteLine($"{zones} zones, {entries} entries, {landed} landed ({100.0 * landed / entries:F2}%)");
            // 2026-09-28: 296 zones, 209,144 entries, 99.29% (98.27% before #73). The rest hold inline data (strings,
            // sub-case 0x80 records) the opcode notes do not describe.
            Assert.True(landed >= entries * 0.9925);
        }

        /// <summary>Per-zone floors from the 2026-09-28 walk: Southern / Northern San d'Oria.</summary>
        [Theory]
        [InlineData(230, 4792)]
        [InlineData(231, 3345)]
        public void SandOria_EventEntriesLandOnTheNextOffset(int zone, int expectedLanded)
        {
            var rm = Open();
            if (rm == null) return;
            var (entries, landed) = WalkZone(LoadZone(rm, zone)!, zone, log: true);
            _output.WriteLine($"zone {zone}: {landed} of {entries} landed");
            Assert.True(landed >= expectedLanded);
        }

        /// <summary>La Theine Plateau's chocobo scenes (0x7E, CodeCHOCOBO): every entry that uses it lands.</summary>
        [Fact]
        public void LaTheinePlateau_ChocoboEntriesLandOnTheNextOffset()
        {
            var rm = Open();
            if (rm == null) return;
            const int laTheine = 102;
            int chocobo = 0;
            foreach (var (block, index, start, end) in Entries(LoadZone(rm, laTheine)!))
            {
                bool uses7E = false;
                int pc = start;
                while (pc < end)
                {
                    if (block.Code[pc] == 0x7E) uses7E = true;
                    int length = EventOpcodeTable.GetLength(block.Code, pc);
                    if (length <= 0) break;
                    pc += length;
                }
                if (!uses7E) continue;
                chocobo++;
                Assert.True(pc == end, $"actor {block.ActorId:X8} event {block.EventIds[index]} stopped at {pc}, expected {end}");
            }
            Assert.True(chocobo > 0);
        }
    }
}
