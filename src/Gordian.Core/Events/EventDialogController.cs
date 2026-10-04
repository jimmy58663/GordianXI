// src/Gordian.Core/Events/EventDialogController.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Gordian.Core.Animation;
using Gordian.Core.Diagnostics;
using Gordian.Core.Input;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Events;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Ui;
using Gordian.Core.World;

namespace Gordian.Core.Events
{
    /// <summary>
    /// Dialog text for a session (Tier 2 chunk 6): runs the server's events (S2C 0x032/0x033/0x034) through the
    /// <see cref="EventVm"/> with the zone's scripts and dialog table, prints the lines to the chat log with the
    /// speaker's name, opens the query window for choices, answers the server (0x05B) and prints the direct zone
    /// messages (S2C 0x036 / 0x02A).
    /// <para>
    /// Packet handlers arrive on the network thread and only queue work; the event runs on the game tick
    /// (<see cref="Tick"/>, from the locomotion update), where the input is read too. While an event runs the
    /// character is held still, as in retail; Confirm (or Cancel) dismisses a line that waits for the player.
    /// </para>
    /// </summary>
    public sealed class EventDialogController : IEventVmHost
    {
        /// <summary>Reads a DAT by file id (the app's resource manager, <see cref="ZoneDatLoader"/>); without it events end at once.</summary>
        public static Func<int, byte[]?>? DatLoader
        {
            get => ZoneDatLoader.Load;
            set => ZoneDatLoader.Load = value;
        }

        /// <summary>Reads a DAT by its path under the game directory (<c>ROM/32/40.DAT</c>): the race emote motions of 0x6E.</summary>
        public static Func<string, byte[]?>? DatPathLoader { get; set; }

        /// <summary>Names an item / key item / zone by id for the 0x01 codes of the text; null names show as &lt;#id&gt;.</summary>
        public static Func<byte, int, string?>? NameResolver { get; set; }

        private readonly Func<int, byte[]?>? _datLoader;

        /// <summary>A controller that reads its DATs through the static <see cref="DatLoader"/> (the app's).</summary>
        public EventDialogController()
        {
        }

        /// <summary>
        /// A controller that reads its DATs by file id through <paramref name="datLoader"/> instead of the static
        /// <see cref="DatLoader"/>, so tests running in parallel do not share (and race on) one global loader.
        /// </summary>
        public EventDialogController(Func<int, byte[]?> datLoader)
        {
            _datLoader = datLoader;
        }

        /// <summary>This controller's DAT loader: its own when one was given, else the static <see cref="DatLoader"/>.</summary>
        private Func<int, byte[]?>? Loader => _datLoader ?? DatLoader;

        private readonly object _sync = new();
        private readonly EventWorkZone _zone = new();
        private ProgressionState? _progression;
        private ProgressionPacketModule? _module;
        private WorldState? _world;
        private LocalPlayerState? _player;
        private StockUiChat? _chat;
        private StockUiMenuController? _menus;
        private Func<string> _playerName = () => string.Empty;

        private int _scriptZone = -1;
        private ZoneEventScript? _script;
        private ZoneDialogTable? _dialog;
        private PartyState? _party;
        private Action<ushort>? _requestEntity;
        private CutsceneEventInfo? _pendingStart;
        private CutsceneEventInfo? _info;
        /// <summary>
        /// The running event: one VM per entity taking part, sharing the work zone (<see cref="StartEvent"/>). Null
        /// when no event runs. Replaced as a whole, so other threads can read it.
        /// </summary>
        private volatile EventScene? _scene;
        /// <summary>An event whose start waits for the entities it names to arrive (<see cref="EntityWaitSeconds"/>).</summary>
        private CutsceneEventInfo? _waitingStart;
        private double _waitingSeconds;
        /// <summary>The world entities taking part in the running event (game tick thread only), released when it ends.</summary>
        private readonly List<WorldEntity> _participants = new();
        /// <summary>The entities the running event placed or hid (game tick thread only), reset when it ends.</summary>
        private readonly HashSet<uint> _staged = new();
        private volatile bool _cutsceneHud;
        private bool _clockLocked;
        /// <summary>The zone's weather number before the event set its own (0x77), or -1.</summary>
        private int _savedWeather = -1;

        /// <summary>
        /// Whether the running event is in its event message mode (opcode 0x67 until 0x68 or the end): the HUD is hidden
        /// (log and party windows, name plates, target, status icons: XiEvents OpCodes/0x0067 hides "the entire HUD") and
        /// the event's lines show on the screen (<see cref="EventText"/>) instead of the log.
        /// </summary>
        public bool IsCutsceneHud => _cutsceneHud;

        /// <summary>
        /// The line the event shows on the screen in its event message mode while it is open, or null. Read by the
        /// renderer (replaced as a whole).
        /// </summary>
        public EventScreenText? EventText
        {
            get
            {
                var text = _eventText;
                var scene = _scene;
                return text != null && _cutsceneHud && scene != null && scene.IsWaitingForConfirm ? text : null;
            }
        }

        private volatile EventScreenText? _eventText;
        private int _eventTextX;
        private int _eventTextY;
        private volatile bool _receivePending;
        private volatile bool _cancelRequested;
        private volatile bool _zoneChanged;
        private volatile int _queryResult;
        private StockUiOpenMenu? _queryMenu;
        private int _queryStart;

        /// <summary>
        /// How long an event's start waits for the entities it names that are not in the zone yet, after asking the
        /// server for them (C2S 0x016). Retail waits for every one (XiEvents InitEvent2); the limit keeps an event
        /// whose NPC the server never sends from holding the start forever.
        /// </summary>
        public const double EntityWaitSeconds = 1.5;

        /// <summary>
        /// How long a zone-in event's start waits for the first of its NPCs: they arrive only once the zone is entered
        /// (the maintainer's Southern San d'Oria intro, 2026-09-30: the event came with the zone, its 35 NPCs 7 s later).
        /// Once any has arrived the start waits <see cref="EntityWaitSeconds"/> after the last arrival.
        /// </summary>
        public const double ZoneInEntityWaitSeconds = 15.0;

        /// <summary>The entities still missing at the last check of a waiting start, and the seconds since one arrived.</summary>
        private int _waitingMissing;
        private double _waitingIdleSeconds;
        private bool _waitingAnyArrived;

        /// <summary>
        /// Poses and hide flags the running event gave entities that were not in the zone yet (game tick thread only);
        /// applied when they arrive.
        /// </summary>
        private readonly Dictionary<uint, EventPose> _pendingPoses = new();
        private readonly Dictionary<uint, bool> _pendingHidden = new();
        private readonly Dictionary<uint, bool> _pendingKeepHeight = new();
        private readonly Dictionary<uint, bool> _pendingHidesName = new();
        private readonly Dictionary<uint, (EventRenderFlags Set, EventRenderFlags Clear)> _pendingRenderFlags = new();

        /// <summary>The speaker of the line the player has not closed yet (retail's <c>MouthIndex</c>), or null.</summary>
        private WorldEntity? _talker;

        /// <summary>
        /// How far (yalms) an event may have placed an entity from its server position and still turn it back smoothly at
        /// the event's end; farther, it is put back at once (a staged actor returning from across the scene).
        /// </summary>
        public const float EventReturnSnapDistance = 1.5f;

        /// <summary>The running event's cutscene flags, for entities that arrive after its start.</summary>
        private CutsceneFlags _flags;

        /// <summary>The entities the running event has already sorted (taking part, or hidden by its flags).</summary>
        private readonly HashSet<uint> _sorted = new();

        /// <summary>
        /// The running event's camera shots and screen fades (#165), which the viewport follows; reset when the event
        /// ends.
        /// </summary>
        public EventPresentation Presentation { get; } = new();

        /// <summary>Scene resource DATs by file id (null: missing), read once per zone.</summary>
        private readonly Dictionary<int, EventSceneResource?> _sceneResources = new();

        /// <summary>Event motion DATs by file id (null: missing), read once per zone.</summary>
        private readonly Dictionary<int, EventMotionBank?> _motionBanks = new();

        /// <summary>Emote motion banks by race and emote (null: none), read once per zone.</summary>
        private readonly Dictionary<(CharacterRace Race, int Emote, int Variant), EventMotionBank?> _emoteBanks = new();

        /// <summary>
        /// The entities the running event gave gestures (game tick thread only): when it ends their motion banks are dropped
        /// and a gesture still holding its pose (<c>sha0</c>, <c>corp</c>, #193) is stopped.
        /// </summary>
        private readonly List<WorldEntity> _banked = new();


        /// <summary>Whether an event is running (the character is held and Confirm belongs to the dialog).</summary>
        public bool IsActive => _scene != null;

        /// <summary>Whether the running event shows a line the player must confirm.</summary>
        public bool IsWaitingForConfirm => _scene?.IsWaitingForConfirm ?? false;

        /// <summary>Raised when the dialog state changed (an event started or ended, a line waits).</summary>
        public event Action? Changed;

        /// <summary>
        /// Subscribes to a session's event and message sources. <paramref name="party"/> resolves the scripts' party
        /// actor codes and <paramref name="requestEntity"/> asks the server for an entity by target index (C2S 0x016).
        /// </summary>
        public void Attach(ProgressionState progression, ProgressionPacketModule module, WorldState world, LocalPlayerState player,
            StockUiChat chat, StockUiMenuController menus, Func<string> playerName, PartyState? party = null, Action<ushort>? requestEntity = null)
        {
            _party = party;
            _requestEntity = requestEntity;
            _progression = progression;
            _module = module;
            _world = world;
            _player = player;
            _chat = chat;
            _menus = menus;
            _playerName = playerName;
            progression.EventStarted += info =>
            {
                lock (_sync) _pendingStart = info;
            };
            progression.EventUpdateAcknowledged += () => _receivePending = false;
            progression.EventCancelledByServer += () => _cancelRequested = true;
            progression.DialogMessageReceived += OnDialogMessage;
            world.ZoneChanged += _ => _zoneChanged = true;
        }

        /// <summary>Advances the running event; starts a queued one.</summary>
        public void Tick(TimeSpan elapsed)
        {
            CutsceneEventInfo? start;
            lock (_sync)
            {
                start = _pendingStart;
                _pendingStart = null;
            }
            if (_zoneChanged)
            {
                _zoneChanged = false;
                _waitingStart = null;
                DropEvent();
                _script = null;
                _dialog = null;
                _scriptZone = -1;
                _zone.Clear();
                _sceneResources.Clear();
                _motionBanks.Clear();
                _emoteBanks.Clear();
            }
            if (_cancelRequested)
            {
                _cancelRequested = false;
                _waitingStart = null;
                if (_scene != null)
                {
                    GordianLog.Info("EVENT", $"Event {_info?.EventPara} cancelled by the server.");
                    DropEvent();
                }
            }
            if (start != null) BeginStart(start);
            else if (_waitingStart != null)
            {
                _waitingSeconds += elapsed.TotalSeconds;
                _waitingIdleSeconds += elapsed.TotalSeconds;
                int missing = MissingEntities(_waitingStart, request: false);
                if (missing < _waitingMissing)
                {
                    _waitingMissing = missing;
                    _waitingIdleSeconds = 0;
                    _waitingAnyArrived = true;
                }
                bool timedOut = _waitingAnyArrived || !_waitingStart.FromZoneIn
                    ? _waitingIdleSeconds >= EntityWaitSeconds
                    : _waitingSeconds >= ZoneInEntityWaitSeconds;
                if (missing == 0 || timedOut)
                {
                    var waiting = _waitingStart;
                    _waitingStart = null;
                    StartEvent(waiting);
                }
            }
            var scene = _scene;
            if (scene == null) return;
            SortArrivals(scene);
            bool wasWaiting = scene.IsWaitingForConfirm;
            if (!scene.IsFinished) scene.Tick(elapsed);
            if (wasWaiting && !scene.IsWaitingForConfirm) StopTalker();
            if (scene.IsFinished) FinishEvent(scene);
            else if (wasWaiting != scene.IsWaitingForConfirm) Changed?.Invoke();
        }

        /// <summary>
        /// Reads the dialog's input: Confirm (or Cancel) dismisses a waiting line. Returns true while an event runs
        /// and no menu is open, so the action keys stay with the dialog.
        /// </summary>
        public bool ProcessInput(InputState input)
        {
            var scene = _scene;
            if (scene == null) return false;
            if (input.WasActionTriggered(InputAction.Confirm) || input.WasActionTriggered(InputAction.Cancel)) Confirm();
            return true;
        }

        /// <summary>Dismisses the line the running event waits on (Confirm or Cancel on it).</summary>
        public void Confirm()
        {
            var scene = _scene;
            if (scene == null || !scene.IsWaitingForConfirm) return;
            scene.Confirm();
            Changed?.Invoke();
        }

        /// <summary>
        /// Starts an event, or holds it until the NPCs whose blocks carry it are in the zone: retail asks the server
        /// for a missing one (C2S 0x016) and starts once all are there (XiEvents InitEvent2).
        /// </summary>
        private void BeginStart(CutsceneEventInfo info)
        {
            _waitingStart = null;
            EnsureZoneData(_world?.CurrentZoneId ?? 0);
            int missing = MissingEntities(info, request: true);
            if (missing == 0)
            {
                StartEvent(info);
                return;
            }
            GordianLog.Info("EVENT", $"Event {info.EventPara} waits for {missing} entities it names{(info.FromZoneIn ? " (zone-in)" : string.Empty)}.");
            _waitingStart = info;
            _waitingSeconds = 0;
            _waitingIdleSeconds = 0;
            _waitingMissing = missing;
            _waitingAnyArrived = false;
        }

        /// <summary>
        /// Counts the NPCs whose blocks carry the event but that are not in the zone, asking the server for each when
        /// <paramref name="request"/> is set.
        /// </summary>
        private int MissingEntities(CutsceneEventInfo info, bool request)
        {
            var script = _script;
            var world = _world;
            if (script == null || world == null) return 0;
            int missing = 0;
            foreach (var block in script.Blocks)
            {
                if ((block.ActorId & 0xFF000000) == 0 || block.ActorId >= 0x7F000000 || block.IndexOf(info.EventPara) < 0) continue;
                if (world.TryGetByServerId(block.ActorId, out _)) continue;
                missing++;
                if (request) _requestEntity?.Invoke((ushort)(block.ActorId & 0x3FF));
            }
            return missing;
        }

        private void StartEvent(CutsceneEventInfo info)
        {
            if (_scene != null)
            {
                GordianLog.Warning("EVENT", $"Event {info.EventPara} started while event {_info?.EventPara} runs; the running one is dropped.");
                DropEvent();
            }
            int zoneId = _world?.CurrentZoneId ?? 0;
            EnsureZoneData(zoneId);
            _info = info;
            // 0x034 carries the numbers in NumericParams; 0x033 (string events) carries them in DataParams.
            bool numeric = Array.Exists(info.NumericParams, v => v != 0) || !Array.Exists(info.DataParams, v => v != 0);
            _zone.SetParameters(numeric ? info.NumericParams : Array.ConvertAll(info.DataParams, v => unchecked((int)v)));
            _zone.Selection = 0;
            _zone.EndParameter = 0;

            var scene = CreateScene(info);
            if (scene == null)
            {
                GordianLog.Warning("EVENT", $"No script for event {info.EventPara} of actor 0x{info.UniqueNo:X8} in zone {zoneId}; ending it.");
                SendEnd(info, 0);
                return;
            }
            var blocks = new List<string>(scene.Actors.Count);
            foreach (var v in scene.Actors) blocks.Add(v.CarriesEvent ? $"0x{v.EntityServerId:X8}" : $"0x{v.EntityServerId:X8} (catch-all)");
            GordianLog.Info("EVENT", $"Running event {info.EventPara} of actor 0x{info.UniqueNo:X8} on {scene.Actors.Count} entities: {string.Join(", ", blocks)}.");
            _receivePending = false;
            _scene = scene;
            MarkParticipants(scene);
            ApplyCutsceneFlags(scene, (CutsceneFlags)info.Mode);
            if (_player != null) _player.IsMovementLocked = true;
            Changed?.Invoke();
        }

        /// <summary>
        /// NO_PCS / NO_NPCS: the other players, or the NPCs and monsters, that take no part in the event are not drawn
        /// while it runs (entities that arrive during the event are not hidden).
        /// </summary>
        private void ApplyCutsceneFlags(EventScene scene, CutsceneFlags flags)
        {
            _flags = flags;
            int hidden = SortArrivals(scene);
            GordianLog.Info("EVENT", $"Cutscene flags 0x{(uint)flags:X}: {hidden} entities outside the event hidden.");
        }

        /// <summary>
        /// Sorts the entities not seen yet since the event started: one taking part is marked as such and gets the pose
        /// and hide flag its script gave it before it arrived (a zone-in intro's NPCs arrive seconds after its start);
        /// any other is hidden when the cutscene flags say so (NO_PCS / NO_NPCS). Returns how many it hid.
        /// </summary>
        private int SortArrivals(EventScene scene)
        {
            var world = _world;
            if (world == null) return 0;
            uint self = _player?.ServerId ?? 0;
            int hidden = 0;
            foreach (var entity in world.GetAllEntities())
            {
                if (!_sorted.Add(entity.ServerId) || entity.ServerId == self) continue;
                if (scene.FindActor(entity.ServerId) != null)
                {
                    JoinEvent(entity);
                    if (_pendingPoses.Remove(entity.ServerId, out var pose))
                    {
                        entity.EventPose = pose;
                        _staged.Add(entity.ServerId);
                    }
                    if (_pendingHidden.Remove(entity.ServerId, out bool isHidden))
                    {
                        entity.IsEventHidden = isHidden;
                        _staged.Add(entity.ServerId);
                    }
                    if (_pendingKeepHeight.Remove(entity.ServerId, out bool keepsHeight))
                    {
                        entity.KeepsEventHeight = keepsHeight;
                        _staged.Add(entity.ServerId);
                    }
                    if (_pendingHidesName.Remove(entity.ServerId, out bool hidesName))
                    {
                        entity.HidesEventName = hidesName;
                        _staged.Add(entity.ServerId);
                    }
                    if (_pendingRenderFlags.Remove(entity.ServerId, out var renderFlags))
                    {
                        entity.EventRenderFlags = (entity.EventRenderFlags | renderFlags.Set) & ~renderFlags.Clear;
                        _staged.Add(entity.ServerId);
                    }
                    continue;
                }
                bool isPlayer = entity.Type == EntityType.Player;
                if (isPlayer ? (_flags & CutsceneFlags.NoPcs) == 0 : (_flags & CutsceneFlags.NoNpcs) == 0) continue;
                entity.IsEventHidden = true;
                _staged.Add(entity.ServerId);
                hidden++;
            }
            return hidden;
        }

        /// <summary>
        /// Gives every entity the event placed or hid back to the world (retail's ~XiEvent resets the event state), ends
        /// the HUD's cutscene mode and lets the clock and weather go if the script did not.
        /// </summary>
        private void ClearStaging()
        {
            _cutsceneHud = false;
            _eventText = null;
            _pendingPoses.Clear();
            _pendingHidden.Clear();
            _pendingKeepHeight.Clear();
            _pendingHidesName.Clear();
            _pendingRenderFlags.Clear();
            _sorted.Clear();
            _flags = 0;
            UnlockEnvironment();
            if (_world != null) _world.EventZoneId = 0;
            Presentation.Reset();
            foreach (var entity in _banked)
            {
                entity.Animation.ClearEventMotionBanks();
                entity.Animation.EnqueueAction(new ActionRequest
                {
                    ActorId = entity.ServerId,
                    Motion = ActionMotion.EventMotionStop,
                    Routine = string.Empty,
                    ReceivedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
                });
            }
            _banked.Clear();
            var world = _world;
            if (world != null)
            {
                foreach (uint id in _staged)
                {
                    if (!world.TryGetByServerId(id, out var entity)) continue;
                    if (entity.EventPose is { } pose && id != (_player?.ServerId ?? 0)
                        && Vector2.Distance(new Vector2(pose.Position.X, pose.Position.Z), new Vector2(entity.TargetPosition.X, entity.TargetPosition.Z)) > EventReturnSnapDistance)
                    {
                        // Back where the server has it at once, not walked there from the event's spot. An actor the event
                        // only turned (Deraquien facing the player) turns back smoothly instead (EntityRenderer), as in retail.
                        entity.SnapToTargetPending = true;
                        entity.RenderHeadingRadians = entity.HeadingRadians;
                    }
                    entity.EventPose = null;
                    entity.EventLook = null;
                    entity.EventHeadTurnSpeed = 0;
                    entity.EventTurnSpeed = 0;
                    entity.EventAlpha = WorldEntity.OpaqueEventAlpha;
                    entity.IsEventHidden = false;
                    entity.KeepsEventHeight = false;
                    entity.HidesEventName = false;
                    entity.EventRenderFlags = EventRenderFlags.None;
                    entity.EventStatus = 0;
                }
            }
            _staged.Clear();
        }

        /// <summary>
        /// The entities an event runs on (XiEvents "Event VM Functions.md", InitEvent2 and XiEventInit): every actor
        /// block that carries the event id, or the 0xFFFE catch-all, gets its own VM on its entity, all in one scene on
        /// the shared work zone. The player's block (0x7FFFFFF0) runs on the local player, the party codes on the party
        /// member in that slot (skipped when the slot is empty), any other block on the NPC with its server id. An NPC's
        /// event usually lives in its own block; a zone-in cutscene often puts a few bytes in the player's block and the
        /// scene in an NPC's (Bastok Mines event 1: 8 bytes in the player's block, 1,837 in NPC 0x010EA001's and short
        /// staging parts in 20 others; Northern San d'Oria event 878: Anilla's block directs, seven others toggle render
        /// flags). In the retail intro zones only the player's block has a catch-all, a bare 0x00. The zone's block
        /// (0x7FFFFFFF) runs the event, as its owner, only when no entity carries it. Null when nothing runs it.
        /// </summary>
        private EventScene? CreateScene(CutsceneEventInfo info)
        {
            var script = _script;
            if (script == null) return null;
            var scene = new EventScene(_zone) { PlayerServerId = _player?.ServerId ?? 0 };
            bool carried = false;
            foreach (var block in script.Blocks)
            {
                if (block.ActorId == EventBlock.ZoneActor) continue;
                bool carries = block.IndexOf(info.EventPara) >= 0;
                if (!carries && block.IndexOf(EventBlock.AnyEventId) < 0) continue;
                if (!TryResolveBlockActor(block.ActorId, info, out uint serverId, out ushort index)) continue;
                if (scene.FindActor(serverId) != null) continue; // one event object per entity
                _ = new EventVm(block, info.EventPara, scene, this, serverId, index);
                carried |= carries;
            }
            if (!carried && script.TryGetBlock(EventBlock.ZoneActor, out var zone) &&
                (zone.IndexOf(info.EventPara) >= 0 || zone.IndexOf(EventBlock.AnyEventId) >= 0))
            {
                _ = new EventVm(zone, info.EventPara, scene, this, info.UniqueNo, info.ActIndex);
                carried = zone.IndexOf(info.EventPara) >= 0;
            }
            if (!carried) GordianLog.Warning("EVENT", $"No block carries event {info.EventPara}; only catch-all events run.");
            return scene.Actors.Count > 0 ? scene : null;
        }

        /// <summary>The local player's target index, from its world entity (0 before it is known).</summary>
        private ushort PlayerIndex()
        {
            uint id = _player?.ServerId ?? 0;
            return id != 0 && _world != null && _world.TryGetByServerId(id, out var entity) ? entity.TargetIndex : (ushort)0;
        }

        /// <summary>The entity an actor block runs on (XiEvents GetActorNum); false for an empty party slot.</summary>
        private bool TryResolveBlockActor(uint actor, CutsceneEventInfo info, out uint serverId, out ushort index)
        {
            var player = _player;
            if (actor is EventBlock.PlayerActor or 0x7FFFFFC0)
            {
                // The local player; before the session knows its id, the event's owner (who is the player then).
                bool known = player != null && player.ServerId != 0;
                serverId = known ? player!.ServerId : info.UniqueNo;
                index = known ? PlayerIndex() : info.ActIndex;
                return true;
            }
            if (EventVm.TryGetPartySlot(actor, out int party, out int slot))
            {
                return TryGetPartyMember(party, slot, out serverId, out index);
            }
            serverId = actor;
            index = (ushort)(actor & 0x3FF);
            return true;
        }

        /// <summary>Loads the zone's scripts and dialog table once per zone (called from the game tick and the network thread).</summary>
        private void EnsureZoneData(int zoneId)
        {
            lock (_sync)
            {
                if (_scriptZone == zoneId) return;
                _scriptZone = zoneId;
                _script = null;
                _dialog = null;
                var loader = Loader;
                if (loader == null)
                {
                    GordianLog.Warning("EVENT", "No DAT loader is set; events cannot run.");
                    return;
                }
                try
                {
                    var scriptBytes = loader(ZoneEventScript.GetFileId(zoneId));
                    if (scriptBytes != null) _script = ZoneEventScript.Parse(scriptBytes);
                    var dialogBytes = loader(ZoneDialogTable.GetFileId(zoneId));
                    if (dialogBytes != null) _dialog = ZoneDialogTable.Parse(dialogBytes);
                    GordianLog.Info("EVENT", $"Zone {zoneId}: {_script?.Blocks.Count ?? 0} event blocks, {_dialog?.Count ?? 0} dialog messages.");
                }
                catch (Exception ex)
                {
                    GordianLog.Error("EVENT", $"Failed to load the event data of zone {zoneId}: {ex.Message}");
                }
            }
        }

        /// <summary>The zone's dialog table (loaded on demand), for the direct messages.</summary>
        private ZoneDialogTable? Dialog(int zoneId)
        {
            EnsureZoneData(zoneId);
            return _dialog;
        }

        private void FinishEvent(EventScene scene)
        {
            var info = _info;
            _scene = null;
            _info = null;
            CloseQueryMenu();
            ReleaseParticipants();
            ClearStaging();
            if (_player != null) _player.IsMovementLocked = false;
            // The end value is the shared work zone's, unless a query was cancelled.
            if (info != null) SendEnd(info, scene.EndParameter);
            Changed?.Invoke();
        }

        private void DropEvent()
        {
            _scene = null;
            _info = null;
            CloseQueryMenu();
            ReleaseParticipants();
            ClearStaging();
            if (_player != null) _player.IsMovementLocked = false;
            Changed?.Invoke();
        }

        /// <summary>
        /// Marks the entities of the event as taking part (<see cref="WorldEntity.IsInEvent"/>), so the renderer draws
        /// them even when the server hides them: the cutscene-only NPCs of an intro.
        /// </summary>
        private void MarkParticipants(EventScene scene)
        {
            var world = _world;
            if (world == null) return;
            foreach (var actor in scene.Actors)
            {
                if (world.TryGetByServerId(actor.EntityServerId, out var entity)) JoinEvent(entity);
            }
        }

        /// <summary>
        /// Marks an entity as taking part in the event. One the server hides (a cutscene-only NPC) starts the event with
        /// its event hide flag set, so it stays out of sight until the script shows it (0x4E 00 / 0x22 00): the Southern
        /// San d'Oria intro places the returning knights at its start and shows them only for their scene (the
        /// maintainer's retail recording: absent at 1:21, walking in at 1:50), with no script hiding them before; the
        /// Windurst Woods intro shows the hidden Nanaa Mihgo the same way. Retail's mapping from the server's hide state to
        /// Render.Flags0 bit 17 is inferred from these scripts.
        /// </summary>
        private void JoinEvent(WorldEntity entity)
        {
            if (entity.IsInEvent) return;
            entity.IsInEvent = true;
            _participants.Add(entity);
            if (entity.IsHidden && entity.ServerId != (_player?.ServerId ?? 0))
            {
                entity.IsEventHidden = true;
                _staged.Add(entity.ServerId);
            }
        }

        /// <summary>
        /// The event is over: its entities follow the server's state again, so the cutscene-only NPCs, which the server
        /// keeps hidden, disappear (retail restores an entity's own state when its event object is destroyed).
        /// </summary>
        private void ReleaseParticipants()
        {
            foreach (var entity in _participants) entity.IsInEvent = false;
            _participants.Clear();
        }

        private void SendEnd(CutsceneEventInfo info, uint endParameter)
        {
            GordianLog.Info("EVENT", $"Event {info.EventPara} ended with 0x{endParameter:X}.");
            _ = _module?.SendEventEndAsync(info.UniqueNo, endParameter, info.ActIndex, 0, info.EventNum, info.EventPara);
        }

        #region Messages

        private string EntityName(uint serverId, ushort index)
        {
            if (serverId == 0 || (_player != null && serverId == _player.ServerId)) return _playerName();
            if (_world != null)
            {
                if (_world.TryGetByServerId(serverId, out var entity) && !string.IsNullOrEmpty(entity.Name)) return entity.Name;
                if (index != 0 && _world.TryGetByTargetIndex(index, out entity) && !string.IsNullOrEmpty(entity.Name)) return entity.Name;
            }
            return "???";
        }

        /// <summary>The entity a line is spoken by (0 = the player), resolved as <see cref="EntityName"/> does, or null.</summary>
        private WorldEntity? SpeakingEntity(uint serverId, ushort index)
        {
            if (_world == null) return null;
            if (serverId == 0) serverId = _player?.ServerId ?? 0;
            if (serverId != 0 && _world.TryGetByServerId(serverId, out var entity)) return entity;
            return index != 0 && _world.TryGetByTargetIndex(index, out entity) ? entity : null;
        }

        private IEventMessageContext EventContext(string npcName) => new WorkZoneContext(_zone, _playerName(), npcName, PlayerIsFemale());

        /// <summary>
        /// The player's sex from its look's race byte (1/2 Hume, 3/4 Elvaan, 5/6 Tarutaru male/female, 7 Mithra, 8 Galka),
        /// or null before the look is known.
        /// </summary>
        private bool? PlayerIsFemale()
        {
            uint id = _player?.ServerId ?? 0;
            if (id == 0 || _world == null || !_world.TryGetByServerId(id, out var entity)) return null;
            return ((entity.Appearance.FaceModel >> 8) & 0xFF) switch
            {
                1 or 3 or 5 or 8 => false,
                2 or 4 or 6 or 7 => true,
                _ => null,
            };
        }

        private static string? ResolveName(byte kind, int id) => NameResolver?.Invoke(kind, id);

        /// <summary>Prints the lines of a message; the first carries the speaker's name when there is one.</summary>
        private void PrintLines(IReadOnlyList<string> lines, string? speaker, ChatLogChannel channel)
        {
            var chat = _chat;
            if (chat == null) return;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = i == 0 && !string.IsNullOrEmpty(speaker) ? $"{speaker} : {lines[i]}" : lines[i];
                chat.Log.Add(channel, line);
            }
        }

        private void OnDialogMessage(DialogMessageInfo message)
        {
            var dialog = Dialog(_world?.CurrentZoneId ?? 0);
            var decoded = dialog?.GetMessage(message.MessageId);
            if (decoded == null)
            {
                GordianLog.Warning("DIALOG", $"Zone message {message.MessageId} is not in the dialog table.");
                return;
            }
            string speaker = message.HideName ? string.Empty
                : !string.IsNullOrEmpty(message.Name) ? message.Name
                : EntityName(message.UniqueNo, message.ActIndex);
            var context = new SimpleMessageContext(message.Numbers, _playerName(), speaker, ResolveName);
            var lines = EventMessageFormatter.FormatLines(decoded, context);
            PrintLines(lines, speaker, message.HideName ? ChatLogChannel.Message : ChatLogChannel.Dialog);
        }

        #endregion

        #region IEventVmHost

        /// <summary>
        /// A prompt message stays open until the player confirms it: retail holds the character and shows a "waiting"
        /// arrow by the line (the maintainer's second recording, 2026-09-28, without the Enternity addon that had
        /// auto-confirmed the first one).
        /// </summary>
        public const double PromptOpenSeconds = double.PositiveInfinity;

        double IEventVmHost.PrintMessage(int messageId, EventSpeaker speaker, uint speakerServerId, ushort speakerIndex)
        {
            var decoded = _dialog?.GetMessage(messageId);
            if (decoded == null)
            {
                GordianLog.Warning("DIALOG", $"Event message {messageId} is not in the dialog table.");
                return 0;
            }
            string name = speaker == EventSpeaker.Entity ? EntityName(speakerServerId, speakerIndex) : string.Empty;
            var lines = EventMessageFormatter.FormatLines(decoded, EventContext(name));
            _talker = null;
            if (speaker == EventSpeaker.Entity && SpeakingEntity(speakerServerId, speakerIndex) is { } talker)
            {
                // The mouth flaps by the line's length (retail EventMessDecodePutMoute, XiEvents OpCodes/0x001D).
                int shownCharacters = 0;
                foreach (string line in lines) shownCharacters += line.Length;
                talker.Speak(FaceMotion.FlapsFor(shownCharacters));
                _talker = talker;
            }
            if (_cutsceneHud)
            {
                // The event message mode shows the line on the screen, not in the log.
                var shown = new List<string>(lines.Count);
                for (int i = 0; i < lines.Count; i++) shown.Add(i == 0 && name.Length > 0 ? $"{name} : {lines[i]}" : lines[i]);
                // The line's own position (0x02 code), else the mode's values.
                var (x, y) = decoded.Position ?? (_eventTextX, _eventTextY);
                _eventText = new EventScreenText(shown, x, y);
            }
            else
            {
                PrintLines(lines, speaker == EventSpeaker.Entity ? name : null, ChatLogChannel.Dialog);
            }
            int length = 0;
            foreach (string line in lines) length += line.Length;
            GordianLog.Debug("DIALOG", $"Event message {messageId} ({length} chars, prompt={decoded.HasPrompt}, closes after {decoded.AutoCloseSeconds?.ToString() ?? "-"} s): {(lines.Count > 0 ? lines[0] : string.Empty)}");
            // A timed message (0x7F 0x34 n) closes by itself; a prompt waits for Confirm.
            if (decoded.AutoCloseSeconds is int seconds) return seconds;
            return decoded.HasPrompt ? PromptOpenSeconds : 0;
        }

        void IEventVmHost.OpenQuery(int messageId, int defaultIndex, uint hiddenMask)
        {
            _queryResult = 0;
            var decoded = _dialog?.GetMessage(messageId);
            var menus = _menus;
            if (decoded == null || menus == null)
            {
                GordianLog.Warning("DIALOG", $"Query message {messageId} is not available; the query is cancelled.");
                _queryResult = 255;
                return;
            }
            var (comments, choices) = EventMessageFormatter.FormatQuery(decoded, EventContext(string.Empty));
            var options = new List<StockUiQueryOption>();
            int cursor = 0;
            for (int i = 0; i < choices.Count; i++)
            {
                if (((hiddenMask >> i) & 1) != 0) continue;
                if (i == defaultIndex) cursor = options.Count;
                options.Add(new StockUiQueryOption(i + 1, choices[i]));
            }
            if (options.Count == 0)
            {
                _queryResult = 255;
                return;
            }
            foreach (string comment in comments) _chat?.Log.Add(ChatLogChannel.Dialog, comment);
            _queryStart = Environment.TickCount;
            _queryMenu = menus.OpenQuery(comments, options, cursor, result => _queryResult = result);
            if (_queryMenu == null) _queryResult = 255;
        }

        int IEventVmHost.QueryResult => _queryResult;

        void IEventVmHost.CloseQuery() => CloseQueryMenu();

        private void CloseQueryMenu()
        {
            var menu = _queryMenu;
            _queryMenu = null;
            if (menu != null) _menus?.CloseQuery(menu);
        }

        void IEventVmHost.SendEventUpdate(uint endParameter)
        {
            var info = _info;
            if (info == null || _module == null) return;
            _receivePending = true;
            GordianLog.Info("EVENT", $"Event {info.EventPara} update 0x{endParameter:X}.");
            _ = _module.SendEventUpdateAsync(info.UniqueNo, endParameter, info.ActIndex, info.EventNum, info.EventPara);
        }

        void IEventVmHost.SendEventUpdateXzy(uint endParameter, float x, float y, float z, float heading)
        {
            var info = _info;
            if (info == null || _module == null) return;
            _receivePending = true;
            // The wire heading is a byte of 256 steps per turn.
            sbyte dir = unchecked((sbyte)(byte)Math.Round(heading / (2 * Math.PI) * 256) );
            GordianLog.Info("EVENT", $"Event {info.EventPara} position update 0x{endParameter:X} to ({x:F2}, {y:F2}, {z:F2}).");
            _ = _module.SendEventUpdateXzyAsync(new System.Numerics.Vector3(x, y, z), info.UniqueNo, endParameter, info.EventNum, info.EventPara, info.ActIndex, dir);
        }

        bool IEventVmHost.ReceivePending => _receivePending;

        void IEventVmHost.SetControlLock(bool locked)
        {
            // The character is held for the whole event (see the class notes); the opcode's release is honoured.
            if (!locked && _player != null) _player.IsMovementLocked = false;
        }

        bool IEventVmHost.EntityExists(uint serverId) => serverId != 0 && (_world?.TryGetByServerId(serverId, out _) ?? false);

        int IEventVmHost.GetEntityValue(uint serverId, int key)
        {
            var player = _player;
            bool isPlayer = serverId == 0 || (player != null && serverId == player.ServerId);
            switch (key)
            {
                case 0x06: return isPlayer && player != null ? (int)player.MainJob : 0;
                case 0x08: return isPlayer && player != null ? player.MainJobLevel : 1;
                case 0x0A: return unchecked((int)(isPlayer ? player?.ServerId ?? 0 : serverId));
                case 0x07:
                    if (_world != null && _world.TryGetByServerId(isPlayer ? player?.ServerId ?? 0 : serverId, out var entity))
                        return (entity.Appearance.FaceModel >> 8) & 0xFF; // the look's race byte rides above the face model
                    return 0;
                default: return 0;
            }
        }

        int IEventVmHost.GameTime => (int)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;

        bool IEventVmHost.TryGetPartyMember(int party, int slot, out uint serverId, out ushort index) =>
            TryGetPartyMember(party, slot, out serverId, out index);

        bool IEventVmHost.TryGetEntityPose(uint serverId, out System.Numerics.Vector3 position, out float heading, out float speed)
        {
            position = default;
            heading = 0;
            speed = 0;
            if (serverId == 0) serverId = _player?.ServerId ?? 0;
            if (serverId == 0 || _world == null || !_world.TryGetByServerId(serverId, out var entity)) return false;
            if (entity.EventPose is { } pose)
            {
                (position, heading) = (pose.Position, pose.Heading);
            }
            else
            {
                position = entity.Position;
                heading = entity.RenderHeadingRadians != 0f ? entity.RenderHeadingRadians : entity.HeadingRadians;
            }
            // Speed bytes are tenths of a yalm per second (50 = the 5.0 yalms/s base run).
            byte baseSpeed = entity.SpeedBase != 0 ? entity.SpeedBase : entity.Speed;
            speed = baseSpeed / 10f;
            return true;
        }

        void IEventVmHost.SetEntityPose(uint serverId, System.Numerics.Vector3 position, float heading, float speed)
        {
            if (serverId == 0) serverId = _player?.ServerId ?? 0;
            var pose = new EventPose(position, heading, speed);
            if (_world == null || !_world.TryGetByServerId(serverId, out var entity))
            {
                _pendingPoses[serverId] = pose; // applied when the entity arrives (SortArrivals)
                return;
            }
            entity.EventPose = pose;
            _staged.Add(serverId);
        }

        void IEventVmHost.SetEventMessageMode(bool on, int x, int y)
        {
            _cutsceneHud = on;
            (_eventTextX, _eventTextY) = (x, y);
            if (!on) _eventText = null;
        }

        void IEventVmHost.LockEnvironment(int hour, int weather)
        {
            if (hour >= 0)
            {
                _world?.LockTimeOfDay(hour);
                _clockLocked = true;
            }
            if (weather >= 0 && _world != null)
            {
                if (_savedWeather < 0) _savedWeather = _world.WeatherNumber;
                _world.UpdateWeather((ushort)weather);
            }
            GordianLog.Info("EVENT", $"Event environment: hour {hour}, weather {weather}.");
        }

        void IEventVmHost.UnlockEnvironment() => UnlockEnvironment();

        void IEventVmHost.OpenEventZone(int zoneId)
        {
            var world = _world;
            if (world == null) return;
            world.EventZoneId = zoneId <= 0 || zoneId == world.CurrentZoneId ? (ushort)0 : (ushort)zoneId;
            GordianLog.Info("EVENT", world.EventZoneId == 0 ? $"Event zone: back to zone {world.CurrentZoneId}." : $"Event zone: zone {zoneId} opened for the scene.");
        }

        // No viewport has drawn this session yet (0): nothing to wait for.
        bool IEventVmHost.IsEventZoneLoading =>
            _world is { } world && world.DisplayedZoneId != 0 && world.DisplayedZoneId != world.SceneZoneId;

        private void UnlockEnvironment()
        {
            if (_clockLocked)
            {
                _world?.UnlockTimeOfDay();
                _clockLocked = false;
            }
            if (_savedWeather >= 0)
            {
                _world?.UpdateWeather((ushort)_savedWeather);
                _savedWeather = -1;
            }
        }

        void IEventVmHost.SetEntityLook(uint serverId, uint targetServerId, int speechFrame)
        {
            uint own = _player?.ServerId ?? 0;
            if (serverId == 0) serverId = own;
            if (targetServerId == 0) targetServerId = own;
            if (_world == null || !_world.TryGetByServerId(serverId, out var entity)) return;
            entity.EventLook = targetServerId == uint.MaxValue || targetServerId == serverId ? null : new EventLook(targetServerId, speechFrame);
            _staged.Add(serverId);
            if (entity.EventLook != null)
            {
                var at = _world.TryGetByServerId(targetServerId, out var target) ? (target.EventPose?.Position ?? target.Position).ToString() : "not in the zone";
                GordianLog.Debug("EVENT", $"Look: 0x{serverId:X8} at 0x{targetServerId:X8} {at}.");
            }
        }

        void IEventVmHost.SetEntityLookAxis(uint serverId, int axisX, int axisY)
        {
            if (serverId == 0) serverId = _player?.ServerId ?? 0;
            if (_world == null || !_world.TryGetByServerId(serverId, out var entity)) return;
            entity.EventLook = EventLook.Fixed(axisX, axisY);
            _staged.Add(serverId);
            GordianLog.Debug("EVENT", $"Look axis: 0x{serverId:X8} ({axisX}, {axisY}).");
        }

        void IEventVmHost.SetEntityHeadTurnSpeed(uint serverId, int speed)
        {
            if (serverId == 0) serverId = _player?.ServerId ?? 0;
            if (_world == null || !_world.TryGetByServerId(serverId, out var entity)) return;
            entity.EventHeadTurnSpeed = speed;
            _staged.Add(serverId);
        }

        void IEventVmHost.SetEntityTurnSpeed(uint serverId, int speed)
        {
            if (serverId == 0) serverId = _player?.ServerId ?? 0;
            if (_world == null || !_world.TryGetByServerId(serverId, out var entity)) return;
            entity.EventTurnSpeed = speed;
            _staged.Add(serverId);
        }

        bool IEventVmHost.TryGetEntityAlpha(uint serverId, out int alpha)
        {
            if (serverId == 0) serverId = _player?.ServerId ?? 0;
            alpha = WorldEntity.OpaqueEventAlpha;
            if (_world == null || !_world.TryGetByServerId(serverId, out var entity)) return false;
            alpha = entity.EventAlpha;
            return true;
        }

        void IEventVmHost.SetEntityAlpha(uint serverId, int alpha)
        {
            if (serverId == 0) serverId = _player?.ServerId ?? 0;
            if (_world == null || !_world.TryGetByServerId(serverId, out var entity)) return;
            entity.EventAlpha = Math.Clamp(alpha, 0, 255);
            _staged.Add(serverId);
        }

        void IEventVmHost.SetEntityKeepsHeight(uint serverId, bool keep)
        {
            if (serverId == 0) serverId = _player?.ServerId ?? 0;
            if (_world == null || !_world.TryGetByServerId(serverId, out var entity))
            {
                _pendingKeepHeight[serverId] = keep; // the marker of Port Jeuno 324 arrives after the script sets it
                return;
            }
            entity.KeepsEventHeight = keep;
            _staged.Add(serverId);
        }

        void IEventVmHost.SetEntityHidesName(uint serverId, bool hide)
        {
            if (serverId == 0) serverId = _player?.ServerId ?? 0;
            if (_world == null || !_world.TryGetByServerId(serverId, out var entity))
            {
                _pendingHidesName[serverId] = hide;
                return;
            }
            entity.HidesEventName = hide;
            _staged.Add(serverId);
        }

        /// <summary>The player closed the open line: its speaker's mouth stops (retail 0x23, SpeakStop).</summary>
        private void StopTalker()
        {
            _talker?.StopSpeaking();
            _talker = null;
        }

        void IEventVmHost.SetEntityRenderFlag(uint serverId, EventRenderFlags flag, bool set)
        {
            if (serverId == 0) serverId = _player?.ServerId ?? 0;
            if (_world == null || !_world.TryGetByServerId(serverId, out var entity))
            {
                _pendingRenderFlags.TryGetValue(serverId, out var pending);
                _pendingRenderFlags[serverId] = set ? (pending.Set | flag, pending.Clear & ~flag) : (pending.Set & ~flag, pending.Clear | flag);
                return;
            }
            entity.EventRenderFlags = set ? entity.EventRenderFlags | flag : entity.EventRenderFlags & ~flag;
            _staged.Add(serverId);
        }

        bool IEventVmHost.TryGetEntityRenderFlags(uint serverId, out EventRenderFlags flags)
        {
            if (serverId == 0) serverId = _player?.ServerId ?? 0;
            flags = EventRenderFlags.None;
            if (_world == null || !_world.TryGetByServerId(serverId, out var entity)) return false;
            flags = entity.EventRenderFlags;
            return true;
        }

        void IEventVmHost.SetEntityEventStatus(uint serverId, byte status)
        {
            if (serverId == 0) serverId = _player?.ServerId ?? 0;
            if (_world == null || !_world.TryGetByServerId(serverId, out var entity)) return; // retail skips a missing entity too
            entity.EventStatus = status;
            _staged.Add(serverId);
        }

        void IEventVmHost.SetEntityHidden(uint serverId, bool hidden)
        {
            if (serverId == 0) serverId = _player?.ServerId ?? 0;
            if (_world == null || !_world.TryGetByServerId(serverId, out var entity))
            {
                _pendingHidden[serverId] = hidden;
                return;
            }
            entity.IsEventHidden = hidden;
            _staged.Add(serverId);
        }

        /// <summary>
        /// A party slot's member: in the player's own party slot 0 is the player and slots 1-5 the other members in
        /// slot order; the alliance's other parties count from 0.
        /// </summary>
        private bool TryGetPartyMember(int party, int slot, out uint serverId, out ushort index)
        {
            serverId = 0;
            index = 0;
            var state = _party;
            var player = _player;
            if (party < 0 || party > 2 || slot < 0) return false;
            uint self = player?.ServerId ?? 0;
            if (party == 0 && slot == 0)
            {
                serverId = self;
                index = PlayerIndex();
                return self != 0;
            }
            if (state == null) return false;
            var members = state.GetPartyMembers((byte)party);
            int n = party == 0 ? 0 : -1;
            foreach (var member in members)
            {
                if (party == 0 && member.ServerId == self) continue;
                if (++n == slot)
                {
                    serverId = member.ServerId;
                    index = member.TargetIndex;
                    return true;
                }
            }
            return false;
        }

        int IEventVmHost.StartSceneTask(int taskId, int fileId, string routine, uint casterServerId, uint targetServerId)
        {
            var resource = LoadSceneResource(fileId);
            if (resource == null || !resource.TryGetRoutine(routine, out var scene))
            {
                GordianLog.Debug("EVENT", $"Scene task {routine} of file {fileId}: {(resource == null ? "no such file" : "no such routine")}; it ends at once.");
                return 0;
            }
            // Actor-relative camera routes are placed at the first actor, where the event shows it.
            // Effects follow the actor where it is drawn; the pose here stands in for an actor that is not drawn.
            var origin = System.Numerics.Vector3.Zero;
            float heading = 0f;
            if (_scene?.FindActor(casterServerId) is { } actor) (origin, heading) = actor.EventPosition;
            else if (((IEventVmHost)this).TryGetEntityPose(casterServerId, out var position, out var poseHeading, out _)) (origin, heading) = (position, poseHeading);
            if (casterServerId == 0) casterServerId = _player?.ServerId ?? 0;
            Presentation.Play(taskId, resource, scene, origin, fileId, casterServerId, targetServerId, heading);
            return scene.TotalFrames;
        }

        void IEventVmHost.StopSceneTask(int taskId) => Presentation.Stop(taskId);

        void IEventVmHost.SetEventCamera(bool held) => Presentation.SetCameraHeld(held);

        int IEventVmHost.PlayEntityMotion(uint serverId, EventMotionSource source, int resource, string routine, uint targetServerId)
        {
            if (serverId == 0) serverId = _player?.ServerId ?? 0;
            if (_world == null || !_world.TryGetByServerId(serverId, out var entity)) return 0;
            if (source == EventMotionSource.Bank)
            {
                if (LoadMotionBank(resource) is { } bank) entity.Animation.AddEventMotionBank(bank);
            }
            else if (source == EventMotionSource.Package)
            {
                if (LoadMotionPackage(resource) is { } package) entity.Animation.AddEventMotionBank(package);
            }
            if (!_banked.Contains(entity)) _banked.Add(entity);
            entity.Animation.EnqueueAction(new ActionRequest
            {
                ActorId = serverId,
                Motion = ActionMotion.EventMotion,
                Routine = routine,
                ReceivedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
            });
            return entity.Animation.GetRoutineFrames(routine);
        }

        int IEventVmHost.PlayEntityEmote(uint serverId, int emote, int variant)
        {
            if (serverId == 0) serverId = _player?.ServerId ?? 0;
            if (_world == null || !_world.TryGetByServerId(serverId, out var entity)) return 0;
            // Emotes are the player races' motions: an entity with a race look plays its race's (fixed models have none).
            var race = (CharacterRace)((entity.Appearance.FaceModel >> 8) & 0xFF);
            if (entity.Appearance.ModelId > 0 || race == CharacterRace.Unknown) return 0;
            if (!_emoteBanks.TryGetValue((race, emote, variant), out var bank))
            {
                try
                {
                    bank = DatPathLoader == null ? null : EmoteMotion.LoadBank(race, emote, variant, DatPathLoader);
                }
                catch (Exception ex)
                {
                    GordianLog.Warning("EVENT", $"Emote {emote} of {race} could not be read: {ex.Message}");
                }
                _emoteBanks[(race, emote, variant)] = bank;
                if (bank == null) GordianLog.Debug("EVENT", $"Emote {emote} (variant {variant}) has no motion for {race}.");
            }
            if (bank == null) return 0;
            entity.Animation.AddEventMotionBank(bank);
            if (!_banked.Contains(entity)) _banked.Add(entity);
            string routine = EmoteMotion.RoutineName(emote);
            entity.Animation.EnqueueAction(new ActionRequest
            {
                ActorId = serverId,
                Motion = ActionMotion.EventMotion,
                Routine = routine,
                ReceivedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
            });
            return bank.GetRoutineFrames(routine);
        }

        void IEventVmHost.StopEntityMotion(uint serverId, string routine)
        {
            if (serverId == 0) serverId = _player?.ServerId ?? 0;
            if (_world == null || !_world.TryGetByServerId(serverId, out var entity)) return;
            entity.Animation.EnqueueAction(new ActionRequest
            {
                ActorId = serverId,
                Motion = ActionMotion.EventMotionStop,
                Routine = routine,
                ReceivedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
            });
        }

        private EventSceneResource? LoadSceneResource(int fileId)
        {
            if (_sceneResources.TryGetValue(fileId, out var cached)) return cached;
            EventSceneResource? resource = null;
            try
            {
                if (Loader?.Invoke(fileId) is { } bytes) resource = EventSceneResource.Parse(bytes);
            }
            catch (Exception ex)
            {
                GordianLog.Warning("EVENT", $"Scene resource {fileId} could not be read: {ex.Message}");
            }
            _sceneResources[fileId] = resource;
            return resource;
        }

        private EventMotionBank? LoadMotionBank(int fileId)
        {
            if (_motionBanks.TryGetValue(fileId, out var cached)) return cached;
            EventMotionBank? bank = null;
            try
            {
                if (Loader?.Invoke(fileId) is { } bytes) bank = EventMotionBank.Parse(bytes, fileId);
            }
            catch (Exception ex)
            {
                GordianLog.Warning("EVENT", $"Event motion DAT {fileId} could not be read: {ex.Message}");
            }
            _motionBanks[fileId] = bank;
            return bank;
        }

        /// <summary>
        /// A 0x66 motion package: the first of its DATs that has routines (<see cref="EventMotionBank.PackageFiles"/>: with
        /// the waist part, else without it, for 0-69; the three race sets tables for 70-279; the 0x5B bank file from 280).
        /// </summary>
        private EventMotionBank? LoadMotionPackage(int package)
        {
            var files = EventMotionBank.PackageFiles(package);
            foreach (int fileId in files)
            {
                if (LoadMotionBank(fileId) is { } bank) return bank;
            }
            GordianLog.Debug("EVENT", files.Length == 0
                ? $"Motion package {package} is not located."
                : $"Motion package {package} has no routines in files {string.Join(" / ", files)}.");
            return null;
        }

        void IEventVmHost.OnSkippedOpcode(byte opcode, int pc)
        {
            if (opcode == 0xFF) GordianLog.Warning("EVENT", $"Event step limit hit at {pc}; the event is ended.");
            else GordianLog.Debug("EVENT", $"Skipped opcode 0x{opcode:X2} at {pc}.");
        }

        #endregion

        /// <summary>
        /// Message numbers of an event (<see cref="EventWorkZone.GetMessageParameter"/>): the server parameter slots,
        /// then the 1700 block. Checked on the home point script against the maintainer's recording and capture
        /// (2026-09-28): the script clears the slots, writes the zone list it offers into them through its table of
        /// work references, the region list's parameter 1 reads 0 there ("San d'Oria", as retail), and the zone name
        /// of the home point list is parameter 33.
        /// </summary>
        private sealed class WorkZoneContext : IEventMessageContext
        {
            private readonly EventWorkZone _zone;

            public WorkZoneContext(EventWorkZone zone, string playerName, string npcName, bool? playerIsFemale = null)
            {
                _zone = zone;
                PlayerName = playerName;
                NpcName = npcName;
                PlayerIsFemale = playerIsFemale;
            }

            public string PlayerName { get; }
            public string NpcName { get; }
            public bool? PlayerIsFemale { get; }

            public int GetNumber(int index) => _zone.GetMessageParameter(index);

            public string? GetEntityName(int index) => null;

            public string? ResolveName(byte kind, int id) => EventDialogController.ResolveName(kind, id);
        }
    }
}
