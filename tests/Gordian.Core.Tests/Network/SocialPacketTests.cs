// tests/Gordian.Core.Tests/Network/SocialPacketTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
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
    /// #113: the delivery box (S2C 0x04B, C2S 0x04D), the blacklist (S2C 0x041 / 0x042, C2S 0x03C / 0x03D), the world pass
    /// (S2C 0x059, C2S 0x01B), /itemsearch (S2C 0x049, C2S 0x02C), the linkshell concierge (S2C 0x048), the linkshell item
    /// requests (C2S 0x0C3 / 0x0C4), and the party group id and map positions (S2C 0x0E1 / 0x0A0, C2S 0x078 / 0x0D2).
    /// </summary>
    public class SocialPacketTests
    {
        private static PacketParser NewParser(out PacketDispatcher dispatcher, List<byte[]>? sent = null)
        {
            dispatcher = new PacketDispatcher();
            return new PacketParser(new SessionProfile(), (data, _) =>
            {
                sent?.Add(data.ToArray());
                return Task.CompletedTask;
            }, dispatcher: dispatcher);
        }

        private static void Receive(PacketDispatcher dispatcher, ushort id, byte[] payload) =>
            Assert.True(dispatcher.Dispatch(new PacketHeader(id, payload.Length + 4, 1), payload));

        private static void U16(byte[] p, int o, int v) => BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o), (ushort)v);
        private static void U32(byte[] p, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(o), v);
        private static void Text(byte[] p, int o, string s) => Encoding.ASCII.GetBytes(s).CopyTo(p, o);

        private static (ushort Id, int Size, ushort Seq) Header(byte[] packet)
        {
            Assert.True(PacketHeader.TryParse(packet, out var h));
            return (h.PacketId, h.TotalSize, h.SequenceId);
        }

        // ---- delivery box ----

        private static byte[] PbxResult(DeliveryCommand command, sbyte box, sbyte slot, byte result, sbyte p1 = -1, sbyte p2 = -1, sbyte p3 = -1,
            DeliveryItemStat? stat = null, string name = "", ushort itemId = 0, uint quantity = 0)
        {
            var p = new byte[stat.HasValue ? 84 : 16];
            p[0] = (byte)command;
            p[1] = (byte)box;
            p[2] = (byte)slot;
            p[3] = 0xFF;
            U32(p, 4, 0xFFFFFFFF);
            p[8] = result;
            p[9] = (byte)p1;
            p[10] = (byte)p2;
            p[11] = (byte)p3;
            if (stat.HasValue)
            {
                U32(p, 12, (uint)stat.Value);
                Text(p, 16, name);
                U32(p, 32, 77);
                U32(p, 36, 1234);
                U16(p, 44, itemId);
                U32(p, 52, quantity);
                p[56] = 0xAB;
            }
            return p;
        }

        [Fact]
        public void BuildPbx_LaysOutTheXiPacketsFields()
        {
            byte[] set = SocialPacketBuilder.BuildPbxSet(slot: 3, inventoryIndex: 17, count: 12, recipient: "ayame", sequenceId: 9);

            Assert.Equal((0x04D, 32, 9), Header(set));
            Assert.Equal(32, set.Length);
            Assert.Equal((byte)DeliveryCommand.Set, set[4]);
            Assert.Equal((byte)DeliveryBox.Outgoing, set[5]);
            Assert.Equal(3, set[6]);
            Assert.Equal(17, set[7]);
            Assert.Equal(12, BinaryPrimitives.ReadInt32LittleEndian(set.AsSpan(8)));
            Assert.Equal(0, set[12]);                                        // Result, ResParam1-3 stay 0
            Assert.Equal(0, set[13] | set[14] | set[15]);
            Assert.Equal("Ayame", Encoding.ASCII.GetString(set, 16, 5));      // first letter upper-cased
            Assert.Equal(0, set[21]);
        }

        [Theory]
        [InlineData(DeliveryCommand.Send, 2, 4, -1, -1)]
        [InlineData(DeliveryCommand.Cancel, 2, 4, -1, -1)]
        [InlineData(DeliveryCommand.Recv, 1, 4, 1, -1)]
        [InlineData(DeliveryCommand.Accept, 1, 4, -1, -1)]
        [InlineData(DeliveryCommand.Reject, 1, 4, -1, -1)]
        public void BuildPbx_PerCommandArgumentsMatchWhatLandSandBoatValidates(DeliveryCommand command, int box, int slot, int itemWork, int stacks)
        {
            byte[] p = command switch
            {
                DeliveryCommand.Send => SocialPacketBuilder.BuildPbxSend(slot),
                DeliveryCommand.Cancel => SocialPacketBuilder.BuildPbxCancel(slot),
                DeliveryCommand.Recv => SocialPacketBuilder.BuildPbxRecv(slot),
                DeliveryCommand.Accept => SocialPacketBuilder.BuildPbxAccept(slot),
                _ => SocialPacketBuilder.BuildPbxReject(slot)
            };

            Assert.Equal((byte)command, p[4]);
            Assert.Equal(box, (sbyte)p[5]);
            Assert.Equal(slot, (sbyte)p[6]);
            Assert.Equal(itemWork, (sbyte)p[7]);
            Assert.Equal(stacks, BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(8)));
        }

        [Fact]
        public void BuildPbx_ModeCheckAndQueryPackets()
        {
            byte[] check = SocialPacketBuilder.BuildPbxCheck(DeliveryBox.Incoming);
            Assert.Equal(-1, (sbyte)check[6]);
            Assert.Equal(-1, (sbyte)check[7]);

            byte[] query = SocialPacketBuilder.BuildPbxQuery("cybin");
            Assert.Equal((byte)DeliveryCommand.Query, query[4]);
            Assert.Equal(-1, (sbyte)query[5]);
            Assert.Equal("Cybin", Encoding.ASCII.GetString(query, 16, 5));

            Assert.Equal((byte)DeliveryCommand.DeliOpen, SocialPacketBuilder.BuildPbxMode(DeliveryCommand.DeliOpen)[4]);
            Assert.Equal((byte)DeliveryCommand.PostalClose, SocialPacketBuilder.BuildPbxMode(DeliveryCommand.PostalClose)[4]);
        }

        [Fact]
        public void S2C_0x04B_ShortFormCarriesOnlyTheCounts()
        {
            var result = new S2C_0x04B_PbxResult(PbxResult(DeliveryCommand.Check, 1, -1, 1, p2: 5));

            Assert.True(result.IsValid);
            Assert.False(result.HasItemState);
            Assert.Equal(DeliveryCommand.Check, result.Command);
            Assert.Equal(DeliveryResultCode.Success, result.ResultCode);
            Assert.Equal(5, result.ResParam2);
            Assert.Equal(DeliveryItemStat.None, result.Stat);
            Assert.False(new S2C_0x04B_PbxResult(new byte[15]).IsValid);
        }

        [Fact]
        public void DeliveryBoxState_FollowsTheAnswers()
        {
            var parser = NewParser(out var dispatcher);
            var delivery = parser.Delivery;
            int changes = 0;
            delivery.Changed += () => changes++;

            Receive(dispatcher, 0x04B, PbxResult(DeliveryCommand.PostOpen, -1, -1, 1));
            Assert.Equal(DeliveryBox.Incoming, delivery.OpenBox);

            Receive(dispatcher, 0x04B, PbxResult(DeliveryCommand.Check, 1, -1, 1, p2: 3));
            Receive(dispatcher, 0x04B, PbxResult(DeliveryCommand.Check, 2, -1, 1, p3: 2));
            Assert.Equal(3, delivery.IncomingCount);
            Assert.Equal(2, delivery.OutgoingCount);

            Receive(dispatcher, 0x04B, PbxResult(DeliveryCommand.Work, 1, 4, 1, stat: DeliveryItemStat.RecvDone, name: "Ayame", itemId: 4096, quantity: 12));
            var slot = delivery.GetSlot(DeliveryBox.Incoming, 4)!;
            Assert.Equal(DeliveryItemStat.RecvDone, slot.Stat);
            Assert.Equal("Ayame", slot.Name);
            Assert.True(slot.CanReturn);
            Assert.Equal(4096, slot.ItemId);
            Assert.Equal(12u, slot.Quantity);
            Assert.Equal(77u, slot.RequestId);
            Assert.Equal(0xAB, slot.ExtData[0]);

            Receive(dispatcher, 0x04B, PbxResult(DeliveryCommand.Work, 1, 5, 1, stat: DeliveryItemStat.RecvDone, name: "AH-Jeuno", itemId: 4097, quantity: 1));
            Assert.False(delivery.GetSlot(DeliveryBox.Incoming, 5)!.CanReturn);
            Assert.Equal(2, delivery.SnapshotSlots(DeliveryBox.Incoming).Count);

            // A finished Get: the item is resent with an empty state, which empties the slot.
            Receive(dispatcher, 0x04B, PbxResult(DeliveryCommand.Get, 1, 4, 1, stat: DeliveryItemStat.None, itemId: 4096, quantity: 12));
            Assert.Null(delivery.GetSlot(DeliveryBox.Incoming, 4));

            // An error leaves the slots alone and is recorded.
            Receive(dispatcher, 0x04B, PbxResult(DeliveryCommand.Get, 1, 5, 0xB9));
            Assert.NotNull(delivery.GetSlot(DeliveryBox.Incoming, 5));
            Assert.Equal(DeliveryResultCode.ProbInventoryFull, delivery.LastResponse!.Result);
            Assert.False(delivery.LastResponse.Succeeded);

            Receive(dispatcher, 0x04B, PbxResult(DeliveryCommand.PostalClose, -1, -1, 1));
            Assert.Equal(DeliveryBox.None, delivery.OpenBox);
            Assert.True(changes >= 8);

            delivery.Clear();
            Assert.Empty(delivery.SnapshotSlots(DeliveryBox.Incoming));
            Assert.Equal(-1, delivery.IncomingCount);
        }

        [Fact]
        public async Task DeliveryQuery_RecordsWhetherTheNameExists()
        {
            var parser = NewParser(out var dispatcher);
            await parser.SocialModule.QueryRecipientAsync("ayame");

            Assert.Equal("ayame", parser.Delivery.LastQueriedName);
            Receive(dispatcher, 0x04B, PbxResult(DeliveryCommand.Query, -1, -1, 1, p1: 1));
            Assert.True(parser.Delivery.LastQueryFound);
            Receive(dispatcher, 0x04B, PbxResult(DeliveryCommand.Query, -1, -1, 0));
            Assert.False(parser.Delivery.LastQueryFound);
        }

        // ---- blacklist ----

        private static byte[] BlackListPage(byte stat, params (uint Id, string Name)[] entries)
        {
            var p = new byte[244];
            for (int i = 0; i < entries.Length; i++)
            {
                U32(p, i * 20, entries[i].Id);
                Text(p, (i * 20) + 4, entries[i].Name);
            }
            p[240] = stat;
            p[241] = (byte)entries.Length;
            return p;
        }

        [Fact]
        public void S2C_0x041_DecodesAPage()
        {
            var page = new S2C_0x041_BlackList(BlackListPage(0x03, (0x1001, "Ayame"), (0x1002, "Cybin")));

            Assert.True(page.IsValid);
            Assert.True(page.ResetsList);
            Assert.True(page.IsLastPage);
            Assert.Equal(2, page.Count);
            Assert.Equal(0x1002u, page.GetId(1));
            Assert.Equal("Cybin", page.GetName(1));
            Assert.Equal(0u, page.GetId(2));
            Assert.False(new S2C_0x041_BlackList(new byte[243]).IsValid);
        }

        [Fact]
        public void BlacklistState_FillsFromPagesAndEdits()
        {
            var parser = NewParser(out var dispatcher);
            var list = parser.Blacklist;

            Receive(dispatcher, 0x041, BlackListPage(0x01, (1, "Ayame")));
            Assert.False(list.IsComplete);
            Receive(dispatcher, 0x041, BlackListPage(0x02, (2, "Cybin")));
            Assert.True(list.IsComplete);
            Assert.Equal(2, list.Count);
            Assert.True(list.IsBlacklisted(1u));
            Assert.True(list.IsBlacklisted("cybin"));

            var add = new byte[24];
            U32(add, 0, 3);
            Text(add, 4, "Zed");
            add[20] = (byte)BlacklistEditMode.Add;
            Receive(dispatcher, 0x042, add);
            Assert.True(list.IsBlacklisted(3u));

            add[20] = (byte)BlacklistEditMode.Delete;
            Receive(dispatcher, 0x042, add);
            Assert.False(list.IsBlacklisted(3u));

            bool failed = false;
            list.EditFailed += () => failed = true;
            add[20] = (byte)BlacklistEditMode.Error;
            Receive(dispatcher, 0x042, add);
            Assert.True(failed);

            // A new list replaces the old one.
            Receive(dispatcher, 0x041, BlackListPage(0x03));
            Assert.Equal(0, list.Count);
            Assert.True(list.IsComplete);
        }

        [Fact]
        public void BlacklistedPlayersMessagesAreDropped()
        {
            var parser = NewParser(out var dispatcher);
            var received = new List<SystemMessage>();
            parser.ChatModule.SystemMessageReceived += received.Add;
            Receive(dispatcher, 0x041, BlackListPage(0x03, (0x1001, "Ayame")));

            byte[] Message(uint sender, byte attr)
            {
                var p = new byte[9 + 8];
                U32(p, 0, sender);
                U16(p, 6, 123);
                p[8] = attr;
                Text(p, 9, "Para0 1");
                return p;
            }

            Receive(dispatcher, 0x009, Message(0x1001, 0x10));   // a blacklisted player's: dropped
            Receive(dispatcher, 0x009, Message(0x1002, 0x10));   // another player's: shown
            Receive(dispatcher, 0x009, Message(0x1001, 0x00));   // not a player message (no Attr 0x10): shown

            Assert.Equal(2, received.Count);
            Assert.Equal(0x1002u, received[0].UniqueNo);
        }

        [Fact]
        public void BuildBlackEdit_And_BlackList()
        {
            byte[] list = SocialPacketBuilder.BuildBlackList(4);
            Assert.Equal((0x03C, 28, 4), Header(list));
            Assert.All(list.Skip(4), b => Assert.Equal(0, b));

            byte[] add = SocialPacketBuilder.BuildBlackEdit("Ayame", BlacklistEditMode.Add);
            Assert.Equal((0x03D, 28, 0), Header(add));
            Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(add.AsSpan(4)));
            Assert.Equal("Ayame", Encoding.ASCII.GetString(add, 8, 5));
            Assert.Equal(0, add[24]);

            Assert.Equal(1, SocialPacketBuilder.BuildBlackEdit("Ayame", BlacklistEditMode.Delete)[24]);
        }

        // ---- world pass, item search ----

        [Fact]
        public void BuildFriendPass_And_ItemSearch()
        {
            byte[] pass = SocialPacketBuilder.BuildFriendPass(FriendPassPara.ConfirmGoldPurchase);
            Assert.Equal((0x01B, 28, 0), Header(pass));
            Assert.Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(pass.AsSpan(4)));

            byte[] search = SocialPacketBuilder.BuildItemSearch("Cure Potion", ItemSearchLanguage.English, 6);
            Assert.Equal((0x02C, 72, 6), Header(search));
            Assert.Equal(1, search[4]);
            Assert.Equal("Cure Potion", Encoding.ASCII.GetString(search, 8, 11));
            Assert.Equal(0, search[19]);
            Assert.Equal(72, search.Length);
        }

        [Fact]
        public void FriendPassAndItemSearchAnswers_FillTheState()
        {
            var parser = NewParser(out var dispatcher);

            var pass = new byte[32];
            U32(pass, 0, 1);
            U32(pass, 4, 167);
            U32(pass, 8, 10000);
            Text(pass, 12, "1234567890");
            pass[28] = 6;
            Receive(dispatcher, 0x059, pass);
            Assert.Equal(new FriendPassInfo(1, 167, 10000, "1234567890", 6), parser.Social.LastFriendPass);

            // The character holds item 4096 in the inventory (slot 5, 12 of them) and the wardrobe (slot 2).
            parser.Inventory.GetContainer(ContainerId.Inventory)._items[5] = new InventoryItem(4096, 12, 5, ContainerId.Inventory, 0, ItemLockFlag.Normal);
            parser.Inventory.GetContainer(ContainerId.Wardrobe)._items[2] = new InventoryItem(4096, 1, 2, ContainerId.Wardrobe, 0, ItemLockFlag.Normal);

            var search = new byte[68];
            U16(search, 0, 4096);
            Text(search, 4, "Cure Potion");
            Receive(dispatcher, 0x049, search);

            var result = parser.Social.LastItemSearch!;
            Assert.Equal(4096, result.ItemId);
            Assert.Equal("Cure Potion", result.ItemName);
            Assert.False(result.IsAsync);
            Assert.Equal(new[] { (ContainerId.Inventory, 12u), (ContainerId.Wardrobe, 1u) }, result.Containers.ToArray());

            search[2] = 1;
            Receive(dispatcher, 0x049, search);
            Assert.True(parser.Social.LastItemSearch!.IsAsync);
            Assert.Empty(parser.Social.LastItemSearch.Containers);
        }

        // ---- linkshell ----

        [Fact]
        public void BuildComlinkPackets()
        {
            byte[] make = SocialPacketBuilder.BuildComlinkMake(2, 3);
            Assert.Equal((0x0C3, 8, 3), Header(make));
            Assert.Equal(0, make[4]);
            Assert.Equal(2, make[5]);

            byte[] equip = SocialPacketBuilder.BuildComlinkActive(true, 1, 12, ContainerId.Inventory);
            Assert.Equal((0x0C4, 28, 0), Header(equip));
            Assert.Equal(0xF, BinaryPrimitives.ReadUInt16LittleEndian(equip.AsSpan(4)) >> 12);   // a == 15
            Assert.Equal(12, equip[6]);
            Assert.Equal((byte)ContainerId.Inventory, equip[7]);
            Assert.Equal(1, equip[8]);
            Assert.Equal("dummy", Encoding.ASCII.GetString(equip, 12, 5));
            Assert.Equal(1, equip[27]);

            Assert.Equal(0, SocialPacketBuilder.BuildComlinkActive(false, 2, 12, ContainerId.Inventory)[8]);
            Assert.Equal(2, SocialPacketBuilder.BuildComlinkActive(false, 2, 12, ContainerId.Inventory)[27]);
        }

        [Fact]
        public void BuildComlinkCreate_PacksTheNameAndColor()
        {
            byte[]? create = SocialPacketBuilder.BuildComlinkCreate("Moogles01", 3, 5, 9, 1, 7, ContainerId.Inventory);

            Assert.NotNull(create);
            ushort color = BinaryPrimitives.ReadUInt16LittleEndian(create.AsSpan(4));
            Assert.Equal(3, color & 0xF);
            Assert.Equal(5, (color >> 4) & 0xF);
            Assert.Equal(9, (color >> 8) & 0xF);
            Assert.Equal(15, color >> 12);
            Assert.Equal("Moogles01", LinkshellNameCodec.Decode(create.AsSpan(12, 15)));
            Assert.Equal(1, create[8]);

            Assert.Null(SocialPacketBuilder.BuildComlinkCreate(new string('a', 21), 0, 0, 0, 1, 7, ContainerId.Inventory));
        }

        [Fact]
        public void S2C_0x048_DecodesTheHeaderAndRecordForms()
        {
            var header = new byte[124];
            header[0] = header[1] = header[2] = header[3] = 0xFE;
            U16(header, 4, 6);
            U16(header, 6, 0xFFFF);
            Text(header, 24, "22d");
            header[44] = 1;
            var h = new S2C_0x048_LinkConcierge(header);
            Assert.True(h.IsHeader);
            Assert.Equal(6, h.SlotIndex);
            Assert.True(h.Registered);
            Assert.Equal(22, h.PostedDays);

            var record = new byte[124];
            for (int i = 0; i < 4; i++) record[i] = 0xFF;
            record[1] = 9;                               // slot 1 holds concierge slot 9
            U32(record, 12 + 24, 555);                    // GroupId
            U16(record, 12 + 24 + 4, 0x0204);             // GroupKey
            U16(record, 12 + 24 + 6, 0x1234);             // Color
            record[12 + 24 + 8] = 7;                      // Flag
            Assert.True(LinkshellNameCodec.TryEncode("Moogles", record.AsSpan(12 + 24 + 9, 15)));
            // Active, JP, EN, Other; goal 8; tier 2; characteristics 0xBEEF.
            U32(record, 12 + 96 + 4, 1u | 2u | 4u | 0x20u | (8u << 6) | (2u << 14) | (0xBEEFu << 16));
            var r = new S2C_0x048_LinkConcierge(record);

            Assert.False(r.IsHeader);
            Assert.False(r.TryGetLinkshell(0, out _));
            Assert.True(r.TryGetLinkshell(1, out var ls));
            Assert.Equal(9, ls.SlotIndex);
            Assert.Equal(555u, ls.GroupId);
            Assert.Equal("Moogles", ls.Name);
            Assert.Equal(0x1234, ls.Color);
            Assert.True(ls.Active && ls.LanguageJapanese && ls.LanguageEnglish && ls.LanguageOther);
            Assert.Equal(8, ls.MembersGoal);
            Assert.Equal(2, ls.ActiveTier);
            Assert.Equal(0xBEEF, ls.Characteristics);
            Assert.False(new S2C_0x048_LinkConcierge(new byte[123]).IsValid);

            var parser = NewParser(out var dispatcher);
            Receive(dispatcher, 0x048, header);
            Receive(dispatcher, 0x048, record);
            Assert.Equal(6, parser.Social.ConciergeOwnSlot);
            Assert.Equal(22, parser.Social.ConciergePostedDays);
            Assert.Equal("Moogles", parser.Social.GetConciergeLinkshell(9)!.Value.Name);
        }

        // ---- group id, map group ----

        [Fact]
        public void GroupIdAndMapGroup_RoundTrip()
        {
            byte[] check = SocialPacketBuilder.BuildGroupCheckId(2);
            Assert.Equal((0x078, 4, 2), Header(check));
            byte[] map = SocialPacketBuilder.BuildMapGroup(230, 3);
            Assert.Equal((0x0D2, 8, 3), Header(map));
            Assert.Equal(230u, BinaryPrimitives.ReadUInt32LittleEndian(map.AsSpan(4)));

            var parser = NewParser(out var dispatcher);
            var id = new byte[4];
            U32(id, 0, 0x4242);
            Receive(dispatcher, 0x0E1, id);
            Assert.Equal(0x4242u, parser.Social.GroupId);

            var pos = new byte[20];
            U32(pos, 0, 0x01000009);
            U16(pos, 4, 230);
            BinaryPrimitives.WriteSingleLittleEndian(pos.AsSpan(8), 1.5f);
            BinaryPrimitives.WriteSingleLittleEndian(pos.AsSpan(12), -2.5f);
            BinaryPrimitives.WriteSingleLittleEndian(pos.AsSpan(16), 3.5f);
            Receive(dispatcher, 0x0A0, pos);

            var member = Assert.Single(parser.Social.SnapshotMapGroup());
            Assert.Equal(0x01000009u, member.ServerId);
            Assert.Equal(230, member.ZoneId);
            Assert.Equal(new System.Numerics.Vector3(1.5f, -2.5f, 3.5f), member.Position);
        }

        // ---- module sends ----

        [Fact]
        public async Task SocialModule_SendsWhatTheBuildersBuild()
        {
            var sent = new List<byte[]>();
            var parser = NewParser(out _, sent);
            var module = parser.SocialModule;

            await module.OpenDeliveryAsync();
            await module.SetOutgoingAsync(0, 5, 3, "ayame");
            await module.RequestBlacklistAsync();
            await module.AddToBlacklistAsync("Zed");
            await module.SendItemSearchAsync("Potion");
            await module.RequestGroupIdAsync();
            await module.RequestMapGroupAsync(230);
            Assert.True(await module.CreateLinkshellAsync("Moogles", 1, 2, 3, 1, 4, ContainerId.Inventory));
            Assert.False(await module.CreateLinkshellAsync(new string((char)97, 21), 1, 2, 3, 1, 4, ContainerId.Inventory));

            Assert.Equal(new ushort[] { 0x04D, 0x04D, 0x03C, 0x03D, 0x02C, 0x078, 0x0D2, 0x0C4 }, sent.Select(p => Header(p).Id).ToArray());
            // Every packet advances the sequence number.
            Assert.Equal(Enumerable.Range(1, 8).Select(i => (ushort)i).ToArray(), sent.Select(p => Header(p).Seq).ToArray());
        }

        [Fact]
        public void CommandRouter_ParsesItemSearchAndBlacklist()
        {
            var search = ChatCommandRouter.Parse("/itemsearch Cure Potion");
            Assert.Equal(ChatCommandResultKind.ItemSearch, search.Kind);
            Assert.Equal("Cure Potion", search.Message);

            var black = ChatCommandRouter.Parse("/blacklist add Ayame");
            Assert.Equal(ChatCommandResultKind.Blacklist, black.Kind);
            Assert.Equal("add Ayame", black.Message);
        }

        [Fact]
        public async Task ItemSearchCommand_ReportsTheContainers()
        {
            var sent = new List<byte[]>();
            var parser = NewParser(out var dispatcher, sent);
            parser.Inventory.GetContainer(ContainerId.Inventory)._items[5] = new InventoryItem(4096, 12, 5, ContainerId.Inventory, 0, ItemLockFlag.Normal);

            var task = parser.ActionService.ItemSearchAsync("Cure Potion");
            // The command subscribes to the answer and sends the request before its first real await, so the request is out
            // by now; answer at once. (A fixed delay here raced the command's reply timeout on a busy thread pool: the timeout
            // completes on the timer thread, while the delayed answer needed a pool thread to run.)
            Assert.False(task.IsCompleted);
            Assert.Equal(0x02C, Header(Assert.Single(sent)).Id);
            var search = new byte[68];
            U16(search, 0, 4096);
            Text(search, 4, "Cure Potion");
            Receive(dispatcher, 0x049, search);
            var result = await task;

            Assert.Contains("Inventory (12)", result.Message);
        }

        [Fact]
        public async Task BlacklistCommand_AddsAndListsFromTheState()
        {
            var sent = new List<byte[]>();
            var parser = NewParser(out var dispatcher, sent);
            Receive(dispatcher, 0x041, BlackListPage(0x03, (1, "Ayame"), (2, "Cybin")));

            var added = await parser.ActionService.BlacklistAsync("add Zed");
            Assert.Contains("Zed", added.Message);
            Assert.Equal(0x03D, Header(sent[^1]).Id);

            var listed = await parser.ActionService.BlacklistAsync("list");
            Assert.Contains("Ayame, Cybin", listed.Message);

            var usage = await parser.ActionService.BlacklistAsync("add");
            Assert.Contains("Usage", usage.Message);
        }
    }
}
