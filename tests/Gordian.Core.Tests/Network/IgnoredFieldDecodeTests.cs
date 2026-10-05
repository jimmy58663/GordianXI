// tests/Gordian.Core.Tests/Network/IgnoredFieldDecodeTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Config;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// #116: the fields LandSandBoat sends that handled S2C packets used to ignore (0x00A, 0x057, 0x056, 0x052, 0x032,
    /// 0x034, 0x02A, 0x009, 0x028, 0x00D, 0x037, 0x04C, 0x113, 0x118, 0x0C8, 0x0DD, 0x0E2, 0x01B, 0x061, 0x086).
    /// Payloads are laid out as XiPackets documents them (offsets minus the 4-byte header).
    /// </summary>
    public class IgnoredFieldDecodeTests
    {
        private static PacketParser NewParser(out PacketDispatcher dispatcher)
        {
            dispatcher = new PacketDispatcher();
            return new PacketParser(new SessionProfile(), (_, _) => Task.CompletedTask, dispatcher: dispatcher);
        }

        private static void Receive(PacketDispatcher dispatcher, ushort id, byte[] payload) =>
            Assert.True(dispatcher.Dispatch(new PacketHeader(id, payload.Length + 4, 1), payload));

        private static void U16(byte[] p, int o, int v) => BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o), (ushort)v);
        private static void U32(byte[] p, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(o), v);

        // ---- 0x00A and 0x057 ----

        private static byte[] LoginPayload()
        {
            var p = new byte[256];
            U32(p, 0, 0x1234);
            U16(p, 4, 7);
            U16(p, 44, 230);                       // ZoneNo
            U16(p, 62, 230);                       // MapNumber
            for (int i = 0; i < 5; i++) U16(p, 82 + (i * 2), 100 + i); // MusicNum
            U16(p, 92, 3);                         // SubMapNumber
            U16(p, 100, 4);                        // WeatherNumber
            U16(p, 102, 6);                        // WeatherNumber2
            U32(p, 104, 5000);                     // WeatherTime
            U32(p, 108, 4000);                     // WeatherTime2
            U32(p, 112, 0x00030002);               // WeatherOffsetTime
            U32(p, 124, 1);                        // LoginState = MyRoom
            U16(p, 154, 1100);                     // ZoneSubNo
            U32(p, 156, 7200);                     // PlayTime
            U16(p, 166, 0x0121);                   // MyroomMapNumber
            p[164] = 2;                            // MyroomSubMapNumber
            p[170] = 4;                            // MyRoomExitBit
            p[171] = 1;                            // MogZoneFlag
            p[176] = 3;                            // mjob_no
            p[179] = 4;                            // sjob_no
            U32(p, 228, 1500);                     // hpmax
            U32(p, 232, 800);                      // mpmax
            p[236] = 1;                            // sjobflg
            return p;
        }

        [Fact]
        public void S2C_0x00A_DecodesTheZoneSetUpFields()
        {
            var ack = new S2C_0x00A_LoginAck(LoginPayload());
            ZoneLoginInfo info = ack.GetZoneInfo();

            Assert.Equal(230, info.MapNumber);
            Assert.Equal(3, info.SubMapNumber);
            Assert.Equal(1100, info.ZoneSubNo);
            Assert.Equal(100, info.MusicDay);
            Assert.Equal(101, info.MusicNight);
            Assert.Equal(102, info.MusicBattleSolo);
            Assert.Equal(103, info.MusicBattleParty);
            Assert.Equal(104, info.MusicMount);
            Assert.Equal(4, info.WeatherNumber);
            Assert.Equal(6, info.WeatherNumber2);
            Assert.Equal(5000u, info.WeatherTime);
            Assert.Equal(4000u, info.WeatherTime2);
            Assert.Equal(3, info.PreviousWeatherOffsetHours);
            Assert.Equal(SaveLoginState.MyRoom, info.LoginState);
            Assert.True(info.InMogHouse);
            Assert.Equal(7200u, info.PlayTime);
            Assert.Equal(2, info.MyroomSubMapNumber);
            Assert.Equal(0x0121, info.MyroomMapNumber);
            Assert.Equal(4, info.MyRoomExitBit);
            Assert.True(info.MogZoneFlag);
            Assert.Equal(3, info.MainJob);
            Assert.Equal(4, info.SubJob);
            Assert.True(info.SubJobUnlocked);
            Assert.Equal(1500, info.MaxHp);
            Assert.Equal(800, info.MaxMp);
        }

        [Fact]
        public void S2C_0x00A_ShortPayloadReadsZeroForMissingFields()
        {
            var ack = new S2C_0x00A_LoginAck(new byte[60]);
            ZoneLoginInfo info = ack.GetZoneInfo();

            Assert.Equal(0, info.MusicDay);
            Assert.Equal(0, info.MaxHp);
            Assert.Equal(SaveLoginState.None, info.LoginState);
        }

        [Fact]
        public void LoginAndWeatherPackets_FillTheWorldState()
        {
            var parser = NewParser(out var dispatcher);
            Assert.Null(parser.World.ZoneLoginInfo);

            Receive(dispatcher, 0x00A, LoginPayload());

            Assert.Equal(3, parser.World.ZoneLoginInfo!.Value.SubMapNumber);
            Assert.Equal(new WeatherTiming(4, 5000, 0x00030002), parser.World.WeatherTiming);
            Assert.Equal(5000u * 60, parser.World.WeatherTiming.StartSeconds);

            var weather = new byte[8];
            U32(weather, 0, 7777);
            U16(weather, 4, 6);
            U16(weather, 6, 30);
            Receive(dispatcher, 0x057, weather);

            Assert.Equal(6, parser.World.WeatherNumber);
            Assert.Equal(new WeatherTiming(6, 7777, 30), parser.World.WeatherTiming);
        }

        // ---- 0x056 ----

        private static byte[] MissionPayload(ushort port, params uint[] data)
        {
            var p = new byte[36];
            for (int i = 0; i < data.Length; i++) U32(p, i * 4, data[i]);
            U16(p, 32, port);
            return p;
        }

        [Theory]
        [InlineData(0x0030, MissionTable.MissionComplete, 16)]
        [InlineData(0x0038, MissionTable.MissionComplete, 24)]
        [InlineData(0x0050, MissionTable.QuestOffer, 0)]
        [InlineData(0x0088, MissionTable.QuestOffer, 56)]
        [InlineData(0x0090, MissionTable.QuestComplete, 0)]
        [InlineData(0x00C8, MissionTable.QuestComplete, 56)]
        [InlineData(0x00D0, MissionTable.MissionComplete, 0)]
        [InlineData(0x00D8, MissionTable.MissionComplete, 8)]
        [InlineData(0x00E0, MissionTable.QuestOffer, 64)]
        [InlineData(0x00E8, MissionTable.QuestComplete, 64)]
        [InlineData(0x00F0, MissionTable.QuestOffer, 72)]
        [InlineData(0x00F8, MissionTable.QuestComplete, 72)]
        [InlineData(0x0100, MissionTable.QuestOffer, 80)]
        [InlineData(0x0108, MissionTable.QuestComplete, 80)]
        public void MissionPorts_ResolveToTheirTables(int port, MissionTable table, int offset)
        {
            Assert.True(MissionPorts.TryResolve((ushort)port, out var t, out int o));
            Assert.Equal(table, t);
            Assert.Equal(offset, o);
        }

        [Fact]
        public void MissionPorts_RejectTheFixedAndUnknownPorts()
        {
            Assert.False(MissionPorts.TryResolve(0xFFFF, out _, out _));
            Assert.False(MissionPorts.TryResolve(0xFFFE, out _, out _));
            Assert.False(MissionPorts.TryResolve(0x0051, out _, out _));
            Assert.False(MissionPorts.TryResolve(0x0110, out _, out _));
        }

        [Fact]
        public void MissionPorts_ReceivedBitsReachTheCompleteMask()
        {
            uint bits = MissionPorts.ReceivedBit(MissionPorts.Main);
            foreach (ushort port in new ushort[]
            {
                0x30, 0x38, 0x50, 0x58, 0x60, 0x68, 0x70, 0x78, 0x80, 0x88, 0x90, 0x98, 0xA0, 0xA8, 0xB0, 0xB8, 0xC0, 0xC8,
                0xD0, 0xD8, 0xE0, 0xE8, 0xF0, 0xF8, 0x100, 0x108
            })
            {
                bits |= MissionPorts.ReceivedBit(port);
            }
            Assert.Equal(MissionPorts.AllReceived, bits);
        }

        [Fact]
        public void MissionPackets_FillTheQuestLogAndTvr()
        {
            var parser = NewParser(out var dispatcher);

            // Bastok (area 1) active quests: bit 3 and bit 33 of the area block (port 0x58 = words 8..15).
            Receive(dispatcher, 0x056, MissionPayload(0x0058, 0b1000, 0b10));
            // Windurst completed quests (port 0xA0 = QuestComplete words 16..23): quest 5.
            Receive(dispatcher, 0x056, MissionPayload(0x00A0, 1u << 5));
            // Completed nation missions (port 0xD0): bit 36 (word 1, bit 4).
            Receive(dispatcher, 0x056, MissionPayload(0x00D0, 0, 1u << 4));
            Receive(dispatcher, 0x056, MissionPayload(MissionPorts.Tvr, 0x80000000u | 12));
            Receive(dispatcher, 0x056, MissionPayload(MissionPorts.Main, 1, 0xFFFF, 5, 0, 0, 0, 7, 8));

            var state = parser.Progression;
            Assert.True(state.IsQuestActive(QuestLogArea.Bastok, 3));
            Assert.True(state.IsQuestActive(QuestLogArea.Bastok, 33));
            Assert.False(state.IsQuestActive(QuestLogArea.Bastok, 4));
            Assert.False(state.IsQuestActive(QuestLogArea.SandOria, 3));
            Assert.True(state.IsQuestComplete(QuestLogArea.Windurst, 5));
            Assert.False(state.IsQuestComplete(QuestLogArea.Bastok, 5));
            Assert.True(state.IsMissionComplete(36));
            Assert.False(state.IsMissionComplete(35));
            Assert.Equal(0x80000000u | 12, state.ExpansionTvr);
            Assert.Equal(1u, state.NationId);
            Assert.Equal(7u, state.ExpansionSoA);
            Assert.Equal(8u, state.ExpansionRoV);
            Assert.False(state.QuestLogComplete);
        }

        // ---- 0x052, 0x032, 0x034 ----

        private static byte[] EventPayload(ushort eventNum, ushort eventPara, ushort eventNum2, ushort eventPara2)
        {
            var p = new byte[16];
            U32(p, 0, 0x01000001);
            U16(p, 4, 20);
            U16(p, 6, eventNum);
            U16(p, 8, eventPara);
            U16(p, 12, eventNum2);
            U16(p, 14, eventPara2);
            return p;
        }

        [Fact]
        public void S2C_0x052_SplitsTheKindFromTheEventId()
        {
            var p = new byte[4];
            U32(p, 0, ((uint)1000 << 8) | 2);
            var ucoff = new S2C_0x052_EventUcOff(p);

            Assert.Equal(EventUcOffMode.CancelEvent, ucoff.Kind);
            Assert.Equal(1000u, ucoff.EventId);

            U32(p, 0, 1);
            Assert.Equal(0u, new S2C_0x052_EventUcOff(p).EventId);
        }

        [Fact]
        public void EventCancel_OnlyEndsTheNamedEvent()
        {
            var parser = NewParser(out var dispatcher);
            var cancelled = 0;
            parser.Progression.EventCancelledByServer += () => cancelled++;
            Receive(dispatcher, 0x032, EventPayload(230, 1001, 230, 0));

            var other = new byte[4];
            U32(other, 0, ((uint)77 << 8) | 2);
            Receive(dispatcher, 0x052, other);
            Assert.True(parser.Progression.IsInEvent);
            Assert.Equal(0, cancelled);

            var match = new byte[4];
            U32(match, 0, ((uint)1001 << 8) | 2);
            Receive(dispatcher, 0x052, match);
            Assert.False(parser.Progression.IsInEvent);
            Assert.Equal(1, cancelled);
        }

        [Fact]
        public void EventUcOff_Modes0And3RaiseTheirEvents()
        {
            var parser = NewParser(out var dispatcher);
            int released = 0, inputs = 0;
            parser.Progression.EventControlReleased += () => released++;
            parser.Progression.EventInputCancelled += () => inputs++;

            Receive(dispatcher, 0x052, new byte[4]);
            var mode3 = new byte[4];
            mode3[0] = 3;
            Receive(dispatcher, 0x052, mode3);

            Assert.Equal(1, released);
            Assert.Equal(1, inputs);
        }

        [Fact]
        public void EventNum2_IsKeptAndPicksTheEventFile()
        {
            var parser = NewParser(out var dispatcher);
            Receive(dispatcher, 0x032, EventPayload(230, 5, 230, 9));
            var same = parser.Progression.ActiveEvent!;
            Assert.True(same.HasEventNum2);
            Assert.Equal(9, same.EventPara2);
            Assert.Equal(230, same.GetEventFileNumber(0));
            Assert.Equal(1230, same.GetEventFileNumber(1100)); // an instance: EventNum2 + 1000

            Receive(dispatcher, 0x032, EventPayload(230, 5, 7300, 0));
            Assert.Equal(8300, parser.Progression.ActiveEvent!.GetEventFileNumber(0)); // the numbers differ

            // Zone-in events carry no second number.
            parser.Progression.StartEvent(1, 2, 230, 5, 0, fromZoneIn: true);
            Assert.False(parser.Progression.ActiveEvent!.HasEventNum2);
            Assert.Equal(230, parser.Progression.ActiveEvent!.GetEventFileNumber(1100));
        }

        // ---- 0x02A ----

        private static byte[] TalkNumWork(uint unique, ushort mesNum, byte flag, string text)
        {
            var p = new byte[0x3C];
            U32(p, 0, unique);
            U16(p, 0x14, 5);
            U16(p, 0x16, mesNum);
            p[0x19] = flag;
            Encoding.ASCII.GetBytes(text).CopyTo(p, 0x1A);
            return p;
        }

        [Fact]
        public void TalkNumWork_StringIsTheSpeakerOnlyInTheEventForm()
        {
            var parser = NewParser(out var dispatcher);
            var received = new List<DialogMessageInfo>();
            parser.Progression.DialogMessageReceived += received.Add;

            // Event form: Flag set, no UniqueNo: the string heads the message even with the no-name bit.
            Receive(dispatcher, 0x02A, TalkNumWork(0, 0x8000 | 10, 1, "Mogsworth"));
            // LandSandBoat's form: Flag 0, the entity's own name; the string is a parameter and the entity heads the message.
            Receive(dispatcher, 0x02A, TalkNumWork(0x010E6001, 11, 0, "Ayame"));
            // Flag set but a UniqueNo: the entity decides, the string is no speaker.
            Receive(dispatcher, 0x02A, TalkNumWork(0x010E6001, 0x8000 | 12, 1, "Ayame"));

            Assert.False(received[0].HideName);
            Assert.Equal("Mogsworth", received[0].Name);
            Assert.False(received[1].HideName);
            Assert.Equal(new[] { "Ayame" }, received[1].Strings);
            Assert.True(received[2].HideName);
        }

        // ---- 0x009 ----

        [Fact]
        public void SystemMessageParameters_ReadTheKeyedValues()
        {
            var p = SystemMessageParameters.Parse("CasUniqueNo 16777217 TarUniqueNo 33 Para0 12 Para1 -3 Para2 4 Para3 5 Mode 2 string2 Ayame string3 Cybin");

            Assert.Equal(16777217u, p.CasUniqueNo);
            Assert.Equal(33u, p.TarUniqueNo);
            Assert.Equal(12, p.Para0);
            Assert.Equal(-3, p.Para1);
            Assert.Equal(4, p.Para2);
            Assert.Equal(5, p.Para3);
            Assert.Equal(2, p.Mode);
            Assert.Equal("Ayame", p.String2);
            Assert.Equal("Cybin", p.String3);
        }

        [Fact]
        public void SystemMessageParameters_MissingKeysAreZero_AndStringsAreClamped()
        {
            var p = SystemMessageParameters.Parse("Para0\t7 string2 " + new string('x', 40));

            Assert.Equal(7, p.Para0);
            Assert.Equal(0, p.Para1);
            Assert.Equal(new string('x', 24), p.String2);
            Assert.Equal(string.Empty, p.String3);
            Assert.Equal(0, SystemMessageParameters.Parse(null).Para0);
            Assert.Equal(string.Empty, SystemMessageParameters.Parse(string.Empty).String2);
        }

        [Fact]
        public void S2C_0x009_FlagsBlacklistableMessages()
        {
            var payload = new byte[9 + 8];
            U32(payload, 0, 0x01000001);
            U16(payload, 6, 123);
            payload[8] = 0x10;
            Encoding.ASCII.GetBytes("Para0 9").CopyTo(payload, 9);

            var msg = new S2C_0x009_SysMessage(payload);
            Assert.True(msg.IsBlacklistable);
            Assert.Equal(9, msg.GetParameters().Para0);

            payload[8] = 0;
            Assert.False(new S2C_0x009_SysMessage(payload).IsBlacklistable);
        }

        // ---- 0x028 ----

        [Fact]
        public void S2C_0x028_SplitsScaleFlagsAndTheSkillchain()
        {
            byte[] payload = new byte[64];
            payload[0] = 30;
            var writer = new BitStreamWriter(payload.AsSpan(1));
            writer.WriteUInt32(0x10001111, 32);
            writer.WriteByte(1, 6);
            writer.WriteByte(0, 4);
            writer.WriteByte((byte)ActionCategory.SkillFinish, 4);
            writer.WriteUInt32(33, 32);
            writer.WriteUInt32(0, 32);
            writer.WriteUInt32(0x20002222, 32);
            writer.WriteByte(1, 4);
            writer.WriteByte(0, 3);          // hit
            writer.WriteByte(3, 2);          // kind
            writer.WriteUInt16(39, 12);      // animation
            writer.WriteByte(2, 5);          // info
            writer.WriteByte((3 << 2) | 2, 5); // scale: knockback row 3, distortion 2
            writer.WriteUInt32(500, 17);
            writer.WriteUInt16(110, 10);
            writer.WriteUInt32(0x04 | 0x10, 31); // magic burst + critical
            writer.WriteBool(true);          // proc
            writer.WriteByte(7, 6);          // proc_kind = skillchain: Compression
            writer.WriteByte(0, 4);
            writer.WriteUInt32(120, 17);
            writer.WriteUInt16(288, 10);
            writer.WriteBool(false);

            var action = new S2C_0x028_CombatAction(payload);
            var record = action.ToRecord();
            var result = record.Targets[0].Results[0];

            Assert.Equal(2, result.HitDistortionIndex);
            Assert.Equal(0.5f, result.HitDistortion);
            Assert.Equal(3, result.KnockbackIndex);
            Assert.Equal(ActionResultFlags.MagicBurst | ActionResultFlags.CriticalHit, result.Flags);
            Assert.Equal(ActionSkillchain.Compression, result.Skillchain);
            Assert.Equal(ActionProcAddEffect.None, result.GetAddEffect(ActionCategory.SkillFinish));
            Assert.True(result.IsCriticalInfo);

            var line = CombatLogFormatter.FormatAction(record, id => "X");
            Assert.Contains(line, l => l.Contains("Skillchain: Compression"));
        }

        [Fact]
        public void ActionRecast_IsOnlyARecastForSpells()
        {
            var spell = new CombatActionRecord { Category = ActionCategory.MagicFinish, Recast = 30 };
            var ability = new CombatActionRecord { Category = ActionCategory.AbilityFinish, Recast = 5 };

            Assert.Equal(30u, spell.RecastSeconds);
            Assert.Equal(0u, ability.RecastSeconds);
            Assert.Equal(ActionProcAddEffect.Haste, (ActionProcAddEffect)23);
        }

        // ---- 0x00D, 0x037 ----

        [Fact]
        public void S2C_0x00D_ReadsGeoMountAndHitboxFlags()
        {
            var p = new byte[0x5C];
            U32(p, 0, 0x01000001);
            U16(p, 4, 9);
            p[0x2E] = 6;
            U32(p, 0x30, 0xABCD);
            U16(p, 0x3A, 0x0102);
            p[0x3C] = 11;
            p[0x3D] = 12;
            p[0x3E] = 0x40 | 0x20 | 0x05;   // indi up, size 2, element 5
            p[0x3F] = 25;
            U32(p, 0x40, (17u << 4) | 0x9);   // mount 17, gate 9
            U32(p, 28, 1u << 13);             // YellFlag

            var pc = new S2C_0x00D_CharPc(p);

            Assert.True(pc.IsCalledForHelp);
            Assert.Equal(6, pc.BallistaInfo);
            Assert.Equal(0xABCDu, pc.CustomProperty);
            Assert.Equal(0x0102, pc.MonstrosityFlags);
            Assert.Equal(11, pc.MonstrosityNameId1);
            Assert.Equal(12, pc.MonstrosityNameId2);
            Assert.True(pc.HasGeoIndi);
            Assert.Equal(5, pc.GeoIndiElement);
            Assert.Equal(2, pc.GeoIndiSize);
            Assert.Equal(25, pc.ModelHitboxSize);
            Assert.Equal(2.5f, pc.ModelHitboxRadius, 3);
            Assert.Equal(17, pc.MountIndex);
            Assert.Equal(9, pc.GateId);

            Assert.Equal(0, new S2C_0x00D_CharPc(new byte[0x2C]).MountIndex);
        }

        [Fact]
        public void S2C_0x037_ReadsFreezeAndTheSecondDeadCounter()
        {
            var p = new byte[0x60];
            U32(p, 32, 0x01000001);
            U32(p, 40, 1u << 13);
            U32(p, 56, 21600);
            U32(p, 60, 99);

            var status = new S2C_0x037_CharStatus(p);

            Assert.True(status.IsValid);
            Assert.True(status.IsFrozen);
            Assert.Equal(99u, status.DeadCounter2Ticks);
        }

        // ---- 0x04C ----

        [Fact]
        public void S2C_0x04C_ReadsTheParamUnionAndTheParcel()
        {
            var p = new byte[56];
            p[0] = (byte)AuctionCommand.AskCommit;
            p[1] = 0xFF;
            p[2] = 1;
            p[3] = 2;
            U32(p, 4, 120);            // Commission
            U16(p, 8, 3);              // ItemWorkIndex
            U16(p, 10, 4096);          // ItemNo
            U32(p, 12, 1);             // ItemStacks

            var fee = new S2C_0x04C_Auc(p);
            Assert.Equal(AuctionCommand.AskCommit, fee.Command);
            Assert.Equal(4096, fee.ItemId);
            Assert.Equal(120u, fee.Price);
            Assert.Equal(3, fee.ParamWorkIndex);
            Assert.Equal(1u, fee.ParamStacks);
            Assert.Equal(AuctionResultStatus.InOutLot, fee.Status);

            // A bid reply: BidPrice, ItemNo, ItemStacks.
            var bid = (byte[])p.Clone();
            bid[0] = (byte)AuctionCommand.Bid;
            U32(bid, 4, 9000);
            U16(bid, 8, 4097);
            U32(bid, 12, 1);
            var bidReply = new S2C_0x04C_Auc(bid);
            Assert.Equal(4097, bidReply.ItemId);
            Assert.Equal(9000u, bidReply.Price);

            // The parcel wins when it has an item.
            var parcel = (byte[])bid.Clone();
            parcel[16] = (byte)AuctionParcelStat.LotInDone;
            Encoding.ASCII.GetBytes("Cybin").CopyTo(parcel, 20);
            U16(parcel, 36, 5000);
            parcel[38] = 1;
            parcel[39] = 7;
            U32(parcel, 40, 700);
            U32(parcel, 44, 4);
            U32(parcel, 48, 55);
            U32(parcel, 52, 1234);
            var withParcel = new S2C_0x04C_Auc(parcel);
            Assert.Equal(5000, withParcel.ItemId);
            Assert.Equal(700u, withParcel.Price);
            Assert.Equal(AuctionParcelStat.LotInDone, withParcel.Stat);
            Assert.Equal(7, withParcel.ParcelCategory);
            Assert.Equal(4u, withParcel.ParcelMarketNo);
            Assert.Equal(55u, withParcel.ParcelLotNo);
            Assert.Equal(1234u, withParcel.ParcelTimeStamp);
            Assert.Equal("Cybin", withParcel.SellerName);

            Assert.Equal(AuctionCommand.Close, (AuctionCommand)3);
            Assert.Equal(AuctionResultCode.Limit, (AuctionResultCode)0xF6);
        }

        // ---- 0x113, 0x118 ----

        [Fact]
        public void Currencies1_ReadsEveryFieldFromItsXiPacketsOffset()
        {
            // Payload offsets: XiPackets 0x0113 struct offsets minus the 4-byte header.
            var p = new byte[248];
            U32(p, 28, 100);             // guild_points_fishing (struct 0x20)
            U32(p, 64, 200);             // cinders (struct 0x44)
            U32(p, 148, 300);            // zeni (struct 0x98)
            U32(p, 144, 400);            // tokens (struct 0x94)
            U32(p, 112, 777);            // sparks_of_eminence
            U32(p, 224, 888);            // unity_accolades
            // Plans: five 9-bit fields in a 64-bit word at payload 212.
            ulong plans = 0;
            for (int i = 0; i < 5; i++) plans |= (ulong)(i + 1) << (9 * i);
            BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(212), plans);
            p[68] = 3;                   // synergy_fewell_fire

            var cur = new S2C_0x113_Currencies1(p);

            Assert.True(cur.IsValid);
            Assert.Equal(100, cur.GetCurrency(Currency1Kind.GuildPointsFishing));
            Assert.Equal(200, cur.GetCurrency(Currency1Kind.Cinders));
            Assert.Equal(300, cur.GetCurrency(Currency1Kind.Zeni));
            Assert.Equal(400, cur.GetCurrency(Currency1Kind.Tokens));
            Assert.Equal(777, cur.GetCurrency(Currency1Kind.SparksOfEminence));
            Assert.Equal(777, cur.SparksOfEminence);
            Assert.Equal(888, cur.GetCurrency(Currency1Kind.UnityAccolades));
            Assert.Equal(1, cur.GetCurrency(Currency1Kind.BloodshedPlans));
            Assert.Equal(2, cur.GetCurrency(Currency1Kind.UmbragePlans));
            Assert.Equal(5, cur.GetCurrency(Currency1Kind.PrimacyPlans));
            Assert.Equal(3, cur.GetCurrency(Currency1Kind.SynergyFewellFire));
        }

        [Fact]
        public void Currencies_FillTheInventoryState()
        {
            var parser = NewParser(out var dispatcher);
            var p1 = new byte[248];
            U32(p1, 64, 4242);
            Receive(dispatcher, 0x113, p1);
            var p2 = new byte[150];
            U32(p2, 128, 55);       // domain points
            U32(p2, 124, 66);       // silver aman vouchers
            p2[7] = 9;              // mystical canteens
            Receive(dispatcher, 0x118, p2);

            Assert.Equal(4242, parser.Inventory.GetCurrency(Currency1Kind.Cinders));
            Assert.Equal(55, parser.Inventory.DomainPoints);
            Assert.Equal(55, parser.Inventory.GetCurrency(Currency2Kind.DomainPoints));
            Assert.Equal(66, parser.Inventory.GetCurrency(Currency2Kind.SilverAmanVouchers));
            Assert.Equal(9, parser.Inventory.GetCurrency(Currency2Kind.MysticalCanteens));
        }

        // ---- 0x0C8, 0x0DD, 0x0E2 ----

        private static byte[] GroupList(uint gattr, byte masterLevel)
        {
            var p = new byte[52];
            U32(p, 0, 0x01000009);
            U32(p, 16, gattr);
            U16(p, 20, 9);
            p[30] = 3;
            p[31] = 75;
            p[34] = masterLevel;
            p[35] = 1;
            Encoding.ASCII.GetBytes("Ayame").CopyTo(p, 36);
            return p;
        }

        [Fact]
        public void GroupLists_ReadQuartermasterLevelSyncAndMasterLevel()
        {
            var parser = NewParser(out var dispatcher);
            uint gattr = 1 | 0x04 | 0x10 | 0x20 | 0x100;

            Receive(dispatcher, 0x0DD, GroupList(gattr, 20));

            var member = Assert.Single(parser.Party.Members);
            Assert.True(member.IsLeader);
            Assert.True(member.IsQuartermaster);
            Assert.True(member.IsAllianceQuartermaster);
            Assert.True(member.IsLevelSynced);
            Assert.Equal(20, member.MasterJobLevel);
            Assert.Equal(1, member.MasterJobFlags);

            // 0x0E2 carries the same fields.
            var list2 = new S2C_0x0E2_GroupList2(GroupList(0x100 | 0x10, 33));
            Assert.True(list2.IsLevelSynced);
            Assert.True(list2.IsQuartermaster);
            Assert.False(list2.IsAllianceQuartermaster);
            Assert.Equal(33, list2.MasterJobLevel);
        }

        [Fact]
        public void S2C_0x0C8_ReadsTheQuartermasterBits()
        {
            var p = new byte[4 + 12];
            U32(p, 4, 0x01000009);
            p[4 + 6] = 1 | 0x10;           // party 1, quartermaster
            var tbl = new S2C_0x0C8_GroupTbl(p);

            Assert.True(tbl.IsEntryQuartermaster(0));
            Assert.False(tbl.IsEntryAllianceQuartermaster(0));
            p[4 + 6] = 0x20;
            Assert.True(new S2C_0x0C8_GroupTbl(p).IsEntryAllianceQuartermaster(0));
        }

        // ---- 0x01B, 0x061 ----

        [Fact]
        public void S2C_0x01B_ReadsMasteryAndMentorFields()
        {
            var p = new byte[128];
            p[4] = 3;
            U32(p, 92, 0x5);
            p[96] = 1;
            p[97] = 2;
            p[98] = 21;
            U32(p, 100, 1u << 3);
            p[104 + 3] = 9;

            var info = new S2C_0x01B_JobInfo(p);

            Assert.Equal(5u, info.Encumbrance);
            Assert.True(info.CanThumbsUpMentor);
            Assert.Equal(2, info.MentorRank);
            Assert.Equal(21, info.MasteryRank);
            Assert.True(info.HasJobMastery((JobId)3));
            Assert.False(info.HasJobMastery((JobId)4));
            Assert.Equal(9, info.GetJobMasteryLevel((JobId)3));
            Assert.Equal(0, new S2C_0x01B_JobInfo(new byte[100]).GetJobMasteryLevel((JobId)3));
        }

        [Fact]
        public void S2C_0x061_ReadsItemLevelsUnityAndMastery()
        {
            var p = new byte[108];
            U32(p, 72, 0xF0);
            p[77] = 2;
            p[80] = 119;
            p[81] = 117;
            p[82] = 118;
            p[83] = 116;
            U16(p, 88, 1500);
            U16(p, 90, 1700);
            U32(p, 92, 1);
            p[96] = 7;
            p[97] = 40;
            p[98] = 0x03;
            U32(p, 100, 12345);
            U32(p, 104, 50000);

            var status = new S2C_0x061_CliStatus(p);

            Assert.Equal(0xF0u, status.MonsterBuster);
            Assert.Equal(2, status.MyRoom);
            Assert.Equal(119, status.HighestItemLevel);
            Assert.Equal(117, status.ItemLevel);
            Assert.Equal(118, status.ItemLevelMainHand);
            Assert.Equal(116, status.ItemLevelRanged);
            Assert.Equal(1500, status.UnityPoints1);
            Assert.Equal(1700, status.UnityPoints2);
            Assert.True(status.UnityChatLightColor);
            Assert.Equal(40, status.MasteryJobLevel);
            Assert.True(status.MasteryUnlocked);
            Assert.True(status.MasteryExemplarCapped);
            Assert.Equal(12345u, status.MasteryExpNow);
            Assert.Equal(50000u, status.MasteryExpNext);
        }

        // ---- 0x086 ----

        [Theory]
        [InlineData(1u, 0x000001E0u, 5, 9)]
        [InlineData(1u, 0x00003FF8u, 3, 14)]
        [InlineData(1u, 0x0000FFF0u, 4, 16)]
        public void S2C_0x086_ReadsTheClosedHours(uint stat, uint time, int open, int close)
        {
            var p = new byte[8];
            p[0] = (byte)stat;
            U32(p, 4, time);

            var guild = new S2C_0x086_GuildOpen(p);

            Assert.Equal(open, guild.OpenHour);
            Assert.Equal(close, guild.CloseHour);
            Assert.Equal(-1, guild.HolidayDay);
        }

        [Fact]
        public void S2C_0x086_ReadsTheHolidayAndFillsTheState()
        {
            var parser = NewParser(out var dispatcher);
            var p = new byte[8];
            p[0] = (byte)ShopOpenStatus.Holiday;
            U32(p, 4, 3);
            Receive(dispatcher, 0x086, p);

            var guild = new S2C_0x086_GuildOpen(p);
            Assert.Equal(3, guild.HolidayDay);
            Assert.False(guild.SuppressesMessage);
            Assert.Equal(3, parser.Inventory.GuildHours!.HolidayDay);
            Assert.Equal(ShopOpenStatus.Holiday, parser.Inventory.ShopStatus);

            p[0] = (byte)ShopOpenStatus.Open;
            Receive(dispatcher, 0x086, p);
            Assert.Null(parser.Inventory.GuildHours);
        }

        [Fact]
        public void WeaponSkillSkillchain_ReachesTheCombatLog()
        {
            var parser = NewParser(out var dispatcher);
            var chat = new Gordian.Core.Ui.StockUiChat();
            var lines = new List<string>();
            chat.Log.LineAdded += line => lines.Add(line.Text);
            chat.Attach(parser.ChatModule, parser.Party, parser.Combat, parser.ActionService.Menus,
                id => id == 0x10001111 ? "Cybin" : id == 0x20002222 ? "Goblin" : null);

            // As LandSandBoat packs a weapon skill that closes a Liquefaction chain: proc kind 8, damage 412, message 287 + 8.
            byte[] payload = new byte[64];
            payload[0] = 30;
            var writer = new BitStreamWriter(payload.AsSpan(1));
            writer.WriteUInt32(0x10001111, 32);
            writer.WriteByte(1, 6);
            writer.WriteByte(0, 4);
            writer.WriteByte((byte)ActionCategory.SkillFinish, 4);
            writer.WriteUInt32(33, 32);
            writer.WriteUInt32(0, 32);
            writer.WriteUInt32(0x20002222, 32);
            writer.WriteByte(1, 4);
            writer.WriteByte(0, 3);
            writer.WriteByte(3, 2);
            writer.WriteUInt16(39, 12);
            writer.WriteByte(0, 5);
            writer.WriteByte(0, 5);
            writer.WriteUInt32(250, 17);
            writer.WriteUInt16(110, 10);
            writer.WriteUInt32(0, 31);
            writer.WriteBool(true);
            writer.WriteByte(8, 6);
            writer.WriteByte(0, 4);
            writer.WriteUInt32(412, 17);
            writer.WriteUInt16(295, 10);
            writer.WriteBool(false);
            Receive(dispatcher, 0x028, payload);

            Assert.Contains("Skillchain: Liquefaction. Goblin takes 412 points of damage.", lines);
        }

        [Fact]
        public void AuctionReply_IsLoggedWithItsPrices()
        {
            var parser = NewParser(out var dispatcher);
            var p = new byte[56];
            p[0] = (byte)AuctionCommand.AskCommit;
            p[1] = 0xFF;
            p[2] = 1;
            U32(p, 4, 120);
            U16(p, 10, 4096);
            // The state still carries the prices the log line prints.
            Receive(dispatcher, 0x04C, p);
            var response = parser.Inventory.LastAuctionResponse!;
            Assert.Equal(4096, response.ItemId);
            Assert.Equal(120u, response.Price);
        }
    }
}
