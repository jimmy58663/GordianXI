// tests/Gordian.App.Tests/Audio/ActionSoundTrackerRealDataTests.cs
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Gordian.App.Audio;
using Gordian.Core.Animation;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.App.Tests.Audio
{
    /// <summary>Combat sounds on the retail models (#328). Skipped without the game install.</summary>
    public class ActionSoundTrackerRealDataTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public ActionSoundTrackerRealDataTests(ITestOutputHelper output) => _output = output;

        /// <summary>
        /// An Elvaan male monk's hits on a Macro Test dummy (model 431, whose damg links no chit): a hit plays the weapon's
        /// hit sound, main and off hand, 0 damage included (rounds 3-4, D29).
        /// </summary>
        [Fact]
        public void HitOnTheMacroTestDummy_PlaysTheWeaponHitSound()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var grap = new ushort[9];
            grap[6] = 1;
            EntityModel monk = EntityModelLoader.AssembleCharacter(CharacterRace.ElvaanMale, 1, grap, rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
            EntityModel dummy = EntityModelLoader.LoadMonsterModel(431, rm.LoadDatBytesByFileId)!;
            _output.WriteLine("monk chit: " + string.Join(",", monk.GetRoutineSounds("chit").Select(c => string.Join("/", c.Choices))));
            _output.WriteLine("dummy damg: " + string.Join(",", dummy.GetRoutineSounds("damg").Select(c => (c.IsTargetLink ? c.TargetRoutine : "") + string.Join("/", c.Choices))));

            foreach (bool offHand in new[] { false, true })
            {
                var tracker = new ActionSoundTracker(_ => 0);
                var attacker = new ActorSoundState(3, Vector3.Zero, monk, null, 0, 0, 0, default, false, true, offHand);
                var hit = new HitReaction(ActionResolution.Hit, 0f, 0, true, default, 3, 0, 0, 0, new CombatActionResult { Resolution = ActionResolution.Hit }, ActionCategory.BasicAttack);
                var before = new ActorSoundState(0x0106E217, Vector3.One, dummy, null, 0, 0, 0, default, false);
                var after = before with { ReactionSerial = 1, LastReaction = hit };
                var into = new List<ActionSoundEvent>();
                tracker.Step(before, id => id == 3 ? attacker : null, into);
                tracker.Step(after, id => id == 3 ? attacker : null, into);
                _output.WriteLine($"offHand {offHand}: [{string.Join(",", into.Select(e => e.SoundId))}]");
                Assert.Contains(into, e => monk.GetRoutineSounds("chit", offHand).SelectMany(c => c.Choices).Contains(e.SoundId));
            }
        }
    }
}
