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

        private static byte[] Tag(byte kind, params int[] parameters)
        {
            var bytes = new System.Collections.Generic.List<byte> { 0x01, (byte)(1 + 4 * parameters.Length), kind };
            foreach (int p in parameters) bytes.AddRange(new byte[] { 0x82, (byte)(0x80 | p), 0x80, 0x80 });
            return bytes.ToArray();
        }

        private static string Item(byte kind, int id) => id == 640
            ? kind switch { EventMessageFormatter.ItemKind => "Copper Ore", EventMessageFormatter.ItemLogNameKind => "chunk of copper ore", EventMessageFormatter.ItemPluralKind => "chunks of copper ore", _ => null! }
            : id == 4096
                ? kind switch { EventMessageFormatter.ItemKind => "Fire Crystal", EventMessageFormatter.ItemLogNameKind => "fire crystal", EventMessageFormatter.ItemPluralKind => "fire crystals", _ => null! }
                : kind switch { EventMessageFormatter.KeyItemKind => "traverser stone", EventMessageFormatter.KeyItemPluralKind => "traverser stones", EventMessageFormatter.ZoneKind => "Southern San d'Oria", _ => null! };

        [Fact]
        public void FormatLines_PluralSelectorPicksByOne()
        {
            // "Objective: " {0A 00} " " {7F 92 00} "[monster/monsters] of the hippogryph family."
            var raw = Ascii("Objective: ").Concat(new byte[] { 0x0A, 0x00, (byte)' ', 0x7F, 0x92, 0x00 })
                .Concat(Ascii("[monster/monsters] of the hippogryph family.")).ToArray();
            var message = EventMessageDecoder.Decode(raw);
            Assert.Equal("Objective: 1 monster of the hippogryph family.", Assert.Single(EventMessageFormatter.FormatLines(message, new SimpleMessageContext(new[] { 1 }))));
            Assert.Equal("Objective: 5 monsters of the hippogryph family.", Assert.Single(EventMessageFormatter.FormatLines(message, new SimpleMessageContext(new[] { 5 }))));
            Assert.Equal("Objective: 0 monsters of the hippogryph family.", Assert.Single(EventMessageFormatter.FormatLines(message, new SimpleMessageContext(new[] { 0 }))));

            // "...within " {0A 01} " minute" {7F 92 01} "[/s]."
            var minutes = EventMessageDecoder.Decode(Ascii("within ").Concat(new byte[] { 0x0A, 0x01 }).Concat(Ascii(" minute"))
                .Concat(new byte[] { 0x7F, 0x92, 0x01 }).Concat(Ascii("[/s].")).ToArray());
            Assert.Equal("within 3 minutes.", Assert.Single(EventMessageFormatter.FormatLines(minutes, new SimpleMessageContext(new[] { 0, 3 }))));
            Assert.Equal("within 1 minute.", Assert.Single(EventMessageFormatter.FormatLines(minutes, new SimpleMessageContext(new[] { 0, 1 }))));
        }

        [Fact]
        public void FormatLines_ItemTagForms()
        {
            // "You obtain " {01 05 03 p1} " " {01 09 29 p1 p0} "!" -- count from parameter 1, item from parameter 0.
            var counted = EventMessageDecoder.Decode(Ascii("You obtain ").Concat(Tag(0x03, 1)).Concat(Ascii(" ")).Concat(Tag(0x29, 1, 0)).Concat(Ascii("!")).ToArray());
            Assert.Equal("You obtain 12 fire crystals!", Assert.Single(EventMessageFormatter.FormatLines(counted, new SimpleMessageContext(new[] { 4096, 12 }, resolveName: Item))));
            Assert.Equal("You obtain 1 fire crystal!", Assert.Single(EventMessageFormatter.FormatLines(counted, new SimpleMessageContext(new[] { 4096, 1 }, resolveName: Item))));

            // "You dig up " {01 01 01} " " {01 05 24 p0} ", but your inventory is full." -- the article of the item after it.
            var dug = EventMessageDecoder.Decode(Ascii("You dig up ").Concat(new byte[] { 0x01, 0x01, 0x01 }).Concat(Ascii(" ")).Concat(Tag(0x24, 0))
                .Concat(Ascii(", but your inventory is full.")).ToArray());
            Assert.Equal("You dig up a chunk of copper ore, but your inventory is full.",
                Assert.Single(EventMessageFormatter.FormatLines(dug, new SimpleMessageContext(new[] { 640 }, resolveName: Item))));
            var crystal = EventMessageDecoder.Decode(new byte[] { 0x01, 0x01, 0x01, (byte)' ' }.Concat(Tag(0x24, 0)).ToArray());
            Assert.Equal("a fire crystal", Assert.Single(EventMessageFormatter.FormatLines(crystal, new SimpleMessageContext(new[] { 4096 }, resolveName: Item))));

            // "You cannot obtain the " {01 05 25 p0} "." -- plural log name; unresolved items keep the placeholder.
            var plural = EventMessageDecoder.Decode(Ascii("You cannot obtain the ").Concat(Tag(0x25, 0)).Concat(Ascii(".")).ToArray());
            Assert.Equal("You cannot obtain the chunks of copper ore.", Assert.Single(EventMessageFormatter.FormatLines(plural, new SimpleMessageContext(new[] { 640 }, resolveName: Item))));
            Assert.Equal("You cannot obtain the <%7>.", Assert.Single(EventMessageFormatter.FormatLines(plural, new SimpleMessageContext(new[] { 7 }))));
        }

        [Fact]
        public void FormatLines_KeyItemZoneAndCaseCodes()
        {
            // "Obtained key item: " {7F 80 01} {01 05 33 p0} "." -- 7F 80 01 capitalises the name.
            var keyItem = EventMessageDecoder.Decode(Ascii("Obtained key item: ").Concat(new byte[] { 0x7F, 0x80, 0x01 }).Concat(Tag(0x33, 0)).Concat(Ascii(".")).ToArray());
            Assert.Equal("Obtained key item: Traverser stone.", Assert.Single(EventMessageFormatter.FormatLines(keyItem, new SimpleMessageContext(new[] { 1 }, resolveName: Item))));

            // {01 05 36} key item, {01 05 35} key item plural, {01 05 37} zone; the capital applies to the next substitution only.
            var raw = new byte[] { 0x7F, 0x80, 0x01 }.Concat(Tag(0x35, 0)).Concat(Ascii(" and ")).Concat(Tag(0x36, 0)).Concat(Ascii(" in "))
                .Concat(Tag(0x37, 1)).ToArray();
            Assert.Equal("Traverser stones and traverser stone in Southern San d'Oria",
                Assert.Single(EventMessageFormatter.FormatLines(EventMessageDecoder.Decode(raw), new SimpleMessageContext(new[] { 1, 230 }, resolveName: Item))));

            // A kind that is not printable shows as hex when the name is not known: the weather tags 0x17 / 0x18.
            Assert.Equal("will be <17:4>", Assert.Single(EventMessageFormatter.FormatLines(
                EventMessageDecoder.Decode(Ascii("will be ").Concat(Tag(0x17, 0)).ToArray()), new SimpleMessageContext(new[] { 4 }))));
        }

        [Fact]
        public void FormatLines_WeatherTagsNameTheWeather()
        {
            // "will be " {01 05 17 p0} " with a chance of " {01 05 18 p1} (the weather reporters' forecast lines, #125).
            var raw = Ascii("will be ").Concat(Tag(0x17, 0)).Concat(Ascii(" with a chance of ")).Concat(Tag(0x18, 1)).ToArray();
            string? Weather(byte kind, int id) => kind == EventMessageFormatter.WeatherKind
                ? id switch { 6 => "rain", 10 => "winds", _ => null }
                : null;
            Assert.Equal("will be rain with a chance of winds", Assert.Single(EventMessageFormatter.FormatLines(
                EventMessageDecoder.Decode(raw), new SimpleMessageContext(new[] { 6, 10 }, resolveName: Weather))));
        }

        [Fact]
        public void FormatLines_NumberFormsAndDateFields()
        {
            // "until " {0A 00} ":" {7F 94 01}: a clock time; {7F 99 03} a four-digit number; hex and binary.
            var raw = Ascii("until ").Concat(new byte[] { 0x0A, 0x00 }).Concat(Ascii(":")).Concat(new byte[] { 0x7F, 0x94, 0x01 })
                .Concat(Ascii(" #")).Concat(new byte[] { 0x7F, 0x99, 0x02 }).Concat(Ascii(" 0x")).Concat(new byte[] { 0x7F, 0x95, 0x03 })
                .Concat(Ascii(" b")).Concat(new byte[] { 0x7F, 0x96, 0x04 }).ToArray();
            Assert.Equal("until 9:05 #0042 0xFF b101",
                Assert.Single(EventMessageFormatter.FormatLines(EventMessageDecoder.Decode(raw), new SimpleMessageContext(new[] { 9, 5, 42, 255, 5 }))));

            // The Mog Garden lease line (280:7538) LandSandBoat sends: {7F A1}/{7F A2}/{7F A0} at {7F A3}:{7F A9}:{7F AA},
            // seconds since 2001-12-31 15:00 UTC, in local time. Retail (2026-10-03, US Pacific, UTC-7) shows 781790400
            // (2026-10-10 12:00 JST) as "10/9/2026 at 20:00:00".
            var garden = new byte[] { 0x7F, 0xA1, 0x00, (byte)'/', 0x7F, 0xA2, 0x00, (byte)'/', 0x7F, 0xA0, 0x00 }.Concat(Ascii(" at "))
                .Concat(new byte[] { 0x7F, 0xA3, 0x00, (byte)':', 0x7F, 0xA9, 0x00, (byte)':', 0x7F, 0xAA, 0x00 }).ToArray();
            Assert.Equal("10/9/2026 at 20:00:00",
                Assert.Single(EventMessageFormatter.FormatLines(EventMessageDecoder.Decode(garden), new SimpleMessageContext(new[] { 781790400 }, timeZone: Pacific))));
            Assert.Equal("10/10/2026 at 12:00:00",
                Assert.Single(EventMessageFormatter.FormatLines(EventMessageDecoder.Decode(garden), new SimpleMessageContext(new[] { 781790400 }, timeZone: Japan))));

            // 2026-10-03 07:04:09 JST: the unpadded and two-digit fields.
            int seconds = (int)(new System.DateTime(2026, 10, 3, 7, 4, 9) - new System.DateTime(2002, 1, 1)).TotalSeconds;
            Assert.Equal("2026", EventMessageFormatter.FormatDateField(0xA0, seconds, Japan));
            Assert.Equal("10", EventMessageFormatter.FormatDateField(0xA1, seconds, Japan));
            Assert.Equal("3", EventMessageFormatter.FormatDateField(0xA2, seconds, Japan));
            Assert.Equal("7", EventMessageFormatter.FormatDateField(0xA3, seconds, Japan));
            Assert.Equal("4", EventMessageFormatter.FormatDateField(0xA4, seconds, Japan));
            Assert.Equal("9", EventMessageFormatter.FormatDateField(0xA5, seconds, Japan));
            Assert.Equal("10", EventMessageFormatter.FormatDateField(0xA6, seconds, Japan));
            Assert.Equal("03", EventMessageFormatter.FormatDateField(0xA7, seconds, Japan));
            Assert.Equal("07", EventMessageFormatter.FormatDateField(0xA8, seconds, Japan));
            Assert.Equal("04", EventMessageFormatter.FormatDateField(0xA9, seconds, Japan));
            Assert.Equal("09", EventMessageFormatter.FormatDateField(0xAA, seconds, Japan));
            // The same moment on the Pacific machine is the day before.
            Assert.Equal("2", EventMessageFormatter.FormatDateField(0xA2, seconds, Pacific));
            Assert.Equal("15", EventMessageFormatter.FormatDateField(0xA3, seconds, Pacific));
        }

        /// <summary>Fixed-offset zones, so the tests do not depend on the machine's zone or its zone database.</summary>
        internal static readonly System.TimeZoneInfo Pacific = System.TimeZoneInfo.CreateCustomTimeZone("Test UTC-7", System.TimeSpan.FromHours(-7), "UTC-7", "UTC-7");
        internal static readonly System.TimeZoneInfo Japan = System.TimeZoneInfo.CreateCustomTimeZone("Test UTC+9", System.TimeSpan.FromHours(9), "UTC+9", "UTC+9");

        [Fact]
        public void FormatLines_EntityAndStringCodes()
        {
            // {19 01}'s. (party member 1), {18 01} (the entity whose server id is parameter 1), {1C 00} (event string 0).
            var raw = new byte[] { 0x19, 0x01 }.Concat(Ascii("'s, ")).Concat(new byte[] { 0x18, 0x01 }).Concat(Ascii(", "))
                .Concat(new byte[] { 0x1C, 0x00 }).Concat(Ascii(", ")).Concat(new byte[] { 0x7F, 0x93 }).Concat(Ascii(".")).ToArray();
            var message = EventMessageDecoder.Decode(raw);
            var context = new StringContext(new[] { 0, 0x01234567 }, "Teodor");
            Assert.Equal("Member1's, Entity01234567, Teodor, .", Assert.Single(EventMessageFormatter.FormatLines(message, context)));
            // Without names the codes print nothing, as before.
            Assert.Equal("'s, , , .", Assert.Single(EventMessageFormatter.FormatLines(message, new SimpleMessageContext(new[] { 0, 5 }))));
        }

        private sealed class StringContext(int[] numbers, string eventString) : IEventMessageContext
        {
            public int GetNumber(int index) => index < numbers.Length ? numbers[index] : 0;
            public string PlayerName => "Cybin";
            public string NpcName => string.Empty;
            public string? GetEntityName(int index) => $"Member{index}";
            public string? ResolveName(byte kind, int id) => null;
            public string? GetEventString(int index) => index == 0 ? eventString : null;
            public string? GetEntityNameById(uint serverId) => $"Entity{serverId:X8}";
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
