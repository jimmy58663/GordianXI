// src/Gordian.Engine/Animation/LevelChangeEffectController.cs
using System;
using System.Diagnostics;
using Gordian.Core.Animation;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Engine.Animation
{
    /// <summary>
    /// The level up and level down effects (#581). The server sends no effect packet: LandSandBoat's <c>charutils.cpp</c>
    /// sends S2C 0x02D message 9 ("<c>attains level</c>", commented "Level up animation and message") or 11 ("<c>falls to
    /// level</c>") to the character and everyone in range, and the client plays a fixed effect file on the message's caster
    /// (the character; for 9 the target is the defeated monster). Played on every character it arrives for, as the server
    /// sends it to all in range.
    /// <list type="bullet">
    /// <item>Level up: <see cref="LevelUpFileId"/> (<c>ROM/13/35</c>, xi-tools <c>mv/lists/effects.json</c> "Level Up"). Its
    /// <c>main</c> (170 ticks) plays sound command 0x4A <c>0007</c> (<c>se000007</c>, xi-tools <c>sfx.json</c> "Main Sounds -
    /// Level-Up", <c>StockUiSoundCue.LevelUp</c>) and generator <c>g0s0</c>'s sound 12094 at tick 0, spawns <c>g000</c> and
    /// <c>g001</c> (frame 15, 60 long), and links the actor's <c>mdam</c> at 30 (read 2026-11-15); textures <c>lvup</c>
    /// <c>lvu1</c>-<c>lvu4</c>. Retail (local LSB, <c>581-retail-level-up.mp4</c>, 2026-11-15): an orange "Level Up !!" text
    /// over the character rises in with a streaky reveal and sparkles for about 1 s, holds about 2 s and fades, about 3 s
    /// in all.</item>
    /// <item>Level down: <see cref="LevelDownFileId"/> (<c>ROM/13/34</c>, "Level Down", textures <c>lvdw</c> <c>lvd1</c>):
    /// generator sound 12099, <c>g000</c>, <c>g002</c>, and a 0x29 actor colour fade at 0 and 80. Retail
    /// (<c>581-retail-level-down.mp4</c>): a blue "Level Down..." text while the character darkens to black and back.</item>
    /// <item>Master level up / down (811 / 813): the same files (PROVISIONAL: LandSandBoat never sends either, so neither
    /// could be seen on local LSB). <c>ROM/306/49</c> (file 3704, xi-tools' "Level Up!") is not it: it is the level up file
    /// again with <c>mnp_lvup</c> textures, the Monstrosity (Monipulator) level up.</item>
    /// </list>
    /// </summary>
    public sealed class LevelChangeEffectController
    {
        /// <summary>The basic messages (S2C 0x02D <c>MessageNum</c>, LandSandBoat <c>MsgBasic</c>) that play an effect.</summary>
        public const int LevelUpMessage = 9, LevelDownMessage = 11, MasterLevelUpMessage = 811, MasterLevelDownMessage = 813;

        /// <summary>The level up effect file (<c>ROM/13/35</c>).</summary>
        public const int LevelUpFileId = 3310;

        /// <summary>The level down effect file (<c>ROM/13/34</c>).</summary>
        public const int LevelDownFileId = 3309;

        private WorldState? _world;

        /// <summary>The library the effect files come from (the shared one by default; tests set their own).</summary>
        public ActionMotionLibrary Library { get; set; } = ActionMotionLibrary.Shared;

        /// <summary>The effect file a basic message plays on its caster, or 0.</summary>
        public static int EffectFileFor(int messageId) => messageId switch
        {
            LevelUpMessage or MasterLevelUpMessage => LevelUpFileId,
            LevelDownMessage or MasterLevelDownMessage => LevelDownFileId,
            _ => 0,
        };

        /// <summary>Subscribes to a session's battle messages.</summary>
        public void Attach(CombatState combat, WorldState world)
        {
            ArgumentNullException.ThrowIfNull(combat);
            _world = world ?? throw new ArgumentNullException(nameof(world));
            combat.BattleMessageReceived += OnBattleMessage;
        }

        private void OnBattleMessage(CombatMessageRecord record)
        {
            int fileId = EffectFileFor(record.MessageId);
            if (fileId == 0 || _world == null) return;
            WorldEntity? entity = null;
            if (record.CasterId == 0 || !_world.TryGetByServerId(record.CasterId, out entity) || entity == null)
            {
                _world.TryGetByTargetIndex(record.CasterIndex, out entity);
            }
            if (entity == null)
            {
                GordianLog.Debug(LogCategories.Anim, $"Message {record.MessageId}: caster {record.CasterId:X8} not in the world, no effect.");
                return;
            }
            var asset = Library.ResolveEffectFile(fileId);
            if (asset == null)
            {
                GordianLog.Debug(LogCategories.Anim, $"Message {record.MessageId}: effect file {fileId} could not be read.");
                return;
            }
            GordianLog.Debug(LogCategories.Anim, $"Message {record.MessageId} on {entity.ServerId:X8}: {asset}.");
            entity.Animation.EnqueueAction(new ActionRequest
            {
                ActorId = entity.ServerId,
                Category = ActionCategory.None,
                Motion = ActionMotion.Routine,
                Asset = asset,
                ReceivedTimestamp = Stopwatch.GetTimestamp(),
            });
        }
    }
}
