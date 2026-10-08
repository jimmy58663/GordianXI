// tests/Gordian.Core.Tests/Network/CraftingRound2Tests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Animation;
using Gordian.Core.Events;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// Round 2 of #112 (in-game test, 2026-10-07): the retail message wording, skill-up lines, the synthesis lock, the
    /// synthesis animation hand-off and the guild shop log.
    /// </summary>
    public class CraftingRound2Tests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private static string? Resolve(byte kind, int id) => kind switch
        {
            0x24 or 0x27 => id == 640 ? "seashell" : "ponze of shell powder",
            0x25 => id == 640 ? "seashells" : "ponzes of shell powder",
            _ => $"<{(char)kind}{id}>",
        };

        private static EventMessage CountMessage() => new(new[]
        {
            new EventMessageSegment(EventMessageSegmentKind.Text, "You synthesized "),
            new EventMessageSegment(EventMessageSegmentKind.Name, Code: 0x04, Values: new[] { 1 }),
            new EventMessageSegment(EventMessageSegmentKind.Text, " "),
            new EventMessageSegment(EventMessageSegmentKind.Name, Code: 0x2A, Values: new[] { 1, 0 }),
            new EventMessageSegment(EventMessageSegmentKind.Text, "."),
        });

        [Theory]
        [InlineData(1, true, "You synthesized a ponze of shell powder.")]
        [InlineData(3, true, "You synthesized 3 ponzes of shell powder.")]
        [InlineData(1, false, "You synthesized 1 ponze of shell powder.")]
        public void CountOneIsArticle_PrintsAnArticleForOne(int count, bool article, string expected)
        {
            var context = new SimpleMessageContext(new[] { 5000, count }, "Me", string.Empty, Resolve) { CountOneIsArticle = article };

            Assert.Equal(expected, EventMessageFormatter.FormatLines(CountMessage(), context).Single());
        }

        [Fact]
        public void RetailTable_PrintsTheSynthesisLinesAsRetailDoes()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var messages = new ClientMessageController(new ClientMessageTables(rm.LoadDatBytesByFileId), _ => null) { NameResolver = Resolve };

            var ok = new SynthesisOutcome(SynthesisAnswer.Success, 0, 0, 1, 5000, 4096, new ushort[8], new ushort[8], Array.Empty<SynthesisSkillUp>());
            Assert.Equal("You synthesized a ponze of shell powder.", CraftingLog.FormatOwn(ok, null, messages).Single());

            var lost = new ushort[8];
            lost[0] = 640;
            lost[1] = 640;
            var fail = new SynthesisOutcome(SynthesisAnswer.Failed, 1, -1, 1, CraftingLog.MangledMessItemId, 4096, lost, new ushort[8], Array.Empty<SynthesisSkillUp>());
            Assert.Equal(new[]
            {
                "Synthesis failed. You lost the crystal you were using.",
                "A seashell was lost.",
                "A seashell was lost.",
            }, CraftingLog.FormatOwn(fail, null, messages).ToArray());

            var other = new OtherSynthesisOutcome(SynthesisAnswer.Success, 0, 0, 1, 5000, new ushort[8], 1, 1, "Gemini");
            Assert.Equal("Gemini synthesized a ponze of shell powder.", CraftingLog.FormatOther(other, null, messages).Single());

            var otherLost = new OtherSynthesisOutcome(SynthesisAnswer.Failed, 1, -1, 1, 0, lost, 1, 1, "Gemini");
            Assert.Equal(new[] { "Gemini lost a seashell.", "Gemini lost a seashell." }, CraftingLog.FormatOther(otherLost, null, messages).ToArray());
        }

        [Theory]
        [InlineData(1, "Knot's bonecraft skill rises 0.1 points.")]
        [InlineData(25, "Knot's bonecraft skill rises 2.5 points.")]
        public void SkillUpMessage_NamesTheSkillAndTheGain(uint tenths, string expected)
        {
            var record = new CombatMessageRecord { CasterId = 1, TargetId = 1, MessageId = 38, Param = 54, Value = tenths };

            Assert.Equal(expected, CombatLogFormatter.FormatBattleMessage(record, _ => "Knot"));
        }

        [Fact]
        public void SkillLevelMessage_NamesTheSkillAndTheLevel()
        {
            var record = new CombatMessageRecord { CasterId = 1, TargetId = 1, MessageId = 53, Param = 3, Value = 12 };

            Assert.Equal("Knot's sword skill reaches level 12.", CombatLogFormatter.FormatBattleMessage(record, _ => "Knot"));
        }

        [Fact]
        public async Task SynthesisLock_StartsOnSend_AndEndsOnTheResult()
        {
            var state = new CraftingState();
            var module = new CraftingPacketModule(state, (_, _) => Task.CompletedTask);
            var dispatcher = new PacketDispatcher();
            module.Register(dispatcher);
            Assert.False(state.IsSynthesizing);

            await module.SynthesizeAsync(4096, 1, new (ushort, byte)[] { (640, 3) });
            Assert.True(state.IsSynthesizing);

            state.ConfirmSynthesis();
            Assert.True(state.IsSynthesizing);

            var payload = new byte[52];
            dispatcher.Dispatch(new PacketHeader(S2C_0x06F_CombineAns.PacketId, 56, 1), payload);
            Assert.False(state.IsSynthesizing);
        }

        [Fact]
        public void SynthesisMotion_MapsTheResultToTheRoutinePair()
        {
            Assert.Equal("lc01", SynthesisMotion.StartRoutine(0));
            Assert.Equal("ls02", SynthesisMotion.EndRoutine(1));
            Assert.Equal("lc05", SynthesisMotion.StartRoutine(4));
            Assert.Equal("lc06", SynthesisMotion.StartRoutine(200));
        }

        [Fact]
        public void SynthesisAnimation_PlaysTheStartOnTheCrafter_AndTheEndOnTheResult()
        {
            var combat = new CombatState();
            var crafting = new CraftingState();
            var world = new WorldState();
            var player = new LocalPlayerState { ServerId = 0x01000001 };
            world.UpsertEntity(new WorldEntity(0x01000001, 5, EntityType.Player) { Name = "Me" });
            world.UpsertEntity(new WorldEntity(0x01000002, 6, EntityType.Player) { Name = "Gemini" });
            var controller = new SynthesisAnimationController();
            controller.Attach(combat, crafting, world, player);
            var module = new CraftingPacketModule(crafting, (_, _) => Task.CompletedTask);
            var dispatcher = new PacketDispatcher();
            module.Register(dispatcher);

            // Another crafter: the start plays on them, the lock is untouched.
            combat.SetCraftEffect(new CraftEffectInfo(0x01000002, 6, SynthesisEffect.Fire, 2, 44, 0));
            Assert.Equal("lc03", controller.LastRoutine);
            Assert.False(crafting.IsSynthesizing);

            // Their result (S2C 0x070 names them by index) plays the end.
            var inf = new byte[44];
            BinaryPrimitives.WriteUInt16LittleEndian(inf.AsSpan(24, 2), 6);
            dispatcher.Dispatch(new PacketHeader(S2C_0x070_CombineInf.PacketId, 48, 1), inf);
            Assert.Equal("ls03", controller.LastRoutine);

            // The character: the start confirms the lock, the result plays the end and releases it.
            combat.SetCraftEffect(new CraftEffectInfo(0x01000001, 5, SynthesisEffect.Water, 0, 44, 0));
            Assert.Equal("lc01", controller.LastRoutine);
            Assert.True(crafting.IsSynthesizing);
            dispatcher.Dispatch(new PacketHeader(S2C_0x06F_CombineAns.PacketId, 56, 2), new byte[52]);
            Assert.Equal("ls01", controller.LastRoutine);
            Assert.False(crafting.IsSynthesizing);
        }

        [Fact]
        public void SynthesisAnimation_IgnoresAnEffectOfNone()
        {
            var combat = new CombatState();
            var world = new WorldState();
            var player = new LocalPlayerState { ServerId = 1 };
            world.UpsertEntity(new WorldEntity(1, 5, EntityType.Player));
            var controller = new SynthesisAnimationController();
            controller.Attach(combat, new CraftingState(), world, player);

            combat.SetCraftEffect(new CraftEffectInfo(1, 5, SynthesisEffect.None, 0, 0, 0));

            Assert.Equal(string.Empty, controller.LastRoutine);
        }

        [Fact]
        public void RetailBaseMotion_HasTheSynthesisRoutines()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var container = EntityModelLoader.ParseDatContainer(rm.LoadDatBytes(Path.Combine("ROM", "32", "58.DAT"))!, "base");
            var names = container.Routines.Select(r => r.Name).ToHashSet();
            for (byte type = 0; type <= 4; type++)
            {
                Assert.Contains(SynthesisMotion.StartRoutine(type), names);
                Assert.Contains(SynthesisMotion.EndRoutine(type), names);
            }
        }

        // ---- guild shop ----

        [Theory]
        [InlineData(true, 640, 5, 1, "You buy a ponze of shell powder from the guild.")]
        [InlineData(true, 640, 5, 4, "You buy 4 ponzes of shell powder from the guild.")]
        [InlineData(false, 640, 5, 1, "You sell a ponze of shell powder to the guild.")]
        [InlineData(false, 640, 5, 3, "You sell 3 ponzes of shell powder to the guild.")]
        public void GuildTransaction_Success_PrintsTheAmountTraded(bool purchase, ushort item, byte stock, sbyte trade, string expected)
        {
            var lines = GuildShopLog.FormatTransaction(new GuildTransaction(purchase, item, stock, trade), id => new ItemRecord
            {
                ItemId = id, Name = "Shell Powder", LogName = "ponze of shell powder", LogPlural = "ponzes of shell powder", StackSize = 12,
            }).ToArray();

            Assert.Equal(new[] { expected }, lines);
        }

        [Theory]
        [InlineData(true, -3, "Please wait longer before making another purchase.")]
        [InlineData(true, -5, "Transaction cancelled. You can only hold one item of that type.")]
        [InlineData(true, -1, "You were unable to carry out that transaction.")]
        [InlineData(false, -4, "You were unable to carry out that transaction.")]
        public void GuildTransaction_Failure_PrintsRetailsText(bool purchase, sbyte trade, string expected)
        {
            var lines = GuildShopLog.FormatTransaction(new GuildTransaction(purchase, 0, 0, trade), null).ToArray();

            Assert.Equal(new[] { expected }, lines);
        }

        [Fact]
        public void GuildSale_ZeroSold_And_PartialSale()
        {
            Assert.Equal("The guild would not buy that from you. Its stock of that item is full.",
                GuildShopLog.FormatTransaction(new GuildTransaction(false, 640, 10, 0), null).Single());

            var partial = GuildShopLog.FormatTransaction(new GuildTransaction(false, 640, 10, -1), id => new ItemRecord { ItemId = id, Name = "Seashell", LogName = "seashell", StackSize = 12 }).ToArray();
            Assert.Equal(new[] { "You sell a seashell to the guild.", "The guild could not purchase the full amount. Its stock of that item became full." }, partial);
        }

        [Fact]
        public void GuildList_And_Status_Lines()
        {
            var list = GuildShopLog.FormatList(true, new[] { new GuildItemEntry(640, 5, 20, 12) }, id => new ItemRecord { ItemId = id, Name = "Seashell", StackSize = 12 }).ToArray();
            Assert.Equal("[Guild] The guild sells 1 item:", list[0]);
            Assert.Equal("[Guild]   Seashell (item 640): 12 gil, stock 5/20", list[1]);

            Assert.Equal("[Guild] The guild shop is closed; it opens 05:00 to 09:00 (Vana'diel time).",
                GuildShopLog.FormatStatus(ShopOpenStatus.Close, new GuildHoursInfo(5, 9, 0)));
        }

        private static byte[] GuildList(byte stat, params (ushort Item, byte Stock, byte Max, int Price)[] items)
        {
            var p = new byte[242];
            for (int i = 0; i < items.Length; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(i * 8, 2), items[i].Item);
                p[(i * 8) + 2] = items[i].Stock;
                p[(i * 8) + 3] = items[i].Max;
                BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan((i * 8) + 4, 4), items[i].Price);
            }
            p[240] = (byte)items.Length;
            p[241] = stat;
            return p;
        }

        [Fact]
        public void GuildBuyList_CompletesOnTheLastPacket_AndKeepsStockAndPrice()
        {
            var inventory = new InventoryState();
            var module = new InventoryPacketModule(inventory, new LocalPlayerState(), (_, _) => Task.CompletedTask);
            var dispatcher = new PacketDispatcher();
            module.Register(dispatcher);
            var completed = new List<(bool Sells, GuildItemEntry[] Items)>();
            inventory.GuildListCompleted += (sells, items) => completed.Add((sells, items));

            // LandSandBoat: the first of several packets has Stat 0x40, the last PacketCount + 0x80.
            dispatcher.Dispatch(new PacketHeader(0x083, 248, 1), GuildList(0x40, (640, 5, 20, 12)));
            Assert.Empty(completed);
            dispatcher.Dispatch(new PacketHeader(0x083, 248, 2), GuildList(0x81, (641, 1, 10, 30)));

            var done = Assert.Single(completed);
            Assert.True(done.Sells);
            Assert.Equal(new[] { new GuildItemEntry(640, 5, 20, 12), new GuildItemEntry(641, 1, 10, 30) }, done.Items);
        }

        [Fact]
        public void GuildSellList_SinglePacket_CompletesAtOnce()
        {
            var inventory = new InventoryState();
            var module = new InventoryPacketModule(inventory, new LocalPlayerState(), (_, _) => Task.CompletedTask);
            var dispatcher = new PacketDispatcher();
            module.Register(dispatcher);
            var completed = new List<bool>();
            inventory.GuildListCompleted += (sells, _) => completed.Add(sells);

            dispatcher.Dispatch(new PacketHeader(0x085, 248, 1), GuildList(0x80, (640, 5, 20, 12)));

            Assert.Equal(new[] { false }, completed);
        }

        [Fact]
        public void GuildTransactionAndStatus_RaiseEvents()
        {
            var inventory = new InventoryState();
            var module = new InventoryPacketModule(inventory, new LocalPlayerState(), (_, _) => Task.CompletedTask);
            var dispatcher = new PacketDispatcher();
            module.Register(dispatcher);
            GuildTransaction? seen = null;
            inventory.GuildTransactionReceived += t => seen = t;

            var buy = new byte[4];
            BinaryPrimitives.WriteUInt16LittleEndian(buy.AsSpan(0, 2), 640);
            buy[2] = 7;
            buy[3] = 2;
            dispatcher.Dispatch(new PacketHeader(0x082, 8, 1), buy);

            Assert.Equal(new GuildTransaction(true, 640, 7, 2), seen);
        }
    }
}
