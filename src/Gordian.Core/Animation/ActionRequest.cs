// src/Gordian.Core/Animation/ActionRequest.cs
using System;
using System.Collections.Generic;
using System.Threading;
using Gordian.Core.Network.Packets;

namespace Gordian.Core.Animation
{
    /// <summary>
    /// How an action picks the motion routine its actor plays.
    /// </summary>
    public enum ActionMotion : byte
    {
        /// <summary>No body motion (the action still delivers its hits).</summary>
        None,

        /// <summary>
        /// A melee swing: one of the actor's standing swing routines <c>ati0</c>-<c>ati9</c> at random, or the moving swing
        /// for the direction it is moving (<c>atf0</c> forward, <c>atb0</c> back, <c>atl0</c> left, <c>atr0</c> right).
        /// </summary>
        Swing,

        /// <summary>A counter-attack: the <c>cni0</c> / <c>cnf0</c> / <c>cnb0</c> / <c>cnl0</c> / <c>cnr0</c> family, else a swing.</summary>
        Counter,

        /// <summary>The routine named by <see cref="ActionRequest.Routine"/> (a chant, a release, an item use).</summary>
        Routine,

        /// <summary>The spell release matching the actor's last chant: <c>ca??</c> -> <c>sh??</c>.</summary>
        CastRelease,

        /// <summary>A job ability gesture: the actor's <c>cm0</c> clip.</summary>
        Ability,

        /// <summary>Stops the actor's sustained action (a chant or item use that was interrupted).</summary>
        Interrupt
    }

    /// <summary>
    /// One target result an action shows when its motion reaches the hit (S2C 0x028 target + result).
    /// </summary>
    /// <param name="TargetId">Server id of the target.</param>
    /// <param name="Result">The result as decoded: resolution, hit distortion and knockback (<c>Scale</c>), reaction.</param>
    public readonly record struct ActionHit(uint TargetId, CombatActionResult Result);

    /// <summary>
    /// One motion for an actor, built from an S2C 0x028 action by <see cref="ActionPlaybackQueue"/> on the network thread and
    /// played by the actor's <see cref="EntityAnimationState"/> on the render thread. The hits are delivered to the targets once,
    /// either when the actor's routine reaches its hit tick or, when the actor is not animated (off screen, no model), by the
    /// queue's fallback timer.
    /// </summary>
    public sealed class ActionRequest
    {
        private int _hitsDelivered;
        private long _acceptedTimestamp;
        private long _startedTimestamp;

        public uint ActorId { get; init; }
        public ActionCategory Category { get; init; }
        public ActionMotion Motion { get; init; }

        /// <summary>The routine to play for <see cref="ActionMotion.Routine"/> (the 0x028 <c>cmd_arg</c> FourCC, e.g. <c>cabk</c>).</summary>
        public string Routine { get; init; } = string.Empty;

        /// <summary>The result's <c>sub_kind</c> (for a basic attack: 0 main hand, 1 off hand, 2/3 kicks, 4 throw).</summary>
        public ushort SubKind { get; init; }

        public IReadOnlyList<ActionHit> Hits { get; init; } = Array.Empty<ActionHit>();

        /// <summary><see cref="System.Diagnostics.Stopwatch"/> timestamp the request was built at.</summary>
        public long ReceivedTimestamp { get; init; }

        /// <summary>Where the hits go; null for a request that only moves its actor.</summary>
        public IActionHitSink? Sink { get; init; }

        /// <summary>
        /// <see cref="System.Diagnostics.Stopwatch"/> timestamp the actor's animation state took the request into its queue (it is
        /// being animated and will play it), or 0 before.
        /// </summary>
        public long AcceptedTimestamp => Interlocked.Read(ref _acceptedTimestamp);

        /// <summary><see cref="System.Diagnostics.Stopwatch"/> timestamp the actor started the motion, or 0 while it waits.</summary>
        public long StartedTimestamp => Interlocked.Read(ref _startedTimestamp);

        /// <summary>Ticks (60 Hz) from the start of the motion to its hit, set when the actor starts it (-1 while unknown).</summary>
        public int HitTick { get; private set; } = -1;

        public bool HitsDelivered => Volatile.Read(ref _hitsDelivered) != 0;

        /// <summary>Records that the actor's animation state took the request into its queue.</summary>
        public void MarkAccepted(long timestamp) => Interlocked.CompareExchange(ref _acceptedTimestamp, timestamp, 0);

        /// <summary>Records that the actor started playing the request, and when its routine lands the hit.</summary>
        public void MarkStarted(long timestamp, int hitTick)
        {
            HitTick = hitTick;
            Interlocked.Exchange(ref _startedTimestamp, timestamp);
        }

        /// <summary>Delivers the hits to their targets, once.</summary>
        public void DeliverHits()
        {
            if (Interlocked.Exchange(ref _hitsDelivered, 1) != 0) return;
            if (Hits.Count > 0) Sink?.DeliverHits(this);
        }
    }

    /// <summary>Receives an action's hits when its motion lands them.</summary>
    public interface IActionHitSink
    {
        void DeliverHits(ActionRequest request);
    }

    /// <summary>
    /// A reaction for a target to play when an action's hit lands on it.
    /// </summary>
    /// <param name="Resolution">Hit, miss, guard, parry, block (or evade).</param>
    /// <param name="Distortion">Hit distortion amount (0, 0.25, 0.5 or 1) from the low two bits of the result's <c>scale</c>.</param>
    /// <param name="KnockbackLevel">Knockback level 0-7 from the upper three bits of <c>scale</c>.</param>
    /// <param name="FromFront">Whether the attacker stands in front of the target (front vs back damage pose).</param>
    /// <param name="ReactKind">The result's reaction (spikes, counter).</param>
    /// <param name="AttackerId">Server id of the actor.</param>
    /// <param name="PushDirectionX">Ground-plane (X, Z) unit vector pushing the target away from the attacker (for knockback).</param>
    /// <param name="PushDirectionZ">See <paramref name="PushDirectionX"/>.</param>
    /// <param name="Timestamp"><see cref="System.Diagnostics.Stopwatch"/> timestamp the hit landed at.</param>
    public readonly record struct HitReaction(
        ActionResolution Resolution,
        float Distortion,
        byte KnockbackLevel,
        bool FromFront,
        ActionReactKind ReactKind,
        uint AttackerId,
        float PushDirectionX,
        float PushDirectionZ,
        long Timestamp);
}
