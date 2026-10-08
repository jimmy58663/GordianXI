// tests/Gordian.Core.Tests/Network/CraftingPacketTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class CraftingPacketTests
    {
        private static byte[] CombineAns(byte result, sbyte grade, byte count, ushort item, ushort[]? broken = null,
            (sbyte Kind, sbyte Level)[]? ups = null, ushort crystal = 4096, ushort[]? materials = null)
        {
            var p = new byte[52];
            p[0] = result;
            p[1] = (byte)grade;
            p[2] = count;
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4, 2), item);
            for (int i = 0; i < (broken?.Length ?? 0); i++) BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(6 + (i * 2), 2), broken![i]);
            for (int i = 0; i < (ups?.Length ?? 0); i++)
            {
                p[22 + i] = (byte)ups![i].Kind;
                p[26 + i] = (byte)ups[i].Level;
            }
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(30, 2), crystal);
            for (int i = 0; i < (materials?.Length ?? 0); i++) BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(32 + (i * 2), 2), materials![i]);
            return p;
        }

        private static byte[] CombineInf(byte result, sbyte grade, byte count, ushort item, string name, ushort[]? broken = null)
        {
            var p = new byte[44];
            p[0] = result;
            p[1] = (byte)grade;
            p[2] = count;
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4, 2), item);
            for (int i = 0; i < (broken?.Length ?? 0); i++) BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(6 + (i * 2), 2), broken![i]);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(22, 2), 0x1234);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(24, 2), 0x0201);
            Encoding.ASCII.GetBytes(name).CopyTo(p.AsSpan(26));
            return p;
        }

        private static (CraftingState, PacketDispatcher, List<byte[]>) Create()
        {
            var state = new CraftingState();
            var sent = new List<byte[]>();
            var module = new CraftingPacketModule(state, (data, _) =>
            {
                sent.Add(data.ToArray());
                return Task.CompletedTask;
            });
            var dispatcher = new PacketDispatcher();
            module.Register(dispatcher);
            return (state, dispatcher, sent);
        }

        private static void Dispatch(PacketDispatcher d, ushort id, byte[] payload) =>
            d.Dispatch(new PacketHeader(id, payload.Length + 4, 1), payload);

        private static ItemRecord Item(ushort id) => new() { ItemId = id, Name = "Bronze Ingot", LogName = "bronze ingot", StackSize = 12 };

        #region S2C 0x06F

        [Fact]
        public void CombineAns_DecodesEveryField()
        {
            var d = new S2C_0x06F_CombineAns(CombineAns(0x00, 2, 3, 650, broken: new ushort[] { 0, 0, 640 }, ups: new (sbyte, sbyte)[] { (50, 1), (51, 0) },
                crystal: 4098, materials: new ushort[] { 640, 640, 641 }));

            Assert.True(d.IsValid);
            Assert.Equal(SynthesisAnswer.Success, d.Result);
            Assert.Equal(2, d.Grade);
            Assert.Equal(3, d.Count);
            Assert.Equal(650, d.ItemId);
            Assert.Equal(640, d.GetBreakNo(2));
            Assert.Equal(0, d.GetBreakNo(0));
            Assert.Equal(50, d.GetUpKind(0));
            Assert.Equal(1, d.GetUpLevel(0));
            Assert.Equal(4098, d.CrystalNo);
            Assert.Equal(641, d.GetMaterialNo(2));
            Assert.Equal(0, d.GetMaterialNo(8)); // out of range is 0, not an exception
        }

        [Fact]
        public void CombineAns_ShortPayload_IsInvalid() =>
            Assert.False(new S2C_0x06F_CombineAns(new byte[47]).IsValid);

        [Fact]
        public void CombineAns_Handler_RaisesOutcomeWithSkillUpsOnly()
        {
            var (state, d, _) = Create();
            SynthesisOutcome? seen = null;
            state.SynthesisCompleted += o => seen = o;

            Dispatch(d, S2C_0x06F_CombineAns.PacketId, CombineAns(0x01, -1, 1, CraftingLog.MangledMessItemId,
                broken: new ushort[] { 640, 0, 641 }, ups: new (sbyte, sbyte)[] { (49, 0), (53, 3) }));

            Assert.NotNull(seen);
            Assert.Same(seen, state.LastOutcome);
            Assert.Equal(SynthesisAnswer.Failed, seen!.Result);
            Assert.False(seen.IsSuccess);
            Assert.Equal(new ushort[] { 640, 0, 641, 0, 0, 0, 0, 0 }, seen.LostItemIds);
            // UpKind 49 had no gain and is not a skill-up; 53 gained 0.3.
            var up = Assert.Single(seen.SkillUps);
            Assert.Equal(53, up.SkillId);
            Assert.Equal(3, up.Tenths);
        }

        [Theory]
        [InlineData(0x00, true, false)]
        [InlineData(0x0C, true, false)]
        [InlineData(0x01, false, false)]
        [InlineData(0x02, false, false)]
        [InlineData(0x03, false, true)]
        [InlineData(0x06, false, true)]
        [InlineData(0x0D, false, true)]
        [InlineData(0x0E, false, false)]
        public void SynthesisOutcome_Classifies(byte result, bool success, bool canceled)
        {
            var o = new SynthesisOutcome((SynthesisAnswer)result, result, 0, 1, 1, 4096, Array.Empty<ushort>(), Array.Empty<ushort>(), Array.Empty<SynthesisSkillUp>());
            Assert.Equal(success, o.IsSuccess);
            Assert.Equal(canceled, o.WasCanceled);
        }

        #endregion

        #region S2C 0x070

        [Fact]
        public void CombineInf_DecodesEveryField()
        {
            var d = new S2C_0x070_CombineInf(CombineInf(0x0C, 1, 2, 650, "Ayame", new ushort[] { 640 }));

            Assert.True(d.IsValid);
            Assert.Equal(SynthesisAnswer.SuccessDesynth, d.Result);
            Assert.Equal(1, d.Grade);
            Assert.Equal(2, d.Count);
            Assert.Equal(650, d.ItemId);
            Assert.Equal(640, d.GetBreakNo(0));
            Assert.Equal(0x1234, d.UniqueNo);
            Assert.Equal(0x0201, d.ActIndex);
            Assert.Equal("Ayame", d.Name);
        }

        [Fact]
        public void CombineInf_Handler_RaisesOutcome()
        {
            var (state, d, _) = Create();
            OtherSynthesisOutcome? seen = null;
            state.OtherSynthesisCompleted += o => seen = o;

            Dispatch(d, S2C_0x070_CombineInf.PacketId, CombineInf(0x00, 0, 1, 650, "Kupo"));

            Assert.NotNull(seen);
            Assert.Equal("Kupo", seen!.Name);
            Assert.True(seen.IsSuccess);
            Assert.Equal(650, seen.ItemId);
        }

        [Fact]
        public void CombineInf_ShortPayload_IsInvalid() =>
            Assert.False(new S2C_0x070_CombineInf(new byte[41]).IsValid);

        #endregion

        #region S2C 0x031

        private static byte[] Recipe(RecipeKind kind)
        {
            var p = new byte[48];
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(44, 2), (ushort)kind);
            return p;
        }

        [Fact]
        public void Recipe_Detail_DecodesFields()
        {
            var p = Recipe(RecipeKind.Detail2);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0, 2), 650);   // product
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(2, 2), 3);     // sub-craft 1
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(8, 2), 4096);  // crystal
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(10, 2), 321);  // key item
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(12, 2), 640);  // ingredient 0
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(14, 2), 641);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(28, 2), 2);    // count 0
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(30, 2), 1);

            var d = new S2C_0x031_Recipe(p);

            Assert.True(d.IsValid);
            Assert.Equal(RecipeKind.Detail2, d.Kind);
            Assert.Equal(650, d.ProductItem);
            Assert.Equal(3, d.GetNeedSkill(0));
            Assert.Equal(0, d.GetNeedSkill(1));
            Assert.Equal(4096, d.NeedItem);
            Assert.Equal(321, d.NeedKeyItem);
            Assert.Equal(641, d.GetIngredientItem(1));
            Assert.Equal(2, d.GetIngredientCount(0));
            Assert.Equal(0, d.GetListItem(0)); // not a list
        }

        [Fact]
        public void Recipe_List_DecodesPageAndNext()
        {
            var p = Recipe(RecipeKind.List);
            for (int i = 0; i < 16; i++) BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(12 + (i * 2), 2), (ushort)(700 + i));
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(46, 2), 716);

            var d = new S2C_0x031_Recipe(p);

            Assert.Equal(RecipeKind.List, d.Kind);
            Assert.Equal(700, d.GetListItem(0));
            Assert.Equal(715, d.GetListItem(15));
            Assert.Equal(716, d.ListNextItem);
            Assert.Equal(0, d.ProductItem);
        }

        [Fact]
        public void Recipe_Handler_RaisesDetailAndList()
        {
            var (state, d, _) = Create();
            RecipeDetail? detail = null;
            RecipeListPage? page = null;
            state.RecipeReceived += r => detail = r;
            state.RecipeListReceived += l => page = l;

            var det = Recipe(RecipeKind.Detail1);
            BinaryPrimitives.WriteUInt16LittleEndian(det.AsSpan(0, 2), 650);
            Dispatch(d, S2C_0x031_Recipe.PacketId, det);

            var list = Recipe(RecipeKind.List);
            BinaryPrimitives.WriteUInt16LittleEndian(list.AsSpan(12, 2), 700);
            BinaryPrimitives.WriteUInt16LittleEndian(list.AsSpan(14, 2), 701);
            Dispatch(d, S2C_0x031_Recipe.PacketId, list);

            Assert.Equal(650, detail!.ProductItem);
            Assert.Equal(new ushort[] { 700, 701 }, page!.ItemIds);
            Assert.Equal(0, page.NextPageItem);
        }

        #endregion

        #region C2S builders

        [Fact]
        public void ComputeCombineHash_MatchesDocumentedFormula()
        {
            // (4096 + 3) * (640 + 7) * (2 + 5) % 0x7F
            Assert.Equal((byte)((4099 * 647 * 7) % 0x7F), CraftingPacketBuilders.ComputeCombineHash(4096, 640, 2));
            // A cluster (6506-6513) and a 4238-4245 item fold onto the 4096 crystal the same way.
            byte plain = CraftingPacketBuilders.ComputeCombineHash(4098, 640, 2);
            Assert.Equal(plain, CraftingPacketBuilders.ComputeCombineHash(4098 + 2410, 640, 2));
            Assert.Equal(plain, CraftingPacketBuilders.ComputeCombineHash(4098 + 142, 640, 2));
        }

        [Fact]
        public void BuildCombineAsk_SortsByItemIdAndWritesFields()
        {
            var buf = new byte[36];
            var items = new (ushort ItemId, byte Slot)[] { (900, 7), (640, 3), (641, 9) };

            int len = CraftingPacketBuilders.BuildCombineAsk(buf, 5, 4096, 2, items);

            Assert.Equal(36, len);
            Assert.True(PacketHeader.TryParse(buf, out var h));
            Assert.Equal(0x096, h.PacketId);
            Assert.Equal(36, h.TotalSize);
            Assert.Equal(5, h.SequenceId);
            Assert.Equal(CraftingPacketBuilders.ComputeCombineHash(4096, 640, 3), buf[4]);
            Assert.Equal(0, buf[5]);
            Assert.Equal(4096, BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(6, 2)));
            Assert.Equal(2, buf[8]);
            Assert.Equal(3, buf[9]);
            Assert.Equal(640, BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(10, 2)));
            Assert.Equal(641, BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(12, 2)));
            Assert.Equal(900, BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(14, 2)));
            Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(16, 2)));
            Assert.Equal(3, buf[26]); // slots follow their items
            Assert.Equal(9, buf[27]);
            Assert.Equal(7, buf[28]);
            Assert.Equal(0, buf[29]);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(9)]
        public void BuildCombineAsk_RejectsBadIngredientCount(int count)
        {
            var items = new (ushort, byte)[count];
            Assert.Equal(0, CraftingPacketBuilders.BuildCombineAsk(new byte[36], 1, 4096, 1, items));
        }

        [Fact]
        public void BuildRecipe_WritesFields()
        {
            var buf = new byte[20];

            int len = CraftingPacketBuilders.BuildRecipe(buf, 9, skill: 2, level: 33, RecipeRequestMode.RecipeList, param1: 16, param2: 32, param4: 3);

            Assert.Equal(20, len);
            Assert.True(PacketHeader.TryParse(buf, out var h));
            Assert.Equal(0x058, h.PacketId);
            Assert.Equal(20, h.TotalSize);
            Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(4, 2)));
            Assert.Equal(33, BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(6, 2)));
            Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(8, 2)));
            Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(10, 2)));
            Assert.Equal(16, BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(12, 2)));
            Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(14, 2)));
            Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(16, 2)));
            Assert.Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(18, 2)));
        }

        [Fact]
        public void GuildBuilders_WriteFields()
        {
            var buf = new byte[8];

            Assert.Equal(8, GuildShopPacketBuilders.BuildGuildBuy(buf, 1, 4112, 12));
            Assert.True(PacketHeader.TryParse(buf, out var h));
            Assert.Equal(0x0AA, h.PacketId);
            Assert.Equal(8, h.TotalSize);
            Assert.Equal(4112, BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(4, 2)));
            Assert.Equal(0, buf[6]);
            Assert.Equal(12, buf[7]);

            Assert.Equal(8, GuildShopPacketBuilders.BuildGuildSell(buf, 2, 4112, 17, 3));
            Assert.True(PacketHeader.TryParse(buf, out h));
            Assert.Equal(0x0AC, h.PacketId);
            Assert.Equal(17, buf[6]);
            Assert.Equal(3, buf[7]);

            Assert.Equal(4, GuildShopPacketBuilders.BuildGuildBuyList(buf, 3));
            Assert.True(PacketHeader.TryParse(buf, out h));
            Assert.Equal(0x0AB, h.PacketId);
            Assert.Equal(4, h.TotalSize);

            Assert.Equal(4, GuildShopPacketBuilders.BuildGuildSellList(buf, 4));
            Assert.True(PacketHeader.TryParse(buf, out h));
            Assert.Equal(0x0AD, h.PacketId);
        }

        [Fact]
        public async Task Module_SendsSynthesisAndRecipeRequests()
        {
            var (_, _, sent) = Create();
            var state = new CraftingState();
            var module = new CraftingPacketModule(state, (data, _) =>
            {
                sent.Add(data.ToArray());
                return Task.CompletedTask;
            });

            Assert.False(await module.SynthesizeAsync(4096, 1, Array.Empty<(ushort, byte)>()));
            Assert.Empty(sent);

            Assert.True(await module.SynthesizeAsync(4096, 1, new (ushort, byte)[] { (641, 4), (640, 3) }));
            await module.RequestRecipeAsync(1, 10, RecipeRequestMode.RankList);

            Assert.Equal(2, sent.Count);
            Assert.Equal(0x096, BinaryPrimitives.ReadUInt16LittleEndian(sent[0]) & 0x1FF);
            Assert.Equal(36, sent[0].Length);
            Assert.Equal(0x058, BinaryPrimitives.ReadUInt16LittleEndian(sent[1]) & 0x1FF);
            Assert.Equal(20, sent[1].Length);
        }

        #endregion

        [Theory]
        [InlineData("/synth 2 3 4", ChatCommandResultKind.Synthesize, "2 3 4")]
        [InlineData("/guild buylist", ChatCommandResultKind.GuildShop, "buylist")]
        public void Router_ParsesCraftingCommands(string text, ChatCommandResultKind kind, string args)
        {
            var result = ChatCommandRouter.Parse(text, ChatSendKind.Say, new WorldState());
            Assert.Equal(kind, result.Kind);
            Assert.Equal(args, result.Message);
        }

        #region Message log

        [Fact]
        public void CraftingLog_OwnSuccessAndFailure()
        {
            var ok = new SynthesisOutcome(SynthesisAnswer.Success, 0, 0, 1, 650, 4096, new ushort[8], new ushort[8], Array.Empty<SynthesisSkillUp>());
            Assert.Equal(new[] { "You synthesized a bronze ingot." }, CraftingLog.FormatOwn(ok, Item).ToArray());

            var many = ok with { Count = 3 };
            Assert.Equal("You synthesized 3 bronze ingots.", CraftingLog.FormatOwn(many, Item).Single());

            var lost = new ushort[8];
            lost[1] = 640;
            var fail = new SynthesisOutcome(SynthesisAnswer.Failed, 1, -1, 1, CraftingLog.MangledMessItemId, 4096, lost, new ushort[8], Array.Empty<SynthesisSkillUp>());
            var lines = CraftingLog.FormatOwn(fail, Item).ToArray();
            Assert.Equal("Synthesis failed. You lost the crystal you were using.", lines[0]);
            Assert.Equal("A bronze ingot was lost.", lines[1]);
        }

        [Fact]
        public void CraftingLog_UnknownResultIsAFailureThatLostTheCrystal()
        {
            var o = new SynthesisOutcome((SynthesisAnswer)0x55, 0x55, -1, 1, 0, 4096, new ushort[8], new ushort[8], Array.Empty<SynthesisSkillUp>());
            Assert.Equal("Synthesis failed. You lost the crystal you were using.", CraftingLog.FormatOwn(o, Item).Single());
        }

        [Fact]
        public void CraftingLog_OtherPlayer()
        {
            var ok = new OtherSynthesisOutcome(SynthesisAnswer.Success, 0, 0, 1, 650, new ushort[8], 1, 1, "Ayame");
            Assert.Equal("Ayame synthesized a bronze ingot.", CraftingLog.FormatOther(ok, Item).Single());

            var lost = new ushort[8];
            lost[0] = 640;
            var fail = new OtherSynthesisOutcome(SynthesisAnswer.Failed, 1, -1, 1, 0, lost, 1, 1, "Ayame");
            Assert.Equal("Ayame lost a bronze ingot.", CraftingLog.FormatOther(fail, Item).Single());
        }

        #endregion
    }
}
