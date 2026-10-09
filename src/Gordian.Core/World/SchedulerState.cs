// src/Gordian.Core/World/SchedulerState.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;

namespace Gordian.Core.World
{
    /// <summary>
    /// An actor scheduler the server asked to play (S2C 0x038): a script from the caster actor's animation DAT, looked
    /// up by a FourCC, aimed at the target actor.
    /// </summary>
    /// <param name="ZoneId">The zone the packet arrived in.</param>
    /// <param name="Routine">The key as text ("kesu" fade out, "hitl" sweating); empty when it was not printable ASCII.</param>
    /// <param name="RoutineId">The key's four bytes as a little-endian integer.</param>
    public sealed record ActorSchedulerRequest(
        ushort ZoneId,
        string Routine,
        uint RoutineId,
        uint CasterServerId,
        uint TargetServerId,
        ushort CasterIndex,
        ushort TargetIndex);

    /// <summary>
    /// A magic scheduler the server asked to play outside an action packet (S2C 0x03A): the numbered effect script
    /// <paramref name="FileNumber"/> of the effect DATs.
    /// </summary>
    public sealed record MagicSchedulerRequest(
        ushort ZoneId,
        ushort FileNumber,
        byte TypeId,
        uint CasterServerId,
        uint TargetServerId,
        ushort CasterIndex,
        ushort TargetIndex)
    {
        /// <summary>The type as the enum; ids above 0x0C are outside what the client accepts.</summary>
        public MagicSchedulerType Type => (MagicSchedulerType)TypeId;
    }

    /// <summary>
    /// What the server asked the client to animate with scheduler packets that are not part of an action: S2C 0x038
    /// (actor schedulers) and 0x03A (magic schedulers). Decode only: nothing plays them yet. The requests wait here,
    /// bounded and in arrival order, until a playback component takes them (<see cref="TakeActorSchedulers"/>,
    /// <see cref="TakeMagicSchedulers"/>) or subscribes to the events; a zone change drops what is left.
    /// S2C 0x039 is a separate path (<see cref="WorldState.PostMapScheduler(string, uint, uint)"/>).
    /// </summary>
    public sealed class SchedulerState
    {
        /// <summary>Most requests of each kind kept before the oldest is dropped.</summary>
        public const int MaxPending = 64;

        private readonly object _gate = new();
        private readonly List<ActorSchedulerRequest> _actor = new();
        private readonly List<MagicSchedulerRequest> _magic = new();

        /// <summary>Raised for every S2C 0x038 (after it is queued).</summary>
        public event Action<ActorSchedulerRequest>? ActorSchedulerPosted;

        /// <summary>Raised for every S2C 0x03A (after it is queued).</summary>
        public event Action<MagicSchedulerRequest>? MagicSchedulerPosted;

        public int PendingActorCount { get { lock (_gate) return _actor.Count; } }
        public int PendingMagicCount { get { lock (_gate) return _magic.Count; } }

        internal void PostActor(ActorSchedulerRequest request)
        {
            lock (_gate)
            {
                if (_actor.Count >= MaxPending) _actor.RemoveAt(0);
                _actor.Add(request);
            }
            ActorSchedulerPosted?.Invoke(request);
        }

        internal void PostMagic(MagicSchedulerRequest request)
        {
            lock (_gate)
            {
                if (_magic.Count >= MaxPending) _magic.RemoveAt(0);
                _magic.Add(request);
            }
            MagicSchedulerPosted?.Invoke(request);
        }

        /// <summary>Moves the queued actor schedulers into <paramref name="into"/>, oldest first, and empties the queue.</summary>
        public void TakeActorSchedulers(List<ActorSchedulerRequest> into)
        {
            ArgumentNullException.ThrowIfNull(into);
            lock (_gate)
            {
                into.AddRange(_actor);
                _actor.Clear();
            }
        }

        /// <summary>Moves the queued magic schedulers into <paramref name="into"/>, oldest first, and empties the queue.</summary>
        public void TakeMagicSchedulers(List<MagicSchedulerRequest> into)
        {
            ArgumentNullException.ThrowIfNull(into);
            lock (_gate)
            {
                into.AddRange(_magic);
                _magic.Clear();
            }
        }

        /// <summary>Drops every queued request (a zone change: the actors they name are gone).</summary>
        public void Clear()
        {
            lock (_gate)
            {
                _actor.Clear();
                _magic.Clear();
            }
        }
    }
}
