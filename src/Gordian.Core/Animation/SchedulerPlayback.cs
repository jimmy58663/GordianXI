// src/Gordian.Core/Animation/SchedulerPlayback.cs
using System;
using System.Diagnostics;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Tables;
using Gordian.Core.World;

namespace Gordian.Core.Animation
{
    /// <summary>
    /// Plays the scheduler packets that are not part of an action (#311), as they arrive (network thread):
    /// <list type="bullet">
    /// <item><b>S2C 0x038</b> (<see cref="ActorSchedulerRequest"/>): the caster plays the named routine of its own motion
    /// DATs as an event gesture does (<see cref="ActionMotion.EventMotion"/>), when it has one, and the viewport plays the
    /// routine's effects on it (<see cref="WorldState.PostActorEffect"/>): the model's own generators of that routine (the
    /// Home Point's <c>bind</c>) and the shared <c>ROM/0/0</c> routine of that name, whose colour commands fade the entity
    /// (<c>kesu</c>: 0x29 to alpha 0 over 90 frames, sent by LandSandBoat when an entity despawns) and whose actor links
    /// run its own routines (<c>hitl</c>, the fishing sweat: <c>eflg</c>, <c>selg</c>).</item>
    /// <item><b>S2C 0x03A</b> (<see cref="MagicSchedulerRequest"/>): the presentation file of <c>fileNum</c> in the family
    /// its <c>type</c> names plays on the caster toward the target like an action's (#132): type 0 a spell
    /// (2800 + n), 2 a job ability (4412 + n), 6 a weapon skill (the caster's race bank), 9 a monster skill (3900 + n /
    /// 50159 + n - 512). The other types (1 item, 3 / 4 / 5 / 8 / 10 / 11 / 12 events, banners and warps) are logged and
    /// not played: their files are not known.</item>
    /// </list>
    /// Packet semantics referenced from XiPackets (https://github.com/atom0s/XiPackets, world/server/0x0038 and 0x003A)
    /// and LandSandBoat (<c>entityAnimationPacket</c>, <c>independentAnimation</c>). That 0x03A's fileNum is the same
    /// animation number as an action's for those four types is *inference*: LandSandBoat sends <c>xi.magic.spell.WARP</c>
    /// with type 0 (<c>imperial_agent_rescue.lua</c>) and 122 with type 0 for a teleport-out (<c>Archaic_Gear.lua</c>).
    /// </summary>
    public sealed class SchedulerPlayback
    {
        private readonly WorldState _world;
        private readonly ActionMotionLibrary? _library;

        public SchedulerPlayback(SchedulerState scheduler, WorldState world, ActionMotionLibrary? library)
        {
            ArgumentNullException.ThrowIfNull(scheduler);
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _library = library;
            scheduler.ActorSchedulerPosted += PlayActor;
            scheduler.MagicSchedulerPosted += PlayMagic;
        }

        /// <summary>The action category whose presentation files a 0x03A type plays, or null when it is not known.</summary>
        public static ActionCategory? CategoryOf(MagicSchedulerType type) => (byte)type switch
        {
            0 => ActionCategory.MagicFinish,
            2 => ActionCategory.AbilityFinish,
            6 => ActionCategory.SkillFinish,
            9 => ActionCategory.MobSkillFinish,
            _ => null
        };

        /// <summary>Plays an S2C 0x038 on its caster.</summary>
        public void PlayActor(ActorSchedulerRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.Routine.Length == 0 || !TryFind(request.CasterServerId, request.CasterIndex, out var caster)) return;
            uint target = TryFind(request.TargetServerId, request.TargetIndex, out var found) ? found.ServerId : caster.ServerId;
            bool moves = caster.Animation.GetRoutineFrames(request.Routine) > 0;
            if (moves)
            {
                caster.Animation.EnqueueAction(new ActionRequest
                {
                    ActorId = caster.ServerId,
                    Motion = ActionMotion.EventMotion,
                    Routine = request.Routine,
                    TargetId = target,
                    ReceivedTimestamp = Stopwatch.GetTimestamp(),
                });
            }
            _world.PostActorEffect(new ActorEffectRequest(caster.ServerId, target, request.Routine));
            GordianLog.Debug("ANIM", $"Actor scheduler '{request.Routine}' on {caster.ServerId:X8} toward {target:X8}{(moves ? string.Empty : " (no motion of that name)")}.");
        }

        /// <summary>Plays an S2C 0x03A on its caster toward its target.</summary>
        public void PlayMagic(MagicSchedulerRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!TryFind(request.CasterServerId, request.CasterIndex, out var caster)) return;
            uint target = TryFind(request.TargetServerId, request.TargetIndex, out var found) ? found.ServerId : caster.ServerId;
            if (CategoryOf(request.Type) is not { } category || _library == null)
            {
                GordianLog.Debug("ANIM", $"Magic scheduler {request.FileNumber} type {request.TypeId} on {caster.ServerId:X8}: type not played.");
                return;
            }
            var race = category == ActionCategory.SkillFinish && caster.Appearance.ModelId == 0
                ? (CharacterRace)((caster.Appearance.FaceModel >> 8) & 0xFF)
                : CharacterRace.Unknown;
            var asset = _library.Resolve(category, request.FileNumber, race);
            if (asset == null)
            {
                GordianLog.Debug("ANIM", $"Magic scheduler {request.FileNumber} type {request.TypeId} on {caster.ServerId:X8}: no presentation file.");
                return;
            }
            if (asset.Motion != null)
            {
                // The body plays the presentation's main; its effects are played once, by the viewport, below.
                caster.Animation.EnqueueAction(new ActionRequest
                {
                    ActorId = caster.ServerId,
                    Category = category,
                    Motion = ActionMotion.None,
                    Asset = new ActionAsset(asset.Kind, asset.Animation, asset.FileId, asset.Motion, null),
                    TargetId = target,
                    ReceivedTimestamp = Stopwatch.GetTimestamp(),
                });
            }
            if (asset.Effects != null) _world.PostActorEffect(new ActorEffectRequest(caster.ServerId, target, string.Empty, asset));
            GordianLog.Debug("ANIM", $"Magic scheduler {request.FileNumber} type {request.TypeId} on {caster.ServerId:X8} toward {target:X8}: {asset}.");
        }

        private bool TryFind(uint serverId, ushort index, out WorldEntity entity)
        {
            if (serverId != 0 && _world.TryGetByServerId(serverId, out var byId))
            {
                entity = byId;
                return true;
            }
            if (index != 0 && _world.TryGetByTargetIndex(index, out var byIndex))
            {
                entity = byIndex;
                return true;
            }
            entity = null!;
            return false;
        }
    }
}
