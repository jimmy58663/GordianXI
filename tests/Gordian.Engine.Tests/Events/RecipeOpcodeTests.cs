// tests/Gordian.Engine.Tests/Events/RecipeOpcodeTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gordian.Engine.Events;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Xunit;

namespace Gordian.Engine.Tests.Events
{
    /// <summary>
    /// Event opcode 0x8C, the crafting guild NPCs' recipe requests (C2S 0x058) and the wait for S2C 0x031 (#405),
    /// and where the answer lands in the work zone.
    /// </summary>
    public class RecipeOpcodeTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);

        private static EventVm Make(byte[] code, RecordingHost host, EventWorkZone zone, uint[] references)
        {
            var block = new EventBlock(0x010E6001, offsets: new ushort[] { 0 }, eventIds: new ushort[] { 100 }, references, code);
            return new EventVm(block, 100, zone, host, 0x010E6001, 1);
        }

        [Fact]
        public void Sub2_SendsTheRecipeListRequest_AndSub1WaitsForTheAnswer()
        {
            // 8C 02 skill level rank first last ; 8C 01 ; 00 (operands are references 0-4).
            var code = new byte[] { 0x8C, 0x02, 0x00, 0x80, 0x01, 0x80, 0x02, 0x80, 0x03, 0x80, 0x04, 0x80, 0x8C, 0x01, 0x00 };
            var host = new RecordingHost();
            var vm = Make(code, host, new EventWorkZone(), new uint[] { 5, 27, 2, 16, 32 });

            vm.Tick(Frame);
            var r = Assert.Single(host.Recipes);
            Assert.Equal((2, 5, 27, 0, 16, 32, 0, 2), ((int)r.Mode, (int)r.Skill, (int)r.Level, (int)r.P0, (int)r.P1, (int)r.P2, (int)r.P3, (int)r.P4));
            for (int i = 0; i < 5; i++) vm.Tick(Frame);
            Assert.False(vm.IsFinished);

            host.RecipePending = false; // S2C 0x031 arrived
            for (int i = 0; i < 3 && !vm.IsFinished; i++) vm.Tick(Frame);
            Assert.True(vm.IsFinished);
            Assert.Empty(host.Skipped);
        }

        [Fact]
        public void Subs0And3To5_PutTheirOperandsInTheModesFields()
        {
            var code = new byte[]
            {
                0x8C, 0x00, 0x00, 0x80, 0x01, 0x80, 0x02, 0x80,                                     // mode 1: skill level param0
                0x8C, 0x03, 0x00, 0x80, 0x01, 0x80, 0x02, 0x80, 0x03, 0x80,                         // mode 3: skill level rank index
                0x8C, 0x04, 0x00, 0x80, 0x01, 0x80, 0x02, 0x80, 0x03, 0x80,                         // mode 4
                0x8C, 0x05, 0x00, 0x80, 0x01, 0x80, 0x02, 0x80, 0x03, 0x80, 0x04, 0x80, 0x05, 0x80, // mode 5: p1 skill level p4 p3 p0
                0x00,
            };
            var host = new RecordingHost { AnswerRecipe = _ => { } };
            var vm = Make(code, host, new EventWorkZone(), new uint[] { 1, 2, 3, 4, 5, 6 });
            for (int i = 0; i < 10 && !vm.IsFinished; i++) vm.Tick(Frame);

            Assert.True(vm.IsFinished);
            Assert.Equal(new[]
            {
                (1, 1, 2, 3, 0, 0, 0, 0),
                (3, 1, 2, 0, 0, 0, 4, 3),
                (4, 1, 2, 0, 0, 0, 4, 3),
                (5, 2, 3, 6, 1, 0, 5, 4),
            }, host.Recipes.Select(r => ((int)r.Mode, (int)r.Skill, (int)r.Level, (int)r.P0, (int)r.P1, (int)r.P2, (int)r.P3, (int)r.P4)));
        }

        [Fact]
        public void Sub5_DoesNotWaitForAnAnswer()
        {
            var code = new byte[] { 0x8C, 0x05, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x8C, 0x01, 0x00 };
            var host = new RecordingHost();
            var vm = Make(code, host, new EventWorkZone(), new uint[] { 0 });
            for (int i = 0; i < 4 && !vm.IsFinished; i++) vm.Tick(Frame);
            Assert.True(vm.IsFinished);
        }

        [Fact]
        public void Recipe_LandsWhereTheGuildScriptsReadIt()
        {
            var zone = new EventWorkZone();
            zone.ApplyRecipe(4500, new ushort[] { 2, 5, 0 }, 4096, 1234,
                new ushort[] { 640, 641, 642, 643, 644, 645, 646, 647 }, new ushort[] { 1, 2, 3, 4, 5, 6, 7, 8 });

            Assert.Equal(4500, zone.Zone[0x40]);
            Assert.Equal(new[] { 2, 5, 0 }, zone.Zone[0x41..0x44]);
            Assert.Equal(4096, zone.Zone[0x44]);
            Assert.Equal(new[] { 640, 641, 642, 643, 644, 645, 646, 647 }, zone.Zone[0x45..0x4D]);
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, zone.Zone[0x4D..0x55]);
            Assert.Equal(1234, zone.Zone[0x55]);
        }

        [Fact]
        public void RecipeList_FillsMessageParameters0To15_AndTheNextPageSlot()
        {
            var zone = new EventWorkZone();
            zone.SetParameters(Enumerable.Repeat(99, 8).ToArray());
            zone.ApplyRecipeList(Enumerable.Range(1, 10).Select(i => (ushort)(100 + i)).ToArray(), 0);

            for (int i = 0; i < 10; i++) Assert.Equal(101 + i, zone.GetMessageParameter(i));
            for (int i = 10; i < 16; i++) Assert.Equal(0, zone.GetMessageParameter(i));
            Assert.Equal(0, zone.Zone1700[8]);

            zone.ApplyRecipeList(Enumerable.Range(1, 16).Select(i => (ushort)i).ToArray(), 777);
            Assert.Equal(777, zone.Zone1700[8]);
        }

        /// <summary>
        /// Zone 48 event 222 (a guild's recipe NPC; work 4 = 2 opens the rank list): Information on synthesis materials,
        /// a rank, then the recipe list. The list query offers each non-empty slot, and "View more recipes." only when the
        /// page names a next item; that option asks for the next page of 16, and a recipe asks for its materials by index.
        /// </summary>
        [Fact]
        public void GuildRecipeNpc_PagesTheListAndAsksForTheChosenRecipe()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var script = ZoneEventScript.Parse(rm.LoadDatBytesByFileId(ZoneEventScript.GetFileId(48))!)!;
            var host = new RecordingHost();
            var work = new EventWorkZone();
            work.SetParameters(new[] { 0, 35, 2, 0xFFFF, 0, 0, 1, 0 });
            var scene = new EventScene(work) { PlayerServerId = 0x12345 };
            host.AnswerRecipe = r =>
            {
                if (r.Mode != 2) work.ApplyRecipe(4500, new ushort[] { 0, 0, 0 }, 4096, 0, new ushort[] { 640 }, new ushort[] { 1 });
                else if (r.P1 == 0) work.ApplyRecipeList(Enumerable.Range(0, 16).Select(i => (ushort)(5000 + i)).ToArray(), 5016);
                else work.ApplyRecipeList(new ushort[] { 5016, 5017, 5018 }, 0);
            };
            foreach (var block in script.Blocks)
            {
                if (block.ActorId == EventBlock.ZoneActor || block.IndexOf(222) < 0) continue;
                _ = new EventVm(block, 222, scene, host, block.ActorId, (ushort)(block.ActorId & 0x3FF));
            }
            // Information on synthesis materials, rank 2 (Recruit), View more recipes. (option 17), the second recipe.
            var answers = new Queue<int>(new[] { 4, 3, 18, 3 });
            int seen = 0;
            for (int t = 0; t < 20000 && !scene.IsFinished; t++)
            {
                scene.Tick(Frame);
                if (scene.IsWaitingForConfirm) scene.Confirm();
                if (seen < host.Queries.Count) { seen++; host.QueryResult = answers.Count > 0 ? answers.Dequeue() : 1; }
            }

            Assert.True(scene.IsFinished);
            Assert.Equal(new[] { (2, 0, 16, 0, 1), (2, 16, 32, 0, 1), (3, 0, 0, 17, 1) },
                host.Recipes.Select(r => ((int)r.Mode, (int)r.P1, (int)r.P2, (int)r.P3, (int)r.P4)));
            Assert.All(host.Recipes, r => Assert.Equal(35, r.Level));
            // The first page shows all 17 options; the second hides the empty slots and View more recipes.
            var lists = host.Queries.Where(q => q.Message == 7592).ToList();
            Assert.Equal(2, lists.Count);
            Assert.Equal(0u, lists[0].Hidden);
            for (int option = 4; option <= 17; option++) Assert.NotEqual(0u, lists[1].Hidden & (1u << option));
            for (int option = 1; option <= 3; option++) Assert.Equal(0u, lists[1].Hidden & (1u << option));
        }
    }
}
