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
        [InlineData(256, false, 85591)]
        [InlineData(299, false, 85634)]
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
        public void Decode_ReadsTheTimedClose()
        {
            // The Southern San d'Oria intro's narration: text, 0x7F 0x34 0x09 (close after 9 s), then the prompt.
            var raw = Ascii("But now, her reign of glory is but a memory.").Concat(new byte[] { 0x7F, 0x34, 0x09, 0x7F, 0x31, 0x00, 0x07 }).ToArray();
            var message = EventMessageDecoder.Decode(raw);
            Assert.Equal(9, message.AutoCloseSeconds);
            Assert.True(message.HasPrompt);
            Assert.Equal("But now, her reign of glory is but a memory.", message.ToPlainText());
            Assert.Null(EventMessageDecoder.Decode(Ascii("Halt!").Concat(new byte[] { 0x7F, 0x31, 0x00 }).ToArray()).AutoCloseSeconds);
        }

        [Fact]
        public void Decode_ReadsTheScreenPositionAndTheGenderSelector()
        {
            // 02 50 00 03 54 01 (x 80, y 340) "Of course, " 7F 85 "[he/she]" " has only begun." 7F 34 09 7F 31 00
            var raw = new byte[] { 0x02, 0x50, 0x00, 0x03, 0x54, 0x01 }.Concat(Ascii("Of course, ")).Concat(new byte[] { 0x7F, 0x85 })
                .Concat(Ascii("[he/she]")).Concat(Ascii(" has only begun.")).Concat(new byte[] { 0x7F, 0x34, 0x09, 0x7F, 0x31, 0x00 }).ToArray();
            var message = EventMessageDecoder.Decode(raw);
            Assert.Equal((80, 340), message.Position);
            Assert.Equal(9, message.AutoCloseSeconds);
            var female = new GenderContext(true);
            Assert.Equal("Of course, she has only begun.", Assert.Single(Gordian.Core.Events.EventMessageFormatter.FormatLines(message, female)));
            Assert.Equal("Of course, he has only begun.", Assert.Single(Gordian.Core.Events.EventMessageFormatter.FormatLines(message, new GenderContext(false))));
            Assert.Equal("Of course, [he/she] has only begun.", Assert.Single(Gordian.Core.Events.EventMessageFormatter.FormatLines(message, new GenderContext(null))));
        }

        private sealed class GenderContext(bool? female) : Gordian.Core.Events.IEventMessageContext
        {
            public int GetNumber(int index) => 0;
            public string PlayerName => "Cybin";
            public string NpcName => string.Empty;
            public string? GetEntityName(int index) => null;
            public string? ResolveName(byte kind, int id) => null;
            public bool? PlayerIsFemale => female;
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
        public void Decode_ThreeByteExtendedCodesKeepTheTextAfterThem()
        {
            // "until " 7F A1 00 "/" 7F A2 00 "/" 7F A0 00 " at " 7F A3 00 ":" 7F A9 00 " (JST)." (the Assist Channel line):
            // the 00 argument must not end the string.
            var raw = Ascii("until ").Concat(new byte[] { 0x7F, 0xA1, 0x00 }).Concat(Ascii("/")).Concat(new byte[] { 0x7F, 0xA2, 0x00 })
                .Concat(Ascii("/")).Concat(new byte[] { 0x7F, 0xA0, 0x00 }).Concat(Ascii(" at ")).Concat(new byte[] { 0x7F, 0xA3, 0x00 })
                .Concat(Ascii(":")).Concat(new byte[] { 0x7F, 0xA9, 0x00 }).Concat(Ascii(" (JST).")).ToArray();
            var message = EventMessageDecoder.Decode(raw);
            Assert.Equal("until // at : (JST).", message.ToPlainText());
            Assert.Equal(new byte[] { 0xA1, 0xA2, 0xA0, 0xA3, 0xA9 },
                message.Segments.Where(s => s.Kind == EventMessageSegmentKind.DateField).Select(s => s.Code).ToArray());

            // 7F 81 00 (a linkshell name), 7F 94 01, 7F 95 / 96 / 99, 7F B4 / B5: three bytes each.
            foreach (byte code in new byte[] { 0x81, 0x94, 0x95, 0x96, 0x99, 0xAB, 0xAC, 0xB4, 0xB5, 0x87, 0x88, 0x8F, 0x97, 0xB0, 0xB1 })
            {
                var coded = Ascii("a").Concat(new byte[] { 0x7F, code, 0x00 }).Concat(Ascii("b")).ToArray();
                Assert.Equal("ab", EventMessageDecoder.Decode(coded).ToPlainText());
            }
        }

        [Fact]
        public void Decode_LoneSetXAndSetYAreThreeBytes()
        {
            var x = EventMessageDecoder.Decode(new byte[] { 0x02, 0x50, 0x00, (byte)'T', (byte)'o' });
            Assert.Equal("To", x.ToPlainText());
            Assert.Null(x.Position);
            Assert.Equal("To", EventMessageDecoder.Decode(new byte[] { 0x03, 0x54, 0x01, (byte)'T', (byte)'o' }).ToPlainText());
        }

        [Fact]
        public void Decode_PluralAndIndexSelectorsFindTheirListAfterText()
        {
            // {0A 02} " " {7F 92 02} "credit[/s]" (Jeuno 9388): the list follows the noun.
            var raw = new byte[] { 0x0A, 0x02, (byte)' ', 0x7F, 0x92, 0x02 }.Concat(Ascii("credit[/s]: Level 60 offer")).ToArray();
            var message = EventMessageDecoder.Decode(raw);
            var selector = message.Segments.Single(s => s.Kind == EventMessageSegmentKind.Selector);
            Assert.Equal(0x92, selector.Code);
            Assert.Equal(2, selector.Argument);
            Assert.Equal(new[] { "", "s" }, selector.Alternatives);
            Assert.DoesNotContain(message.Segments, s => s.Kind == EventMessageSegmentKind.Text && s.Text.Contains('['));

            // {0C 01} " [days/hours/hour or less]": a space between the code and its list.
            var spaced = EventMessageDecoder.Decode(new byte[] { 0x0C, 0x01 }.Concat(Ascii(" [days/hours/hour or less] (Earth time).")).ToArray());
            Assert.Equal(new[] { "days", "hours", "hour or less" }, spaced.Segments.Single(s => s.Kind == EventMessageSegmentKind.Selector).Alternatives);

            // A list with no selector before it stays text; a selector does not reach past a line break.
            Assert.Equal("Status [Eff, yeah/Heck, no].", EventMessageDecoder.Decode(Ascii("Status [Eff, yeah/Heck, no].")).ToPlainText());
            var broken = EventMessageDecoder.Decode(new byte[] { 0x0C, 0x01, 0x07 }.Concat(Ascii("[a/b]")).ToArray());
            Assert.DoesNotContain(broken.Segments, s => s.Kind == EventMessageSegmentKind.Selector);
        }

        [Fact]
        public void Decode_ThePromptEndsTheString()
        {
            // 7F 31 00, then a second sub-string: retail shows only the first.
            var raw = Ascii("Halt!").Concat(new byte[] { 0x7F, 0x31, 0x00 }).Concat(Ascii("hidden")).ToArray();
            Assert.Equal("Halt!", EventMessageDecoder.Decode(raw).ToPlainText());
            foreach (byte code in new byte[] { 0x32, 0x33, 0x37 })
                Assert.True(EventMessageDecoder.Decode(Ascii("a").Concat(new byte[] { 0x7F, code }).ToArray()).HasPrompt);
        }

        [Fact]
        public void Decode_PausesCloseALineWithoutAPrompt()
        {
            // "Shhh! Be quiet!" 7F 36 01 00: no prompt, the line stays one second.
            var quiet = EventMessageDecoder.Decode(Ascii("Shhh! Be quiet!").Concat(new byte[] { 0x7F, 0x36, 0x01, 0x00 }).ToArray());
            Assert.False(quiet.HasPrompt);
            Assert.Equal(1, quiet.AutoCloseSeconds);
            Assert.Equal("Shhh! Be quiet!", quiet.ToPlainText());

            // "Canst thou..." 7F 36 02 07 "...hear me..." 7F 36 03 07 "...Promathia?" 7F 31: pauses inside a prompted line.
            var prompted = EventMessageDecoder.Decode(Ascii("Canst thou...").Concat(new byte[] { 0x7F, 0x36, 0x02, 0x07 })
                .Concat(Ascii("...hear me...")).Concat(new byte[] { 0x7F, 0x36, 0x03, 0x07 }).Concat(Ascii("...Promathia?"))
                .Concat(new byte[] { 0x7F, 0x31, 0x00 }).ToArray());
            Assert.True(prompted.HasPrompt);
            Assert.Null(prompted.AutoCloseSeconds);
            Assert.Equal(2, prompted.Segments.Count(s => s.Kind == EventMessageSegmentKind.Pause));
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
