// tests/Gordian.Core.Tests/Events/EventMessageFormatterTests.cs
using System.Linq;
using System.Text;
using Gordian.Core.Events;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Events
{
    public class EventMessageFormatterTests
    {
        private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

        [Fact]
        public void FormatLines_SubstitutesNumbersNamesAndSelectors()
        {
            // "Obtained: " 0x01 0x05 '#' 0x82 0x80 0x80 0x80 "." 0x07 "You have " 0x0A 0x01 " gil " 0x0C 0x02 "[left/right]" "." 0x7F 0x31 0x00
            var raw = Ascii("Obtained: ").Concat(new byte[] { 0x01, 0x05, (byte)'#', 0x82, 0x80, 0x80, 0x80 }).Concat(Ascii("."))
                .Concat(new byte[] { 0x07 }).Concat(Ascii("You have ")).Concat(new byte[] { 0x0A, 0x01 }).Concat(Ascii(" gil "))
                .Concat(new byte[] { 0x0C, 0x02 }).Concat(Ascii("[left/right].")).Concat(new byte[] { 0x7F, 0x31, 0x00 }).ToArray();
            var message = EventMessageDecoder.Decode(raw);
            var context = new SimpleMessageContext(new[] { 4096, 250, 1 }, "Player", "Npc",
                (kind, id) => kind == EventMessageFormatter.ItemKind && id == 4096 ? "Fire Crystal" : null);

            var lines = EventMessageFormatter.FormatLines(message, context);
            Assert.Equal(new[] { "Obtained: Fire Crystal.", "You have 250 gil right." }, lines);
        }

        [Fact]
        public void FormatQuery_SplitsCommentsFromChoices()
        {
            var raw = Ascii("What will you do?").Concat(new byte[] { 0x07, 0x0B }).Concat(Ascii("Travel.")).Concat(new byte[] { 0x07 })
                .Concat(Ascii("Set.")).Concat(new byte[] { 0x07 }).Concat(Ascii("Never mind.")).Concat(new byte[] { 0x7F, 0x31, 0x00 }).ToArray();
            var message = EventMessageDecoder.Decode(raw);
            var (comments, choices) = EventMessageFormatter.FormatQuery(message, new SimpleMessageContext());
            Assert.Equal(new[] { "What will you do?" }, comments);
            Assert.Equal(new[] { "Travel.", "Set.", "Never mind." }, choices);
        }

        [Fact]
        public void FormatLines_UsesPlayerAndNpcNames_AndPlaceholdersForUnknownNames()
        {
            var raw = new byte[] { 0x08 }.Concat(Ascii(", says ")).Concat(new byte[] { 0x09 }).Concat(Ascii(" of "))
                .Concat(new byte[] { 0x01, 0x05, (byte)'8', 0x82, 0x80, 0x80, 0x80 }).ToArray();
            var message = EventMessageDecoder.Decode(raw);
            var lines = EventMessageFormatter.FormatLines(message, new SimpleMessageContext(new[] { 230 }, "Tarudrake", "Ailevia"));
            Assert.Equal(new[] { "Tarudrake, says Ailevia of <8230>" }, lines);
        }

        [Fact]
        public void FormatLines_DropsTheEmptyLineAfterAFinalBreak()
        {
            var raw = Ascii("Home point set!").Concat(new byte[] { 0x07, 0x7F, 0x31, 0x00 }).ToArray();
            var lines = EventMessageFormatter.FormatLines(EventMessageDecoder.Decode(raw), new SimpleMessageContext());
            Assert.Equal(new[] { "Home point set!" }, lines);
        }
    }
}
