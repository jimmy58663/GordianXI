// tests/Gordian.Core.Tests/Resources/SharedCharacterPartsRealDataTests.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>
    /// Player models are built from shared pieces (#573): a race base, a motion set per race and weapons, and one part per
    /// gear piece, shared by every character wearing it, instead of one full model per look. Skipped without the game install.
    /// </summary>
    public class SharedCharacterPartsRealDataTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private static ResourceManager? Resources()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return rm;
        }

        /// <summary>A Hume Male (race 1, face 1) wearing the given grap ids (head, body, hands, legs, feet, main, sub, ranged).</summary>
        private static WorldEntity Player(uint serverId, ushort head = 0, ushort body = 8, ushort hands = 8, ushort legs = 8, ushort feet = 8,
            ushort main = 0, ushort sub = 0, ushort ranged = 0)
        {
            var entity = new WorldEntity(serverId, (ushort)serverId, EntityType.Player);
            entity.Appearance.CopyFrom([0x0101, head, body, hands, legs, feet, main, sub, ranged]);
            return entity;
        }

        private static CharacterPart PartIn(EntityModel model, CharacterSlot slot) => model.Parts.Single(p => p.Key.Slot == slot);

        [Fact]
        public void LooksDifferingInOnePiece_ShareTheRestAndTheMotions()
        {
            if (Resources() is not { } rm) return;
            Assert.True(rm.TryLoadEntityModel(Player(1, hands: 8), out var a));
            Assert.True(rm.TryLoadEntityModel(Player(2, hands: 9), out var b));
            Assert.NotSame(a, b);

            Assert.Same(a!.Skeleton, b!.Skeleton);
            Assert.Same(a.Animations, b.Animations);
            // The routines are each model's: the face's voice routines (vded, vdam...) and the body's damage sound lie over the
            // race's and the packs' (faces, bodies and weapons all carry some).
            Assert.NotEmpty(PartIn(a, CharacterSlot.Face).Routines);
            Assert.All(PartIn(a, CharacterSlot.Face).Routines, r => Assert.True(a.RawMotionRoutines.ContainsKey(r.Name), r.Name));
            Assert.Equal(a.MotionRoutines.Keys.Order(), b.MotionRoutines.Keys.Order());
            foreach (var slot in new[] { CharacterSlot.Face, CharacterSlot.Body, CharacterSlot.Legs, CharacterSlot.Feet })
            {
                Assert.Same(PartIn(a, slot), PartIn(b, slot));
            }
            Assert.NotSame(PartIn(a, CharacterSlot.Hands), PartIn(b, CharacterSlot.Hands));

            // The models draw the shared parts' own pieces, not copies.
            var bodyGroups = PartIn(a, CharacterSlot.Body).Groups;
            Assert.All(bodyGroups, g => Assert.Contains(b.AnimatedMeshGroups, x => ReferenceEquals(x, g)));
        }

        [Fact]
        public void WeaponsOfOneType_ShareAMotionSet_AndAnotherTypePlaysAnother()
        {
            if (Resources() is not { } rm) return;
            // Main weapons by their Info type (the battle pack they pick).
            var byType = new Dictionary<byte, List<EntityModel>>();
            for (ushort id = 1; id < 300; id++)
            {
                if (!rm.TryLoadEntityModel(Player(100u + id, main: id), out var model)) continue;
                if (model!.Parts.FirstOrDefault(p => p.Key.Slot == CharacterSlot.Main)?.WeaponInfo is not { Type: not 0xFF } info) continue;
                if (!byType.TryGetValue(info.Type, out var list)) byType[info.Type] = list = new List<EntityModel>();
                list.Add(model);
            }
            var shared = byType.Values.First(l => l.Count >= 2);
            var other = byType.Values.First(l => !ReferenceEquals(l, shared));

            // Two different weapons of one type: different models, one set of clips.
            Assert.NotSame(shared[0], shared[1]);
            Assert.Same(shared[0].Animations, shared[1].Animations);
            Assert.NotSame(shared[0].Animations, other[0].Animations);
            Assert.Same(shared[0].Skeleton, other[0].Skeleton);
            Assert.Same(PartIn(shared[0], CharacterSlot.Body), PartIn(other[0], CharacterSlot.Body));
            Assert.True(rm.EntityModelStats.MotionSets < byType.Values.Sum(l => l.Count), "weapons of a type share their motion set");
        }

        [Fact]
        public void EachCharacter_HidesTheSharedPiecesItsOwnGearCovers()
        {
            if (Resources() is not { } rm) return;
            // A full helm (occludeType 0x05) hides the face DAT's hair and face; the face part itself is shared.
            ushort helm = 0;
            for (ushort id = 1; id < 400 && helm == 0; id++)
            {
                if (!CharacterEquipmentResolver.TryResolveGearFileId(CharacterRace.HumeMale, CharacterSlot.Head, id, out int fid)) continue;
                if (rm.LoadDatBytesByFileId(fid) is { } dat && EntityModelLoader.ParseDatContainer(dat).Meshes.Any(m => m.OccludeType == 0x05)) helm = id;
            }
            Assert.NotEqual(0, helm);

            Assert.True(rm.TryLoadEntityModel(Player(1), out var bare));
            Assert.True(rm.TryLoadEntityModel(Player(2, head: helm), out var helmed));
            var face = PartIn(bare!, CharacterSlot.Face);
            Assert.Same(face, PartIn(helmed!, CharacterSlot.Face));

            int faceDrawnBare = face.Groups.Count(g => bare!.AnimatedMeshGroups.Contains(g));
            int faceDrawnHelmed = face.Groups.Count(g => helmed!.AnimatedMeshGroups.Contains(g));
            Assert.Equal(face.Groups.Count, faceDrawnBare);
            Assert.True(faceDrawnHelmed < faceDrawnBare, $"helm {helm}: {faceDrawnHelmed} of {faceDrawnBare} face pieces drawn");
        }

        [Fact]
        public void TheEntity_KeepsItsModel_UntilItsLookChanges()
        {
            if (Resources() is not { } rm) return;
            var player = Player(1);
            Assert.True(rm.TryLoadEntityModel(player, out var first));
            Assert.True(rm.TryLoadEntityModel(player, out var again));
            Assert.Same(first, again);

            player.Appearance.CopyFrom([0x0101, 0, 8, 9, 8, 8, 0, 0, 0]);
            Assert.True(rm.TryLoadEntityModel(player, out var changed));
            Assert.NotSame(first, changed);
            Assert.Same(PartIn(first!, CharacterSlot.Body), PartIn(changed!, CharacterSlot.Body));
        }

        [Fact]
        public void Parts_AreFreed_WhenNoCachedModelUsesThem_AndKeptForLiveEntities()
        {
            if (Resources() is not { } rm) return;
            var stays = Player(1, hands: 8);
            var leaves = Player(2, hands: 9);
            Assert.True(rm.TryLoadEntityModel(stays, out var kept));
            Assert.True(rm.TryLoadEntityModel(leaves, out var dropped));
            long before = rm.EntityModelStats.PartBytes;
            Assert.True(before > 0);

            Assert.Equal(1, rm.EvictIdleEntityModels([stays], idleSeconds: 0));
            Assert.True(dropped!.IsReleased);
            Assert.False(kept!.IsReleased);
            // Only the gloves the leaving player wore were theirs alone.
            var stats = rm.EntityModelStats;
            Assert.Equal(kept.Parts.Count, stats.Parts);
            Assert.Equal(before - PartIn(dropped, CharacterSlot.Hands).ApproximateBytes, stats.PartBytes);

            Assert.Equal(1, rm.EvictIdleEntityModels([], idleSeconds: 0));
            Assert.Equal(0, rm.EntityModelStats.Parts);
            Assert.Equal(0, rm.EntityModelStats.PartBytes);
            Assert.Equal(0, rm.EntityModelStats.MotionSets);
        }

        [Fact]
        public void OverTheBudget_ModelsNoLiveEntityWears_GoBeforeTheIdleTime()
        {
            if (Resources() is not { } rm) return;
            var live = Player(1, hands: 8);
            Assert.True(rm.TryLoadEntityModel(live, out var kept));
            Assert.True(rm.TryLoadEntityModel(Player(2, hands: 9, legs: 9), out var passing));

            // Within the idle time nothing goes while the parts fit.
            Assert.Equal(0, rm.EvictIdleEntityModels([live]));

            // A limit of 0: everything not worn by a live entity goes at once; the live entity's parts stay.
            rm.EntityPartBudgetBytes = 0;
            Assert.Equal(1, rm.EvictIdleEntityModels([live]));
            Assert.True(passing!.IsReleased);
            Assert.False(kept!.IsReleased);
            Assert.Equal(kept.Parts.Count, rm.EntityModelStats.Parts);
            Assert.True(rm.TryLoadEntityModel(live, out var again));
            Assert.Same(kept, again);
        }

        [Fact]
        public void RequestEntityModel_BuildsOnAWorker_AndKeepsTheOldLookMeanwhile()
        {
            if (Resources() is not { } rm) return;
            var player = Player(1);
            var request = rm.RequestEntityModel(player, out var model);
            Assert.Equal(EntityModelRequest.Pending, request);
            Assert.Null(model);

            var built = WaitForReady(rm, player);
            Assert.NotNull(built);
            Assert.True(rm.TryLoadEntityModel(player, out var same));
            Assert.Same(built, same);

            // A gear change: the old look until the new one is ready.
            player.Appearance.CopyFrom([0x0101, 0, 8, 9, 8, 8, 0, 0, 0]);
            request = rm.RequestEntityModel(player, out var meanwhile);
            if (request == EntityModelRequest.Pending) Assert.Same(built, meanwhile);
            var changed = WaitForReady(rm, player);
            Assert.NotSame(built, changed);
        }

        [Fact]
        public void ConcurrentRequests_ForOneLook_BuildIt_Once()
        {
            if (Resources() is not { } rm) return;
            var players = Enumerable.Range(1, 16).Select(i => Player((uint)i, hands: 9)).ToArray();
            foreach (var p in players) rm.RequestEntityModel(p, out _);
            var models = players.Select(p => WaitForReady(rm, p)).ToArray();
            Assert.All(models, m => Assert.Same(models[0], m));
            Assert.Equal(1, rm.EntityModelStats.CharacterModels);
        }

        [Fact]
        public void AnUnregisteredModelId_HasNoModel_WithoutABuild()
        {
            if (Resources() is not { } rm) return;
            var costumed = Player(1);
            costumed.Appearance.ModelId = 3300; // 3193-3499 draw nothing in retail (#83)
            Assert.Equal(EntityModelRequest.None, rm.RequestEntityModel(costumed, out var model));
            Assert.Null(model);
            Assert.False(rm.TryLoadEntityModel(costumed, out _));
        }

        private static EntityModel? WaitForReady(ResourceManager rm, WorldEntity entity)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(30))
            {
                if (rm.RequestEntityModel(entity, out var model) == EntityModelRequest.Ready) return model;
                Thread.Sleep(5);
            }
            return null;
        }
    }
}
