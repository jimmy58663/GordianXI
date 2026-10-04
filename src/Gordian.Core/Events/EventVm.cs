// src/Gordian.Core/Events/EventVm.cs
using System;
using System.Buffers.Binary;
using System.Numerics;
using Gordian.Core.Resources.Events;
using Gordian.Core.World;

namespace Gordian.Core.Events
{
    /// <summary>
    /// The talk subset of the retail event VM: runs one event of an <see cref="EventBlock"/> far enough to print
    /// its dialog, show its choice menus and answer the server (Tier 2 chunk 6). Control flow, the work-value
    /// arithmetic and bit opcodes, message and query opcodes, waits, the event update / end handshake and the
    /// control lock are interpreted, and so are the cutscene staging opcodes (#86) and the schedulers that carry the
    /// camera, fades and gestures (#165: 0x45 / 0x52 / 0x55, 0x2C / 0x5B / 0x66 / 0x50 / 0x53, 0x46); the other
    /// opcodes are stepped over by their documented length (<see cref="EventOpcodeTable"/>).
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
            /// <summary>The goal of the stack's walk (0x1F / 0x5A), internal axes.</summary>
            public Vector3 MovePosition;
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
        /// <summary>Walk speed of 0x1F / 0x5A in yalms per second (retail <c>MainSpeed</c>; opcode 0x32 sets it).</summary>
        private float _mainSpeed = DefaultWalkSpeed;
        /// <summary>The script placed or turned the entity since the last publish (<see cref="PublishPose"/>).</summary>
        private bool _poseDirty;
        /// <summary>The script has placed the entity at least once: from then on the event owns its pose.</summary>
        private bool _posed;
        private bool _walkedThisRun;
        /// <summary>Whether the event position is a real place (the entity's, or one the script set), not the origin.</summary>
        private bool _positionKnown;
        private float _publishedSpeed;

        /// <summary>The walk speed when neither the entity nor the script gives one (yalms per second).</summary>
        public const float DefaultWalkSpeed = 4.0f;

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
            // The event position starts where the entity stands (XiEvent::XiEventInit copies it from the entity).
            if (host.TryGetEntityPose(entityServerId, out var position, out float heading, out float speed))
            {
                (_eventX, _eventY, _eventZ, _eventDir) = (position.X, position.Y, position.Z, heading);
                _positionKnown = true;
                if (speed > 0) _mainSpeed = speed;
            }
            scene.Add(this);
        }

        /// <summary>The event position (internal axes, Y = height) and heading (wire-convention radians) of this entity.</summary>
        public (Vector3 Position, float Heading) EventPosition => (new Vector3(_eventX, _eventY, _eventZ), _eventDir);

        /// <summary>Whether the script has placed or turned the entity (the event then owns its pose until it ends).</summary>
        public bool IsPosed => _posed;

        /// <summary>Moves the event position (a staging opcode of this or another entity's script).</summary>
        private void SetEventPosition(float x, float y, float z)
        {
            (_eventX, _eventY, _eventZ) = (x, y, z);
            _positionKnown = true;
            _poseDirty = true;
        }

        /// <summary>
        /// A turn rate model: the drawn heading eases toward the event heading (exponential, 8 per second: the viewport's
        /// <c>EventPoseSmoother</c>), so a turn is over once the remaining angle is under <see cref="TurnDoneRadians"/>.
        /// Retail's own turn speed is not measured.
        /// </summary>
        private const float TurnEaseRate = 8f, TurnDoneRadians = 0.05f;

        /// <summary>
        /// The body turn speed 0x59 sub 0 / 1 set for this entity (retail <c>TurnSpeed</c>), or 0 for the turn ease. Read as
        /// 4096ths of a turn per 60 Hz frame (provisional, #197): the scripts set 5 to 900, mostly 50 to 100, which at this
        /// unit turn a quarter turn in 10 to 20 frames, about what the ease takes.
        /// </summary>
        private int _turnSpeed;

        /// <summary>
        /// Turns the entity's event heading. An explicit turn (<paramref name="turn"/>, not a placement) keeps the entity
        /// turning for the time the ease (or the turn speed 0x59 set) takes, which 0x76 / 0x70 wait for (retail's
        /// <c>Render.Flags3</c> bit 1, TurnCancel).
        /// </summary>
        private void SetEventHeading(float heading, bool turn = true)
        {
            if (turn && _positionKnown)
            {
                float delta = MathF.Abs(MathF.IEEERemainder(heading - _eventDir, 2f * MathF.PI));
                float frames = _turnSpeed > 0
                    ? delta / (_turnSpeed * (2f * MathF.PI / 4096f))
                    : (delta > TurnDoneRadians ? MathF.Log(delta / TurnDoneRadians) / TurnEaseRate : 0f) * 60f;
                Scene.StartTurn(EntityServerId, frames);
            }
            _eventDir = heading;
            _poseDirty = true;
        }

        /// <summary>
        /// Radians per step of the angles 0x16 / 0x17 read: retail's 0.0015339355 is 6.283 / 4096 (the same rounded 2π as
        /// 0x47's heading), not 2π / 4096.
        /// </summary>
        private const double ScriptAngleRadians = 6.283 / 4096.0;

        /// <summary>A script heading (4096 steps per turn) in wire-convention radians, within [0, 2π).</summary>
        private static float ScriptHeading(int value)
        {
            float radians = (value & 0xFFF) * (2f * MathF.PI / 4096f);
            return radians < 0f ? radians + 2f * MathF.PI : radians;
        }

        /// <summary>Where the named actor stands: its event position when it takes part in the event, else the world's.</summary>
        private bool TryGetActorPosition(int lookup, out Vector3 position) => TryGetActorPose(lookup, out position, out _);

        /// <summary>Where the named actor stands and which way it faces: its event pose when it takes part in the event, else the world's.</summary>
        private bool TryGetActorPose(int lookup, out Vector3 position, out float heading)
        {
            var (serverId, _) = ResolveActor(lookup);
            position = default;
            heading = 0f;
            if (serverId == uint.MaxValue) return false;
            if (Scene.FindActor(serverId) is { } actor)
            {
                (position, heading) = actor.EventPosition;
                return true;
            }
            return _host.TryGetEntityPose(serverId == 0 ? Scene.PlayerServerId : serverId, out position, out heading, out _);
        }

        /// <summary>
        /// 0xAB, XiEvents OpCodes/0x00AB: sub-cases that set or clear one render flag of the event's own entity (0x1B / 0x1C
        /// of the actor at +2), kept on the entity without a known effect. Sub 4 first waits while the entity plays an event
        /// action (retail yields while <c>AnimationPlay</c> is set unless the entity is in an event status, which GordianXI
        /// does not model). The client-wide subs (0x09 / 0x0A, 0x0F / 0x10, the respawn value
        /// 0x11, the per-entity helpers 0x14-0x18) are stepped over; an unknown sub-case ends the request, as retail stalls
        /// on it.
        /// </summary>
        private void ExecRenderFlags()
        {
            byte sub = Code8(1);
            if (sub == 0x04 && Scene.IsEntityActing(EntityServerId))
            {
                _retFlag = true;
                return;
            }
            (EventRenderFlags Flag, bool Set) change = sub switch
            {
                0x01 or 0x02 => (EventRenderFlags.Flags0Bit1, sub == 0x01),
                0x03 or 0x04 => (EventRenderFlags.Flags0Bit2, sub == 0x03),
                0x05 or 0x06 => (EventRenderFlags.Flags0Bit3, sub == 0x05),
                0x07 or 0x08 => (EventRenderFlags.Flags2Bit1, sub == 0x08),
                0x0B or 0x0C => (EventRenderFlags.Flags0Bit6, sub == 0x0B),
                0x0D or 0x0E => (EventRenderFlags.Flags4Bit1, sub == 0x0D),
                0x12 or 0x13 => (EventRenderFlags.Flags2Bit24, sub == 0x12),
                0x19 or 0x1A or 0x1B or 0x1C => (EventRenderFlags.Flags7Bit19, sub is 0x19 or 0x1B),
                _ => (EventRenderFlags.None, false),
            };
            if (change.Flag != EventRenderFlags.None)
            {
                uint target = sub is 0x1B or 0x1C ? TaskActor(Code32(2)) : EntityServerId;
                if (target != uint.MaxValue) _host.SetEntityRenderFlag(target, change.Flag, change.Set);
            }
            else if (sub is not (0x00 or 0x09 or 0x0A or 0x0F or 0x10 or 0x11 or (>= 0x14 and <= 0x18)))
            {
                _host.OnSkippedOpcode(0xAB, _pc);
            }
            int length = EventOpcodeTable.GetLength(_code, _pc);
            if (length <= 0)
            {
                EndRequest();
                return;
            }
            _pc += length;
        }

        /// <summary>0x59 sub 0 / 1: an entity's body turn speed, for its event turns here and for its drawing.</summary>
        private void SetTurnSpeed(uint serverId, int speed)
        {
            if (Scene.FindActor(serverId) is { } actor) actor._turnSpeed = speed;
            _host.SetEntityTurnSpeed(serverId, speed);
        }

        // The fade of 0x6C, kept per VM as retail keeps it in the VM's ExtData (FadeFlag, NowAlpha, EndAlpha, OfsAlpha,
        // AlphaTime).
        private bool _fading;
        private float _fadeAlpha, _fadeStep, _fadeFrames;
        private int _fadeEnd;

        /// <summary>
        /// 0x6C (CodeTRANSPAR), XiEvents OpCodes/0x006C: <c>6C actor:u32 alpha:work frames:work</c> fades the actor's colour
        /// alpha (0x80 = opaque) to <c>alpha</c> over <c>frames</c> 60 Hz frames (0 taken as 1). The first call takes the
        /// actor's alpha and runs the opcode again at once; each later call takes the frame delay off the time left, steps the
        /// alpha and yields, and once the time is below zero sets the target and goes on. An actor that is missing or has no
        /// model goes on at once. The decompiled pseudo code writes the target alpha on every step and leaves the stepped value
        /// unused; read here as the stepped value, so the fade is gradual.
        /// </summary>
        private void ExecTransparency()
        {
            uint actor = TaskActor(Code32(1));
            if (actor == uint.MaxValue || !_host.TryGetEntityAlpha(actor, out int alpha))
            {
                _fading = false;
                _pc += 9;
                return;
            }
            if (!_fading)
            {
                _fadeEnd = GetWork(5);
                _fadeFrames = GetWork(7);
                if (_fadeFrames == 0f) _fadeFrames = 1f;
                _fadeAlpha = alpha;
                _fadeStep = (_fadeEnd - _fadeAlpha) / _fadeFrames;
                _fading = true;
                return;
            }
            _fadeFrames -= _frameDelay;
            if (_fadeFrames < 0f)
            {
                _host.SetEntityAlpha(actor, _fadeEnd);
                _fading = false;
                _pc += 9;
                return;
            }
            _fadeAlpha += _frameDelay * _fadeStep;
            _host.SetEntityAlpha(actor, (int)MathF.Round(_fadeAlpha));
            _retFlag = true;
        }

        /// <summary>
        /// 0x1F (CodeMOVE) and 0x5A (CodeMOVE2), XiEvents OpCodes/0x001F and 0x005A. Sub-case 0 stores the goal (x, y,
        /// height operands) in the running stack; sub-case 1 walks the event position toward it at the walk speed, turning
        /// the entity to face its way, yielding each frame until it arrives. 0x1F walks on the ground (the height moves toward the
        /// goal's with the walk and is drawn on the floor below), 0x5A moves in all three axes.
        /// </summary>
        private void ExecMove(bool freeFlight)
        {
            byte mode = Code8(1);
            ref var stack = ref _stacks[_runPos];
            if (mode == 0)
            {
                stack.MovePosition = new Vector3(GetWork(2) * 0.001f, GetWork(6) * 0.001f, GetWork(4) * 0.001f);
                _pc += 8;
                return;
            }
            if (mode != 1)
            {
                _host.OnSkippedOpcode(_code[_pc], _pc);
                _pc += 2;
                return;
            }
            var goal = stack.MovePosition;
            if (!_positionKnown)
            {
                // Nowhere to walk from (the entity never arrived and the script did not place it): be at the goal.
                SetEventPosition(goal.X, goal.Y, goal.Z);
                _pc += 2;
                _retFlag = true;
                return;
            }
            float dx = goal.X - _eventX, dy = goal.Y - _eventY, dz = goal.Z - _eventZ;
            float distance = freeFlight ? MathF.Sqrt(dx * dx + dy * dy + dz * dz) : MathF.Sqrt(dx * dx + dz * dz);
            float step = _mainSpeed * _frameDelay / 60f;
            if (dx * dx + dz * dz > 1e-8f) _eventDir = WorldEntity.HeadingOf(dx, dz);
            if (step > 0f && distance > step)
            {
                // The height moves with the walk (a ground walk's goal height is often not the floor's: the Southern
                // San d'Oria knights walk from height -2 to a goal at 0; taken at once it sank them into the floor).
                _eventX += dx / distance * step;
                _eventZ += dz / distance * step;
                _eventY += dy / distance * step;
                _walkedThisRun = true;
            }
            else
            {
                (_eventX, _eventY, _eventZ) = (goal.X, goal.Y, goal.Z);
                _pc += 2;
            }
            _poseDirty = true;
            _retFlag = true;
        }

        /// <summary>
        /// Hands the entity's event pose to the host when the script changed it, or when a walk started or stopped
        /// (retail draws an event entity at its <c>EventPos</c>, XiAtelBuff::CopyAllPosEvent).
        /// </summary>
        private void PublishPose()
        {
            float speed = _walkedThisRun ? _mainSpeed : 0f;
            _walkedThisRun = false;
            if (!_poseDirty && (!_posed || speed == _publishedSpeed)) return;
            _poseDirty = false;
            _posed = true;
            _publishedSpeed = speed;
            _host.SetEntityPose(EntityServerId, new Vector3(_eventX, _eventY, _eventZ), _eventDir, speed);
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
            if (priority == FreePriority)
            {
                PublishPose(); // another entity's script may have placed this one
                return;
            }
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
            PublishPose();
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
                    // -r sin(angle) and r cos(angle), the angle in 4096 steps to a turn (XiEvents OpCodes/0x0016, 0x0017):
                    // the scripts' offset of a radius along a heading. The result is cut toward zero into the work value.
                    SetWork(1, (int)(-GetWork(5) * Math.Sin(GetWork(3) * ScriptAngleRadians)));
                    _pc += 7;
                    return;
                case 0x17:
                    SetWork(1, (int)(GetWork(5) * Math.Cos(GetWork(3) * ScriptAngleRadians)));
                    _pc += 7;
                    return;
                case 0x18:
                    // atan2(-a, b) scaled by 4096 / pi (XiEvents OpCodes/0x0018): 8192 steps to a turn, not the 4096 of
                    // 0x16 / 0x17, kept as written; no retail script runs it (every corpus hit is table data).
                    SetWork(1, (int)(Math.Atan2(-GetWork(3), GetWork(5)) * 4096.0 / Math.PI));
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
                    // Operands x, y, height (the scripts' order); internal Y is the height.
                    SetEventPosition(GetWork(1) * 0.001f, GetWork(5) * 0.001f, GetWork(3) * 0.001f);
                    _pc += 7;
                    return;
                case 0x37:
                    SetEventPosition(GetWork(1) * 0.001f, GetWork(5) * 0.001f, GetWork(3) * 0.001f);
                    SetEventHeading(ScriptHeading(GetWork(7)), turn: false);
                    _pc += 9;
                    return;
                case 0xBA:
                {
                    // Another entity's event position and heading: actor, x, y, height, heading (XiEvents OpCodes/0x00BA).
                    var target = RequestTarget(Code32(1));
                    if (target != null)
                    {
                        target.SetEventPosition(GetWork(5) * 0.001f, GetWork(9) * 0.001f, GetWork(7) * 0.001f);
                        target.SetEventHeading(ScriptHeading(GetWork(11)), turn: false);
                    }
                    _pc += 13;
                    return;
                }
                case 0x1F:
                case 0x5A:
                    ExecMove(op == 0x5A);
                    return;
                case 0x32:
                    _mainSpeed = GetWork(1) * 0.1f;
                    _pc += 3;
                    return;
                case 0x3A:
                {
                    // The actor's heading in 4096 steps to a turn, as the 0x7F03 fact (XiEvents OpCodes/0x003A); 0 for an
                    // actor that is not in the zone, unchanged for a code that names nobody.
                    if (ResolveActor(Code32(1)).ServerId != uint.MaxValue)
                    {
                        SetWork(5, TryGetActorPose(Code32(1), out _, out float heading) ? (int)(heading * 4096f / (2f * MathF.PI)) : 0);
                    }
                    _pc += 7;
                    return;
                }
                case 0x3B:
                {
                    // The actor's position in thousandths of a yalm, the scripts' order x, y, height (XiEvents
                    // OpCodes/0x003B): the event position of an actor in the event, else its world position; the scripts
                    // add 0x16 / 0x17 offsets to it to stand actors around one another (Lower Jeuno event 70).
                    if (ResolveActor(Code32(1)).ServerId != uint.MaxValue)
                    {
                        TryGetActorPose(Code32(1), out var at, out _);
                        SetWork(5, (int)(at.X * 1000f));
                        SetWork(7, (int)(at.Z * 1000f));
                        SetWork(9, (int)(at.Y * 1000f));
                    }
                    _pc += 11;
                    return;
                }
                case 0x39:
                    SetEventHeading(ScriptHeading(GetWork(1)));
                    _pc += 3;
                    return;
                case 0x1E:
                {
                    // Face the named actor and look at it (XiEvents OpCodes/0x001E: lookatone with speech frame 6).
                    if (TryGetActorPosition(Code32(1), out var at))
                    {
                        SetEventHeading(WorldEntity.HeadingOf(at.X - _eventX, at.Z - _eventZ));
                        LookAt(EntityServerId, TaskActor(Code32(1)), 6);
                    }
                    _pc += 5;
                    return;
                }
                case 0x4A:
                {
                    // The first actor turns to face the second (XiEvents OpCodes/0x004A, CodeDTURA).
                    var turner = RequestTarget(Code32(1));
                    if (turner != null && TryGetActorPosition(Code32(5), out var at))
                    {
                        var (from, _) = turner.EventPosition;
                        turner.SetEventHeading(WorldEntity.HeadingOf(at.X - from.X, at.Z - from.Z));
                        LookAt(TaskActor(Code32(1)), TaskActor(Code32(5)), 6);
                    }
                    _pc += 9;
                    return;
                }
                case 0x4B:
                {
                    // An actor's heading (only actors in the event: retail turns others directly, not done here).
                    RequestTarget(Code32(1))?.SetEventHeading(ScriptHeading(GetWork(5)));
                    _pc += 7;
                    return;
                }
                case 0x22:
                    _host.SetEntityHidden(EntityServerId, (Code8(1) & 1) != 0);
                    _pc += 2;
                    return;
                case 0x67:
                    // The event message mode and no compass (XiEvents OpCodes/0x0067, PresetEventMessageMode with two
                    // work values): the HUD steps aside and the event's lines show on the screen, not in the log.
                    _host.SetEventMessageMode(true, GetWork(1), GetWork(3));
                    _pc += 5;
                    return;
                case 0x68:
                    _host.SetEventMessageMode(false, 0, 0);
                    _pc++;
                    return;
                case 0x77:
                {
                    // Stop the clock at an hour and / or set the weather; 255 leaves either as it is (OpCodes/0x0077).
                    int hour = GetWork(1), weather = GetWork(3);
                    _host.LockEnvironment(hour == 255 ? -1 : hour, weather == 255 ? -1 : weather);
                    _pc += 5;
                    return;
                }
                case 0x78:
                    _host.UnlockEnvironment();
                    _pc++;
                    return;
                case 0x34:
                case 0x35:
                    ExecOpenZone();
                    return;
                case 0x4E:
                {
                    var (serverId, _) = ResolveActor(Code32(2));
                    if (serverId != uint.MaxValue) _host.SetEntityHidden(serverId == 0 ? Scene.PlayerServerId : serverId, (Code8(1) & 1) != 0);
                    _pc += 6;
                    return;
                }
                case 0x44:
                    if (_host.EntityExists(unchecked((uint)GetWork(1)))) _pc += 5;
                    else _pc = Code16(3);
                    return;
                case 0x46:
                    // The event camera (XiEvents OpCodes/0x0046, CodeDEFCAMERA): 1 takes the camera from the player, 0
                    // gives it back (ending every camera task), 2 reads whether the player has it.
                    switch (Code8(1))
                    {
                        case 0:
                            Scene.IsCameraHeld = false;
                            _host.SetEventCamera(false);
                            break;
                        case 1:
                            Scene.IsCameraHeld = true;
                            _host.SetEventCamera(true);
                            break;
                        case 2:
                            SetWork(2, Scene.IsCameraHeld ? 0 : 1);
                            _pc += 4;
                            return;
                    }
                    _pc += 2;
                    return;
                case 0x45:
                    ExecStartTask(EventSceneResource.GetFileId(GetWork(1)));
                    return;
                case 0x52:
                case 0xA1:
                    // 0xA1 sits in the 0x62 family's stop place but calls 0x52's helper with 0x52's base 30704 (XiEvents
                    // OpCodes/0x00A1), so it stops a main scene task, remapped like 0x52. No retail script uses it.
                    ExecEndTask(EventSceneResource.GetFileId(GetWork(1)));
                    return;
                case 0x55:
                    ExecWaitTask(EventSceneResource.GetFileId(GetWork(1)));
                    return;
                case 0x2D:
                    ExecZoneScheduler(ZoneSchedulerAction.Start);
                    return;
                case 0x51:
                    ExecZoneScheduler(ZoneSchedulerAction.Stop);
                    return;
                case 0x54:
                    ExecZoneScheduler(ZoneSchedulerAction.Wait);
                    return;
                case 0x60 when Code8(1) == 2:
                {
                    // A zone routine with no actors (XiEvents OpCodes/0x0060: XiZone::SetAction with null actors), e.g.
                    // the i0on-i3on lamps of six zones; its length is kept under actors 0 / 0 for a 0x54 that names none.
                    uint tag = unchecked((uint)Code32(2));
                    int frames = _host.StartZoneScheduler(FourCc(tag), 0, 0);
                    Scene.AddTask(Scene.NewTaskId(), ZoneSchedulerFileId, tag, 0, 0, frames);
                    _pc += 6;
                    return;
                }
                case 0x9F or 0x62 or 0xBB or 0xC5 or 0xCD or 0xD0 or 0xD5:
                    // The same scheduler on the other scene ranges, file base + the work value without 0x45's remapping
                    // (XiEvents OpCodes/0x009F, 0x0062, 0x00BB, 0x00C5, 0x00CD, 0x00D0, 0x00D5): the effect and screen
                    // routines, such as Port Jeuno 324's flash and blink (0x9F) and its sparkles on the player (0xCD).
                    ExecStartTask(EventSceneResource.GetBandFileId(op, GetWork(1)));
                    return;
                case 0xA3 or 0xBD or 0xC7 or 0xCF or 0xD2 or 0xD7:
                    ExecEndTask(EventSceneResource.GetBandFileId(op, GetWork(1)));
                    return;
                case 0xA2 or 0xA0 or 0xBC or 0xC6 or 0xCE or 0xD1 or 0xD6:
                    ExecWaitTask(EventSceneResource.GetBandFileId(op, GetWork(1)));
                    return;
                case 0x2C:
                    ExecEntityMotion(EventMotionSource.Own, 0, 1);
                    _pc += 13;
                    return;
                case 0x5B:
                case 0x66:
                    // Both yield a frame after starting the motion (RetFlag = 1).
                    if (op == 0x5B) ExecEntityMotion(EventMotionSource.Bank, MotionBankFileId(GetWork(1)), 3);
                    else ExecEntityMotion(EventMotionSource.Package, GetWork(1), 3);
                    _pc += 15;
                    _retFlag = true;
                    return;
                case 0x50:
                    if (TryTaskActors(Code32(1), Code32(5), out uint stopped, out _))
                    {
                        uint tag = unchecked((uint)Code32(9));
                        Scene.EndEntityAction(stopped, tag);
                        _host.StopEntityMotion(stopped, FourCc(tag));
                    }
                    _pc += 13;
                    return;
                case 0x53:
                    if (TryTaskActors(Code32(1), Code32(5), out uint waited, out _) && Scene.IsEntityActionPlaying(waited, unchecked((uint)Code32(9))))
                    {
                        _retFlag = true;
                        return;
                    }
                    _pc += 13;
                    return;
                case 0x59 when Code8(1) == 0:
                    // The body turn speed of the event's own entity from the work value at +2 (XiEvents OpCodes/0x0059,
                    // TurnSpeed); sub 1 is the same for the actor at +2 from the work value at +6.
                    SetTurnSpeed(EntityServerId, GetWork(2));
                    _pc += 4;
                    return;
                case 0x59 when Code8(1) == 1:
                    if (TaskActor(Code32(2)) is var bodyTurner && bodyTurner != uint.MaxValue) SetTurnSpeed(bodyTurner, GetWork(6));
                    _pc += 8;
                    return;
                case 0x59 when Code8(1) == 4:
                    // The walk speed of this VM's own walks (0x1F / 0x5A), the work value at +6 in tenths of a yalm per
                    // second, as 0x32 (XiEvents OpCodes/0x0059: retail sets the running VM's MainSpeed and only checks that
                    // the actor at +2 has a model). The scripts agree: each sub 4 is followed by a walk of the VM's own
                    // entity, also when it names another actor (Wajaom Woodlands actor 0x01033243 names three in turn).
                    if (TaskActor(Code32(2)) != uint.MaxValue) _mainSpeed = GetWork(6) * 0.1f;
                    _pc += 8;
                    return;
                case 0x59 when Code8(1) == 6:
                    // Waits while the actor at +2 plays an emote (XiEvents OpCodes/0x0059: IsMovingAction on its emote).
                    if (TaskActor(Code32(2)) is var emoter && emoter != uint.MaxValue && Scene.IsEntityActionPlaying(emoter, EmoteTag))
                    {
                        _retFlag = true;
                        return;
                    }
                    _pc += 6;
                    return;
                case 0x6C:
                    ExecTransparency();
                    return;
                case 0x59 when Code8(1) is 2 or 3:
                    // The head turn speed (XiEvents OpCodes/0x0059: sub 2 sets TurnSpeedHead of the event's own entity from
                    // the work value at +2, sub 3 of the actor at +2 from the work value at +6). Sub 5 is below; subs 7 / 8
                    // (a movement flag) are stepped over.
                    if (Code8(1) == 2) _host.SetEntityHeadTurnSpeed(EntityServerId, GetWork(2));
                    else if (TaskActor(Code32(2)) is var turner && turner != uint.MaxValue) _host.SetEntityHeadTurnSpeed(turner, GetWork(6));
                    _pc += Code8(1) == 2 ? 4 : 8;
                    return;
                case 0x59 when Code8(1) == 5:
                    // Render.Flags0 bit 21 of the actor at +2 from the literal byte at +6 (XiEvents OpCodes/0x0059): the
                    // event keeps the height it places the actor at (Port Jeuno 324's marker in the sky).
                    if (TaskActor(Code32(2)) is var floating && floating != uint.MaxValue) _host.SetEntityKeepsHeight(floating, (Code8(6) & 1) != 0);
                    _pc += 7;
                    return;
                case 0x33:
                    // The same bit of the event's own entity from the byte at +1 (XiEvents OpCodes/0x0033).
                    _host.SetEntityKeepsHeight(EntityServerId, (Code8(1) & 1) != 0);
                    _pc += 2;
                    return;
                case 0x92:
                    // Render.Flags3 bit 16 of the actor at +2 from the byte at +1 (XiEvents OpCodes/0x0092): no name plate.
                    // Port Jeuno 324 sets it on every NPC it places and on Joachim, not on the player, and retail draws only
                    // the player's plate there (#191).
                    if (TaskActor(Code32(2)) is var nameless && nameless != uint.MaxValue) _host.SetEntityHidesName(nameless, (Code8(1) & 1) != 0);
                    _pc += 6;
                    return;
                case 0x94:
                    // Render.Flags3 bit 17 of the actor at +2 from the byte at +1 (XiEvents OpCodes/0x0094). Kept, effect unknown:
                    // Port Jeuno 324 sets it on the player too, whose name plate stays.
                    if (TaskActor(Code32(2)) is var flagged && flagged != uint.MaxValue) _host.SetEntityRenderFlag(flagged, EventRenderFlags.Flags3Bit17, (Code8(1) & 1) != 0);
                    _pc += 6;
                    return;
                case 0xC0:
                    // Render.Flags3 bit 12 of the event's own entity from a work value's low bit (XiEvents OpCodes/0x00C0). Kept,
                    // effect unknown.
                    _host.SetEntityRenderFlag(EntityServerId, EventRenderFlags.Flags3Bit12, (GetWork(1) & 1) != 0);
                    _pc += 3;
                    return;
                case 0x81:
                    // The blink switch of the actor at +2: any non-zero byte at +1 turns it on, zero off (XiEvents OpCodes/0x0081).
                    if (TaskActor(Code32(2)) is var blinker && blinker != uint.MaxValue) _host.SetEntityRenderFlag(blinker, EventRenderFlags.NoBlink, Code8(1) == 0);
                    _pc += 6;
                    return;
                case 0x90:
                    // Sets the event hide flag of the event's own entity, and Render.Flags1 bit 12, which makes retail ask the
                    // server for the entity again when the next event starts (XiEvents OpCodes/0x0090; not needed here, the
                    // controller asks for missing participants itself).
                    _host.SetEntityHidden(EntityServerId, true);
                    _pc++;
                    return;
                case 0xAB:
                    ExecRenderFlags();
                    return;
                case 0x4C:
                case 0x4D:
                    // Open (8) or close (9) the event's own entity as a door, unless its Render.Flags0 bit 2 (0xAB sub 3)
                    // is set (XiEvents OpCodes/0x004C, 0x004D: StatusEvent).
                    if (_host.TryGetEntityRenderFlags(EntityServerId, out var doorFlags) && (doorFlags & EventRenderFlags.Flags0Bit2) == 0)
                        _host.SetEntityEventStatus(EntityServerId, op == 0x4C ? (byte)8 : (byte)9);
                    _pc++;
                    return;
                case 0x5E:
                    // Stop the event entity's action and return it to idle (XiEvents OpCodes/0x005E: KillLastAction, then the
                    // idle motion named by the operand); the idle name is not used, the entity's own idle plays.
                    ResetMotion(EntityServerId);
                    _pc += 5;
                    return;
                case 0x6B:
                    // The same for the actor at +5 (OpCodes/0x006B: operands motion:u32 actor:u32).
                    if (TaskActor(Code32(5)) is var resetActor && resetActor != uint.MaxValue) ResetMotion(resetActor);
                    _pc += 9;
                    return;
                case 0x76:
                {
                    // Wait while the actor turns (OpCodes/0x0076: Render.Flags3 bit 1 set by a turn, yields).
                    uint turning = TaskActor(Code32(1));
                    if (turning != uint.MaxValue && Scene.IsTurning(turning))
                    {
                        _retFlag = true;
                        return;
                    }
                    _pc += 5;
                    return;
                }
                case 0x70:
                    if (Scene.IsTurning(EntityServerId))
                    {
                        _retFlag = true;
                        return;
                    }
                    _pc++;
                    return;
                case 0x38:
                    // Sets the low word of CliEventModeLocal (XiEvents OpCodes/0x0038: the operand's low byte with 0x20 in
                    // the high byte); recorded only, its bits are not mapped yet.
                    Scene.EventModeLocal = (GetWork(1) & 0xFF) | 0x2000;
                    _pc += 3;
                    return;
                case 0x6E:
                {
                    // An entity plays an emote (XiEvents OpCodes/0x006E, CodeEMOT): the work value at +5 holds the emote id
                    // (low byte) and a variant (high byte). While the entity still plays an action the opcode waits.
                    uint emoting = TaskActor(Code32(1));
                    if (emoting == uint.MaxValue)
                    {
                        _pc += 7;
                        return;
                    }
                    if (Scene.IsEntityActing(emoting))
                    {
                        _retFlag = true;
                        return;
                    }
                    int emote = GetWork(5);
                    Scene.SetEntityAction(emoting, EmoteTag, _host.PlayEntityEmote(emoting, emote & 0xFF, (emote >> 8) & 0xFF));
                    _pc += 7;
                    return;
                }
                case 0x99:
                {
                    // Yields a frame while the entity plays an action, then goes on either way (OpCodes/0x0099 steps past
                    // itself before yielding, so it does not wait for the end).
                    uint acting = TaskActor(Code32(1));
                    if (acting != uint.MaxValue && Scene.IsEntityActing(acting)) _retFlag = true;
                    _pc += 5;
                    return;
                }
                case 0x80:
                    // Waits for the actor's action resources to load (OpCodes/0x0080, CodeLOADWAIT); they load when asked here.
                    _pc += 5;
                    return;
                case 0x79:
                    // Look at another actor (XiEvents OpCodes/0x0079): sub 0 lookatone(actor, target, 6), sub 1 with the
                    // speech frame from a work value; sub 2 holds the head on a look axis (two work values).
                    switch (Code8(1))
                    {
                        case 0:
                            LookAt(TaskActor(Code32(2)), TaskActor(Code32(6)), 6);
                            break;
                        case 1:
                            LookAt(TaskActor(Code32(2)), TaskActor(Code32(6)), GetWork(10));
                            break;
                        case 2:
                            if (TaskActor(Code32(2)) is var looker && looker != uint.MaxValue) _host.SetEntityLookAxis(looker, GetWork(6), GetWork(8));
                            break;
                        default:
                            // Retail does not advance on an unknown sub and would spin until the step guard; end it now.
                            _host.OnSkippedOpcode(0x79, _pc);
                            EndRequest();
                            return;
                    }
                    _pc += EventOpcodeTable.GetLength(_code, _pc);
                    return;
                case 0x7B:
                    // The actor stops looking and talking (OpCodes/0x007B: look mode cleared, NpcSpeechFrame -1).
                    if (TaskActor(Code32(1)) is var quiet && quiet != uint.MaxValue) _host.SetEntityLook(quiet, uint.MaxValue, -1);
                    _pc += 5;
                    return;
                case 0x2F:
                case 0x42:
                case 0x7C:
                    // Opcodes with no effect on a client that draws no render-flag variants. The
                    // scripts' use of 0x2F (Render.Flags0 bit 19) always sits next to the 0x22 / 0x4E hide that does the work.
                    _pc += EventOpcodeTable.GetLength(_code, _pc);
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

        #region Schedulers

        /// <summary>
        /// The two actors of a scheduler opcode as server ids (the local player as <see cref="EventScene.PlayerServerId"/>,
        /// or 0 before it is known). False when either names nobody in the zone: retail then steps over the opcode.
        /// </summary>
        private bool TryTaskActors(int first, int second, out uint actor, out uint target)
        {
            actor = TaskActor(first);
            target = TaskActor(second);
            return actor != uint.MaxValue && target != uint.MaxValue;
        }

        private uint TaskActor(int lookup)
        {
            var (serverId, _) = ResolveActor(lookup);
            if (serverId == uint.MaxValue) return uint.MaxValue;
            if (serverId == 0) return Scene.PlayerServerId;
            if (serverId == Scene.PlayerServerId || Scene.FindActor(serverId) != null || _host.EntityExists(serverId)) return serverId;
            return uint.MaxValue;
        }

        /// <summary>lookatone: one actor (server id) looks at another, when both are known.</summary>
        private void LookAt(uint looker, uint target, int speechFrame)
        {
            if (looker == uint.MaxValue || target == uint.MaxValue) return;
            _host.SetEntityLook(looker, target, speechFrame);
        }

        /// <summary>The scene's action tag of an emote (0x6E), which no motion opcode names.</summary>
        private const uint EmoteTag = 0x746F6D65; // "emot"

        /// <summary>
        /// 0x34 / 0x35 (XiEvents OpCodes/0x0034, 0x0035): retail deletes every actor and yields, yields again while the map
        /// load flag is up, then opens the zone the work value at +1 names (XiZone::Open) and goes on; 0x34 may close the
        /// event zone first. The Windurst intros open Windurst Walls (239) with 0x34 for their first scene and their own
        /// zone with 0x35 after it. Here the host draws the zone in place of the session's own and the opcode waits while
        /// it loads, at most <see cref="ZoneOpenTimeoutFrames"/>.
        /// </summary>
        private void ExecOpenZone()
        {
            if (_zoneOpenFrames < 0f)
            {
                _host.OpenEventZone(GetWork(1));
                _zoneOpenFrames = 0f;
                _retFlag = true;
                return;
            }
            _zoneOpenFrames += _frameDelay;
            if (_host.IsEventZoneLoading && _zoneOpenFrames < ZoneOpenTimeoutFrames)
            {
                _retFlag = true;
                return;
            }
            _zoneOpenFrames = -1f;
            _pc += 3;
        }

        /// <summary>How long 0x34 / 0x35 wait for the zone to load before going on without it (15 s).</summary>
        private const float ZoneOpenTimeoutFrames = 900f;

        /// <summary>Frames 0x34 / 0x35 have waited for their zone, -1 while none is opening.</summary>
        private float _zoneOpenFrames = -1f;

        /// <summary>0x5E / 0x6B: the entity's event action ends and it returns to idle.</summary>
        private void ResetMotion(uint serverId)
        {
            Scene.EndEntityActions(serverId);
            _host.StopEntityMotion(serverId, string.Empty);
        }

        /// <summary>A little-endian FourCC operand as text (trailing NULs dropped).</summary>
        private static string FourCc(uint value)
        {
            Span<char> chars = stackalloc char[4];
            int length = 0;
            for (int i = 0; i < 4; i++)
            {
                char c = (char)((value >> (8 * i)) & 0xFF);
                if (c == (char)0) break;
                chars[length++] = c;
            }
            return new string(chars[..length]);
        }

        /// <summary>
        /// The event motion DAT of 0x5B's resource number (XiEvents OpCodes/0x005B, ReadEventMotionRes): 32104 + n below
        /// 512, then 49135 + n, 56345 + n (from 1024), 59739 + n (from 2048) and 66339 + n (from 3072).
        /// </summary>
        public static int MotionBankFileId(int resource) => resource switch
        {
            < 512 => resource + 32104,
            < 1024 => resource + 49135,
            < 2048 => resource + 56345,
            < 3072 => resource + 59739,
            _ => resource + 66339,
        };

        /// <summary>
        /// 0x45 (CodeLOADSCHEDULER): <c>op p:u16 actor:u32 target:u32 routine:u32 value:u16</c>. Plays routine
        /// <c>routine</c> of scene resource <c>p</c> (<see cref="EventSceneResource.GetFileId"/>) on the two actors as a
        /// task of the main scheduler: its camera shots and fades. The trailing value (0 in every intro) is not used.
        /// </summary>
        private void ExecStartTask(int fileId)
        {
            if (TryTaskActors(Code32(3), Code32(7), out uint caster, out uint target))
            {
                uint tag = unchecked((uint)Code32(11));
                int id = Scene.NewTaskId();
                int frames = _host.StartSceneTask(id, fileId, FourCc(tag), caster, target);
                int replaced = Scene.AddTask(id, fileId, tag, caster, target, frames);
                if (replaced >= 0) _host.StopSceneTask(replaced);
            }
            _pc += 17;
        }

        /// <summary>0x52 (CodeENDLOADSCHEDULER): the same operands without the value; stops that task.</summary>
        private void ExecEndTask(int fileId)
        {
            if (TryTaskActors(Code32(3), Code32(7), out uint caster, out uint target))
            {
                int id = Scene.RemoveTask(fileId, unchecked((uint)Code32(11)), caster, target);
                if (id >= 0) _host.StopSceneTask(id);
            }
            _pc += 15;
        }

        /// <summary>0x55 (CodeWAITLOADSCHEDULER): the same operands; waits while that task runs.</summary>
        private void ExecWaitTask(int fileId)
        {
            if (TryTaskActors(Code32(3), Code32(7), out uint caster, out uint target)
                && Scene.IsTaskRunning(fileId, unchecked((uint)Code32(11)), caster, target))
            {
                _retFlag = true;
                return;
            }
            _pc += 15;
        }

        private enum ZoneSchedulerAction { Start, Stop, Wait }

        /// <summary>The task file key under which <see cref="EventScene"/> keeps the zone routines an event started (no scene file).</summary>
        internal const int ZoneSchedulerFileId = -1;

        /// <summary>
        /// 0x2D (CodeMAPSCHEDULOR), 0x51 (CodeENDMAPSCHEDULOR) and 0x54 (CodeWAITMAPSCHEDULOR): <c>op actor:u32
        /// target:u32 routine:u32</c> (13 bytes). The zone's own routine <c>routine</c> (a Section 0x07 of the zone DAT,
        /// what an S2C 0x039 map scheduler names: Alzadaal's portal activation <c>1pa2</c>, Lower Jeuno's <c>sc00</c>)
        /// starts on the two actors through the host, like a scene task; 0x51 ends it and 0x54 waits while it runs
        /// (XiEvents OpCodes/0x002D, 0x0051, 0x0054: XiZone::SetAction / KillAction / IsMovingAction, when both actors'
        /// models are loaded; here when both are in the zone). The routine's length comes from the zone DAT
        /// (<see cref="IEventVmHost.StartZoneScheduler"/>); a routine the zone lacks, or one of 0 frames, is over at once.
        /// </summary>
        private void ExecZoneScheduler(ZoneSchedulerAction action)
        {
            if (TryTaskActors(Code32(1), Code32(5), out uint caster, out uint target))
            {
                uint tag = unchecked((uint)Code32(9));
                switch (action)
                {
                    case ZoneSchedulerAction.Start:
                    {
                        int frames = _host.StartZoneScheduler(FourCc(tag), caster, target);
                        Scene.AddTask(Scene.NewTaskId(), ZoneSchedulerFileId, tag, caster, target, frames);
                        break;
                    }
                    case ZoneSchedulerAction.Stop:
                        Scene.RemoveTask(ZoneSchedulerFileId, tag, caster, target);
                        _host.StopZoneScheduler(FourCc(tag), caster, target);
                        break;
                    case ZoneSchedulerAction.Wait:
                        if (Scene.IsTaskRunning(ZoneSchedulerFileId, tag, caster, target))
                        {
                            _retFlag = true;
                            return;
                        }
                        break;
                }
            }
            _pc += 13;
        }

        /// <summary>
        /// 0x2C (CodeSCHEDULOR: <c>op actor:u32 target:u32 routine:u32</c>), 0x5B and 0x66 (CodeLOADEXTSCHEDULER:
        /// <c>op resource:u16 actor:u32 target:u32 routine:u32</c>): the actor plays a motion routine toward the target,
        /// from its own motions, from an event motion DAT loaded onto it first (0x5B) or from its player-model motion
        /// package (0x66). Routine 0 and <c>xxxx</c> start nothing (XiEvents OpCodes/0x005B).
        /// </summary>
        private void ExecEntityMotion(EventMotionSource source, int resource, int actorOffset)
        {
            if (!TryTaskActors(Code32(actorOffset), Code32(actorOffset + 4), out uint actor, out uint target)) return;
            uint tag = unchecked((uint)Code32(actorOffset + 8));
            if (tag == 0 || tag == 0x78787878) return;
            int frames = _host.PlayEntityMotion(actor, source, resource, FourCc(tag), target);
            Scene.SetEntityAction(actor, tag, frames);
        }

        #endregion

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
