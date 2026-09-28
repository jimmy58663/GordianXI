// tests/Gordian.Core.Tests/Resources/ZoneDialogTableTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class ZoneDialogTableTests
    {
        /// <summary>Builds a dialog DAT from raw (control-coded) messages the way the retail files are laid out.</summary>
        internal static byte[] BuildDialogDat(params byte[][] messages)
        {
            var body = new List<byte>();
            int tableSize = messages.Length * 4;
            var offsets = new List<int>();
            int next = tableSize;
            foreach (var message in messages)
            {
                offsets.Add(next);
                next += message.Length + 1;
            }
            foreach (int offset in offsets)
            {
                var bytes = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(bytes, offset);
                body.AddRange(bytes);
            }
            foreach (var message in messages)
            {
                body.AddRange(message);
                body.Add(0);
            }
            var file = new byte[4 + body.Count];
            BinaryPrimitives.WriteUInt32LittleEndian(file, 0x10000000u | (uint)body.Count);
            for (int i = 0; i < body.Count; i++) file[4 + i] = (byte)(body[i] ^ 0x80);
            return file;
        }

        private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

        [Fact]
        public void Parse_DecodesXoredBodyAndOffsets()
        {
            var file = BuildDialogDat(Ascii("Home point set!"), Ascii("Obtained: "), Ascii("Third"));
            var table = ZoneDialogTable.Parse(file);
            Assert.NotNull(table);
            Assert.Equal(3, table!.Count);
            Assert.Equal("Home point set!", table.GetPlainText(0));
            Assert.Equal("Obtained:", table.GetPlainText(1));
            Assert.Equal("Third", table.GetPlainText(2));
            Assert.Equal(string.Empty, table.GetPlainText(3));
            Assert.Equal(new[] { 0 }, table.Find("home point").ToArray());
        }

        [Fact]
        public void Parse_RejectsOtherFiles()
        {
            Assert.Null(ZoneDialogTable.Parse(new byte[] { 1, 2, 3 }));
            var file = BuildDialogDat(Ascii("x"));
            file[3] = 0x20; // wrong header flag byte
            Assert.Null(ZoneDialogTable.Parse(file));
        }

        [Theory]
        [InlineData(230, false, 6650)]
        [InlineData(0, false, 6420)]
        [InlineData(230, true, 6350)]
        [InlineData(256, false, 85335)]
        [InlineData(299, false, 85378)]
        [InlineData(300, false, -1)]
        public void GetFileId_FollowsTheRetailFileTable(int zone, bool japanese, int expected)
        {
            Assert.Equal(expected, ZoneDialogTable.GetFileId(zone, japanese));
        }

        [Fact]
        public void Decode_SplitsLinesChoicesAndPrompt()
        {
            // "Care to learn about Mog Houses?" 0x07 0x0B "Sure!" 0x07 "No, thanks." 0x7F 0x31 0x00
            var raw = Ascii("Care to learn about Mog Houses?").Concat(new byte[] { 0x07, 0x0B }).Concat(Ascii("Sure!"))
                .Concat(new byte[] { 0x07 }).Concat(Ascii("No, thanks.")).Concat(new byte[] { 0x7F, 0x31, 0x00 }).ToArray();
            var message = EventMessageDecoder.Decode(raw);
            Assert.True(message.HasPrompt);
            Assert.True(message.HasChoices);
            var kinds = message.Segments.Select(s => s.Kind).ToArray();
            Assert.Equal(new[]
            {
                EventMessageSegmentKind.Text, EventMessageSegmentKind.LineBreak, EventMessageSegmentKind.ChoicesStart,
                EventMessageSegmentKind.Text, EventMessageSegmentKind.LineBreak, EventMessageSegmentKind.Text, EventMessageSegmentKind.Prompt,
            }, kinds);
            Assert.Equal("Care to learn about Mog Houses? / Sure! No, thanks.", message.ToPlainText());
        }

        [Fact]
        public void Decode_ReadsNumberNameAndSelectorCodes()
        {
            // "It costs " 0x0A 0x03 " gil to " 0x01 0x05 '#' 0x82 0x80 0x80 0x80 " " 0x0C 0x01 "[registered to/removed from]" "."
            var raw = Ascii("It costs ").Concat(new byte[] { 0x0A, 0x03 }).Concat(Ascii(" gil to "))
                .Concat(new byte[] { 0x01, 0x05, (byte)'#', 0x82, 0x80, 0x80, 0x80 }).Concat(Ascii(" "))
                .Concat(new byte[] { 0x0C, 0x01 }).Concat(Ascii("[registered to/removed from]")).Concat(Ascii(".")).ToArray();
            var message = EventMessageDecoder.Decode(raw);
            var segments = message.Segments;
            Assert.Equal(EventMessageSegmentKind.Number, segments[1].Kind);
            Assert.Equal(3, segments[1].Argument);
            var name = segments[3];
            Assert.Equal(EventMessageSegmentKind.Name, name.Kind);
            Assert.Equal((byte)'#', name.Code);
            Assert.Equal(new[] { 0 }, name.Values);
            Assert.Equal(" ", segments[4].Text);
            var selector = segments[5];
            Assert.Equal(EventMessageSegmentKind.Selector, selector.Kind);
            Assert.Equal(1, selector.Argument);
            Assert.Equal(new[] { "registered to", "removed from" }, selector.Alternatives);
            Assert.Equal(".", segments[6].Text);
            Assert.Equal("It costs gil to registered to.", message.ToPlainText());
        }

        [Fact]
        public void Decode_NameBlockValuesAreLittleEndianXor80()
        {
            // A two-byte value 0x0123 and a one-byte value 5.
            var raw = new byte[] { 0x01, 0x08, (byte)'8', 0x82, 0x23 ^ 0x80, 0x01 ^ 0x80, 0x80, 0x81, 0x05 ^ 0x80, 0x80 };
            var message = EventMessageDecoder.Decode(raw);
            var name = Assert.Single(message.Segments);
            Assert.Equal(new[] { 0x0123, 5 }, name.Values);
        }

        [Fact]
        public void Decode_ExtendedCodesHaveDocumentedLengths()
        {
            // 0x7F 0x80 0x01 (three bytes) then text; 0x7F 0x38 x x (four bytes) then text; 0x1F colour then text.
            var raw = new byte[] { 0x7F, 0x80, 0x01, (byte)'a', 0x7F, 0x38, 1, 2, (byte)'b', 0x1F, 0x79, (byte)'c', 0xEF, 0x1F, (byte)'d' };
            var message = EventMessageDecoder.Decode(raw);
            Assert.Equal("abcd", message.ToPlainText());
            Assert.Contains(message.Segments, s => s.Kind == EventMessageSegmentKind.Colour && s.Argument == 0x79);
            Assert.Contains(message.Segments, s => s.Kind == EventMessageSegmentKind.Icon && s.Argument == 0x1F);
        }

        [Fact]
        public void Decode_ShiftJisPairsStayTogether()
        {
            // Full-width parentheses (0x81 0x69 / 0x81 0x6A) around "x": the trail byte 0x69 must not be read as text on its own.
            var raw = new byte[] { 0x81, 0x69, (byte)'x', 0x81, 0x6A };
            Assert.Equal("（x）", EventMessageDecoder.Decode(raw).ToPlainText());
        }
    }
}
