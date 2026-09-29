// src/Gordian.Core/Animation/ActionPlaybackQueue.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Core.Animation
{
    /// <summary>
    /// Bridges S2C 0x028 combat actions (<see cref="CombatState.ActionExecuted"/>) into entity animation: each action becomes
    /// one or more <see cref="ActionRequest"/>s on its actor's <see cref="EntityAnimationState"/> (a basic attack round plays
    /// one swing per result), and each request's results become <see cref="HitReaction"/>s on the targets when the actor's
    /// routine reaches its hit. When the actor is not being animated (off screen, no model) the queue shows the hits itself,
    /// shortly after they would have landed, so targets never miss their reactions.
    /// <para>
    /// What the action plays follows the packet's <c>cmd_no</c> / <c>cmd_arg</c> (XiPackets 0x0028): a basic attack's
    /// <c>cmd_arg</c> is the routine <c>atk0</c>, which dispatches to the weapon's swings; the start packets of casts, item
    /// uses, ranged attacks and readied skills carry the actor's routine FourCC (<c>cabk</c>, <c>cait</c>, <c>calg</c>,
    /// <c>cate</c>), or an <c>sp??</c> one when interrupted; a finished spell plays the release of the chant (<c>ca??</c> to
    /// <c>sh??</c>).
    /// </para>
    /// Packet semantics referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x0028) and
    /// LandSandBoat (https://github.com/LandSandBoat/server, src/map/attack.h AttackAnimation, enums/action/knockback.h,
    /// enums/action/hit_distortion.h).
    /// </summary>
    public sealed class ActionPlaybackQueue : IActionHitSink, IDisposable
    {
        /// <summary>Hits of a request no animation state took up (actor off screen or without a model) show after this long.</summary>
        public const float UnacceptedHitSeconds = 0.7f;

        /// <summary>Hits of a request waiting in an animated actor's queue show after this long if it never starts.</summary>
        public const float QueuedHitSeconds = 3.0f;

        /// <summary>Hits of a started request show this long after its hit tick if the actor stopped being animated.</summary>
        public const float StartedHitSlackSeconds = 0.5f;

        private readonly CombatState _combat;
        private readonly WorldState _world;
        private readonly List<ActionRequest> _outstanding = new();
        private readonly object _lock = new();

        /// <summary>Raised on the delivering thread when a hit lands on a target (after its reaction is queued).</summary>
        public event Action<WorldEntity, HitReaction>? HitLanded;

        public ActionPlaybackQueue(CombatState combat, WorldState world)
        {
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _combat.ActionExecuted += Dispatch;
        }

        /// <summary>Requests whose hits have not been shown yet.</summary>
        public int OutstandingCount
        {
            get { lock (_lock) return _outstanding.Count; }
        }

        /// <summary>Hit distortion (0, 0.25, 0.5, 1) from the low two bits of a result's <c>scale</c> (XiPackets 0x0028).</summary>
        public static float DistortionOf(byte scale) => (scale & 3) switch
        {
            1 => 0.25f,
            2 => 0.5f,
            3 => 1.0f,
            _ => 0f
        };

        /// <summary>Knockback level 0-7 from the upper three bits of a result's <c>scale</c> (LandSandBoat <c>Knockback</c>).</summary>
        public static byte KnockbackLevelOf(byte scale) => (byte)((scale >> 2) & 7);

        /// <summary>
        /// Reads a 0x028 <c>cmd_arg</c> as the routine FourCC it carries (little-endian ASCII, e.g. 812348513 = <c>atk0</c>),
        /// or an empty string when it is an id instead.
        /// </summary>
        public static string FourCcOf(uint value)
        {
            Span<char> chars = stackalloc char[4];
            for (int i = 0; i < 4; i++)
            {
                byte b = (byte)(value >> (8 * i));
                if (b < 0x20 || b > 0x7E) return string.Empty;
                chars[i] = (char)b;
            }
            return new string(chars);
        }

        /// <summary>Turns one action into requests on its actor (network thread).</summary>
        public void Dispatch(CombatActionRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            long now = Stopwatch.GetTimestamp();
            _world.TryGetByServerId(record.ActorId, out var actor);

            if (record.Category == ActionCategory.BasicAttack)
            {
                // One swing per result: a double attack or dual wield round swings each hit in turn.
                foreach (var target in record.Targets)
                {
                    foreach (var result in target.Results)
                    {
                        Post(actor, new ActionRequest
                        {
                            ActorId = record.ActorId,
                            Category = record.Category,
                            Motion = ActionMotion.Swing,
                            SubKind = result.Animation,
                            Hits = [new ActionHit(target.TargetId, result)],
                            ReceivedTimestamp = now,
                            Sink = this
                        });
                    }
                }
                return;
            }

            var (motion, routine) = MotionFor(record);
            var hits = new List<ActionHit>(record.Targets.Count);
            foreach (var target in record.Targets)
            {
                if (target.Results.Count > 0) hits.Add(new ActionHit(target.TargetId, target.Results[0]));
            }

            // Start packets name the actor as their only target; they show nothing on it.
            if (IsStart(record.Category)) hits.Clear();

            Post(actor, new ActionRequest
            {
                ActorId = record.ActorId,
                Category = record.Category,
                Motion = motion,
                Routine = routine,
                SubKind = record.Targets.Count > 0 && record.Targets[0].Results.Count > 0 ? record.Targets[0].Results[0].Animation : (ushort)0,
                Hits = hits,
                ReceivedTimestamp = now,
                Sink = this
            });
        }

        private static bool IsStart(ActionCategory category) => category is ActionCategory.SkillStart or ActionCategory.MagicStart
            or ActionCategory.ItemStart or ActionCategory.AbilityStart or ActionCategory.RangedStart;

        /// <summary>The motion an action's actor plays, by <c>cmd_no</c> and <c>cmd_arg</c>.</summary>
        internal static (ActionMotion Motion, string Routine) MotionFor(CombatActionRecord record)
        {
            string fourCc = FourCcOf(record.ActionId);
            switch (record.Category)
            {
                case ActionCategory.MagicStart:
                case ActionCategory.ItemStart:
                case ActionCategory.SkillStart:
                case ActionCategory.RangedStart:
                    if (fourCc.Length == 0) return (ActionMotion.None, string.Empty);
                    return fourCc.StartsWith("sp", StringComparison.Ordinal) ? (ActionMotion.Interrupt, fourCc) : (ActionMotion.Routine, fourCc);

                case ActionCategory.MagicFinish:
                    return (ActionMotion.CastRelease, string.Empty);

                case ActionCategory.ItemFinish:
                    return (ActionMotion.Routine, "shit");

                case ActionCategory.RangedFinish:
                    return fourCc.Length > 0 ? (ActionMotion.Routine, fourCc) : (ActionMotion.None, string.Empty);

                // Weapon skills and monster / pet skills have their own animation DATs (not played yet); a swing stands in.
                case ActionCategory.SkillFinish:
                case ActionCategory.MobSkillFinish:
                case ActionCategory.PetSkillFinish:
                    return (ActionMotion.Swing, string.Empty);

                case ActionCategory.AbilityFinish:
                case ActionCategory.Dancer:
                case ActionCategory.RuneFencer:
                    return (ActionMotion.Ability, string.Empty);

                default:
                    return (ActionMotion.None, string.Empty);
            }
        }

        private void Post(WorldEntity? actor, ActionRequest request)
        {
            if (request.Hits.Count > 0)
            {
                lock (_lock) _outstanding.Add(request);
            }
            if (actor != null && request.Motion != ActionMotion.None)
            {
                actor.Animation.EnqueueAction(request);
            }
        }

        /// <summary>
        /// Shows the hits of requests whose actor is not animating them in time (render thread, once per frame).
        /// </summary>
        public void Update() => Update(Stopwatch.GetTimestamp());

        /// <summary><see cref="Update()"/> at a given <see cref="Stopwatch"/> timestamp.</summary>
        public void Update(long now)
        {
            List<ActionRequest>? due = null;
            lock (_lock)
            {
                for (int i = _outstanding.Count - 1; i >= 0; i--)
                {
                    var request = _outstanding[i];
                    if (request.HitsDelivered)
                    {
                        _outstanding.RemoveAt(i);
                    }
                    else if (IsOverdue(request, now))
                    {
                        _outstanding.RemoveAt(i);
                        (due ??= new List<ActionRequest>()).Add(request);
                    }
                }
            }

            if (due == null) return;
            for (int i = due.Count - 1; i >= 0; i--) due[i].DeliverHits();
        }

        private static bool IsOverdue(ActionRequest request, long now)
        {
            long started = request.StartedTimestamp;
            if (started != 0)
            {
                float hitSeconds = Math.Max(0, request.HitTick) / EntityAnimationState.RoutineTicksPerSecond;
                return Seconds(started, now) > hitSeconds + StartedHitSlackSeconds;
            }

            long accepted = request.AcceptedTimestamp;
            if (accepted != 0) return Seconds(accepted, now) > QueuedHitSeconds;
            return Seconds(request.ReceivedTimestamp, now) > UnacceptedHitSeconds;
        }

        private static float Seconds(long from, long to) => (float)(to - from) / Stopwatch.Frequency;

        /// <summary>Queues each hit's reaction on its target (called once per request, on whichever thread shows it).</summary>
        public void DeliverHits(ActionRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            long now = Stopwatch.GetTimestamp();
            _world.TryGetByServerId(request.ActorId, out var actor);

            foreach (var hit in request.Hits)
            {
                if (!_world.TryGetByServerId(hit.TargetId, out var target) || target == null) continue;

                var reaction = BuildReaction(actor, target, hit, now);
                target.Animation.EnqueueReaction(reaction);
                HitLanded?.Invoke(target, reaction);
            }
        }

        /// <summary>
        /// Builds a target's reaction: which damage pose (the attacker in front of the target or behind), how hard, and the
        /// direction a knockback pushes (away from the attacker).
        /// </summary>
        internal static HitReaction BuildReaction(WorldEntity? actor, WorldEntity target, ActionHit hit, long timestamp)
        {
            bool fromFront = true;
            Vector2 push = WorldEntity.ForwardOf(target.HeadingRadians) * -1f;
            if (actor != null && !ReferenceEquals(actor, target))
            {
                var toActor = new Vector2(actor.Position.X - target.Position.X, actor.Position.Z - target.Position.Z);
                if (toActor.LengthSquared() > 1e-6f)
                {
                    toActor = Vector2.Normalize(toActor);
                    fromFront = Vector2.Dot(toActor, WorldEntity.ForwardOf(target.HeadingRadians)) >= 0f;
                    push = -toActor;
                }
            }

            var result = hit.Result;
            return new HitReaction(
                result.Resolution,
                DistortionOf(result.Scale),
                KnockbackLevelOf(result.Scale),
                fromFront,
                result.HasReaction ? result.ReactionKind : ActionReactKind.None,
                actor?.ServerId ?? 0,
                push.X,
                push.Y,
                timestamp);
        }

        public void Dispose()
        {
            _combat.ActionExecuted -= Dispatch;
        }
    }
}
