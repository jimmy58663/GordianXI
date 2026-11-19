// tests/Gordian.Engine.Tests/Animation/LevelChangeEffectControllerTests.cs
using System.IO;
using System.Linq;
using Gordian.Core.Animation;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.World;
using Gordian.Engine.Animation;
using Xunit;

namespace Gordian.Engine.Tests.Animation
{
    /// <summary>The level up and level down effects (#581).</summary>
    public class LevelChangeEffectControllerTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [Theory]
        [InlineData(9, LevelChangeEffectController.LevelUpFileId)]
        [InlineData(811, LevelChangeEffectController.LevelUpFileId)]
        [InlineData(11, LevelChangeEffectController.LevelDownFileId)]
        [InlineData(813, LevelChangeEffectController.LevelDownFileId)]
        [InlineData(38, 0)]
        [InlineData(53, 0)]
        public void EffectFileFor_MapsTheLevelMessages(int message, int fileId) =>
            Assert.Equal(fileId, LevelChangeEffectController.EffectFileFor(message));

        [Fact]
        public void OtherMessages_QueueNothing()
        {
            var combat = new CombatState();
            var world = new WorldState();
            var me = new WorldEntity(0x01000001, 5, EntityType.Player) { Name = "Me" };
            world.UpsertEntity(me);
            var controller = new LevelChangeEffectController { Library = new ActionMotionLibrary(_ => null) };
            int started = 0;
            controller.EffectStarted += (_, _, _) => started++;
            controller.Attach(combat, world);

            combat.RecordBattleMessage(new CombatMessageRecord { CasterId = me.ServerId, TargetId = 0x01100002, MessageId = 38, Param = 1 });
            Assert.Equal(0, started);
            var effects = new System.Collections.Generic.List<ActorEffectRequest>();
            world.TakeActorEffects(world.CurrentZoneId, effects);
            Assert.Empty(effects);
        }

        /// <summary>
        /// Message 9 plays the retail level up file on its caster (the character, not the defeated monster the target names),
        /// with the Level-Up sound (se 7) at its start, at once and beside the body: the killing blow's finish is still
        /// playing when it arrives (round 1 queued it behind that and it went stale). Skipped without the game install.
        /// </summary>
        [Fact]
        public void LevelUp_PlaysTheLevelUpFileOnTheCaster_WithItsSound()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = new ActionMotionLibrary(rm.LoadDatBytesByFileId);

            var combat = new CombatState();
            var world = new WorldState();
            var me = new WorldEntity(0x01000001, 5, EntityType.Player) { Name = "Me" };
            var mob = new WorldEntity(0x01100002, 2, EntityType.Monster) { Name = "Mandragora" };
            world.UpsertEntity(me);
            world.UpsertEntity(mob);
            var controller = new LevelChangeEffectController { Library = library };
            WorldEntity? soundOn = null;
            controller.EffectStarted += (entity, _, _) => soundOn = entity;
            controller.Attach(combat, world);

            combat.RecordBattleMessage(new CombatMessageRecord { CasterId = me.ServerId, TargetId = mob.ServerId, MessageId = 9, Param = 12 });
            var effects = new System.Collections.Generic.List<ActorEffectRequest>();
            world.TakeActorEffects(world.CurrentZoneId, effects);
            var posted = Assert.Single(effects);
            Assert.Equal(me.ServerId, posted.ActorServerId);
            Assert.Equal(LevelChangeEffectController.LevelUpFileId, posted.Asset!.FileId);
            Assert.Same(me, soundOn);
            Assert.False(me.Animation.HasQueuedActions);
            Assert.False(mob.Animation.HasQueuedActions);

            var asset = library.ResolveEffectFile(LevelChangeEffectController.LevelUpFileId)!;
            Assert.Equal(ActionAssetKind.Effect, asset.Kind);
            Assert.True(asset.Effects!.TryGetRoutine(ActionAsset.MainRoutine, out _));
            var container = EntityModelLoader.ParseDatContainer(rm.LoadDatBytesByFileId(asset.FileId)!, "LevelUp");
            Assert.Contains(container.Routines.Single(r => r.Name == ActionAsset.MainRoutine).SoundCommands,
                s => s.Op == 0x4A && s.SoundId == (int)Gordian.Engine.Ui.StockUiSoundCue.LevelUp);
            Assert.Contains(asset.GeneratorSounds, c => c.Choices.Contains(12094));
        }

        /// <summary>
        /// Level down (message 11) darkens the character with its 0x29 fades (to black at full alpha and back), so its alpha,
        /// the part the renderer draws, stays opaque. Skipped without the game install.
        /// </summary>
        [Fact]
        public void LevelDown_SchedulesItsColourFades_WithoutHidingTheCharacter()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var combat = new CombatState();
            var world = new WorldState();
            var me = new WorldEntity(0x01000001, 5, EntityType.Player) { Name = "Me" };
            world.UpsertEntity(me);
            new LevelChangeEffectController { Library = new ActionMotionLibrary(rm.LoadDatBytesByFileId) }.Attach(combat, world);

            double start = ActorColorTrack.Now;
            combat.RecordBattleMessage(new CombatMessageRecord { CasterId = me.ServerId, TargetId = me.ServerId, MessageId = 11, Param = 11 });
            Assert.True(me.ActorColor.IsActive);
            var dark = me.ActorColor.Sample(start + 60 / 60.0);
            Assert.True(dark.X < 0.1f && dark.Y < 0.1f && dark.Z < 0.1f);
            Assert.Equal(1f, dark.W, 3);
        }

        /// <summary>
        /// The level up text's trail (<c>g004</c>, a child of the text <c>g000</c> that names no attach) is placed by its
        /// parent's attachment, joint reference 1, not at the model origin: placed by its own, it drew a second "Level Up !!"
        /// at the character's feet (#581 round 2). Same for level down's <c>g001</c> under <c>g000</c>. Skipped without the
        /// game install.
        /// </summary>
        [Theory]
        [InlineData(LevelChangeEffectController.LevelUpFileId, "g000", "g004")]
        [InlineData(LevelChangeEffectController.LevelDownFileId, "g000", "g001")]
        public void TheTextsChildGenerator_RidesItsParentsAttachment(int fileId, string parent, string child)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var effects = rm.GetSceneEffects(fileId)!;
            var instance = new Gordian.Engine.Graphics.ActorEffectInstance(effects);
            var parentLayer = effects.Layers.First(l => l.Name == parent && !l.Emitter!.ChildOnly);
            var childLayer = effects.Layers.First(l => l.Name == child && l.Emitter!.ChildOnly);

            var placement = instance.PlacementOf(childLayer.Emitter!);
            Assert.Same(parentLayer.Emitter!.Definition, placement);
            Assert.Equal(Gordian.Core.Resources.Graphics.ParticleAttachType.SourceActor, placement.AttachType);
            // A generator a routine spawns keeps its own.
            Assert.Same(parentLayer.Emitter!.Definition, instance.PlacementOf(parentLayer.Emitter!));
        }

        /// <summary>
        /// The texts carry a negative z offset, drawn as a pull toward the eye so the character does not hide them. Level up
        /// sets it with op 0x30, level down with op 0x72 (ProjectionBiasInitializer), which was not read, so the level down
        /// text was hidden by the body (#590 round 3). Skipped without the game install.
        /// </summary>
        [Theory]
        [InlineData(LevelChangeEffectController.LevelUpFileId, "g000", -0.3f)]
        [InlineData(LevelChangeEffectController.LevelDownFileId, "g000", -0.3f)]
        [InlineData(LevelChangeEffectController.LevelDownFileId, "g001", -0.2f)]
        public void TheTexts_CarryTheirDepthPull(int fileId, string layer, float offset)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var effects = rm.GetSceneEffects(fileId)!;
            Assert.All(effects.Layers.Where(l => l.Name == layer), l => Assert.Equal(offset, l.DepthOffset, 4));
        }
    }
}
