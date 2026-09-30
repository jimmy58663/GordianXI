// src/Gordian.Core/Events/EventDialogController.cs
using System;
using System.Collections.Generic;
using System.Threading;
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

        /// <summary>Names an item / key item / zone by id for the 0x01 codes of the text; null names show as &lt;#id&gt;.</summary>
        public static Func<byte, int, string?>? NameResolver { get; set; }

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
        /// Whether the running event has the HUD in its cutscene mode (opcode 0x67 until 0x68 or the end): the HUD then
        /// leaves out the name plates, the target cursor and window, the alliance windows and the status icons.
        /// </summary>
        public bool IsCutsceneHud => _cutsceneHud;
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
                if (MissingEntities(_waitingStart, request: false) == 0 || _waitingSeconds >= EntityWaitSeconds)
                {
                    var waiting = _waitingStart;
                    _waitingStart = null;
                    StartEvent(waiting);
                }
            }
            var scene = _scene;
            if (scene == null) return;
            bool wasWaiting = scene.IsWaitingForConfirm;
            if (!scene.IsFinished) scene.Tick(elapsed);
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
            GordianLog.Info("EVENT", $"Event {info.EventPara} waits for {missing} entities it names.");
            _waitingStart = info;
            _waitingSeconds = 0;
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
            var world = _world;
            if (world == null || (flags & (CutsceneFlags.NoPcs | CutsceneFlags.NoNpcs)) == 0) return;
            uint self = _player?.ServerId ?? 0;
            int hidden = 0;
            foreach (var entity in world.GetAllEntities())
            {
                if (entity.ServerId == self || scene.FindActor(entity.ServerId) != null) continue;
                bool isPlayer = entity.Type == EntityType.Player;
                if (isPlayer ? (flags & CutsceneFlags.NoPcs) == 0 : (flags & CutsceneFlags.NoNpcs) == 0) continue;
                entity.IsEventHidden = true;
                _staged.Add(entity.ServerId);
                hidden++;
            }
            GordianLog.Info("EVENT", $"Cutscene flags 0x{(uint)flags:X}: {hidden} entities outside the event hidden.");
        }

        /// <summary>
        /// Gives every entity the event placed or hid back to the world (retail's ~XiEvent resets the event state), ends
        /// the HUD's cutscene mode and lets the clock and weather go if the script did not.
        /// </summary>
        private void ClearStaging()
        {
            _cutsceneHud = false;
            UnlockEnvironment();
            var world = _world;
            if (world != null)
            {
                foreach (uint id in _staged)
                {
                    if (!world.TryGetByServerId(id, out var entity)) continue;
                    entity.EventPose = null;
                    entity.IsEventHidden = false;
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
                var loader = DatLoader;
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
                if (!world.TryGetByServerId(actor.EntityServerId, out var entity)) continue;
                entity.IsInEvent = true;
                _participants.Add(entity);
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

        private IEventMessageContext EventContext(string npcName) => new WorkZoneContext(_zone, _playerName(), npcName);

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
            PrintLines(lines, speaker == EventSpeaker.Entity ? name : null, ChatLogChannel.Dialog);
            int length = 0;
            foreach (string line in lines) length += line.Length;
            GordianLog.Debug("DIALOG", $"Event message {messageId} ({length} chars, prompt={decoded.HasPrompt}): {(lines.Count > 0 ? lines[0] : string.Empty)}");
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
            if (_world == null || !_world.TryGetByServerId(serverId, out var entity)) return;
            entity.EventPose = new EventPose(position, heading, speed);
            _staged.Add(serverId);
        }

        void IEventVmHost.SetCutsceneHud(bool on) => _cutsceneHud = on;

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

        void IEventVmHost.SetEntityHidden(uint serverId, bool hidden)
        {
            if (serverId == 0) serverId = _player?.ServerId ?? 0;
            if (_world == null || !_world.TryGetByServerId(serverId, out var entity)) return;
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

            public WorkZoneContext(EventWorkZone zone, string playerName, string npcName)
            {
                _zone = zone;
                PlayerName = playerName;
                NpcName = npcName;
            }

            public string PlayerName { get; }
            public string NpcName { get; }

            public int GetNumber(int index) => _zone.GetMessageParameter(index);

            public string? GetEntityName(int index) => null;

            public string? ResolveName(byte kind, int id) => EventDialogController.ResolveName(kind, id);
        }
    }
}
