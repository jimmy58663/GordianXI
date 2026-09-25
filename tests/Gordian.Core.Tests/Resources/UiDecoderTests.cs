// tests/Gordian.Core.Tests/Resources/UiDecoderTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Gordian.Core.Resources.Ui;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class UiDecoderTests
    {
        private static void WriteId(List<byte> buffer, string id) => buffer.AddRange(Encoding.ASCII.GetBytes(id.PadRight(16)));

        private static void WriteI16(List<byte> buffer, int value)
        {
            buffer.Add((byte)(value & 0xFF));
            buffer.Add((byte)((value >> 8) & 0xFF));
        }

        private static byte[] BuildGroup()
        {
            var b = new List<byte>();
            WriteId(b, "menu    buff");
            b.Add(2);
            WriteId(b, "menu    hfr1");
            WriteId(b, "menu    corner");
            WriteI16(b, 2); // images

            // Image 0: two parts.
            b.Add(2);
            for (int part = 0; part < 2; part++)
            {
                foreach (int v in new[] { -3, -2, 23, -2, -3, 30, 23, 30 }) WriteI16(b, v + part);
                foreach (int v in new[] { 26, 32, 6, 0 }) WriteI16(b, v);
                b.Add(1);
                for (int c = 0; c < 4; c++) b.AddRange(new byte[] { 0x7F, 0x40, 0x40, (byte)(0x60 + c) });
                b.AddRange(new byte[] { 0x01, 0x00, 0x02, 0x01 });
                WriteId(b, part == 0 ? "menu    hfr1" : "menu    corner");
            }

            // Image 1: empty.
            b.Add(0);
            while (b.Count % 16 != 0) b.Add(0);
            return b.ToArray();
        }

        [Fact]
        public void ElementGroup_DecodesTexturesImagesAndParts()
        {
            var group = UiElementGroupDecoder.Decode(BuildGroup(), "buff");

            Assert.NotNull(group);
            Assert.Equal("menu", group!.Category);
            Assert.Equal("buff", group.Name);
            Assert.Equal(new[] { "menu    hfr1    ", "menu    corner  " }, group.TextureNames);
            Assert.Equal(2, group.Images.Count);
            Assert.Equal(2, group.Images[0].Parts.Count);
            Assert.Empty(group.Images[1].Parts);

            var part = group.Images[0].Parts[1];
            Assert.Equal(new UiPoint(-2, -1), part.TopLeft);
            Assert.Equal(new UiPoint(24, 31), part.BottomRight);
            Assert.Equal((ushort)26, part.SourceWidth);
            Assert.Equal((ushort)32, part.SourceHeight);
            Assert.Equal((ushort)6, part.SourceX);
            Assert.Equal((ushort)0, part.SourceY);
            Assert.Equal((byte)1, part.Flags);
            Assert.Equal(new UiColor(0x7F, 0x40, 0x40, 0x60), part.ColorBottomLeft); // colours run bottom row first
            Assert.Equal(new UiColor(0x7F, 0x40, 0x40, 0x61), part.ColorBottomRight);
            Assert.Equal(new UiColor(0x7F, 0x40, 0x40, 0x62), part.ColorTopLeft);
            Assert.Equal(new UiColor(0x7F, 0x40, 0x40, 0x63), part.ColorTopRight);
            Assert.Equal(0x01020001u, part.TextureAttributes);
            Assert.Equal("menu    corner  ", part.TextureName);
        }

        [Fact]
        public void ElementGroup_TruncatedPayload_ReturnsNull()
        {
            var bytes = BuildGroup();
            Assert.Null(UiElementGroupDecoder.Decode(bytes.AsSpan(0, 100), "buff"));
        }

        private static void WriteRecord(List<byte> b, byte[] header, (ushort Kind, ushort Index, string Group)[] shapes, string help, string title)
        {
            var record = new List<byte>(header);
            foreach (var shape in shapes)
            {
                WriteI16(record, shape.Kind);
                WriteI16(record, shape.Index);
                WriteId(record, shape.Group);
            }
            record.AddRange(Encoding.ASCII.GetBytes(help)); record.Add(0);
            record.AddRange(Encoding.ASCII.GetBytes(title)); record.Add(0);
            while (record.Count % 16 != 0) record.Add(0);
            BinaryPrimitives.WriteUInt16LittleEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(record), (ushort)record.Count);
            b.AddRange(record);
        }

        private static byte[] RecordHeader(params (int Offset, int Value, bool Byte)[] fields)
        {
            var header = new byte[0x20];
            foreach (var (offset, value, isByte) in fields)
            {
                if (isByte) header[offset] = (byte)value;
                else BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(offset), (short)value);
            }
            return header;
        }

        [Fact]
        public void Menu_DecodesFrameAnchorButtonsNavigationAndTextIds()
        {
            var b = new List<byte>();
            WriteId(b, "menu    logwindo");
            b.Add(1); // menu type
            b.Add(2); // buttons
            while (b.Count < 0x20) b.Add(0);

            WriteRecord(b, RecordHeader((2, 16, false), (4, 298, false), (10, 366, false), (12, 134, false),
                                        (19, 2, true), (20, 2, true), (21, 3, true), (22, 2, true)),
                new[] { ((ushort)0, (ushort)5, "menu    windowps"), ((ushort)6, (ushort)0, "anc     kaipage") }, "298", "72");

            WriteRecord(b, RecordHeader((2, 3, false), (4, 6, false), (10, 132, false), (12, 16, false), (18, 1, false),
                                        (23, 2, true), (24, 2, true), (25, 1, true), (26, 1, true), (27, 1, true), (29, 2, true), (30, 2, true)),
                new[] { ((ushort)0, (ushort)50, "menu    keytops3") }, "-1", "-1");

            WriteRecord(b, RecordHeader((2, 3, false), (4, 24, false), (10, 132, false), (12, 16, false), (18, 2, false),
                                        (23, 1, true), (24, 0xFF, true), (25, 2, true), (26, 2, true), (27, 2, true), (29, 3, true), (30, 2, true)),
                new[] { ((ushort)0, (ushort)13, "menu    windowps"), ((ushort)4, (ushort)765, "menu    windowps") }, "321", "81");

            var menu = UiMenuDecoder.Decode(b.ToArray(), "logw");

            Assert.NotNull(menu);
            Assert.Equal("logwindo", menu!.Name);
            Assert.Equal((byte)1, menu.MenuType);
            Assert.Equal(16, menu.Frame.X);
            Assert.Equal(298, menu.Frame.Y);
            Assert.Equal(366, menu.Frame.Width);
            Assert.Equal(134, menu.Frame.Height);
            Assert.Equal(UiAnchor.BottomLeft, menu.Frame.Anchor);
            Assert.Equal(new UiShapeReference(0, 5, "menu    windowps"), menu.Frame.Shapes[0]);
            Assert.Equal("kaipage", menu.Frame.Shapes[1].GroupName);
            Assert.Equal(298, menu.Frame.HelpTextId);
            Assert.Equal(72, menu.Frame.TitleTextId);

            Assert.Equal(2, menu.Buttons.Count);
            var first = menu.FindButton(1)!;
            Assert.Equal(132, first.Width);
            Assert.Equal(2, first.NavDown);
            Assert.Equal(-1, first.HelpTextId);
            var second = menu.FindButton(2)!;
            Assert.Equal(1, second.NavUp);
            Assert.Equal(-1, second.NavDown);
            Assert.Equal(2, second.Shapes.Count);
            Assert.Equal((ushort)765, second.Shapes[1].ImageIndex);
            Assert.Equal(321, second.HelpTextId);
            Assert.Equal(81, second.TitleTextId);
        }

        [Theory]
        [InlineData(new byte[] { 0x01, 0x00, 0x01, 0x01 }, UiBlendMode.Alpha)]    // backgrounds, glyphs
        [InlineData(new byte[] { 0x01, 0x02, 0x00, 0x01 }, UiBlendMode.Subtract)] // text shadows, title bands
        [InlineData(new byte[] { 0x01, 0x01, 0x00, 0x01 }, UiBlendMode.Add)]
        [InlineData(new byte[] { 0x01, 0x07, 0x00, 0x01 }, UiBlendMode.Alpha)]    // unknown modes fall back
        public void SpritePart_BlendModeIsTheAttributesSecondByte(byte[] attributes, UiBlendMode expected)
        {
            var part = new UiSpritePart { TextureAttributes = BinaryPrimitives.ReadUInt32LittleEndian(attributes) };
            Assert.Equal(expected, part.BlendMode);
        }

        [Theory]
        [InlineData("menu    windowps", "windowps")]
        [InlineData("anc     anc_item", "anc_item")]
        [InlineData("windowps", "windowps")]
        [InlineData(" yubi ", "yubi")]
        public void TrimResourceName_ReducesResourceIds(string input, string expected)
        {
            Assert.Equal(expected, UiResourceLibrary.TrimResourceName(input));
        }
    }
}
