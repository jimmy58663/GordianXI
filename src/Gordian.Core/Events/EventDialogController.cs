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
        private CutsceneEventInfo? _pendingStart;
        private CutsceneEventInfo? _info;
        private EventVm? _vm;
        private volatile bool _receivePending;
        private volatile bool _cancelRequested;
        private volatile bool _zoneChanged;
        private volatile int _queryResult;
        private StockUiOpenMenu? _queryMenu;
        private int _queryStart;

        /// <summary>Whether an event is running (the character is held and Confirm belongs to the dialog).</summary>
        public bool IsActive => _vm != null;

        /// <summary>Whether the running event shows a line the player must confirm.</summary>
        public bool IsWaitingForConfirm => _vm?.IsWaitingForConfirm == true;

        /// <summary>Raised when the dialog state changed (an event started or ended, a line waits).</summary>
        public event Action? Changed;

        /// <summary>Subscribes to a session's event and message sources.</summary>
        public void Attach(ProgressionState progression, ProgressionPacketModule module, WorldState world, LocalPlayerState player,
            StockUiChat chat, StockUiMenuController menus, Func<string> playerName)
        {
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
                DropEvent();
                _script = null;
                _dialog = null;
                _scriptZone = -1;
                _zone.Clear();
            }
            if (_cancelRequested)
            {
                _cancelRequested = false;
                if (_vm != null)
                {
                    GordianLog.Info("EVENT", $"Event {_vm.EventId} cancelled by the server.");
                    DropEvent();
                }
            }
            if (start != null) StartEvent(start);
            var vm = _vm;
            if (vm == null) return;
            bool wasWaiting = vm.IsWaitingForConfirm;
            vm.Tick(elapsed);
            if (vm.IsFinished) FinishEvent(vm);
            else if (wasWaiting != vm.IsWaitingForConfirm) Changed?.Invoke();
        }

        /// <summary>
        /// Reads the dialog's input: Confirm (or Cancel) dismisses a waiting line. Returns true while an event runs
        /// and no menu is open, so the action keys stay with the dialog.
        /// </summary>
        public bool ProcessInput(InputState input)
        {
            var vm = _vm;
            if (vm == null) return false;
            if (vm.IsWaitingForConfirm && (input.WasActionTriggered(InputAction.Confirm) || input.WasActionTriggered(InputAction.Cancel)))
            {
                vm.Confirm();
                Changed?.Invoke();
            }
            return true;
        }

        private void StartEvent(CutsceneEventInfo info)
        {
            if (_vm != null)
            {
                GordianLog.Warning("EVENT", $"Event {info.EventPara} started while event {_vm.EventId} runs; the running one is dropped.");
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

            EventBlock? block = null;
            if (_script != null)
            {
                if (!_script.TryGetBlock(info.UniqueNo, out block) || block.IndexOf(info.EventPara) < 0 && block.IndexOf(EventBlock.AnyEventId) < 0)
                {
                    block = _script.FindEvent(info.EventPara);
                    if (block == null && _script.TryGetBlock(EventBlock.PlayerActor, out var player)) block = player;
                    if (block == null && _script.TryGetBlock(EventBlock.ZoneActor, out var zone)) block = zone;
                }
            }
            if (block == null)
            {
                GordianLog.Warning("EVENT", $"No script for event {info.EventPara} of actor 0x{info.UniqueNo:X8} in zone {zoneId}; ending it.");
                SendEnd(info, 0);
                return;
            }

            var vm = new EventVm(block, info.EventPara, _zone, this, info.UniqueNo, info.ActIndex);
            if (vm.IsFinished)
            {
                GordianLog.Warning("EVENT", $"Block 0x{block.ActorId:X8} has no event {info.EventPara}; ending it.");
                SendEnd(info, 0);
                return;
            }
            GordianLog.Info("EVENT", $"Running event {info.EventPara} of actor 0x{info.UniqueNo:X8} (block 0x{block.ActorId:X8}).");
            _vm = vm;
            _receivePending = false;
            if (_player != null) _player.IsMovementLocked = true;
            Changed?.Invoke();
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

        private void FinishEvent(EventVm vm)
        {
            var info = _info;
            _vm = null;
            _info = null;
            CloseQueryMenu();
            if (_player != null) _player.IsMovementLocked = false;
            if (info != null) SendEnd(info, vm.EndParameter);
            Changed?.Invoke();
        }

        private void DropEvent()
        {
            _vm = null;
            _info = null;
            CloseQueryMenu();
            if (_player != null) _player.IsMovementLocked = false;
            Changed?.Invoke();
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

        private IEventMessageContext EventContext(string npcName) => new WorkZoneContext(_zone, _playerName(), npcName, this);

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

        void IEventVmHost.OnSkippedOpcode(byte opcode, int pc)
        {
            if (opcode == 0xFF) GordianLog.Warning("EVENT", $"Event step limit hit at {pc}; the event is ended.");
            else GordianLog.Debug("EVENT", $"Skipped opcode 0x{opcode:X2} at {pc}.");
        }

        #endregion

        /// <summary>Message numbers of an event: the zone work values after the selection and end parameter.</summary>
        private sealed class WorkZoneContext : IEventMessageContext
        {
            private readonly EventWorkZone _zone;
            private readonly EventDialogController _owner;

            public WorkZoneContext(EventWorkZone zone, string playerName, string npcName, EventDialogController owner)
            {
                _zone = zone;
                _owner = owner;
                PlayerName = playerName;
                NpcName = npcName;
            }

            public string PlayerName { get; }
            public string NpcName { get; }

            public int GetNumber(int index)
            {
                int slot = EventWorkZone.ParameterBase + index;
                return slot >= 0 && slot < _zone.Zone.Length ? _zone.Zone[slot] : 0;
            }

            public string? GetEntityName(int index) => null;

            public string? ResolveName(byte kind, int id) => EventDialogController.ResolveName(kind, id);
        }
    }
}
