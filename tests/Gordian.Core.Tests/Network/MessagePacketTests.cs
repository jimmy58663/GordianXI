// tests/Gordian.Core.Tests/Network/MessagePacketTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Animation;
using Gordian.Core.Config;
using Gordian.Core.Events;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// The message and event-parameter packets of #110: S2C 0x027, 0x03B, 0x043 (zone dialog messages), 0x053 (system
    /// messages), 0x058 (assist), 0x05A (emote echo), 0x05C / 0x05D (event parameters), 0x0CA (bazaar message) and C2S
    /// 0x0DE. Decoders laid out as LandSandBoat sends them, the modules' state, and the routing to the log, the event VM
    /// and the target.
    /// </summary>
    public class MessagePacketTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const uint LocalId = 0x00012345;
        private const ushort LocalIndex = 0x0400;
        private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);

        // ---- helpers ----

        /// <summary>Concatenates text (ASCII) and raw code bytes into one message.</summary>
        private static byte[] Msg(params object[] parts)
        {
            var bytes = new List<byte>();
            foreach (var part in parts)
            {
                if (part is string s) bytes.AddRange(Encoding.ASCII.GetBytes(s));
                else if (part is byte[] raw) bytes.AddRange(raw);
                else if (part is int b) bytes.Add((byte)b);
                else if (part is byte c) bytes.Add(c);
            }
            return bytes.ToArray();
        }

        /// <summary>A file in the zone dialog table format: the 0x10 header, the body XOR 0x80, the offset table.</summary>
        private static byte[] DialogTable(params byte[][] messages)
        {
            var body = new List<byte>();
            int tableSize = messages.Length * 4;
            int offset = tableSize;
            foreach (var message in messages)
            {
                body.AddRange(BitConverter.GetBytes(offset));
                offset += message.Length;
            }
            foreach (var message in messages) body.AddRange(message);
            var file = new byte[4 + body.Count];
            BinaryPrimitives.WriteUInt32LittleEndian(file, 0x10000000u | (uint)body.Count);
            for (int i = 0; i < body.Count; i++) file[4 + i] = (byte)(body[i] ^ 0x80);
            return file;
        }

        /// <summary>A table whose messages are empty except the ones given.</summary>
        private static byte[] SparseTable(int count, Dictionary<int, byte[]> messages)
        {
            var all = new byte[count][];
            for (int i = 0; i < count; i++) all[i] = messages.TryGetValue(i, out var m) ? m : new byte[] { 0x7F, 0x31, 0x00, 0x07 };
            return DialogTable(all);
        }

        private static readonly byte[] Prompt = { 0x7F, 0x31, 0x00, 0x07 };
        private static readonly byte[] Caster = { 0x7F, 0xFC, 0x01, 0x01, 0x10, 0x7F, 0xFB };
        private static readonly byte[] TargetName = { 0x7F, 0x88, 0x01, (byte)'[', (byte)'t', (byte)'h', (byte)'e', (byte)' ', (byte)'/', (byte)']', 0x01, 0x01, 0x11 };
        private static readonly byte[] HisHer = { 0x7F, 0x90, (byte)'[', (byte)'h', (byte)'i', (byte)'s', (byte)'/', (byte)'h', (byte)'e', (byte)'r', (byte)']' };

        /// <summary>The emote lines of the retail table (ROM/27/70) for /point (0), /wave (8) and /clap (13).</summary>
        private static byte[] EmoteTable() => SparseTable(30, new Dictionary<int, byte[]>
        {
            [0] = Msg(Caster, " points at ", TargetName, ".", Prompt),
            [1] = Msg(Caster, " points ", 0x1D, ".", Prompt),
            [16] = Msg(Caster, " waves to ", TargetName, ".", Prompt),
            [17] = Msg(Caster, " waves.", Prompt),
            [27] = Msg(Caster, " claps ", HisHer, " hands.", Prompt),
        });

        /// <summary>System messages 7 (logout countdown, two lines) and 10 (the 0x7F 0x86 plural) of the retail table.</summary>
        private static byte[] SystemTable() => SparseTable(12, new Dictionary<int, byte[]>
        {
            [7] = Msg(0x1F, 0x88, "Executing logout in ", 0x12, 0x00, " seconds.", 0x07, "Cancel healing to remain logged in.", Prompt),
            [10] = Msg(0x1F, (byte)'y', 0x12, 0x00, " ", 0x7F, 0x86, 0x00, "[second/seconds] until position reset.", Prompt),
        });

        private static ClientMessageTables Tables() => new(id => id switch
        {
            ClientMessageTables.EmoteMessagesFileId => EmoteTable(),
            ClientMessageTables.SystemMessagesFileId => SystemTable(),
            _ => null,
        });

        private static PacketParser NewParser(out PacketDispatcher dispatcher, List<byte[]>? sent = null)
        {
            dispatcher = new PacketDispatcher();
            var parser = new PacketParser(new SessionProfile(), (data, _) =>
            {
                sent?.Add(data.ToArray());
                return Task.CompletedTask;
            }, dispatcher: dispatcher);
            parser.LocalPlayer.ServerId = LocalId;
            return parser;
        }

        private static void Receive(PacketDispatcher dispatcher, ushort id, byte[] payload) =>
            Assert.True(dispatcher.Dispatch(new PacketHeader(id, payload.Length + 4, 1), payload));

        private static List<(ChatLogChannel Channel, string Text)> Lines(StockUiChat chat)
        {
            var lines = new List<(ChatLogChannel, string)>();
            chat.Log.LineAdded += line => lines.Add((line.Channel, line.Text));
            return lines;
        }

        private static byte[] MotionMes(uint caster, ushort casterIndex, uint target, ushort targetIndex, ushort emote, ushort param, EmoteMode mode)
        {
            var p = new byte[52];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0), caster);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), target);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(8), casterIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(10), targetIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(12), emote);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(14), param);
            p[18] = (byte)mode;
            return p;
        }

        private static byte[] SystemMes(ushort id, uint para, uint para2)
        {
            var p = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0), para);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), para2);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(8), id);
            return p;
        }

        private static WorldEntity Player(uint id, ushort index, string name, byte race)
        {
            var entity = new PlayerEntity(id, index) { Name = name };
            entity.Appearance.GrapIdTable[0] = (ushort)(race << 8);
            return entity;
        }

        // ---- decoders ----

        [Fact]
        public void S2C_0x027_TalkNumWork2_DecodesNumbersAndStrings()
        {
            var p = new byte[108];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0), LocalId);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4), LocalIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(6), 0x8000 | 7012);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(8), 4);
            p[10] = 2;
            for (int i = 0; i < 4; i++) BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(12 + i * 4), 100 + i);
            Encoding.ASCII.GetBytes("Cybin").CopyTo(p, 28);
            Encoding.ASCII.GetBytes("Override").CopyTo(p, 60);
            for (int i = 0; i < 8; i++) BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(76 + i * 4), 200 + i);

            var talk = new S2C_0x027_TalkNumWork2(p);

            Assert.True(talk.IsValid);
            Assert.Equal(LocalId, talk.UniqueNo);
            Assert.Equal(LocalIndex, talk.ActIndex);
            Assert.Equal(7012, talk.MessageId);
            Assert.True(talk.HideName);
            Assert.Equal(4, talk.Type);
            Assert.Equal(2, talk.Flags);
            var numbers = new int[12];
            for (int i = 0; i < numbers.Length; i++) numbers[i] = talk.GetNumber(i);
            Assert.Equal(new[] { 100, 101, 102, 103, 200, 201, 202, 203, 204, 205, 206, 207 }, numbers);
            Assert.Equal("Cybin", talk.GetString1());
            Assert.Equal("Override", talk.GetString2());
            Assert.False(new S2C_0x027_TalkNumWork2(new byte[27]).IsValid);
            // A short packet (no Num2) still decodes; the missing numbers read 0.
            Assert.Equal(0, new S2C_0x027_TalkNumWork2(p.AsSpan(0, 60)).GetNumber(4));
        }

        [Fact]
        public void S2C_0x03B_EventMes_SplitsTheNameBit()
        {
            var p = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0), 0x010E6001);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(6), 0x8000 | 42);

            var mes = new S2C_0x03B_EventMes(p);

            Assert.True(mes.IsValid);
            Assert.Equal(0x010E6001u, mes.UniqueNo);
            Assert.Equal(42, mes.MessageId);
            Assert.True(mes.UsesName);
            Assert.False(new S2C_0x03B_EventMes(new byte[7]).IsValid);
        }

        [Fact]
        public void S2C_0x043_TalkNumName_DecodesTheName()
        {
            var p = new byte[28];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0), LocalId);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4), LocalIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(6), 0x8000 | 300);
            p[8] = 3;
            Encoding.ASCII.GetBytes("Cybin").CopyTo(p, 12);

            var talk = new S2C_0x043_TalkNumName(p);

            Assert.True(talk.IsValid);
            Assert.Equal(300, talk.MessageId);
            Assert.True(talk.HideName);
            Assert.Equal(3, talk.Type);
            Assert.Equal("Cybin", talk.GetName());
        }

        [Fact]
        public void S2C_0x05C_PendingNum_ReadsEightSignedNumbers()
        {
            var p = new byte[32];
            for (int i = 0; i < 8; i++) BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(i * 4), i == 1 ? -1 : i * 10);

            var pending = new S2C_0x05C_PendingNum(p);

            Assert.True(pending.IsValid);
            var numbers = new int[8];
            for (int i = 0; i < numbers.Length; i++) numbers[i] = pending.GetParameter(i);
            Assert.Equal(new[] { 0, -1, 20, 30, 40, 50, 60, 70 }, numbers);
            Assert.False(new S2C_0x05C_PendingNum(new byte[31]).IsValid);
        }

        [Fact]
        public void S2C_0x05D_PendingStr_ReadsTheFourStringsAfterNineNumbers()
        {
            var p = new byte[100];
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0), 7);
            string[] expected = { "Shantotto", "", "Golem", "0123456789ABCDEF" };
            for (int i = 0; i < 4; i++) Encoding.ASCII.GetBytes(expected[i]).CopyTo(p, 36 + i * 16);

            var pending = new S2C_0x05D_PendingStr(p);

            Assert.True(pending.IsValid);
            Assert.Equal(7, pending.GetNumber(0));
            // A full 16-byte field has no terminator and is read whole.
            var strings = new string[4];
            for (int i = 0; i < strings.Length; i++) strings[i] = pending.GetString(i);
            Assert.Equal(expected, strings);
            Assert.False(new S2C_0x05D_PendingStr(new byte[99]).IsValid);
        }

        [Fact]
        public void S2C_0x053_SystemMes_DecodesParametersAndNumber()
        {
            var mes = new S2C_0x053_SystemMes(SystemMes(239, 12, 34));
            Assert.True(mes.IsValid);
            Assert.Equal(239, mes.MessageId);
            Assert.Equal(12u, mes.Para);
            Assert.Equal(34u, mes.Para2);
            Assert.False(new S2C_0x053_SystemMes(new byte[9]).IsValid);
        }

        [Fact]
        public void S2C_0x05A_MotionMes_DecodesCasterTargetEmoteAndMode()
        {
            var p = MotionMes(LocalId, LocalIndex, 0x010E6001, 1, (ushort)EmoteId.Salute, 2, EmoteMode.Text);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(24), 0x01000777); // second Trust

            var motion = new S2C_0x05A_MotionMes(p);

            Assert.True(motion.IsValid);
            Assert.Equal(LocalId, motion.CasterId);
            Assert.Equal(LocalIndex, motion.CasterIndex);
            Assert.Equal(0x010E6001u, motion.TargetId);
            Assert.Equal(1, motion.TargetIndex);
            Assert.Equal((ushort)EmoteId.Salute, motion.EmoteId);
            Assert.Equal(2, motion.Param);
            Assert.Equal(EmoteMode.Text, motion.Mode);
            Assert.Equal(0x01000777u, motion.GetFaithId(1));
            Assert.Equal(0u, motion.GetFaithId(0));
            Assert.False(new S2C_0x05A_MotionMes(new byte[18]).IsValid);
        }

        [Fact]
        public void S2C_0x0CA_InspectMessage_SplitsTheThreeLinesAndFlags()
        {
            var p = new byte[144];
            Array.Fill(p, (byte)' ', 0, 123);
            Encoding.ASCII.GetBytes("Selling crystals").CopyTo(p, 0);
            Encoding.ASCII.GetBytes("Cheap!").CopyTo(p, 40);
            p[123] = 0x01 | 0x02 | (7 << 2);
            Encoding.ASCII.GetBytes("Cybin").CopyTo(p, 124);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(140), 225);

            var inspect = new S2C_0x0CA_InspectMessage(p);

            Assert.True(inspect.IsValid);
            Assert.True(inspect.HasBazaar);
            Assert.True(inspect.IsSelf);
            Assert.Equal(7, inspect.Race);
            Assert.Equal("Cybin", inspect.Name);
            Assert.Equal(225u, inspect.TitleId);
            Assert.Equal("Selling crystals\nCheap!", inspect.Message);
            Assert.False(new S2C_0x0CA_InspectMessage(new byte[143]).IsValid);
        }

        [Fact]
        public void S2C_0x0CA_InspectMessage_StopsAtANul()
        {
            // LandSandBoat copies the stored message without padding: the rest of the field is zero.
            var p = new byte[144];
            Encoding.ASCII.GetBytes("Hi").CopyTo(p, 0);
            Assert.Equal("Hi", new S2C_0x0CA_InspectMessage(p).Message);
        }

        [Fact]
        public void S2C_0x058_Assist_DecodesPlayerAndTarget()
        {
            var p = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0), LocalId);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), 0x01001234);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(8), LocalIndex);

            var assist = new S2C_0x058_Assist(p);

            Assert.True(assist.IsValid);
            Assert.Equal(LocalId, assist.PlayerId);
            Assert.Equal(0x01001234u, assist.TargetId);
            Assert.Equal(LocalIndex, assist.PlayerIndex);
            Assert.False(new S2C_0x058_Assist(new byte[9]).IsValid);
        }

        [Fact]
        public void C2S_0x0DE_InspectMessage_Is128BytesPaddedWithSpaces()
        {
            byte[] packet = PlayerCommandPacketBuilder.BuildInspectMessage("Selling crystals\nCheap!", 5);

            Assert.Equal(128, packet.Length);
            Assert.True(PacketHeader.TryParse(packet, out var header));
            Assert.Equal(0x0DE, header.PacketId);
            Assert.Equal(128, header.TotalSize);
            Assert.Equal(5, header.SequenceId);
            Assert.Equal("Selling crystals".PadRight(40) + "Cheap!".PadRight(40) + new string(' ', 43), Encoding.ASCII.GetString(packet, 4, 123));
            Assert.Equal(0, packet[127]);

            // One long line runs on into the next lines, as the buffer is one array.
            string longLine = new string('x', 100);
            Assert.Equal(longLine.PadRight(123), Encoding.ASCII.GetString(PlayerCommandPacketBuilder.BuildInspectMessage(longLine), 4, 123));
        }

        // ---- modules and state ----

        [Fact]
        public void ProgressionModule_TalkNumWork2_PostsNumbersStringsAndSpeaker()
        {
            var parser = NewParser(out var dispatcher);
            var received = new List<DialogMessageInfo>();
            parser.Progression.DialogMessageReceived += received.Add;

            // LandSandBoat's fishing line: the no-name bit, the item and count, the player's name in String1.
            var p = new byte[108];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0), LocalId);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(6), 0x8000 | 7020);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(12), 4401);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(16), 1);
            Encoding.ASCII.GetBytes("Cybin").CopyTo(p, 28);
            Receive(dispatcher, 0x027, p);

            // A message with a speaker: String1.
            var q = (byte[])p.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(q.AsSpan(0), 0x010E6001);
            BinaryPrimitives.WriteUInt16LittleEndian(q.AsSpan(6), 7021);
            Array.Clear(q, 28, 32);
            Encoding.ASCII.GetBytes("Moogle").CopyTo(q, 28);
            Receive(dispatcher, 0x027, q);

            Assert.Equal(2, received.Count);
            Assert.Equal(7020, received[0].MessageId);
            Assert.True(received[0].HideName);
            Assert.Equal(4401, received[0].Numbers[0]);
            Assert.Equal(1, received[0].Numbers[1]);
            Assert.Equal(12, received[0].Numbers.Length);
            Assert.Equal(new[] { "Cybin", "" }, received[0].Strings);
            Assert.False(received[1].HideName);
            Assert.Equal("Moogle", received[1].Name);
        }

        [Fact]
        public void ProgressionModule_TalkNumNameAndEventMes_PostDialogMessages()
        {
            var parser = NewParser(out var dispatcher);
            var received = new List<DialogMessageInfo>();
            parser.Progression.DialogMessageReceived += received.Add;

            var name = new byte[28];
            BinaryPrimitives.WriteUInt32LittleEndian(name.AsSpan(0), LocalId);
            BinaryPrimitives.WriteUInt16LittleEndian(name.AsSpan(6), 0x8000 | 55);
            Encoding.ASCII.GetBytes("Cybin").CopyTo(name, 12);
            Receive(dispatcher, 0x043, name);

            var mes = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(mes.AsSpan(0), 0x010E6001);
            BinaryPrimitives.WriteUInt16LittleEndian(mes.AsSpan(6), 0x8000 | 56);
            Receive(dispatcher, 0x03B, mes);

            Assert.Equal(55, received[0].MessageId);
            Assert.True(received[0].HideName);
            Assert.Equal(new[] { "Cybin" }, received[0].Strings);
            Assert.Equal(56, received[1].MessageId);
            Assert.False(received[1].HideName); // 0x03B's bit 15 asks for the name
            Assert.Null(received[1].Strings);
        }

        [Fact]
        public void ProgressionModule_PendingNumAndStr_UpdateTheRunningEvent()
        {
            var parser = NewParser(out var dispatcher);
            parser.Progression.StartEvent(0x010E6001, 1, 0, 100, 0, new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, new[] { "a", "b", "c", "d" });
            int[]? numbers = null;
            string[]? strings = null;
            parser.Progression.EventNumbersUpdated += n => numbers = n;
            parser.Progression.EventStringsUpdated += s => strings = s;

            var num = new byte[32];
            for (int i = 0; i < 8; i++) BinaryPrimitives.WriteInt32LittleEndian(num.AsSpan(i * 4), 10 + i);
            Receive(dispatcher, 0x05C, num);
            var str = new byte[100];
            Encoding.ASCII.GetBytes("Tateeya").CopyTo(str, 36);
            Receive(dispatcher, 0x05D, str);

            Assert.Equal(Enumerable.Range(10, 8), numbers!);
            Assert.Equal(new[] { "Tateeya", "", "", "" }, strings!);
            Assert.Equal(Enumerable.Range(10, 8), parser.Progression.ActiveEvent!.NumericParams);
            Assert.Equal(new[] { "Tateeya", "", "", "" }, parser.Progression.ActiveEvent.StringParams);
        }

        [Fact]
        public void CommandModule_SystemMesMotionMesAndInspect_ReachTheState()
        {
            var parser = NewParser(out var dispatcher);
            var emotes = new List<EmoteEcho>();
            var system = new List<SystemMessageInfo>();
            parser.Commands.EmotePerformed += emotes.Add;
            parser.Commands.SystemMessageReceived += system.Add;

            Receive(dispatcher, 0x053, SystemMes(7, 30, 0));
            Receive(dispatcher, 0x05A, MotionMes(LocalId, LocalIndex, 0, 0, (ushort)EmoteId.Wave, 0, EmoteMode.All));
            var inspect = new byte[144];
            inspect[123] = 0x02;
            Encoding.ASCII.GetBytes("Cybin").CopyTo(inspect, 124);
            Receive(dispatcher, 0x0CA, inspect);

            Assert.Equal(new SystemMessageInfo(7, 30, 0), Assert.Single(system));
            var emote = Assert.Single(emotes);
            Assert.Equal((ushort)EmoteId.Wave, emote.EmoteId);
            Assert.False(emote.HasTarget);
            Assert.True(emote.ShowsText && emote.PlaysMotion);
            Assert.Equal(emote, parser.Commands.LastEmote);
            Assert.Equal("Cybin", parser.Commands.Inspect.Own!.Name);
            Assert.Same(parser.Commands.Inspect.Own, parser.Commands.Inspect.Last);
        }

        [Fact]
        public void Assist_SelectsTheTargetTheServerPicked()
        {
            var parser = NewParser(out var dispatcher);
            const uint Goblin = 0x01001234;
            parser.World.UpsertEntity(new WorldEntity(Goblin, 0x234, EntityType.Monster) { Name = "Goblin Thug" });
            var p = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0), LocalId);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), Goblin);

            Receive(dispatcher, 0x058, p);

            Assert.Equal(Goblin, parser.Combat.AssistTargetServerId);
            Assert.Equal(Goblin, parser.ActionService.CurrentTarget?.ServerId);

            // Another character's packet, and "no target", change nothing.
            var other = (byte[])p.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(other.AsSpan(0), 0x00099999);
            BinaryPrimitives.WriteUInt32LittleEndian(other.AsSpan(4), 0x01005678);
            Receive(dispatcher, 0x058, other);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), 0);
            Receive(dispatcher, 0x058, p);
            Assert.Equal(Goblin, parser.ActionService.CurrentTarget?.ServerId);
        }

        // ---- the log lines ----

        private static (ClientMessageController Controller, PacketParser Parser, PacketDispatcher Dispatcher, List<(ChatLogChannel Channel, string Text)> Lines) Messages()
        {
            var parser = NewParser(out var dispatcher);
            var chat = new StockUiChat();
            var lines = Lines(chat);
            var controller = new ClientMessageController(Tables(), _ => null);
            controller.Attach(parser.Commands, parser.World, parser.LocalPlayer, chat, () => "Cybin");
            parser.World.UpsertEntity(Player(LocalId, LocalIndex, "Cybin", 7));
            return (controller, parser, dispatcher, lines);
        }

        [Fact]
        public void Emote_OwnEcho_PrintsTheEmoteLine()
        {
            var (_, _, dispatcher, lines) = Messages();

            Receive(dispatcher, 0x05A, MotionMes(LocalId, LocalIndex, 0, 0, (ushort)EmoteId.Wave, 0, EmoteMode.All));

            Assert.Equal((ChatLogChannel.Emote, "Cybin waves."), Assert.Single(lines));
        }

        [Fact]
        public void Emote_AtAMonster_TakesTheArticle_AtAPlayerNot()
        {
            var (_, parser, dispatcher, lines) = Messages();
            parser.World.UpsertEntity(new WorldEntity(0x01001234, 0x234, EntityType.Monster) { Name = "Goblin Thug" });
            parser.World.UpsertEntity(Player(0x00054321, 0x401, "Shantotto", 6));

            Receive(dispatcher, 0x05A, MotionMes(LocalId, LocalIndex, 0x01001234, 0x234, (ushort)EmoteId.Wave, 0, EmoteMode.All));
            Receive(dispatcher, 0x05A, MotionMes(0x00054321, 0x401, LocalId, LocalIndex, (ushort)EmoteId.Point, 0, EmoteMode.Text));

            Assert.Equal(new[] { "Cybin waves to the Goblin Thug.", "Shantotto points at Cybin." }, lines.Select(l => l.Text));
        }

        [Fact]
        public void Emote_PicksTheCastersSex_AndThePointHeading()
        {
            var (_, parser, dispatcher, lines) = Messages();
            var galka = Player(0x00054321, 0x401, "Zeid", 8);
            galka.Direction = 192; // north
            parser.World.UpsertEntity(galka);

            Receive(dispatcher, 0x05A, MotionMes(LocalId, LocalIndex, 0, 0, (ushort)EmoteId.Clap, 0, EmoteMode.All));
            Receive(dispatcher, 0x05A, MotionMes(0x00054321, 0x401, 0, 0, (ushort)EmoteId.Clap, 0, EmoteMode.All));
            Receive(dispatcher, 0x05A, MotionMes(0x00054321, 0x401, 0, 0, (ushort)EmoteId.Point, 0, EmoteMode.All));

            Assert.Equal(new[] { "Cybin claps her hands.", "Zeid claps his hands.", "Zeid points north." }, lines.Select(l => l.Text));
        }

        [Fact]
        public void Emote_MotionOnly_PrintsNothing()
        {
            var (_, _, dispatcher, lines) = Messages();
            Receive(dispatcher, 0x05A, MotionMes(LocalId, LocalIndex, 0, 0, (ushort)EmoteId.Wave, 0, EmoteMode.Motion));
            Assert.Empty(lines);
        }

        [Theory]
        [InlineData(0, "east")]
        [InlineData(20, "southeast")]
        [InlineData(64, "south")]
        [InlineData(128, "west")]
        [InlineData(192, "north")]
        [InlineData(250, "east")]
        public void CompassName_FollowsTheWireHeading(byte direction, string expected)
        {
            Assert.Equal(expected, ClientMessageController.CompassName(direction));
        }

        [Fact]
        public void SystemMessage_FormatsNumbersPluralsAndLines()
        {
            var (_, _, dispatcher, lines) = Messages();

            Receive(dispatcher, 0x053, SystemMes(7, 30, 0));
            Receive(dispatcher, 0x053, SystemMes(10, 1, 0));
            Receive(dispatcher, 0x053, SystemMes(10, 5, 0));

            Assert.Equal(new[]
            {
                "Executing logout in 30 seconds.",
                "Cancel healing to remain logged in.",
                "1 second until position reset.",
                "5 seconds until position reset.",
            }, lines.Select(l => l.Text));
            Assert.All(lines, l => Assert.Equal(ChatLogChannel.System, l.Channel));
        }

        [Fact]
        public void SystemMessage_WithoutTheTable_FallsBackToTheStandardText()
        {
            var controller = new ClientMessageController(new ClientMessageTables(_ => null), _ => null);
            Assert.Equal(new[] { "Event skipped." }, controller.FormatSystemMessage(new SystemMessageInfo(117, 0, 0)));
            Assert.Equal(new[] { "Msg#9999" }, controller.FormatSystemMessage(new SystemMessageInfo(9999, 0, 0)));
        }

        // ---- the event VM ----

        private static (EventDialogController Controller, PacketParser Parser, PacketDispatcher Dispatcher, List<(ChatLogChannel Channel, string Text)> Lines) Dialog(byte[]? dialogTable = null)
        {
            var parser = NewParser(out var dispatcher);
            var controller = new EventDialogController(id => id == ZoneDialogTable.GetFileId(0) ? dialogTable : null);
            var chat = new StockUiChat();
            var lines = Lines(chat);
            controller.Attach(parser.Progression, parser.ProgressionModule, parser.World, parser.LocalPlayer, chat, new StockUiMenuController(), () => "Cybin");
            return (controller, parser, dispatcher, lines);
        }

        [Fact]
        public void PendingNum_ReachesTheEventWorkZone()
        {
            var (controller, _, dispatcher, _) = Dialog();
            var num = new byte[32];
            for (int i = 0; i < 8; i++) BinaryPrimitives.WriteInt32LittleEndian(num.AsSpan(i * 4), 100 + i);

            Receive(dispatcher, 0x05C, num);
            Assert.Equal(0, controller.WorkZone.GetMessageParameter(0)); // applied on the game tick
            controller.Tick(Frame);

            Assert.Equal(Enumerable.Range(100, 8), Enumerable.Range(0, 8).Select(controller.WorkZone.GetMessageParameter));
            Assert.Equal(100, controller.WorkZone.Zone[EventWorkZone.ParameterBase]);
        }

        [Fact]
        public void PendingStr_ReplacesTheEventsStrings()
        {
            var (controller, parser, dispatcher, _) = Dialog();
            parser.Progression.StartEvent(0x010E6001, 1, 0, 100, 0, null, new[] { "old", "", "", "" });
            controller.Tick(Frame);
            Assert.Equal("old", controller.CurrentEventInfo!.StringParams[0]);

            var str = new byte[100];
            Encoding.ASCII.GetBytes("Tateeya").CopyTo(str, 36);
            Receive(dispatcher, 0x05D, str);
            controller.Tick(Frame);

            Assert.Equal("Tateeya", controller.CurrentEventInfo!.StringParams[0]);
        }

        [Fact]
        public void TalkNumWork2_PrintsTheZoneMessageWithItsStrings()
        {
            // Message 0: "{1C 00} caught a monster!" (the fishing line's form); message 1: "Hello, {1C 01}."
            var table = DialogTable(Msg(0x1C, 0x00, " caught ", 0x0A, 0x01, " fish!", Prompt), Msg("Hello, ", 0x1C, 0x00, ".", Prompt));
            var (_, parser, dispatcher, lines) = Dialog(table);
            parser.World.UpsertEntity(new WorldEntity(0x010E6001, 1, EntityType.Npc) { Name = "Moogle" });

            var p = new byte[108];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0), LocalId);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(6), 0x8000 | 0);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(16), 3);
            Encoding.ASCII.GetBytes("Cybin").CopyTo(p, 28);
            Receive(dispatcher, 0x027, p);

            var name = new byte[28];
            BinaryPrimitives.WriteUInt32LittleEndian(name.AsSpan(0), 0x010E6001);
            BinaryPrimitives.WriteUInt16LittleEndian(name.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(name.AsSpan(6), 1);
            Encoding.ASCII.GetBytes("Cybin").CopyTo(name, 12);
            Receive(dispatcher, 0x043, name);

            // 0x043's name is the text's string, not the speaker: the line is headed by the entity's own name.
            Assert.Equal(new[] { (ChatLogChannel.Message, "Cybin caught 3 fish!"), (ChatLogChannel.Dialog, "Moogle : Hello, Cybin.") }, lines);
        }

        // ---- retail data ----

        private static ResourceManager? OpenGame()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return rm;
        }

        [Fact]
        public void RetailTables_FormatEmotesAndSystemMessages()
        {
            var rm = OpenGame();
            if (rm == null) return;
            var parser = NewParser(out var dispatcher);
            var chat = new StockUiChat();
            var lines = Lines(chat);
            var controller = new ClientMessageController(new ClientMessageTables(rm.LoadDatBytesByFileId), rm.LoadDatBytes);
            controller.Attach(parser.Commands, parser.World, parser.LocalPlayer, chat, () => "Cybin");
            parser.World.UpsertEntity(Player(LocalId, LocalIndex, "Cybin", 7));
            parser.World.UpsertEntity(new WorldEntity(0x01001234, 0x234, EntityType.Monster) { Name = "Goblin Thug" });

            Receive(dispatcher, 0x05A, MotionMes(LocalId, LocalIndex, 0, 0, (ushort)EmoteId.Wave, 0, EmoteMode.All));
            Receive(dispatcher, 0x05A, MotionMes(LocalId, LocalIndex, 0x01001234, 0x234, (ushort)EmoteId.Wave, 0, EmoteMode.All));
            Receive(dispatcher, 0x05A, MotionMes(LocalId, LocalIndex, 0, 0, (ushort)EmoteId.Clap, 0, EmoteMode.All));
            Receive(dispatcher, 0x053, SystemMes(117, 0, 0));
            Receive(dispatcher, 0x053, SystemMes(10, 1, 0));
            Receive(dispatcher, 0x053, SystemMes(239, 12, 34));

            Assert.Equal(new[]
            {
                "Cybin waves.",
                "Cybin waves to the Goblin Thug.",
                "Cybin claps her hands.",
                "Event skipped.",
                "1 second until position reset.",
                "The compass reads: X:12 Y:34 Z:0 (0).",
            }, lines.Select(l => l.Text));
        }

        [Fact]
        public void RetailEmote_QueuesTheRaceMotionOnTheCaster()
        {
            var rm = OpenGame();
            if (rm == null) return;
            var world = new WorldState();
            var commands = new PlayerCommandState();
            var controller = new ClientMessageController(new ClientMessageTables(rm.LoadDatBytesByFileId), rm.LoadDatBytes);
            controller.Attach(commands, world, new LocalPlayerState { ServerId = LocalId }, new StockUiChat(), () => "Cybin");
            var hume = Player(LocalId, LocalIndex, "Cybin", 1);
            world.UpsertEntity(hume);

            Assert.True(controller.PlayEmoteMotion(new EmoteEcho(LocalId, LocalIndex, 0, 0, (ushort)EmoteId.Wave, 0, EmoteMode.All)));
            Assert.Contains(hume.Animation.EventMotionBanks, b => b.Routines.ContainsKey(EmoteMotion.RoutineName((int)EmoteId.Wave)));
            // A face-only emote and a fixed model play nothing.
            Assert.False(controller.PlayEmoteMotion(new EmoteEcho(LocalId, LocalIndex, 0, 0, (ushort)EmoteId.Smile, 0, EmoteMode.All)));
            var npc = new WorldEntity(0x010E6001, 1, EntityType.Npc);
            npc.Appearance.ModelId = 51;
            world.UpsertEntity(npc);
            Assert.False(controller.PlayEmoteMotion(new EmoteEcho(0x010E6001, 1, 0, 0, (ushort)EmoteId.Wave, 0, EmoteMode.All)));
        }
    }
}
