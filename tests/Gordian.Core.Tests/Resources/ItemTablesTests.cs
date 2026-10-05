// tests/Gordian.Core.Tests/Resources/ItemTablesTests.cs
using System.IO;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>
    /// #203: <see cref="ResourceManager"/> and <see cref="ItemNameResolver"/> share one item table list, so General 2
    /// (8704-10239) and the tables from 28672 resolve in both.
    /// </summary>
    public class ItemTablesTests
    {
        private const string GameDir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public ItemTablesTests(ITestOutputHelper output) => _output = output;

        [Theory]
        [InlineData(0u, "118", "106")]
        [InlineData(4096u, "118", "107")]
        [InlineData(8703u, "118", "110")]
        [InlineData(8704u, "301", "115")]
        [InlineData(10239u, "301", "115")]
        [InlineData(10240u, "118", "109")]
        [InlineData(16384u, "118", "108")]
        [InlineData(28671u, "286", "73")]
        [InlineData(28672u, "217", "21")]
        [InlineData(29696u, "288", "80")]
        [InlineData(31743u, "387", "14")]
        [InlineData(61440u, "288", "67")]
        [InlineData(65535u, "174", "48")]
        public void TablePath_CoversEveryRange(uint itemId, string folder, string file)
        {
            string expected = Path.Combine("ROM", folder, $"{file}.DAT");
            Assert.Equal(expected, ItemTables.GetTablePath(itemId));
            Assert.Equal(expected, ResourceManager.GetItemTablePathForId(itemId));
        }

        [Theory]
        [InlineData(31744u)]
        [InlineData(32767u)]
        [InlineData(65534u)]
        public void TablePath_IsEmptyOutsideTheTables(uint itemId)
        {
            Assert.Equal(string.Empty, ItemTables.GetTablePath(itemId));
            Assert.False(ItemTables.TryGetRange(itemId, out _));
        }

        [Fact]
        public void Ranges_AreOrderedAndDisjoint_ContiguousUpTo31743()
        {
            uint next = 0;
            foreach (var range in ItemTables.Ranges)
            {
                Assert.True(range.StartId >= next);
                Assert.True(range.EndId >= range.StartId);
                if (range.StartId < 31744) Assert.Equal(next, range.StartId);
                next = range.EndId + 1;
            }
        }

        /// <summary>
        /// Retail: every table's first record holds its start id (so record n is item start + n). Tables missing from
        /// an older install are skipped, as is the whole test without the game install.
        /// </summary>
        [Fact]
        public void RetailTables_FirstRecordIsTheStartId()
        {
            if (!Directory.Exists(GameDir)) return;
            foreach (var range in ItemTables.Ranges)
            {
                string path = Path.Combine(GameDir, range.RelativePath);
                if (!File.Exists(path)) { _output.WriteLine($"{range.Name}: {range.RelativePath} absent"); continue; }
                var items = ItemTableDecoder.ParseItemDat(File.ReadAllBytes(path));
                Assert.NotEmpty(items);
                // Record 0 of General 1 is item 0, which the decoder skips.
                Assert.Equal(range.StartId == 0 ? 1u : range.StartId, items[0].ItemId);
                Assert.True(items[^1].ItemId <= range.EndId, $"{range.Name}: last id {items[^1].ItemId}");
                _output.WriteLine($"{range.Name}: {items.Count} records {items[0].ItemId}-{items[^1].ItemId}");
            }
        }

        /// <summary>
        /// Retail: items of General 2 (Bismuth Ingot 8704 ...) and of the Moblin Maze Mongers table (Maze Tabula M01 28672 ...) load through
        /// <see cref="ResourceManager.TryGetItem"/> with the names <see cref="ItemNameResolver"/> reads. Skipped without
        /// the game install.
        /// </summary>
        [Theory]
        [InlineData(8704u, 10239u)]
        [InlineData(28672u, 29695u)]
        public void RetailItems_ResolveInTheNewRanges(uint firstId, uint lastId)
        {
            if (!Directory.Exists(GameDir)) return;
            var rm = new ResourceManager(GameDir);
            ItemNameResolver.Initialize(GameDir);
            int found = 0;
            for (uint id = firstId; id <= lastId; id++)
            {
                if (!rm.TryGetItem(id, out var item) || item == null || string.IsNullOrWhiteSpace(item.Name) || item.Name == ".") continue;
                found++;
                Assert.Equal(id, item.ItemId);
                if (found <= 20) Assert.Equal(item.Name, ItemNameResolver.Resolve((ushort)id));
                if (found <= 5) _output.WriteLine($"{id}: {item.Name}");
            }
            _output.WriteLine($"{found} named items in {firstId}-{lastId}");
            Assert.True(found > 10, $"only {found} named items in {firstId}-{lastId}");
        }
    }
}
