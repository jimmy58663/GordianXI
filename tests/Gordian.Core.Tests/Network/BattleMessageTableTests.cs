// tests/Gordian.Core.Tests/Network/BattleMessageTableTests.cs
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// S2C 0x029 lines worded from the basic-message table (file 7027, #335). The synthetic messages copy the code layout
    /// of the retail table's messages (written by hand from the decoded bytes, not the file).
    /// </summary>
    public class BattleMessageTableTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private const uint Knot = 0x01000001, Rabbit = 0x01000002;

        private static string? Names(uint id) => id switch { Knot => "Knot", Rabbit => "Wild Rabbit", _ => null };

        private static bool IsMonster(uint id) => id == Rabbit;

        private static byte[] Bytes(params object[] parts)
        {
            var bytes = new List<byte>();
            foreach (var part in parts)
            {
                if (part is string text) bytes.AddRange(Encoding.ASCII.GetBytes(text));
                else bytes.AddRange((byte[])part);
            }
            return bytes.ToArray();
        }

        private static readonly byte[] Prompt = { 0x7F, 0x31, 0x00, 0x07 };
        private static byte[] Article(int entity) => new byte[] { 0x7F, 0x88, (byte)entity };
        private static byte[] Entity(int entity) => new byte[] { 0x01, 0x01, (byte)(0x10 + entity) };
        private static byte[] Verb(int entity) => new byte[] { 0x7F, 0x87, (byte)entity };
        private static byte[] Plural(int number) => new byte[] { 0x7F, 0x86, (byte)number };
        private static byte[] Number(int number) => new byte[] { 0x12, (byte)number };

        /// <summary>The layouts of messages 1, 7, 38, 44, 53 and 565.</summary>
        private static readonly Dictionary<int, byte[]> Table = new()
        {
            [1] = Bytes(Article(0), "[The /]", Entity(0), " ", Verb(0), "[hits/hit] ", Article(1), "[the /]", Entity(1), " for ",
                Number(1), " point", Plural(1), "[/s] of damage.", Prompt),
            [7] = Bytes(Article(0), "[The /]", Entity(0), " ", Verb(0), "[casts/cast] ", new byte[] { 0x10, 0x00 }, ".", new byte[] { 0x07 },
                Article(1), "[The /]", Entity(1), " ", Verb(1), "[recovers/recover] ", Number(1), " HP.", Prompt),
            [38] = Bytes(Article(1), "[The /]", Entity(1), Verb(1), "['s/'] ", new byte[] { 0x05, 0x00 }, " skill rises ",
                new byte[] { 0x7F, 0x9B, 0x01 }, " points.", Prompt),
            [44] = Bytes(Article(1), "[The /]", Entity(1), Verb(1), "['s/'] spikes deal ", Number(3), " point", Plural(3),
                "[/s] of damage to ", Article(0), "[the /]", Entity(0), ".", Prompt),
            [53] = Bytes(Article(1), "[The /]", Entity(1), Verb(1), "['s/'] ", new byte[] { 0x05, 0x00 }, " skill reaches level ",
                Number(1), ".", Prompt),
            [174] = Bytes("from the table", Prompt),
            [565] = Bytes(Entity(1), " obtains ", new byte[] { 0x7F, 0xB4, 0x00 }, ".", Prompt),
        };

        private static Gordian.Core.Resources.Tables.EventMessage? Lookup(int id) =>
            Table.TryGetValue(id, out var raw) ? EventMessageDecoder.Decode(raw) : null;

        private static string Format(ushort message, uint caster, uint target, uint param, uint value) =>
            CombatLogFormatter.FormatBattleMessage(
                new CombatMessageRecord { MessageId = message, CasterId = caster, TargetId = target, Param = param, Value = value },
                Names, entityTakesArticle: IsMonster, battleMessages: Lookup);

        [Fact]
        public void SkillUp_UsesTheTableWithTheSkillNameAndTenths()
        {
            Assert.Equal("Knot's bonecraft skill rises 0.1 points.", Format(38, Knot, Knot, 54, 1));
            Assert.Equal("Knot's sword skill rises 2.5 points.", Format(38, Knot, Knot, 3, 25));
            Assert.Equal("Knot's sword skill reaches level 12.", Format(53, Knot, Knot, 3, 12));
        }

        [Fact]
        public void Hit_PutsTheBeforeAMonsterAndPicksThePlural()
        {
            Assert.Equal("The Wild Rabbit hits Knot for 1 point of damage.", Format(1, Rabbit, Knot, 0, 1));
            Assert.Equal("Knot hits the Wild Rabbit for 12 points of damage.", Format(1, Knot, Rabbit, 0, 12));
        }

        [Fact]
        public void MultiLineMessage_NamesTheSpellAndJoinsLines()
        {
            Assert.Equal("Knot casts Cure.\nThe Wild Rabbit recovers 30 HP.", Format(7, Knot, Rabbit, 1, 30));
        }

        [Fact]
        public void Gil_PrintsTheAmountWithTheWord()
        {
            Assert.Equal("Knot obtains 300 gil.", Format(565, Knot, Knot, 300, 0));
        }

        [Fact]
        public void FallsBackToTheHandWrittenText()
        {
            // A number the packet does not carry (index 3), a message the table lacks, and the monster check.
            Assert.Equal("Knot's spikes deal 5 damage to Wild Rabbit.", Format(44, Rabbit, Knot, 5, 0));
            Assert.Equal("Knot defeats Wild Rabbit.", Format(6, Knot, Rabbit, 0, 0));
            Assert.Equal("The Wild Rabbit seems to be level 40 (EM).", Format(174, Knot, Rabbit, 40, 68));
        }

        [Fact]
        public void Decoder_ReadsTheBasicMessageCodes()
        {
            var message = EventMessageDecoder.Decode(Table[38]);
            Assert.Contains(message.Segments, s => s.Kind == EventMessageSegmentKind.ActionName && s.Code == 0x05 && s.Argument == 0);
            Assert.Contains(message.Segments, s => s.Kind == EventMessageSegmentKind.Number && s.Code == 0x9B && s.Argument == 1);
            Assert.Contains(message.Segments, s => s.Kind == EventMessageSegmentKind.Selector && s.Code == 0x87);
            Assert.DoesNotContain(message.Segments, s => s.Kind == EventMessageSegmentKind.Name && s.Code != 0x11);

            // 0x7F 0xB7 takes an argument byte: the text after it stays text.
            var attribute = EventMessageDecoder.Decode(Bytes(new byte[] { 0x7F, 0xB7, 0x00 }, " attribute", Prompt));
            Assert.Equal("attribute", attribute.ToPlainText());
            // 0x7F 0x8F n: a job ability name.
            var ability = EventMessageDecoder.Decode(Bytes("uses ", new byte[] { 0x7F, 0x8F, 0x00 }, ".", Prompt));
            Assert.Contains(ability.Segments, s => s.Kind == EventMessageSegmentKind.ActionName && s.Code == 0x8F);
        }

        /// <summary>
        /// The retail table (ROM/27/72): message 38 against the retail wording "Gemini's bonecraft skill rises 0.1 points."
        /// (capture 2026-10-07), and a monster's hit. Skipped without the game install.
        /// </summary>
        [Fact]
        public void RetailTable_WordsSkillUpsAndHitsLikeRetail()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            // File id 7027 resolves to ROM/27/72 (checked against the file table 2026-10-10).
            Assert.Equal(rm.LoadDatBytes(Path.Combine("ROM", "27", "72.DAT")), rm.LoadDatBytesByFileId(ClientMessageTables.BattleMessagesFileId));
            var tables = new ClientMessageTables(rm.LoadDatBytesByFileId);
            var table = tables.BattleMessages;
            Assert.NotNull(table);
            Assert.Equal(1024, table!.Count);

            string Retail(ushort message, uint caster, uint target, uint param, uint value) =>
                CombatLogFormatter.FormatBattleMessage(
                    new CombatMessageRecord { MessageId = message, CasterId = caster, TargetId = target, Param = param, Value = value },
                    id => id == Knot ? "Gemini" : Names(id), entityTakesArticle: IsMonster, battleMessages: table.GetMessage);

            Assert.Equal("Gemini's bonecraft skill rises 0.1 points.", Retail(38, Knot, Knot, 54, 1));
            Assert.Equal("Gemini's bonecraft skill reaches level 5.", Retail(53, Knot, Knot, 54, 5));
            Assert.Equal("The Wild Rabbit hits Gemini for 3 points of damage.", Retail(1, Rabbit, Knot, 0, 3));
            Assert.Equal("Gemini defeats the Wild Rabbit.", Retail(6, Knot, Rabbit, 0, 0));
            Assert.Equal("Gemini obtains 120 gil.", Retail(565, Knot, Knot, 120, 0));
        }
    }
}
