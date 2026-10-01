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
    /// The scene also keeps the event's scheduler state (#165): the main scheduler's tasks (0x45 starts one, 0x52 ends
    /// it, 0x55 waits for it) and the action each entity plays for the event (0x2C / 0x5B / 0x66 start one, 0x50 ends
    /// it, 0x53 waits for it), each running for the frames its resource says. A task whose resource is missing lasts
    /// no frames, so no wait on it can hold the scene.
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

        /// <summary>Whether the event holds the camera (0x46 01 until 0x46 00).</summary>
        public bool IsCameraHeld { get; internal set; }

        /// <summary>
        /// The low word of retail's <c>CliEventModeLocal</c> as 0x38 set it (operand | 0x2000; 0 before), the mask of what the
        /// event takes from the player: the Southern San d'Oria intro sets 0x13 for its aerial shots and 0x03 when the
        /// player walks in, Bastok Markets 0x12, Ailevia's tour 0x03 (XiEvents OpCodes/0x0038 gives 0x2003). Which bit does
        /// what is not known yet, so nothing reads it (#176).
        /// </summary>
        public int EventModeLocal { get; internal set; }

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
            AdvanceTasks(frameDelay);
            for (int i = 0; i < _actors.Count && !IsEnded; i++) _actors[i].Run(frameDelay);
        }

        #region Scheduler tasks

        /// <summary>
        /// A task of the main scheduler (0x45): a scene routine on two actors, identified as retail's KillScheduler /
        /// IsMovingScheduler find it, by resource file, routine and actors.
        /// </summary>
        private sealed class SchedulerTask
        {
            public int Id;
            public int FileId;
            public uint Tag;
            public uint Caster;
            public uint Target;
            public float RemainingFrames;
        }

        /// <summary>An action an entity plays for the event (0x2C / 0x5B / 0x66), until its routine's length has passed.</summary>
        private struct EntityAction
        {
            public uint Tag;
            public float RemainingFrames;
        }

        private readonly List<SchedulerTask> _tasks = new();
        private readonly Dictionary<uint, EntityAction> _entityActions = new();
        private readonly Dictionary<uint, float> _turns = new();
        private int _nextTaskId;

        private void AdvanceTasks(float frames)
        {
            for (int i = _tasks.Count - 1; i >= 0; i--)
            {
                _tasks[i].RemainingFrames -= frames;
                if (_tasks[i].RemainingFrames <= 0) _tasks.RemoveAt(i);
            }
            if (_entityActions.Count > 0)
            {
                foreach (uint id in new List<uint>(_entityActions.Keys))
                {
                    var action = _entityActions[id];
                    action.RemainingFrames -= frames;
                    if (action.RemainingFrames <= 0) _entityActions.Remove(id);
                    else _entityActions[id] = action;
                }
            }
            if (_turns.Count > 0)
            {
                foreach (uint id in new List<uint>(_turns.Keys))
                {
                    float left = _turns[id] - frames;
                    if (left <= 0) _turns.Remove(id);
                    else _turns[id] = left;
                }
            }
        }

        /// <summary>An entity starts turning toward a new event heading; it arrives after <paramref name="frames"/> (60 Hz).</summary>
        internal void StartTurn(uint serverId, float frames)
        {
            if (frames > 0) _turns[serverId] = frames;
            else _turns.Remove(serverId);
        }

        /// <summary>Whether an entity is still turning (0x76 / 0x70 wait on it).</summary>
        internal bool IsTurning(uint serverId) => _turns.ContainsKey(serverId);

        private int FindTask(int fileId, uint tag, uint caster, uint target) =>
            _tasks.FindIndex(t => t.FileId == fileId && t.Tag == tag && t.Caster == caster && t.Target == target);

        /// <summary>A new task id, for the host to play a task under before <see cref="AddTask"/> registers it.</summary>
        internal int NewTaskId() => ++_nextTaskId;

        /// <summary>
        /// Registers a started task that lasts <paramref name="frames"/> (none: it is over at once); the same task
        /// started again replaces the old one, whose id is returned (-1 when there was none).
        /// </summary>
        internal int AddTask(int id, int fileId, uint tag, uint caster, uint target, float frames)
        {
            int existing = FindTask(fileId, tag, caster, target);
            int replaced = existing >= 0 ? _tasks[existing].Id : -1;
            if (existing >= 0) _tasks.RemoveAt(existing);
            if (frames > 0) _tasks.Add(new SchedulerTask { Id = id, FileId = fileId, Tag = tag, Caster = caster, Target = target, RemainingFrames = frames });
            return replaced;
        }

        /// <summary>Removes a task (0x52); the id it ran under, or -1 when it was not running.</summary>
        internal int RemoveTask(int fileId, uint tag, uint caster, uint target)
        {
            int index = FindTask(fileId, tag, caster, target);
            if (index < 0) return -1;
            int id = _tasks[index].Id;
            _tasks.RemoveAt(index);
            return id;
        }

        /// <summary>Whether a task is still running (0x55 waits on it).</summary>
        internal bool IsTaskRunning(int fileId, uint tag, uint caster, uint target) => FindTask(fileId, tag, caster, target) >= 0;

        /// <summary>An entity's event action started (it replaces the one it was playing).</summary>
        internal void SetEntityAction(uint serverId, uint tag, float frames)
        {
            if (frames > 0) _entityActions[serverId] = new EntityAction { Tag = tag, RemainingFrames = frames };
            else _entityActions.Remove(serverId);
        }

        /// <summary>Ends an entity's event action if it is <paramref name="tag"/> (0x50).</summary>
        internal void EndEntityAction(uint serverId, uint tag)
        {
            if (_entityActions.TryGetValue(serverId, out var action) && action.Tag == tag) _entityActions.Remove(serverId);
        }

        /// <summary>Ends whatever event action an entity plays (0x5E / 0x6B return it to idle).</summary>
        internal void EndEntityActions(uint serverId) => _entityActions.Remove(serverId);

        /// <summary>Whether an entity still plays the event action <paramref name="tag"/> (0x53 waits on it).</summary>
        internal bool IsEntityActionPlaying(uint serverId, uint tag) =>
            _entityActions.TryGetValue(serverId, out var action) && action.Tag == tag;

        /// <summary>Whether an entity plays any event action (retail <c>AnimationPlay</c>: 0x6E waits for it, 0x99 yields on it).</summary>
        internal bool IsEntityActing(uint serverId) => _entityActions.ContainsKey(serverId);

        #endregion

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
