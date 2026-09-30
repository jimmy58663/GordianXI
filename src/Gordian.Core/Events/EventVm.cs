// src/Gordian.Core/Events/EventVm.cs
using System;
using System.Buffers.Binary;
using Gordian.Core.Resources.Events;

namespace Gordian.Core.Events
{
    /// <summary>
    /// The talk subset of the retail event VM: runs one event of an <see cref="EventBlock"/> far enough to print
    /// its dialog, show its choice menus and answer the server (Tier 2 chunk 6). Control flow, the work-value
    /// arithmetic and bit opcodes, message and query opcodes, waits, the event update / end handshake and the
    /// control lock are interpreted; camera, animation, scheduler, map and other cutscene opcodes are stepped over
    /// by their documented length (<see cref="EventOpcodeTable"/>), so a cutscene plays as its text alone.
    /// <para>
    /// Opcode semantics referenced from XiEvents (https://github.com/atom0s/XiEvents, "Event VM Functions.md",
    /// "Event VM Structures.md" and "OpCodes/"): operands are 16-bit work references resolved by
    /// <c>getworkofs</c> (bit 15: the block's immediate data; 0-79: the event's locals; 4096+: the zone's shared
    /// work values; 0x7F00+: entity facts), jumps keep an 8-deep return stack, waits count 60 Hz frames, a message
    /// opcode holds the following 0x23 while its text is shown (a prompt message stays open for a time that grows with
    /// its length, or until the player confirms; retail auto-advances NPC talk this way), 0x24/0x25 open a query and store the
    /// choice in the zone's work value 0, 0x43 sends the update (0x05B mode 1) with work value 1 and waits for the
    /// server, 0x47 sends the position update (0x05C mode 1, a same-zone warp) the same way, 0xD4 opens a query with the
    /// zone map behind it (the map itself is not drawn yet), 0x9D reads the scripts' tables, and 0x00 / 0x21 end the event, after which the client sends 0x05B mode 0 with work value 1
    /// (0x40000000 when the player cancelled a query).
    /// </para>
    /// <para>
    /// Each entity taking part in an event has its own VM, and all of an event's VMs share one <see cref="EventScene"/>
    /// (#85). A VM holds 16 request stacks (XiEvents "Event VM Structures.md", <c>reqstack_t</c>): each is a program
    /// position with a priority (lower runs first, 255 = free), its own wait timer and the slot (<c>TagNum</c>) of
    /// the event it runs. The event's own stack starts at priority 16; every frame the VM runs its most urgent stack
    /// until it yields (XiEvent::EventIdle). 0x00 (and 0x1B with nothing to return to) frees the running stack; 0x21
    /// ends the whole event. The companion opcodes 0x27-0x29 queue a request on another entity's VM (XiEvent::ReqSet):
    /// that entity runs the event at <em>slot</em> n of its own offset table, not event id n (xi-tools
    /// <c>docs/events/retail-events.md</c>, "The one rule"); 0x28 then waits until the entity has started it, 0x29
    /// until it has finished it, and 0x2A waits until the entity has nothing queued at or above a priority.
    /// </para>
    /// </summary>
    public sealed class EventVm
    {
        /// <summary>The 0x05B end parameter of an event the player cancelled.</summary>
        public const uint CancelledEndParameter = 0x40000000;

        /// <summary>The priority of the event's own request stack at its start (XiEvent::XiEventInit).</summary>
        public const int StartPriority = 16;

        /// <summary>The priority of a free request stack.</summary>
        public const int FreePriority = 255;

        /// <summary>The number of request stacks of a VM.</summary>
        public const int RequestStackCount = 16;

        /// <summary>
        /// Runaway guard only: the home point script walks its zone tables in nested loops of tens of thousands of
        /// opcodes within one tick (retail runs them without a limit).
        /// </summary>
        private const int MaxStepsPerTick = 2_000_000;

        /// <summary>One request stack (XiEvents <c>reqstack_t</c>).</summary>
        private struct RequestStack
        {
            public int Priority;
            public int Pc;
            public float WaitTime;
            public int Slot;
            public byte RequestFlag;
            public uint Who;
        }

        private readonly byte[] _code;
        private readonly int[] _references;
        private readonly IReadOnlyList<ushort> _offsets;
        private readonly int[] _local = new int[80];
        private readonly int[] _jumpStack = new int[8];
        private readonly RequestStack[] _stacks = new RequestStack[RequestStackCount];
        private readonly EventWorkZone _zone;
        private readonly IEventVmHost _host;
        private int _jumpDepth;
        private int _runPos;
        private int _pc;
        private bool _retFlag;
        private float _frameDelay;
        private bool _queryOpen;
        private float _eventX, _eventY, _eventZ, _eventDir;

        /// <summary>A VM that is its event's only entity (its own <see cref="EventScene"/> on the given work zone).</summary>
        public EventVm(EventBlock block, ushort eventId, EventWorkZone zone, IEventVmHost host, uint entityServerId, ushort entityIndex)
            : this(block, eventId, new EventScene(zone), host, entityServerId, entityIndex)
        {
        }

        /// <summary>The VM of one entity of an event; it joins <paramref name="scene"/>.</summary>
        public EventVm(EventBlock block, ushort eventId, EventScene scene, IEventVmHost host, uint entityServerId, ushort entityIndex)
        {
            ArgumentNullException.ThrowIfNull(block);
            Scene = scene ?? throw new ArgumentNullException(nameof(scene));
            _zone = scene.Zone;
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _code = block.Code;
            _offsets = block.Offsets;
            _references = new int[block.References.Count];
            for (int i = 0; i < _references.Length; i++) _references[i] = unchecked((int)block.References[i]);
            EntityServerId = entityServerId;
            EntityIndex = entityIndex;
            EventId = eventId;
            for (int i = 0; i < _stacks.Length; i++) FreeStack(i);
            // The event id, else the block's catch-all (XiEvent::XiEventInit). Only the start is used: an event's code
            // may jump or call past the next event's offset (actor blocks share code), so execution is bounded by the
            // block's code and the end opcodes, not by that offset.
            int slot = block.IndexOf(eventId);
            CarriesEvent = slot >= 0;
            if (slot < 0) slot = block.IndexOf(EventBlock.AnyEventId);
            if (slot >= 0)
            {
                _stacks[0] = new RequestStack { Priority = StartPriority, Pc = _offsets[slot], WaitTime = -1f, Slot = slot, Who = entityServerId };
                _pc = _offsets[slot];
            }
            scene.Add(this);
        }

        /// <summary>The event this VM takes part in.</summary>
        public EventScene Scene { get; }

        public ushort EventId { get; }

        /// <summary>The entity the event belongs to (the NPC talked to, or the player for zone events).</summary>
        public uint EntityServerId { get; }
        public ushort EntityIndex { get; }

        /// <summary>Whether the block carries the event id itself, not only the catch-all.</summary>
        public bool CarriesEvent { get; }

        /// <summary>Whether any request stack of this VM still has work.</summary>
        public bool HasRequests
        {
            get
            {
                foreach (var stack in _stacks)
                {
                    if (stack.Priority != FreePriority) return true;
                }
                return false;
            }
        }

        /// <summary>Whether this VM has nothing left to run, or the event ended; the client then sends 0x05B mode 0.</summary>
        public bool IsFinished => Scene.IsEnded || !HasRequests;

        /// <summary>Whether the player cancelled a query, which ends the event with <see cref="CancelledEndParameter"/>.</summary>
        public bool IsCancelled => Scene.IsCancelled;

        /// <summary>Whether a printed message is still open (the player's confirm closes it early).</summary>
        public bool IsWaitingForConfirm => Scene.IsWaitingForConfirm;

        /// <summary>The value the end packet reports.</summary>
        public uint EndParameter => Scene.EndParameter;

        /// <summary>Current byte-code position, for diagnostics.</summary>
        public int ProgramCounter => _pc;

        /// <summary>Called before every opcode with its position and code (tests and tracing); null when unused.</summary>
        public Action<int, byte>? Trace { get; set; }

        /// <summary>The event's local work values (diagnostics).</summary>
        public IReadOnlyList<int> Locals => _local;

        /// <summary>The player confirmed the open message: the event goes on at its next tick.</summary>
        public void Confirm() => Scene.Confirm();

        /// <summary>Stops the event where it is (a server cancel, or a zone change).</summary>
        public void Abort(bool cancelled) => Scene.End(cancelled);

        /// <summary>
        /// Runs one frame of this VM's event (its <see cref="Scene"/>, which ticks every entity of the event): until
        /// each yields (a wait, a prompt, a query, a server round trip) or ends.
        /// </summary>
        public void Tick(TimeSpan elapsed) => Scene.Tick(elapsed);

        /// <summary>
        /// Runs this VM's most urgent request stack until it yields (XiEvent::EventIdle); called by the scene once a
        /// frame with the frames since the last one.
        /// </summary>
        internal void Run(float frameDelay)
        {
            if (Scene.IsEnded) return;
            int priority = FreePriority;
            for (int i = 0; i < _stacks.Length; i++)
            {
                if (_stacks[i].Priority <= priority)
                {
                    priority = _stacks[i].Priority;
                    _runPos = i;
                }
            }
            if (priority == FreePriority) return;
            _frameDelay = frameDelay;
            _pc = _stacks[_runPos].Pc;
            _retFlag = false;
            int steps = 0;
            while (!_retFlag)
            {
                if (++steps > MaxStepsPerTick)
                {
                    _host.OnSkippedOpcode(0xFF, _pc);
                    EndRequest();
                    break;
                }
                if (_pc < 0 || _pc >= _code.Length)
                {
                    EndRequest();
                    break;
                }
                Step();
            }
            if (_stacks[_runPos].Priority != FreePriority) _stacks[_runPos].Pc = _pc;
        }

        /// <summary>Frees the running request stack (opcode 0x00; 0x1B with an empty return stack).</summary>
        private void EndRequest()
        {
            FreeStack(_runPos);
            _retFlag = true;
            if (!HasRequests) CloseQuery();
        }

        private void FreeStack(int index) =>
            _stacks[index] = new RequestStack { Priority = FreePriority, Pc = 0, WaitTime = -1f, Slot = 0, Who = 0 };

        /// <summary>Ends the whole event (0x21, a cancelled query).</summary>
        private void EndEvent(bool cancelled)
        {
            Scene.End(cancelled);
            _retFlag = true;
        }

        /// <summary>Closes this VM's query if one is open (the event or its last request ended).</summary>
        internal void CloseQuery()
        {
            if (!_queryOpen) return;
            _queryOpen = false;
            _host.CloseQuery();
        }

        #region Requests

        /// <summary>
        /// Queues the event at <paramref name="slot"/> of this VM's offset table at <paramref name="priority"/>
        /// (XiEvent::ReqSet): 0 when that slot is already queued or running, 1 when queued, 2 when no stack is free,
        /// -1 for a slot the block does not have.
        /// </summary>
        private int RequestSet(int slot, int priority, uint who)
        {
            if (slot < 0 || slot >= _offsets.Count) return -1;
            int free = -1;
            for (int i = 0; i < _stacks.Length; i++)
            {
                if (_stacks[i].Priority == FreePriority) free = i;
                else if (_stacks[i].Slot == slot) return 0;
            }
            if (free < 0) return 2;
            _stacks[free] = new RequestStack { Priority = priority, Pc = _offsets[slot], WaitTime = -1f, Slot = slot, Who = who };
            return 1;
        }

        /// <summary>
        /// Where the event at <paramref name="slot"/> stands on this VM (XiEvent::GetReqStatus): 0 while it is the
        /// running stack, 1 while it waits in another stack, -1 once no stack holds it.
        /// </summary>
        private int RequestStatus(int slot)
        {
            if (_stacks[_runPos].Priority != FreePriority && _stacks[_runPos].Slot == slot) return 0;
            foreach (var stack in _stacks)
            {
                if (stack.Priority != FreePriority && stack.Slot == slot) return 1;
            }
            return -1;
        }

        /// <summary>Whether every stack of this VM is less urgent than <paramref name="priority"/> (XiEvent::GetReqLevel).</summary>
        private bool IsFreeAt(int priority)
        {
            foreach (var stack in _stacks)
            {
                if (stack.Priority <= priority) return false;
            }
            return true;
        }

        /// <summary>The VM of the entity an actor operand names, when it takes part in the event.</summary>
        private EventVm? RequestTarget(int lookup)
        {
            var (serverId, _) = ResolveActor(lookup);
            return Scene.FindActor(serverId);
        }

        /// <summary>
        /// 0x27 (CodeREQ), 0x28 (CodeREQSW) and 0x29 (CodeREQEW): <c>op priority actor:u32 slot</c>. Queue the event at
        /// <c>slot</c> on the actor; 0x27 goes on at once (retrying while the actor has no free stack), 0x28 waits
        /// until the actor has started it and 0x29 until the actor has finished it. The waits are kept in the running
        /// stack's request flag (1 = queued, 2 = started). Referenced from XiEvents OpCodes 0x0027-0x0029; the
        /// decompiled status tests read inverted against the opcodes' names (start wait / end wait) and would never
        /// release an end wait, so the waits here follow the names.
        /// </summary>
        private void ExecRequest(byte op)
        {
            int priority = Code8(1);
            int slot = Code8(6);
            var target = RequestTarget(Code32(2));
            ref var stack = ref _stacks[_runPos];
            if (op == 0x27 || stack.RequestFlag == 0)
            {
                int result = target?.RequestSet(slot, priority, EntityServerId) ?? -1;
                if (result == 2)
                {
                    _retFlag = true; // no free stack yet: ask again next frame
                    return;
                }
                if (op != 0x27 && result == 1)
                {
                    stack.RequestFlag = 1;
                    _retFlag = true;
                    return;
                }
                stack.RequestFlag = 0;
                _pc += 7;
                return;
            }
            int status = target?.RequestStatus(slot) ?? -1;
            if (stack.RequestFlag == 1)
            {
                if (status == 1)
                {
                    _retFlag = true; // still queued behind the actor's more urgent work
                    return;
                }
                if (op == 0x29 && status == 0)
                {
                    stack.RequestFlag = 2;
                    _retFlag = true;
                    return;
                }
            }
            else if (status != -1)
            {
                _retFlag = true; // 0x29: the actor is still running it
                return;
            }
            stack.RequestFlag = 0;
            _pc += 7;
        }

        /// <summary>
        /// 0x2A: <c>op priority actor:u32</c>, waits until the actor has no request at or above the priority
        /// (XiEvents OpCodes/0x002A, XiEvent::GetReqLevel); an actor outside the event does not hold it.
        /// </summary>
        private void ExecRequestLevelWait()
        {
            var target = RequestTarget(Code32(2));
            if (target != null && !target.IsFreeAt(Code8(1)))
            {
                _retFlag = true;
                return;
            }
            _pc += 6;
        }

        #endregion

        private ushort Code16(int offset) =>
            _pc + offset + 1 < _code.Length ? BinaryPrimitives.ReadUInt16LittleEndian(_code.AsSpan(_pc + offset, 2)) : (ushort)0;

        private int Code32(int offset) =>
            _pc + offset + 3 < _code.Length ? BinaryPrimitives.ReadInt32LittleEndian(_code.AsSpan(_pc + offset, 4)) : 0;

        private byte Code8(int offset) => _pc + offset < _code.Length ? _code[_pc + offset] : (byte)0;

        /// <summary>Resolves a work reference read at <paramref name="offset"/> (XiEvents' <c>getworkofs</c>).</summary>
        private int GetWork(int offset, int shift = 0) => ResolveKey(Code16(offset) + shift, _references);

        /// <summary>A 16-bit work reference read at an absolute position of a block's code (a table entry).</summary>
        private static int KeyAt(byte[] code, int position) =>
            position >= 0 && position + 1 < code.Length ? BinaryPrimitives.ReadUInt16LittleEndian(code.AsSpan(position, 2)) : 0;

        /// <summary>The value a work reference key names, with the given immediate data for bit-15 keys.</summary>
        private int ResolveKey(int key, int[] references)
        {
            if ((key & 0x8000) != 0)
            {
                int index = key & 0x7FFF;
                return index < references.Length ? references[index] : 0;
            }
            if (key < 2048) return key < _local.Length ? _local[key] : 0;
            if (key < 4352) return key - 4096 < _zone.Zone.Length ? _zone.Zone[key - 4096] : 0;
            if (key < 4608) return key - 4352 < _zone.Memorize.Length ? _zone.Memorize[key - 4352] : 0;
            if (key < 6144) return key - 5888 >= 0 && key - 5888 < _zone.Zone1700.Length ? _zone.Zone1700[key - 5888] : 0;
            if (key < 0x7F80)
            {
                return key switch
                {
                    0x7F00 => (int)(_eventX * 1000f),
                    0x7F01 => (int)(_eventY * 1000f),
                    0x7F02 => (int)(_eventZ * 1000f),
                    0x7F03 => (int)(_eventDir * 4096f / (2f * MathF.PI)),
                    >= 0x7F06 and <= 0x7F0B => _host.GetEntityValue(EntityServerId, key - 0x7F00),
                    _ => 0,
                };
            }
            if (key >= 0x7FFF) return 0;
            return _host.GetEntityValue(0, key - 0x7F80);
        }

        /// <summary>Writes a work reference read at <paramref name="offset"/> (XiEvents' <c>setworkofs</c>).</summary>
        private void SetWork(int offset, int value, int shift = 0) => StoreKey(Code16(offset) + shift, value);

        private void StoreKey(int key, int value)
        {
            if ((key & 0x8000) != 0) return;
            if (key < 2048)
            {
                if (key < _local.Length) _local[key] = value;
            }
            else if (key < 4352)
            {
                if (key - 4096 < _zone.Zone.Length) _zone.Zone[key - 4096] = value;
            }
            else if (key < 4608)
            {
                if (key - 4352 < _zone.Memorize.Length) _zone.Memorize[key - 4352] = value;
            }
            else if (key < 6144)
            {
                if (key - 5888 >= 0 && key - 5888 < _zone.Zone1700.Length) _zone.Zone1700[key - 5888] = value;
            }
            else if (key >= 0x7F00 && key <= 0x7F03)
            {
                switch (key)
                {
                    case 0x7F00: _eventX = value * 0.001f; break;
                    case 0x7F01: _eventY = value * 0.001f; break;
                    case 0x7F02: _eventZ = value * 0.001f; break;
                    default: _eventDir = value * 6.283f * 0.00024414062f; break;
                }
            }
        }

        /// <summary>
        /// The entity an actor operand names (XiEvents' <c>GetActorIndex</c>): the player's hard-coded numbers, the
        /// party and alliance members by slot, the event's own entity (0x7FFFFFF8 and any value without a zone prefix),
        /// or an NPC server id.
        /// </summary>
        private (uint ServerId, ushort Index) ResolveActor(int lookup)
        {
            uint value = unchecked((uint)lookup);
            switch (value)
            {
                case 0x7FFFFFC0:
                case 0x7FFFFFF0:
                case 0x7FFFFFF9:
                    return (0, 0); // the local player (server id 0 = "the player" for the host)
                case 0x7FFFFFF8:
                    return (EntityServerId, EntityIndex);
            }
            if (TryGetPartySlot(value, out int party, out int slot))
            {
                return _host.TryGetPartyMember(party, slot, out uint serverId, out ushort index) ? (serverId, index) : (uint.MaxValue, (ushort)0);
            }
            if ((value & 0xFF000000) != 0) return (value, (ushort)(value & 0x3FF));
            return (EntityServerId, EntityIndex);
        }

        /// <summary>
        /// The party slot an actor code names (XiEvents "Event VM Functions.md", GetActorNum / GetActorIndex):
        /// 0x7FFFFFC1-C5 and 0x7FFFFFF1-F5 are the other members of the player's party (slots 1-5), 0x7FFFFFC6-CB
        /// and 0x7FFFFFCC-D1 the members of alliance parties 1 and 2 (slots 0-5).
        /// </summary>
        public static bool TryGetPartySlot(uint code, out int party, out int slot)
        {
            (party, slot) = code switch
            {
                >= 0x7FFFFFC1 and <= 0x7FFFFFC5 => (0, (int)(code - 0x7FFFFFC0)),
                >= 0x7FFFFFC6 and <= 0x7FFFFFCB => (1, (int)(code - 0x7FFFFFC6)),
                >= 0x7FFFFFCC and <= 0x7FFFFFD1 => (2, (int)(code - 0x7FFFFFCC)),
                >= 0x7FFFFFF1 and <= 0x7FFFFFF5 => (0, (int)(code - 0x7FFFFFF0)),
                _ => (-1, -1),
            };
            return party >= 0;
        }

        private static int BitMask(int from, int to)
        {
            int mask = 0;
            for (int x = 0; x < 32; x++)
            {
                mask = (int)((uint)mask >> 1);
                if (from <= x && to >= x) mask |= unchecked((int)0x80000000);
            }
            return mask;
        }

        private void Step()
        {
            byte op = _code[_pc];
            Trace?.Invoke(_pc, op);
            switch (op)
            {
                case 0x00:
                    EndRequest();
                    return;
                case 0x01:
                    _pc = Code16(1);
                    return;
                case 0x02:
                    ExecIf();
                    return;
                case 0x03: SetWork(1, GetWork(3)); _pc += 5; return;
                case 0x04: _pc += 3; return;
                case 0x05: SetWork(1, 1); _pc += 3; return;
                case 0x06: SetWork(1, 0); _pc += 3; return;
                case 0x07: SetWork(1, GetWork(1) + GetWork(3)); _pc += 5; return;
                case 0x08: SetWork(1, GetWork(1) - GetWork(3)); _pc += 5; return;
                case 0x09: SetWork(1, GetWork(1) | (1 << (GetWork(3) & 31))); _pc += 5; return;
                case 0x0A: SetWork(1, GetWork(1) & ~(1 << (GetWork(3) & 31))); _pc += 5; return;
                case 0x0B: SetWork(1, GetWork(1) + 1); _pc += 3; return;
                case 0x0C: SetWork(1, GetWork(1) - 1); _pc += 3; return;
                case 0x0D: SetWork(1, GetWork(1) & GetWork(3)); _pc += 5; return;
                case 0x0E: SetWork(1, GetWork(1) | GetWork(3)); _pc += 5; return;
                case 0x0F: SetWork(1, GetWork(1) ^ GetWork(3)); _pc += 5; return;
                case 0x10: SetWork(1, GetWork(1) << (GetWork(3) & 31)); _pc += 5; return;
                case 0x11: SetWork(1, GetWork(1) >> (GetWork(3) & 31)); _pc += 5; return;
                case 0x12: SetWork(1, Random.Shared.Next()); _pc += 3; return;
                case 0x13:
                {
                    int bound = GetWork(3) + 1;
                    SetWork(1, bound > 0 ? Random.Shared.Next(bound) : bound);
                    _pc += 5;
                    return;
                }
                case 0x14: SetWork(1, GetWork(1) * GetWork(3)); _pc += 5; return;
                case 0x15:
                {
                    int a = GetWork(1), b = GetWork(3);
                    SetWork(1, a != 0 && b != 0 ? a / b : 0);
                    _pc += 5;
                    return;
                }
                case 0x16:
                case 0x17:
                case 0x18:
                    SetWork(1, 0); // trigonometry of cutscene motion; not needed for dialog
                    _pc += 7;
                    return;
                case 0x19:
                {
                    int a = GetWork(1), b = GetWork(3);
                    SetWork(1, b);
                    SetWork(3, a);
                    _pc += 5;
                    return;
                }
                case 0x1A:
                    if (_jumpDepth >= _jumpStack.Length)
                    {
                        _retFlag = true;
                        return;
                    }
                    _jumpStack[_jumpDepth++] = _pc + 3;
                    _pc = Code16(1);
                    return;
                case 0x1B:
                    if (_jumpDepth > 0) _pc = _jumpStack[--_jumpDepth];
                    else EndRequest();
                    return;
                case 0x1C:
                {
                    ref float wait = ref _stacks[_runPos].WaitTime;
                    if (wait < 0f) wait = GetWork(1);
                    wait -= _frameDelay;
                    _retFlag = true;
                    if (wait < 0f) _pc += 3;
                    return;
                }
                case 0x1D:
                    PrintMessage(GetWork(1), EventSpeaker.Entity, EntityServerId, EntityIndex);
                    _pc += 3;
                    return;
                case 0x20:
                    _host.SetControlLock(Code8(1) != 0);
                    _pc += 2;
                    return;
                case 0x21:
                    EndEvent(false);
                    return;
                case 0x23:
                    if (Scene.IsWaitingForConfirm) _retFlag = true;
                    else _pc++;
                    return;
                case 0x24:
                    OpenQuery(1);
                    _pc += 7;
                    return;
                case 0xD4:
                    // The map opcode: sub-cases 0 (map behind the list) and 2 open a query like 0x24 with the
                    // operands one byte on; 1, 3, 4 and 5 hand marker data to the map window (no map here yet).
                    switch (Code8(1))
                    {
                        case 0:
                        case 2:
                            OpenQuery(2);
                            _pc += 8;
                            return;
                        case 1:
                            _host.OnSkippedOpcode(op, _pc);
                            _pc += 8;
                            return;
                        case 3:
                            _host.OnSkippedOpcode(op, _pc);
                            _pc += 6;
                            return;
                        case 4:
                        case 5:
                            _host.OnSkippedOpcode(op, _pc);
                            _pc += 12;
                            return;
                        default:
                            _host.OnSkippedOpcode(op, _pc);
                            EndRequest();
                            return;
                    }
                case 0x25:
                case 0x7F:
                    ExecQueryWait(op == 0x25);
                    return;
                case 0x26:
                    EndRequest(); // yields forever in retail; nothing more of the dialog would show
                    return;
                case 0x27:
                case 0x28:
                case 0x29:
                    ExecRequest(op);
                    return;
                case 0x2A:
                    ExecRequestLevelWait();
                    return;
                case 0x2B:
                {
                    var (id, index) = ResolveActor(Code32(1));
                    PrintMessage(GetWork(5), EventSpeaker.Entity, id, index);
                    _pc += 7;
                    return;
                }
                case 0x3C:
                case 0x3D:
                {
                    int bit = GetWork(3);
                    if ((bit >> 5) < GetWork(5))
                    {
                        int word = GetWork(1, bit >> 5);
                        int flag = 1 << (bit & 0x1F);
                        SetWork(1, op == 0x3C ? word | flag : word & ~flag, bit >> 5);
                    }
                    _pc += 7;
                    return;
                }
                case 0x3E:
                {
                    int bit = GetWork(3);
                    if ((GetWork(1, bit >> 5) & (1 << (bit & 0x1F))) != 0) _pc += 7;
                    else _pc = Code16(5);
                    return;
                }
                case 0x3F:
                {
                    int a = GetWork(3), b = GetWork(5);
                    SetWork(1, a != 0 && b != 0 ? a % b : 0);
                    _pc += 7;
                    return;
                }
                case 0x40:
                {
                    int from = GetWork(1), to = GetWork(3);
                    int mask = BitMask(from, to);
                    int cleared = ~mask & GetWork(5);
                    int value = GetWork(7);
                    SetWork(5, cleared | (mask & (value << (from & 31))));
                    _pc += 9;
                    return;
                }
                case 0x41:
                {
                    int from = GetWork(1), to = GetWork(3);
                    int mask = BitMask(from, to);
                    SetWork(7, (int)((uint)(mask & GetWork(5)) >> (from & 31)));
                    _pc += 9;
                    return;
                }
                case 0x43:
                    if (Code8(1) == 1)
                    {
                        // Wait for the server's answer to the update (0x052 mode 1).
                        if (!_host.ReceivePending) _pc += 2;
                        _retFlag = true;
                        return;
                    }
                    if (Code8(1) != 0)
                    {
                        // Sub-cases 0x80/0x81 appear in the home point scripts; XiEvents' notes do not describe them.
                        _host.OnSkippedOpcode(op, _pc);
                        _pc += 2;
                        return;
                    }
                    _host.SendEventUpdate(unchecked((uint)_zone.EndParameter));
                    _pc += 2;
                    return;
                case 0x47:
                    if (Code8(1) == 1)
                    {
                        if (!_host.ReceivePending) _pc += 2;
                        _retFlag = true;
                        return;
                    }
                    if (Code8(1) != 0)
                    {
                        _host.OnSkippedOpcode(op, _pc);
                        _pc += 2;
                        return;
                    }
                    // The operands are x, z, y (the scripts' position order, as 0x36) and the heading.
                    _host.SendEventUpdateXzy(unchecked((uint)_zone.EndParameter), GetWork(2) * 0.001f, GetWork(6) * 0.001f, GetWork(4) * 0.001f,
                        GetWork(8) * 6.283f * 0.00024414062f);
                    _pc += 10;
                    return;
                case 0x36:
                    _eventX = GetWork(1) * 0.001f;
                    _eventZ = GetWork(3) * 0.001f;
                    _eventY = GetWork(5) * 0.001f;
                    _pc += 7;
                    return;
                case 0x37:
                    _eventX = GetWork(1) * 0.001f;
                    _eventZ = GetWork(3) * 0.001f;
                    _eventY = GetWork(5) * 0.001f;
                    _eventDir = GetWork(7) * 6.283f * 0.00024414062f;
                    _pc += 9;
                    return;
                case 0x44:
                    if (_host.EntityExists(unchecked((uint)GetWork(1)))) _pc += 5;
                    else _pc = Code16(3);
                    return;
                case 0x46:
                    if (Code8(1) == 2)
                    {
                        SetWork(2, 1);
                        _pc += 4;
                    }
                    else
                    {
                        _pc += 2;
                    }
                    return;
                case 0x48:
                    PrintMessage(GetWork(1), EventSpeaker.None, 0, 0);
                    _pc += 3;
                    return;
                case 0x49:
                {
                    var (id, index) = ResolveActor(Code32(1));
                    PrintMessage(GetWork(5), EventSpeaker.None, id, index);
                    _pc += 7;
                    return;
                }
                case 0x57:
                    SetWork(1, GetWork(1) + (int)MathF.Round(_frameDelay));
                    _pc += 3;
                    return;
                case 0x58:
                    _pc++;
                    _retFlag = true;
                    return;
                case 0x6F:
                {
                    ref float wait = ref _stacks[_runPos].WaitTime;
                    if (wait < 0f) wait = 16f; // a wait already running (0x1C) keeps its time
                    wait -= _frameDelay;
                    _retFlag = true;
                    if (wait < 0f) _pc++;
                    return;
                }
                case 0x82:
                    _pc = Code16(5); // the position rectangle test: not inside (no cutscene staging here)
                    return;
                case 0x83:
                    SetWork(1, _host.GameTime);
                    _pc += 3;
                    return;
                case 0x9C:
                    SetWork(1, 2); // English client
                    _pc += 3;
                    return;
                case 0xB0:
                {
                    var (id, index) = ResolveActor(Code32(2));
                    PrintMessage(GetWork(10), EventSpeaker.Entity, id, index);
                    _pc += 12;
                    return;
                }
                case 0x9D:
                    ExecTable();
                    return;
                case 0xBE:
                    SetWork(1, unchecked((int)_stacks[_runPos].Who)); // who asked for the running request
                    _pc += 3;
                    return;
                default:
                {
                    int length = EventOpcodeTable.GetLength(_code, _pc);
                    _host.OnSkippedOpcode(op, _pc);
                    if (length <= 0)
                    {
                        EndRequest();
                        return;
                    }
                    _pc += length;
                    return;
                }
            }
        }

        /// <summary>
        /// Opcode 0x9D: the scripts' tables. A table is a run of 16-bit work references inside the byte code (mostly
        /// immediate-data keys); the sub-cases read an entry into a work value (0x00, 0x0A with a bound), write a
        /// work value through an entry (0x05, 0x0F), share a table through one of 64 zone-wide pointer slots (0x02,
        /// 0x0C) and read through a slot (0x03, 0x0D), or jump through a table (0x07). The string cases (0x01, 0x04,
        /// 0x06, 0x08, 0x09, 0x0B, 0x0E, 0x10) are stepped over. Semantics referenced from XiEvents (OpCodes/0x009D.md).
        /// </summary>
        private void ExecTable()
        {
            byte sub = Code8(1);
            switch (sub)
            {
                case 0x00:
                case 0x0A:
                {
                    int table = Code16(2);
                    int index = GetWork(6);
                    if (sub == 0x0A)
                    {
                        int bound = GetWork(8);
                        if (bound != 0 && bound <= index) index = 0;
                    }
                    SetWork(4, ResolveKey(KeyAt(_code, table + 2 * index), _references));
                    _pc += sub == 0x00 ? 8 : 10;
                    return;
                }
                case 0x05:
                case 0x0F:
                {
                    int table = Code16(2);
                    int index = GetWork(6);
                    if (sub == 0x0F)
                    {
                        int bound = GetWork(8);
                        if (bound != 0 && bound <= index) index = 0;
                    }
                    StoreKey(KeyAt(_code, table + 2 * index), GetWork(4));
                    _pc += sub == 0x05 ? 8 : 10;
                    return;
                }
                case 0x02:
                case 0x0C:
                {
                    int slot = GetWork(4);
                    if (sub == 0x0C)
                    {
                        int bound = GetWork(6);
                        if (bound != 0 && bound <= slot) slot = 0;
                    }
                    if (slot >= 0 && slot < _zone.Tables.Length) _zone.Tables[slot] = (_code, _references, Code16(2));
                    _pc += sub == 0x02 ? 6 : 8;
                    return;
                }
                case 0x03:
                case 0x0D:
                {
                    int slot = GetWork(2);
                    int index = GetWork(6);
                    if (sub == 0x0D)
                    {
                        int bound = GetWork(8);
                        if (bound != 0 && bound <= index) index = 0;
                    }
                    if (slot >= 0 && slot < _zone.Tables.Length && _zone.Tables[slot] is { } shared)
                    {
                        SetWork(4, ResolveKey(KeyAt(shared.Code, shared.Offset + 2 * index), shared.References));
                    }
                    _pc += sub == 0x03 ? 8 : 10;
                    return;
                }
                case 0x07:
                {
                    int table = Code16(2);
                    int index = GetWork(4);
                    int target = KeyAt(_code, table + 2 * index);
                    if (_jumpDepth >= _jumpStack.Length)
                    {
                        _retFlag = true;
                        return;
                    }
                    _jumpStack[_jumpDepth++] = _pc + 6;
                    _pc = target;
                    return;
                }
                case 0x01:
                case 0x04:
                case 0x06:
                    _host.OnSkippedOpcode(0x9D, _pc);
                    _pc += 8;
                    return;
                case 0x08:
                    _host.OnSkippedOpcode(0x9D, _pc);
                    _pc += 23;
                    return;
                case 0x09:
                    _host.OnSkippedOpcode(0x9D, _pc);
                    _pc += 9;
                    return;
                case 0x0B:
                case 0x0E:
                case 0x10:
                    _host.OnSkippedOpcode(0x9D, _pc);
                    _pc += 10;
                    return;
                default:
                    _host.OnSkippedOpcode(0x9D, _pc);
                    EndRequest();
                    return;
            }
        }

        private void ExecIf()
        {
            int kind = Code8(5) & 0x0F;
            int a = GetWork(1), b = GetWork(3);
            bool jump = kind switch
            {
                0 => a != b,
                1 or 7 => a == b,
                2 => a <= b,
                3 => a >= b,
                4 => a < b,
                5 => a > b,
                6 or 9 => (a & b) == 0,
                8 => (a | b) == 0,
                10 => (~a & b) == 0,
                _ => true,
            };
            if (jump) _pc = Code16(6);
            else _pc += 8;
        }

        /// <summary>Opens the query whose message, default option and hidden mask operands start at <paramref name="offset"/>.</summary>
        private void OpenQuery(int offset)
        {
            if (_queryOpen) return;
            _queryOpen = true;
            _host.OpenQuery(GetWork(offset), GetWork(offset + 2), unchecked((uint)GetWork(offset + 4)));
        }

        private void ExecQueryWait(bool endOnCancel)
        {
            if (!_queryOpen)
            {
                _pc++;
                _retFlag = true;
                return;
            }
            int result = _host.QueryResult;
            if (result == 0)
            {
                _retFlag = true;
                return;
            }
            _pc++;
            _queryOpen = false;
            _host.CloseQuery();
            if (result == 255)
            {
                _zone.Selection = endOnCancel ? 254 : 255;
                if (endOnCancel)
                {
                    EndEvent(true);
                    return;
                }
            }
            else
            {
                _zone.Selection = result - 1;
            }
            _retFlag = true;
        }

        private void PrintMessage(int messageId, EventSpeaker speaker, uint speakerServerId, ushort speakerIndex)
        {
            Scene.OpenMessage(_host.PrintMessage(messageId, speaker, speakerServerId, speakerIndex));
        }
    }
}
