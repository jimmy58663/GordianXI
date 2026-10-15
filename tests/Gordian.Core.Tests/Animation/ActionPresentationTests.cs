// tests/Gordian.Core.Tests/Animation/ActionPresentationTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Gordian.Core.Animation;
using Gordian.Core.Graphics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Tests.Resources;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Animation
{
    /// <summary>
    /// Weapon skills, monster moves, job abilities and spells play their own presentation DAT (#132). The retail data tests
    /// are skipped without the game install.
    /// </summary>
    public class ActionPresentationTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const float Tick = 1f / 60f;

        private sealed class CountingSink : IActionHitSink
        {
            public int Deliveries { get; private set; }
            public void DeliverHits(ActionRequest request) => Deliveries++;
        }

        // --- File ids -------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData(CharacterRace.HumeMale, 1, 0, 33228)]      // Fast Blade, ROM/76/30
        [InlineData(CharacterRace.HumeMale, 1, 1, 33484)]      // its waist companion A, ROM/76/124
        [InlineData(CharacterRace.HumeMale, 1, 2, 33740)]      // companion B, ROM/77/80
        [InlineData(CharacterRace.HumeMale, 259, 0, 61454)]    // extended bank, ROM/204/17
        [InlineData(CharacterRace.HumeMale, 259, 1, 61470)]
        [InlineData(CharacterRace.HumeFemale, 166, 0, 34161)]  // Tachi: Enpi, ROM/101/76
        [InlineData(CharacterRace.TaruMale, 5, 0, 36304)]
        [InlineData(CharacterRace.TaruFemale, 5, 0, 36304)]    // both Tarutaru load one file
        [InlineData(CharacterRace.Galka, 271, 0, 61754)]
        [InlineData(CharacterRace.HumeMale, 272, 0, -1)]       // past the extended bank
        [InlineData(CharacterRace.Unknown, 1, 0, -1)]
        public void WeaponSkillFileId_FollowsThePerRaceBanks(CharacterRace race, int animation, int companion, int fileId) =>
            Assert.Equal(fileId, ActionMotionLibrary.WeaponSkillFileId(race, animation, companion));

        [Theory]
        [InlineData(186, 4086)]   // Bubble Shower (crab), ROM/14/50
        [InlineData(188, 4088)]   // Big Scissors
        [InlineData(505, 4405)]
        [InlineData(513, 50160)]  // Moonlit Charge (Fenrir)
        [InlineData(527, 50174)]  // Fire II (Ifrit)
        [InlineData(1791, 51438)]
        [InlineData(1792, -1)]    // not located
        [InlineData(0, -1)]       // the placeholder
        public void MonsterSkillFileId_UsesTheTwoLocatedRanges(int animation, int fileId) =>
            Assert.Equal(fileId, ActionMotionLibrary.MonsterSkillFileId(animation));

        [Fact]
        public void AbilityAndSpellFileIds_AreOffsetsOfTheAnimation()
        {
            Assert.Equal(4412, ActionMotionLibrary.JobAbilityFileId(0));   // Berserk, ROM/15/89
            Assert.Equal(4429, ActionMotionLibrary.JobAbilityFileId(17));
            Assert.Equal(-1, ActionMotionLibrary.JobAbilityFileId(500));
            Assert.Equal(2801, ActionMotionLibrary.SpellFileId(1));        // Cure, ROM/10/9
            Assert.Equal(2944, ActionMotionLibrary.SpellFileId(144));      // Fire, ROM/11/17
            Assert.Equal(-1, ActionMotionLibrary.SpellFileId(0));
            Assert.Equal(-1, ActionMotionLibrary.SpellFileId(1100));
        }

        [Theory]
        [InlineData(ActionCategory.SkillFinish, ActionAssetKind.WeaponSkill)]
        [InlineData(ActionCategory.MobSkillFinish, ActionAssetKind.MonsterSkill)]
        [InlineData(ActionCategory.PetSkillFinish, ActionAssetKind.MonsterSkill)]
        [InlineData(ActionCategory.AbilityFinish, ActionAssetKind.JobAbility)]
        [InlineData(ActionCategory.Dancer, ActionAssetKind.JobAbility)]
        [InlineData(ActionCategory.RuneFencer, ActionAssetKind.JobAbility)]
        [InlineData(ActionCategory.MagicFinish, ActionAssetKind.Spell)]
        public void KindOf_MapsTheFinishPackets(ActionCategory category, ActionAssetKind kind) =>
            Assert.Equal(kind, ActionMotionLibrary.KindOf(category));

        [Fact]
        public void KindOf_HasNothingForStartsAttacksAndItems()
        {
            Assert.Null(ActionMotionLibrary.KindOf(ActionCategory.BasicAttack));
            Assert.Null(ActionMotionLibrary.KindOf(ActionCategory.MagicStart));
            Assert.Null(ActionMotionLibrary.KindOf(ActionCategory.ItemFinish));
        }

        // --- A synthetic job ability DAT -----------------------------------------------------------------------------

        /// <summary>A DAT section: a 16-byte header (id, type | 16-byte units &lt;&lt; 7) and the padded payload.</summary>
        private static byte[] Section(string datId, byte type, byte[] payload)
        {
            int size = 16 + ((payload.Length + 15) & ~15);
            byte[] section = new byte[size];
            Encoding.ASCII.GetBytes(datId).CopyTo(section, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(4), (uint)type | ((uint)(size / 16) << 7));
            payload.CopyTo(section, 16);
            return section;
        }

        /// <summary>
        /// A job ability DAT as retail lays it out (Sneak Attack, file 4429): <c>main</c> plays the actor's own
        /// <c>cm0?</c> and <c>cm1?</c>, starts <c>hit0</c> and shows the result through <c>mdam</c> at tick 90.
        /// </summary>
        private static byte[] JobAbilityDat() =>
            Section("main", 0x07, MotionRoutineDecoderTests.Routine(120,
                    MotionRoutineDecoderTests.PlayClip("cm0?", 40, 40, 10, 10, 1),
                    MotionRoutineDecoderTests.PlayClip("cm1?", 0, 80, 10, 20, 1),
                    MotionRoutineDecoderTests.Link(0x03, "hit0", 50),
                    MotionRoutineDecoderTests.Link(0x03, "mdam", 30)))
                .Concat(Section("hit0", 0x07, MotionRoutineDecoderTests.Routine(75)))
                .ToArray();

        private static AnimationClip Clip(string name, float seconds) => new()
        {
            Name = name,
            KeyFrameDuration = 1f,
            NumFrames = (int)(seconds * 30f) + 1
        };

        private static EntityModel Model()
        {
            var model = new EntityModel { Name = "Test" };
            foreach (var name in new[] { "idl", "btl", "wlk", "run" }) model.Animations[name] = Clip(name, 1f);
            model.Animations["cm0"] = Clip("cm0", 1f);
            model.Animations["cm1"] = Clip("cm1", 2f);
            model.RebuildMotionRoutines();
            return model;
        }

        [Fact]
        public void Library_ReadsTheFileOnceAndCachesMisses()
        {
            var asked = new List<int>();
            var library = new ActionMotionLibrary(id =>
            {
                asked.Add(id);
                return id == 4429 ? JobAbilityDat() : null;
            });

            var asset = library.Resolve(ActionCategory.AbilityFinish, 17, CharacterRace.Unknown);
            Assert.NotNull(asset);
            Assert.Equal(ActionAssetKind.JobAbility, asset!.Kind);
            Assert.Equal(4429, asset.FileId);
            Assert.NotNull(asset.Motion);
            Assert.True(asset.Effects!.Routines.ContainsKey("main"));
            Assert.Same(asset, library.Resolve(ActionCategory.Dancer, 17, CharacterRace.Unknown));

            Assert.Null(library.Resolve(ActionCategory.AbilityFinish, 18, CharacterRace.Unknown));
            Assert.Null(library.Resolve(ActionCategory.AbilityFinish, 18, CharacterRace.Unknown));
            Assert.Equal(new[] { 4429, 4430 }, asked);

            // A weapon skill needs a race; a basic attack has no presentation.
            Assert.Null(library.Resolve(ActionCategory.SkillFinish, 1, CharacterRace.Unknown));
            Assert.Null(library.Resolve(ActionCategory.BasicAttack, 1, CharacterRace.HumeMale));
        }

        [Fact]
        public void Presentation_LinksIntoTheActorsOwnClips()
        {
            var library = new ActionMotionLibrary(id => id == 4429 ? JobAbilityDat() : null);
            var asset = library.Resolve(ActionCategory.AbilityFinish, 17, CharacterRace.Unknown)!;
            var model = Model();
            var routine = asset.Motion!.BuildOnModel(model, ActionAsset.MainRoutine);
            Assert.NotNull(routine);
            Assert.Equal(new[] { ("cm0", 0), ("cm1", 40) }, routine!.Segments.Select(s => (s.ClipName, s.StartTick)).ToArray());
            Assert.Equal(new[] { 90 }, routine.HitTicks);
            Assert.Same(routine, asset.Motion.BuildOnModel(model, ActionAsset.MainRoutine)); // cached per model

            // A model without the clips gets nothing to play: the stand-in plays instead.
            var bare = new EntityModel { Name = "Bare" };
            bare.Animations["idl"] = Clip("idl", 1f);
            bare.RebuildMotionRoutines();
            Assert.Empty(asset.Motion.BuildOnModel(bare, ActionAsset.MainRoutine)?.Segments ?? []);
        }

        [Fact]
        public void BankClipForAnotherSkeleton_IsNotPlayed()
        {
            var clip = new AnimationClip
            {
                Name = "wz10",
                KeyFrameDuration = 1f,
                NumFrames = 2,
                Tracks = new Dictionary<int, BoneAnimationTrack> { [0] = new() { JointIndex = 0 }, [60] = new() { JointIndex = 60 } }
            };
            Assert.False(EventMotionBank.FitsSkeleton(clip, 48));
            Assert.True(EventMotionBank.FitsSkeleton(clip, 94));
        }

        [Fact]
        public void Presentation_PlaysItsMainAndLandsTheHitAtItsTick()
        {
            var library = new ActionMotionLibrary(id => id == 4429 ? JobAbilityDat() : null);
            var asset = library.Resolve(ActionCategory.AbilityFinish, 17, CharacterRace.Unknown)!;
            var model = Model();
            var sink = new CountingSink();
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);
            state.EnqueueAction(new ActionRequest
            {
                Motion = ActionMotion.Ability,
                Category = ActionCategory.AbilityFinish,
                Asset = asset,
                Hits = [new ActionHit(2, default)],
                Sink = sink,
                ReceivedTimestamp = Stopwatch.GetTimestamp()
            });
            state.Advance(Tick, AnimationCategory.Combat, 0, model);
            Assert.Equal(ActionAsset.MainRoutine, state.ActiveRoutine!.Name);
            Assert.Equal("cm0", state.CurrentClip!.Name);

            for (int i = 0; i < 45; i++) state.Advance(Tick, AnimationCategory.Combat, 0, model);
            Assert.Equal("cm1", state.CurrentClip!.Name);
            Assert.Equal(0, sink.Deliveries);

            for (int i = 0; i < 50; i++) state.Advance(Tick, AnimationCategory.Combat, 0, model);
            Assert.Equal(1, sink.Deliveries);

            for (int i = 0; i < 30; i++) state.Advance(Tick, AnimationCategory.Combat, 0, model);
            Assert.False(state.IsPlayingAction);
            Assert.Equal("btl", state.CurrentClip!.Name);
        }

        /// <summary>
        /// A presentation whose result shows after its last clip (Cure: the release ends near tick 121, <c>mdam</c> at 205)
        /// gives the body back at the clip's end; the hit stays with the queue's timer instead of landing early.
        /// </summary>
        [Fact]
        public void Presentation_EndingBeforeItsHit_LeavesTheHitToTheTimer()
        {
            byte[] dat = Section("main", 0x07, MotionRoutineDecoderTests.Routine(240,
                    MotionRoutineDecoderTests.PlayClip("cm0?", 200, 60, 10, 10, 1),
                    MotionRoutineDecoderTests.Link(0x03, "mdam", 40)));
            var library = new ActionMotionLibrary(id => id == 2801 ? dat : null);
            var asset = library.Resolve(ActionCategory.MagicFinish, 1, CharacterRace.Unknown)!;
            var model = Model();
            var sink = new CountingSink();
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);
            var request = new ActionRequest
            {
                Motion = ActionMotion.CastRelease,
                Category = ActionCategory.MagicFinish,
                Asset = asset,
                Hits = [new ActionHit(2, default)],
                Sink = sink,
                ReceivedTimestamp = Stopwatch.GetTimestamp()
            };
            state.EnqueueAction(request);
            for (int i = 0; i < 80; i++) state.Advance(Tick, AnimationCategory.Combat, 0, model);
            Assert.False(state.IsPlayingAction);
            Assert.Equal(0, sink.Deliveries);
            Assert.True(request.HitsDetached);
            Assert.Equal(200, request.HitTick);
        }

        [Fact]
        public void Queue_ShowsDetachedHitsAtTheirTick()
        {
            var combat = new CombatState();
            var world = new WorldState();
            var queue = new ActionPlaybackQueue(combat, world);
            world.UpsertEntity(new WorldEntity(2, 2, EntityType.Monster) { Position = Vector3.Zero, Hpp = 100 });
            int landed = 0;
            queue.HitLanded += (_, _) => landed++;
            var record = new CombatActionRecord { ActorId = 1, Category = ActionCategory.AbilityFinish, ActionId = 17 };
            var target = new CombatActionTargetRecord { TargetId = 2 };
            target.Results.Add(new CombatActionResult { Animation = 17 });
            record.Targets.Add(target);
            queue.Dispatch(record);
            Assert.Equal(1, queue.OutstandingCount);

            // As if the actor had started it and its body motion ended before the hit tick.
            long start = Stopwatch.GetTimestamp();
            var posted = GetOutstanding(queue);
            posted.MarkAccepted(start);
            posted.MarkStarted(start, 120); // 2 s
            posted.DetachHits();
            queue.Update(start + Stopwatch.Frequency); // 1 s: not yet, although past the usual fallback of a started request
            Assert.Equal(0, landed);
            queue.Update(start + (long)(Stopwatch.Frequency * 2.01));
            Assert.Equal(1, landed);
        }

        private static ActionRequest GetOutstanding(ActionPlaybackQueue queue)
        {
            var field = typeof(ActionPlaybackQueue).GetField("_outstanding", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            return ((List<ActionRequest>)field.GetValue(queue)!)[0];
        }

        [Fact]
        public void Queue_AttachesThePresentationByCategoryAndRace()
        {
            var asked = new List<int>();
            var library = new ActionMotionLibrary(id =>
            {
                asked.Add(id);
                return id is 33228 or 4429 ? JobAbilityDat() : null;
            });
            var combat = new CombatState();
            var world = new WorldState();
            var queue = new ActionPlaybackQueue(combat, world, library);
            var player = new WorldEntity(1, 1, EntityType.Player);
            player.Appearance.GrapIdTable[0] = (ushort)((byte)CharacterRace.HumeMale << 8);
            world.UpsertEntity(player);
            world.UpsertEntity(new WorldEntity(2, 2, EntityType.Monster) { Hpp = 100 });

            ActionRequest Post(ActionCategory category, ushort animation)
            {
                var record = new CombatActionRecord { ActorId = 1, Category = category, ActionId = 32 };
                var target = new CombatActionTargetRecord { TargetId = 2 };
                target.Results.Add(new CombatActionResult { Animation = animation });
                record.Targets.Add(target);
                queue.Dispatch(record);
                var field = typeof(ActionPlaybackQueue).GetField("_outstanding", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                var list = (List<ActionRequest>)field.GetValue(queue)!;
                return list[^1];
            }

            var ws = Post(ActionCategory.SkillFinish, 1);
            Assert.Equal(33228, ws.Asset!.FileId);
            Assert.Equal(ActionMotion.Swing, ws.Motion); // the stand-in if the body cannot play it
            Assert.Equal(2u, ws.PrimaryTargetId);
            Assert.Contains(33484, asked); // the waist companion

            var ja = Post(ActionCategory.AbilityFinish, 17);
            Assert.Equal(4429, ja.Asset!.FileId);

            // A fixed-model actor has no weapon skill bank.
            player.Appearance.ModelId = 300;
            Assert.Null(Post(ActionCategory.SkillFinish, 2).Asset);
        }

        /// <summary>A cast, weapon skill or ability request leaves at once, not on the next 250 ms tick (round 2: casting felt delayed).</summary>
        [Fact]
        public async System.Threading.Tasks.Task ActionRequests_AreFlushedAtOnce()
        {
            var priorities = new List<bool>();
            var module = new Gordian.Core.Network.Packets.CombatPacketModule(new CombatState(), new LocalPlayerState(),
                (_, highPriority) => { priorities.Add(highPriority); return System.Threading.Tasks.Task.CompletedTask; });
            await module.RequestCastMagicAsync(2, 2, 1);
            await module.RequestWeaponskillAsync(2, 2, 32);
            await module.RequestJobAbilityAsync(2, 2, 17);
            Assert.Equal(new[] { true, true, true }, priorities);
        }

        // --- Effects ---------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData(ParticleAttachType.TargetActor, true)]
        [InlineData(ParticleAttachType.TargetActorSourceFacing, true)]
        [InlineData(ParticleAttachType.TargetToSourceBasis, true)]
        [InlineData(ParticleAttachType.SourceActor, false)]
        [InlineData(ParticleAttachType.SourceToTargetBasis, false)]
        [InlineData(ParticleAttachType.SourceActorWeapon, false)]
        [InlineData(ParticleAttachType.None, false)]
        public void TargetSideGenerators_RideTheTarget(ParticleAttachType attach, bool target)
        {
            var generator = new ParticleGeneratorDefinition { AttachType = attach };
            Assert.Equal(target, ActionEffectPlayer.IsTargetSide(generator));
        }

        private static List<ActionEffectEvent> Step(ActionEffectTracker tracker, WorldEntity entity)
        {
            var events = new List<ActionEffectEvent>();
            tracker.Step(entity, events);
            tracker.EndFrame();
            return events;
        }

        [Fact]
        public void Tracker_StartsEachPresentationOnce()
        {
            var library = new ActionMotionLibrary(id => id == 4429 ? JobAbilityDat() : null);
            var asset = library.Resolve(ActionCategory.AbilityFinish, 17, CharacterRace.Unknown)!;
            var model = Model();
            var entity = new WorldEntity(1, 1, EntityType.Player);
            var tracker = new ActionEffectTracker();

            entity.Animation.Advance(0f, AnimationCategory.Combat, 0, model);
            Assert.Empty(Step(tracker, entity));

            entity.Animation.EnqueueAction(new ActionRequest
            {
                Motion = ActionMotion.Ability, Asset = asset, Hits = [new ActionHit(7, default)], ReceivedTimestamp = Stopwatch.GetTimestamp()
            });
            entity.Animation.Advance(Tick, AnimationCategory.Combat, 0, model);
            var started = Assert.Single(Step(tracker, entity));
            Assert.Equal(ActionEffectEventKind.Presentation, started.Kind);
            Assert.Same(asset, started.Request!.Asset);
            Assert.Equal(7u, started.TargetId);
            entity.Animation.Advance(Tick, AnimationCategory.Combat, 0, model);
            Assert.Empty(Step(tracker, entity)); // the same action

            // An action without a presentation starts nothing.
            entity.Animation.EnqueueAction(new ActionRequest { Motion = ActionMotion.Ability, ReceivedTimestamp = Stopwatch.GetTimestamp() });
            for (int i = 0; i < 200; i++) entity.Animation.Advance(Tick, AnimationCategory.Combat, 0, model);
            Assert.Empty(Step(tracker, entity));
        }

        /// <summary>An op 0x19 command (spell effect): u32 spell animation at +8.</summary>
        private static byte[] SpellEffectCommand(int animation, int delay = 0)
        {
            var command = new byte[12];
            command[0] = 0x19;
            BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(1), 3);
            BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(4), (ushort)delay);
            BinaryPrimitives.WriteUInt32LittleEndian(command.AsSpan(8), (uint)animation);
            return command;
        }

        /// <summary>
        /// A caster as retail authors one: the chant <c>cawh</c> plays <c>mw0?</c> looping and links the shared circle
        /// <c>ner2</c>; the release <c>shwh</c> links the shared stop <c>stwh</c> and flash <c>eis4</c>, and the local
        /// <c>sswh</c> that plays the clips; <c>spop</c> plays spell effect 921 (a Trust appearing).
        /// </summary>
        private static EntityModel Caster()
        {
            var model = new EntityModel { Name = "Caster" };
            foreach (var name in new[] { "idl", "btl", "wlk", "run", "mw0", "mw1" }) model.Animations[name] = Clip(name, 1f);
            void Add(string name, byte[] payload) => model.RawMotionRoutines[name] = MotionRoutineDecoder.Decode(payload, name)!;
            Add("cawh", MotionRoutineDecoderTests.Routine(93,
                MotionRoutineDecoderTests.PlayClip("mw0?", 0, 93, 10, 10, 23),
                MotionRoutineDecoderTests.Link(0x03, "hwmg", 0),
                MotionRoutineDecoderTests.Link(0x03, "ner2", 0)));
            Add("shwh", MotionRoutineDecoderTests.Routine(1,
                MotionRoutineDecoderTests.Link(0x03, "stwh", 0),
                MotionRoutineDecoderTests.Link(0x03, "eis4", 1),
                MotionRoutineDecoderTests.Link(0x03, "sswh", 0)));
            Add("sswh", MotionRoutineDecoderTests.Routine(60, MotionRoutineDecoderTests.PlayClip("mw1?", 60, 60, 10, 20, 1)));
            Add("spop", MotionRoutineDecoderTests.Routine(0, SpellEffectCommand(921)));
            model.RebuildMotionRoutines();
            return model;
        }

        [Fact]
        public void RoutineEffects_FollowLocalLinks_AndNameTheSharedRoutines()
        {
            var model = Caster();
            Assert.Equal(new[] { new RoutineEffectCue(0, "ner2") }, model.GetRoutineEffects("cawh"));
            Assert.Equal(new[] { new RoutineEffectCue(0, "stwh"), new RoutineEffectCue(0, "eis4") }, model.GetRoutineEffects("shwh"));
            Assert.Equal(new[] { new RoutineEffectCue(0, string.Empty, 921) }, model.GetRoutineEffects("spop"));
            Assert.Empty(model.GetRoutineEffects("sswh"));
        }

        /// <summary>
        /// The chant starts its circle; the release replaces the chant, so the circle is reported over (the renderer stops
        /// it, as the release's own <c>stwh</c> does) and the release's stop and flash start.
        /// </summary>
        [Fact]
        public void Tracker_ReportsSharedRoutinesOnTheRoutineClock_AndTheChantsEnd()
        {
            var model = Caster();
            var entity = new WorldEntity(1, 1, EntityType.Player);
            var tracker = new ActionEffectTracker();
            entity.Animation.Advance(0f, AnimationCategory.Idle, 0, model);
            Assert.Empty(Step(tracker, entity));

            entity.Animation.EnqueueAction(new ActionRequest { Motion = ActionMotion.Routine, Routine = "cawh", ReceivedTimestamp = Stopwatch.GetTimestamp() });
            entity.Animation.Advance(Tick, AnimationCategory.Idle, 0, model);
            var chant = Assert.Single(Step(tracker, entity));
            Assert.Equal((ActionEffectEventKind.SharedRoutine, "ner2"), (chant.Kind, chant.Routine));
            for (int i = 0; i < 60; i++) entity.Animation.Advance(Tick, AnimationCategory.Idle, 0, model);
            Assert.Empty(Step(tracker, entity));

            entity.Animation.EnqueueAction(new ActionRequest { Motion = ActionMotion.Routine, Routine = "shwh", ReceivedTimestamp = Stopwatch.GetTimestamp() });
            entity.Animation.Advance(Tick, AnimationCategory.Idle, 0, model);
            entity.Animation.Advance(Tick, AnimationCategory.Idle, 0, model);
            var release = Step(tracker, entity);
            Assert.Equal(new[] { (ActionEffectEventKind.ActionEnded, "ner2"), (ActionEffectEventKind.SharedRoutine, "stwh"), (ActionEffectEventKind.SharedRoutine, "eis4") },
                release.Select(e => (e.Kind, e.Routine)).ToArray());
        }

        [Fact]
        public void Tracker_PlaysTheSummonedEffect_WhenATrustAppears()
        {
            var model = Caster();
            var trust = new WorldEntity(9, 9, EntityType.Trust) { IsPetSpawning = true };
            trust.Animation.Advance(0f, AnimationCategory.Idle, 0, model);
            var tracker = new ActionEffectTracker();
            var appear = Assert.Single(Step(tracker, trust));
            Assert.Equal((ActionEffectEventKind.SpellEffect, 921, 9u), (appear.Kind, appear.SpellAnimation, appear.ActorId));
            Assert.Empty(Step(tracker, trust)); // once, when first seen
        }

        /// <summary>
        /// #377: a Trust first drawn long after its spawn flag arrived (the viewport switched to the character who saw it
        /// appear while hidden; the flag stays set until the Trust's next general update) does not appear again, and a new
        /// tracker (a switch clears it) stays quiet for an actor whose last action is long under way.
        /// </summary>
        [Fact]
        public void Tracker_DoesNotReplayAnOldSpawn_OrAnActionUnderWay()
        {
            var model = Caster();
            var trust = new WorldEntity(9, 9, EntityType.Trust) { IsPetSpawning = true };
            trust.PetSpawnTimestamp = Stopwatch.GetTimestamp() - (long)((ActionEffectTracker.MaxSpawnEffectDelaySeconds + 1) * Stopwatch.Frequency);
            trust.Animation.Advance(0f, AnimationCategory.Idle, 0, model);
            Assert.Empty(Step(new ActionEffectTracker(), trust));
            Assert.False(ActionEffectTracker.IsFreshSpawn(new WorldEntity(10, 10, EntityType.Trust), Stopwatch.GetTimestamp()));

            var caster = new WorldEntity(1, 1, EntityType.Player);
            caster.Animation.Advance(0f, AnimationCategory.Idle, 0, model);
            caster.Animation.EnqueueAction(new ActionRequest { Motion = ActionMotion.Routine, Routine = "cawh", ReceivedTimestamp = Stopwatch.GetTimestamp() });
            for (int i = 0; i < 30; i++) caster.Animation.Advance(Tick, AnimationCategory.Idle, 0, model);
            Assert.Empty(Step(new ActionEffectTracker(), caster));
        }

        /// <summary>
        /// A cast's start and finish in one datagram (an instant cast, round 1 C7 / D3): the chant gives way to the waiting
        /// release after a few ticks instead of holding the body for 30 s until the release went stale.
        /// </summary>
        [Fact]
        public void Chant_WithTheReleaseAlreadyQueued_GivesWayAtOnce()
        {
            var model = Caster();
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Idle, 0, model);
            long now = Stopwatch.GetTimestamp();
            state.EnqueueAction(new ActionRequest { Motion = ActionMotion.Routine, Routine = "cawh", ReceivedTimestamp = now });
            state.EnqueueAction(new ActionRequest { Motion = ActionMotion.Routine, Routine = "shwh", ReceivedTimestamp = now });
            state.Advance(Tick, AnimationCategory.Idle, 0, model);
            Assert.Equal("cawh", state.ActiveRoutine!.Name);
            for (int i = 0; i < 10; i++) state.Advance(Tick, AnimationCategory.Idle, 0, model);
            Assert.Equal("shwh", state.ActiveRoutine!.Name);
        }

        // --- Retail data -----------------------------------------------------------------------------------------------

        private static ResourceManager? Retail()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return rm;
        }

        private static EntityModel? HumeMale(ResourceManager rm)
        {
            var saved = EntityModelLoader.EnableSpeculativeMotionPacks;
            try
            {
                EntityModelLoader.EnableSpeculativeMotionPacks = true;
                return EntityModelLoader.AssembleCharacter(CharacterRace.HumeMale, 0, new ushort[9], rm.LoadDatBytes, rm.LoadDatBytesByFileId);
            }
            finally
            {
                EntityModelLoader.EnableSpeculativeMotionPacks = saved;
            }
        }

        /// <summary>
        /// Fast Blade on Hume male (ROM/76/30, waist from ROM/76/124): <c>main</c> plays the body DAT's <c>b00?</c> then
        /// <c>b02?</c> (parts 0-2 joined, every joint of the 94-joint skeleton keyed) and shows the hit at tick 80.
        /// </summary>
        [Fact]
        public void Retail_FastBlade_PlaysItsOwnClipsOnHumeMale()
        {
            if (Retail() is not { } rm || HumeMale(rm) is not { } model) return;
            var library = new ActionMotionLibrary(rm.LoadDatBytesByFileId);
            var asset = library.Resolve(ActionCategory.SkillFinish, 1, CharacterRace.HumeMale);
            Assert.NotNull(asset);
            Assert.Equal(33228, asset!.FileId);
            var routine = asset.Motion!.BuildOnModel(model, ActionAsset.MainRoutine)!;
            Assert.Equal(new[] { "b00", "b02" }, routine.Segments.Select(s => s.ClipName).ToArray());
            Assert.Equal(new[] { 80 }, routine.HitTicks);
            Assert.Equal(model.Skeleton!.Joints.Count, asset.Motion.Clips["b00"].Tracks.Count);
            Assert.Equal(105f, EntityAnimationState.BodyEndTick(routine));
            Assert.True(asset.Effects!.Routines.ContainsKey("main"));
            // Its sound commands (0x0A on 8050 at the hit) play on the routine clock.
            Assert.Contains(asset.Motion.GetSoundsOnModel(model, ActionAsset.MainRoutine), c => !c.IsTargetLink && c.Choices.Any(id => id > 0));
        }

        /// <summary>
        /// Berserk (job ability animation 0, ROM/15/89) plays Hume male's own <c>cm0</c> / <c>cm1</c>; Cure (spell 1,
        /// ROM/10/9) links the caster's release <c>shwh</c> (<c>mw1?</c>, <c>mw2?</c>) and shows its result at tick 205,
        /// after the body is free (121).
        /// </summary>
        [Fact]
        public void Retail_AbilityAndSpell_UseTheActorsOwnMotions()
        {
            if (Retail() is not { } rm || HumeMale(rm) is not { } model) return;
            var library = new ActionMotionLibrary(rm.LoadDatBytesByFileId);

            var berserk = library.Resolve(ActionCategory.AbilityFinish, 0, CharacterRace.HumeMale)!.Motion!.BuildOnModel(model, ActionAsset.MainRoutine)!;
            Assert.Equal(new[] { "cm0", "cm1" }, berserk.Segments.Select(s => s.ClipName).ToArray());

            var cure = library.Resolve(ActionCategory.MagicFinish, 1, CharacterRace.HumeMale)!.Motion!.BuildOnModel(model, ActionAsset.MainRoutine)!;
            Assert.Equal(new[] { "mw1", "mw2" }, cure.Segments.Select(s => s.ClipName).ToArray());
            Assert.Equal(new[] { 205 }, cure.HitTicks);
            Assert.True(EntityAnimationState.BodyEndTick(cure) < 205);
        }

        /// <summary>
        /// Hume male's chant <c>cawh</c> links the shared circle <c>ner2</c>, its release <c>shwh</c> the stop <c>stwh</c> and
        /// flash <c>eis4</c>; Fast Blade's <c>main</c> links <c>eis1</c>; Trust model 3000's <c>spop</c> plays spell effect 921;
        /// <c>ROM/0/0</c> holds those routines and their generators.
        /// </summary>
        [Fact]
        public void Retail_SharedEffects_AreLinkedFromTheActorsRoutines()
        {
            if (Retail() is not { } rm || HumeMale(rm) is not { } model) return;
            Assert.Contains(new RoutineEffectCue(0, "ner2"), model.GetRoutineEffects("cawh"));
            var release = model.GetRoutineEffects("shwh").Select(c => c.Routine).ToArray();
            Assert.Contains("stwh", release);
            Assert.Contains("eis4", release);

            var library = new ActionMotionLibrary(rm.LoadDatBytesByFileId);
            var fastBlade = library.Resolve(ActionCategory.SkillFinish, 1, CharacterRace.HumeMale)!;
            Assert.Contains(fastBlade.Motion!.GetEffectsOnModel(model, ActionAsset.MainRoutine), c => c.Routine == "eis1");

            var trust = EntityModelLoader.LoadMonsterModel(3000, rm.LoadDatBytesByFileId)!;
            Assert.Contains(new RoutineEffectCue(0, string.Empty, 921), trust.GetRoutineEffects(ActionEffectTracker.SummonedRoutine));
            Assert.NotNull(library.ResolveSpellEffect(921)?.Effects);

            var shared = rm.GetSharedRoutineEffects();
            Assert.NotNull(shared);
            foreach (var name in new[] { "ner1", "ner2", "nefa", "stwh", "eis1", "eis4" }) Assert.True(shared!.Value.Routines.TryGetRoutine(name, out _), name);
            var player = new ActionEffectPlayer(shared!.Value.Routines, shared.Value.Effects, 1, 1);
            Assert.True(player.Start("ner2"));
            var frame = new ZoneParticleFrame(new Vector3(0, -2, -5), 0.5f, Vector3.One, Vector3.UnitZ, 0, 0);
            for (int i = 0; i < 30; i++) player.Update(1f, frame, frame, frame);
            Assert.Contains(player.Player.Instance.Emitters, e => e.Emitter.Particles.Count > 0);
            Assert.True(player.StopIfSustained("ner2"));
            Assert.False(player.StopIfSustained("eis4"));
        }

        /// <summary>
        /// The bee (model 272, <c>ROM/172/70</c>) idles with <c>idl0</c> (80 frames, the body) and <c>idl1</c> (4 frames, the
        /// wings): joined into <c>idl</c>, the wing tracks keep their own length and flap through a whole cycle in 4 frames
        /// instead of being stretched over the body's 80 (round 1: "bees' wings aren't animating").
        /// </summary>
        [Fact]
        public void Retail_BeeWings_LoopAtTheirOwnLength()
        {
            if (Retail() is not { } rm) return;
            var bee = EntityModelLoader.LoadMonsterModel(272, rm.LoadDatBytesByFileId);
            Assert.NotNull(bee);
            var idle = bee!.Animations["idl"];
            var wing = bee.Animations["idl1"];
            Assert.True(wing.DurationSeconds < idle.DurationSeconds / 4);
            var joint = wing.Tracks.Keys.First(j => idle.Tracks[j].PartDurationSeconds > 0f);
            Assert.Equal(wing.DurationSeconds, idle.Tracks[joint].PartDurationSeconds, 3);
            // Sampled at the wing part's own times, the joined clip matches the part.
            float t = wing.DurationSeconds * 0.5f;
            Assert.True(idle.TrySample(joint, t, true, out var joined, out _));
            Assert.True(wing.TrySample(joint, t, true, out var own, out _));
            Assert.True(MathF.Abs(System.Numerics.Quaternion.Dot(joined, own)) > 0.9999f);
        }

        private static int LiveParticles(ActionEffectPlayer player)
        {
            int count = 0;
            foreach (var (_, emitter) in player.Player.Instance.Emitters) count += emitter.Particles.Count;
            return count;
        }

        /// <summary>
        /// The release's <c>stwh</c> stops the white-magic circle: its op 0x2D (StopGenerator) names <c>gn21</c> and
        /// <c>gn20</c>, which <c>ner2</c> started for 1800 frames; without it the circle went on for 30 s after the cast
        /// (round 2, D6). The circle's particles then die out.
        /// </summary>
        [Fact]
        public void Retail_ReleaseStop_EndsTheChantCircle()
        {
            if (Retail() is not { } rm || rm.GetSharedRoutineEffects() is not { } shared) return;
            Assert.True(shared.Routines.TryGetRoutine("stwh", out var stop));
            Assert.Contains(stop.Commands, c => c.Kind == Gordian.Core.Resources.Events.SceneCommandKind.StopGenerator && c.Reference == "gn20");

            var frame = new ZoneParticleFrame(new Vector3(0, -2, -5), 0.5f, Vector3.One, Vector3.UnitZ, 0, 0);
            var released = new ActionEffectPlayer(shared.Routines, shared.Effects, 1, 1);
            released.Start("ner2");
            for (int i = 0; i < 60; i++) released.Update(1f, frame, frame, frame);
            Assert.True(LiveParticles(released) > 0);
            released.Start("stwh");
            for (int i = 0; i < 600; i++) released.Update(1f, frame, frame, frame);
            Assert.Equal(0, LiveParticles(released));

            // A chant that ends without its release (interrupted, replaced, or the release skipped) is stopped the same way.
            var ended = new ActionEffectPlayer(shared.Routines, shared.Effects, 1, 1);
            ended.Start("ner2");
            for (int i = 0; i < 60; i++) ended.Update(1f, frame, frame, frame);
            Assert.True(ended.StopIfSustained("ner2"));
            for (int i = 0; i < 600; i++) ended.Update(1f, frame, frame, frame);
            Assert.Equal(0, LiveParticles(ended));

            // Left alone, the circle still runs.
            var alone = new ActionEffectPlayer(shared.Routines, shared.Effects, 1, 1);
            alone.Start("ner2");
            for (int i = 0; i < 660; i++) alone.Update(1f, frame, frame, frame);
            Assert.True(LiveParticles(alone) > 0);
        }

        /// <summary>
        /// The bee's wing beat (<c>idl1</c>) is laid over its single-part motions too: the walk, run and swings beat their
        /// wings at the idle's 4 frames (round 2, D10: they stopped while moving); the death and corpse clips do not.
        /// </summary>
        [Fact]
        public void Retail_BeeWings_BeatWhileMoving()
        {
            if (Retail() is not { } rm) return;
            var bee = EntityModelLoader.LoadMonsterModel(272, rm.LoadDatBytesByFileId)!;
            float wing = bee.Animations["idl1"].DurationSeconds;
            foreach (var name in new[] { "wlk", "run", "at0" })
            {
                Assert.True(bee.Animations.TryGetValue(name, out var clip), name);
                Assert.Contains(clip!.Tracks.Values, t => MathF.Abs(t.PartDurationSeconds - wing) < 0.001f);
            }
            Assert.DoesNotContain(bee.Animations["ded"].Tracks.Values, t => t.PartDurationSeconds > 0f);

            // A swing plays once (not looped), but its wing channel keeps beating (round 3, D12): sampled one-shot past the
            // wing loop's length, the swing's wing joint follows the looped wing part, not its held last key.
            var swing = bee.Animations["at0"];
            var wingPart = bee.Animations["idl1"];
            int joint = swing.Tracks.First(t => t.Value.LoopsAlways).Key;
            float t = wing * 2.5f;
            Assert.True(swing.TrySample(joint, t, false, out var swung, out _));
            Assert.True(wingPart.TrySample(joint, t, true, out var beat, out _));
            Assert.True(MathF.Abs(System.Numerics.Quaternion.Dot(swung, beat)) > 0.9999f);
        }

        /// <summary>
        /// The crab (model 356, ROM/5/27): Bubble Shower (186, ROM/14/50) carries its own 48-joint clips <c>wz10</c>-<c>wz30</c>;
        /// Big Scissors (188, ROM/14/52) plays the crab's <c>ma0</c>, <c>ma2</c>, <c>at2</c> and lands at tick 154.
        /// </summary>
        [Fact]
        public void Retail_CrabMoves_PlayOnTheCrab()
        {
            if (Retail() is not { } rm) return;
            var crab = EntityModelLoader.LoadMonsterModel(356, rm.LoadDatBytesByFileId);
            Assert.NotNull(crab);
            var library = new ActionMotionLibrary(rm.LoadDatBytesByFileId);

            var shower = library.Resolve(ActionCategory.MobSkillFinish, 186, CharacterRace.Unknown)!.Motion!.BuildOnModel(crab!, ActionAsset.MainRoutine)!;
            Assert.Equal(new[] { "wz10", "wz20", "wz30" }, shower.Segments.Select(s => s.ClipName).ToArray());

            var scissors = library.Resolve(ActionCategory.MobSkillFinish, 188, CharacterRace.Unknown)!.Motion!.BuildOnModel(crab!, ActionAsset.MainRoutine)!;
            Assert.Equal(new[] { "ma0", "ma2", "at2" }, scissors.Segments.Select(s => s.ClipName).ToArray());
            Assert.Equal(new[] { 154 }, scissors.HitTicks);

        }
    }
}
