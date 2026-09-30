// src/Gordian.Core/Events/EventScene.cs
using System;
using System.Collections.Generic;

namespace Gordian.Core.Events
{
    /// <summary>
    /// One running event: the <see cref="EventVm"/> of every entity taking part, and the state retail keeps once for
    /// the whole event rather than per entity (the end and cancel flags, the open message, the zone's work values).
    /// <para>
    /// Model referenced from XiEvents (https://github.com/atom0s/XiEvents, "Event VM Functions.md": EventStartWait,
    /// InitEvent2, XiEvent::EventIdle; OpCodes 0x0021, 0x0023, 0x0025): at an event's start every entity whose actor
    /// block carries the event id (or the 0xFFFE catch-all) gets its own event object, and every frame each object
    /// runs its most urgent request stack. 0x21 sets the global end flag (<c>EventExecEnd</c>) that stops them all; a
    /// cancelled query sets it too, with the cancel flag. The message window is one for the event: 0x23 on any entity
    /// waits while it is open.
    /// </para>
    /// <para>
    /// When the event is over: on the end flag, or once every entity that carries the event id itself has run out of
    /// requests (catch-all blocks alone do not hold the event open). Retail's own rule for an event that never runs
    /// 0x21 is not documented; every talk and intro scene checked ends its owner's part with 0x21 or 0x00.
    /// </para>
    /// </summary>
    public sealed class EventScene
    {
        private readonly List<EventVm> _actors = new();
        private double _messageOpenSeconds;

        public EventScene(EventWorkZone zone)
        {
            Zone = zone ?? throw new ArgumentNullException(nameof(zone));
        }

        /// <summary>The zone's shared work values the event reads and writes.</summary>
        public EventWorkZone Zone { get; }

        /// <summary>The local player's server id, so actor code 0x7FFFFFF0 finds the player's event object.</summary>
        public uint PlayerServerId { get; set; }

        /// <summary>The event objects, one per entity, in start order.</summary>
        public IReadOnlyList<EventVm> Actors => _actors;

        /// <summary>Whether 0x21 (or a cancelled query) ended the event.</summary>
        public bool IsEnded { get; private set; }

        /// <summary>Whether the player cancelled a query, which ends the event with <see cref="EventVm.CancelledEndParameter"/>.</summary>
        public bool IsCancelled { get; private set; }

        /// <summary>Whether the event is over (see the class notes).</summary>
        public bool IsFinished
        {
            get
            {
                if (IsEnded) return true;
                bool anyCarrier = false;
                foreach (var actor in _actors)
                {
                    if (actor.CarriesEvent) anyCarrier = true;
                }
                foreach (var actor in _actors)
                {
                    if ((actor.CarriesEvent || !anyCarrier) && actor.HasRequests) return false;
                }
                return true;
            }
        }

        /// <summary>Whether a printed message is still open (the player's confirm closes it early).</summary>
        public bool IsWaitingForConfirm => _messageOpenSeconds > 0;

        /// <summary>The value the end packet reports.</summary>
        public uint EndParameter => IsCancelled ? EventVm.CancelledEndParameter : unchecked((uint)Zone.EndParameter);

        /// <summary>The player confirmed the open message: the event goes on at its next tick.</summary>
        public void Confirm() => _messageOpenSeconds = 0;

        /// <summary>Runs one frame: each entity's most urgent request, in start order, until the event ends.</summary>
        public void Tick(TimeSpan elapsed)
        {
            if (_messageOpenSeconds > 0) _messageOpenSeconds = Math.Max(0, _messageOpenSeconds - elapsed.TotalSeconds);
            // Retail's frame delay: the 60 Hz frames since the last tick, the same for every opcode of the tick.
            float frameDelay = (float)(elapsed.TotalSeconds * 60.0);
            for (int i = 0; i < _actors.Count && !IsEnded; i++) _actors[i].Run(frameDelay);
        }

        /// <summary>Ends the event (0x21, a cancelled query, a server cancel), closing any open query.</summary>
        public void End(bool cancelled)
        {
            if (cancelled) IsCancelled = true;
            if (IsEnded) return;
            IsEnded = true;
            foreach (var actor in _actors) actor.CloseQuery();
        }

        /// <summary>The event object of an entity (server id 0 = the local player), or null when it takes no part.</summary>
        public EventVm? FindActor(uint serverId)
        {
            if (serverId == 0) serverId = PlayerServerId;
            foreach (var actor in _actors)
            {
                if (actor.EntityServerId == serverId) return actor;
            }
            return null;
        }

        internal void Add(EventVm vm) => _actors.Add(vm);

        internal void OpenMessage(double seconds) => _messageOpenSeconds = Math.Max(0, seconds);
    }
}
