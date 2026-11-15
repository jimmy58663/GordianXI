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
            new LevelChangeEffectController { Library = new ActionMotionLibrary(_ => null) }.Attach(combat, world);

            combat.RecordBattleMessage(new CombatMessageRecord { CasterId = me.ServerId, TargetId = 0x01100002, MessageId = 38, Param = 1 });
            Assert.False(me.Animation.HasQueuedActions);
        }

        /// <summary>
        /// Message 9 plays the retail level up file on its caster (the character, not the defeated monster the target names),
        /// with the Level-Up sound (se 7) at its start. Skipped without the game install.
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
            new LevelChangeEffectController { Library = library }.Attach(combat, world);

            combat.RecordBattleMessage(new CombatMessageRecord { CasterId = me.ServerId, TargetId = mob.ServerId, MessageId = 9, Param = 12 });
            Assert.True(me.Animation.HasQueuedActions);
            Assert.False(mob.Animation.HasQueuedActions);

            var asset = library.ResolveEffectFile(LevelChangeEffectController.LevelUpFileId)!;
            Assert.Equal(ActionAssetKind.Effect, asset.Kind);
            Assert.True(asset.Effects!.TryGetRoutine(ActionAsset.MainRoutine, out _));
            var container = EntityModelLoader.ParseDatContainer(rm.LoadDatBytesByFileId(asset.FileId)!, "LevelUp");
            Assert.Contains(container.Routines.Single(r => r.Name == ActionAsset.MainRoutine).SoundCommands,
                s => s.Op == 0x4A && s.SoundId == (int)Gordian.Engine.Ui.StockUiSoundCue.LevelUp);
            Assert.Contains(asset.GeneratorSounds, c => c.Choices.Contains(12094));
        }
    }
}
