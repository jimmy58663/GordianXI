// tests/Gordian.Core.Tests/Resources/ZoneEntityListTests.cs
using System.IO;
using System.Linq;
using System.Text;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class ZoneEntityListTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public ZoneEntityListTests(ITestOutputHelper output) => _output = output;

        private static byte[] Record(string name, uint id)
        {
            var record = new byte[ZoneEntityList.RecordSize];
            Encoding.ASCII.GetBytes(name).CopyTo(record, 0);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(28), id);
            return record;
        }

        [Fact]
        public void Parse_ReadsNamesById()
        {
            var file = Record("Ailevia", 0x010E607E).Concat(Record("Home Point #1", 0x010E6087)).Concat(Record("", 0)).ToArray();
            var list = ZoneEntityList.Parse(file);
            Assert.NotNull(list);
            Assert.Equal(2, list!.Count);
            Assert.Equal("Ailevia", list.GetName(0x010E607E));
            Assert.Equal("Home Point #1", list.GetName(0x010E6087));
            Assert.Null(list.GetName(1));
        }

        [Fact]
        public void Parse_RejectsOtherFiles()
        {
            Assert.Null(ZoneEntityList.Parse(new byte[] { 1, 2, 3 }));
            var binary = new byte[ZoneEntityList.RecordSize];
            binary[0] = 0x01;
            Assert.Null(ZoneEntityList.Parse(binary));
        }

        [Theory]
        [InlineData(230, 6950)]
        [InlineData(0, 6720)]
        [InlineData(256, 86491)]
        [InlineData(300, -1)]
        public void GetFileId_FollowsTheRetailFileTable(int zone, int expected)
        {
            Assert.Equal(expected, ZoneEntityList.GetFileId(zone));
        }

        [Fact]
        public void SouthernSandoria_ListNamesItsNpcs()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var bytes = rm.LoadDatBytesByFileId(ZoneEntityList.GetFileId(230));
            Assert.NotNull(bytes);
            var list = ZoneEntityList.Parse(bytes!);
            Assert.NotNull(list);
            _output.WriteLine($"{list!.Count} entities");
            foreach (var entry in list.Entries.Take(12)) _output.WriteLine($"0x{entry.Key:X8} {entry.Value}");
            // LandSandBoat Ailevia.lua: her server id block in the event DAT is 0x010E607E.
            _output.WriteLine($"0x010E607E = {list.GetName(0x010E607E)}, 0x010E6087 = {list.GetName(0x010E6087)}");
            Assert.True(list.Count > 200);
            Assert.Equal("Ailevia", list.GetName(0x010E607E));
        }
    }
}
