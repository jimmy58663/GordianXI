// src/Gordian.Core/Actions/PlayerActionService.cs
// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Config;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Network.Search;
using Gordian.Core.World;
using Gordian.Core.World.Collision;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;

namespace Gordian.Core.Actions
{
    public enum PlayerActionResultKind
    {
        Success,
        Notice,
        Warning,
        Error
    }

    /// <summary>
    /// Encapsulates the outcome and user-facing feedback of a player command or action.
    /// </summary>
    public sealed class PlayerActionResult
    {
        public bool Success { get; init; }
        public PlayerActionResultKind Kind { get; init; }
        public string Message { get; init; } = string.Empty;
        public ChatCommandResultKind CommandKind { get; init; }

        public static PlayerActionResult Ok(string message, ChatCommandResultKind cmdKind = ChatCommandResultKind.LocalNotice)
            => new PlayerActionResult { Success = true, Kind = PlayerActionResultKind.Success, Message = message, CommandKind = cmdKind };

        public static PlayerActionResult Warn(string message, ChatCommandResultKind cmdKind = ChatCommandResultKind.LocalNotice)
            => new PlayerActionResult { Success = false, Kind = PlayerActionResultKind.Warning, Message = message, CommandKind = cmdKind };

        public static PlayerActionResult Fail(string message, ChatCommandResultKind cmdKind = ChatCommandResultKind.LocalNotice)
            => new PlayerActionResult { Success = false, Kind = PlayerActionResultKind.Error, Message = message, CommandKind = cmdKind };

        public static PlayerActionResult Info(string message, ChatCommandResultKind cmdKind = ChatCommandResultKind.LocalNotice)
            => new PlayerActionResult { Success = true, Kind = PlayerActionResultKind.Notice, Message = message, CommandKind = cmdKind };
    }

    /// <summary>
    /// Centralized action coordinator and intent pipeline for character actions,
    /// combat initiation, spell casting, abilities, locomotion, and command execution.
    /// Enforces <see cref="FeatureRestrictions"/> and acts as the unified bridge
    /// for UI input, CLI commands, and automated gambit execution.
    /// </summary>
    public sealed class PlayerActionService
    {
        private readonly SessionProfile _profile;
        private readonly WorldState _world;
        private readonly LocalPlayerState _localPlayer;
        private readonly CombatPacketModule _combatModule;
        private readonly ChatPacketModule _chatModule;
        private readonly PartyPacketModule _partyModule;
        private readonly EntityPacketModule _entityModule;
        private readonly LifecyclePacketModule _lifecycleModule;
        private readonly Func<ReadOnlyMemory<byte>, bool, Task> _sendChunkCallback;

        public WorldEntity? CurrentTarget { get; private set; }

        /// <summary>
        /// <see cref="System.Diagnostics.Stopwatch"/> timestamp of the last change to a new target (0 before any), which
        /// starts the target's selection flash; changing target again restarts it on the new one.
        /// </summary>
        public long TargetSelectedTimestamp { get; private set; }
        public bool IsLockedOn { get; private set; }
        public CombatState? Combat => _combatModule?.State;
        public event Action<WorldEntity?>? TargetChanged;
        public event Action<bool>? LockOnChanged;
        public event Action<Vector3, byte>? LocalPlayerMoved;

        public void ToggleLockOn()
        {
            if (CurrentTarget == null && Combat is { IsEngaged: true, TargetServerId: not 0 } engaged
                && _world.TryGetByServerId(engaged.TargetServerId, out var engagedTarget) && engagedTarget != null)
            {
                SetTarget(engagedTarget); // engaged by the server with nothing selected: lock on to the fight
            }

            if (CurrentTarget != null)
            {
                SetLockOn(!IsLockedOn);
            }
            else
            {
                SetLockOn(false);
            }
        }

        public void SetLockOn(bool locked)
        {
            if (IsLockedOn != locked)
            {
                IsLockedOn = locked;
                if (Combat != null)
                {
                    Combat.IsLockedOn = locked;
                }
                LockOnChanged?.Invoke(IsLockedOn);
            }
        }

        public WorldState World => _world;
        public LocalPlayerState LocalPlayer => _localPlayer;
        public SessionProfile Profile => _profile;

        /// <summary>
        /// The session's collision toggles (<c>/collision</c>), read by the locomotion controller.
        /// </summary>
        public CollisionSettings Collision { get; } = new();

        /// <summary>
        /// The session's knockback option (<c>/anchor</c>), read when a knockback lands on the player.
        /// </summary>
        public KnockbackSettings Knockback { get; } = new();

        /// <summary>
        /// The character's stock UI layout, edited by <c>/uilayout</c> and read by the HUD.
        /// </summary>
        public StockUiLayout UiLayout { get; set; } = new();

        /// <summary>
        /// The opt-in unlocked stock UI: window regions the HUD registers each frame and the mouse drag that moves
        /// them (<c>/uilayout unlock</c>); locked at every launch.
        /// </summary>
        public StockUiDragController UiDrag { get; } = new();

        /// <summary>The mouse pointer over the viewport (the stock UI draws its hover pointer there over menu entries).</summary>
        public StockUiPointer UiPointer { get; } = new();

        /// <summary>
        /// The stock menu system (main menu, sub-menus, yes/no prompts), fed by the locomotion controller's input
        /// tick and drawn by the HUD.
        /// </summary>
        public StockUiMenuController Menus { get; }

        /// <summary>The dead character's home point window and its Raise / Tractor prompts (#103).</summary>
        public StockUiDeathMenu DeathMenu { get; }

        /// <summary>
        /// Handles the debug audio commands <c>/playsound</c> (false) and <c>/playmusic</c> (true) with their arguments and
        /// returns the reply; set by the client's audio engine (null: no audio).
        /// </summary>
        public Func<bool, string, string>? DebugAudioCommand { get; set; }

        private StockUiSettings _uiSettings = new();
        private ConfigPacketModule? _configModule;

        /// <summary>
        /// The character's stock config-menu settings (<see cref="StockUiSettings"/>), shown and edited by the config
        /// pages. Server-scoped settings (auto-target, character information, chat filters) are mirrored from the
        /// server's 0x0B4 and sent back through <see cref="ConfigModule"/> when changed.
        /// </summary>
        public StockUiSettings UiSettings
        {
            get => _uiSettings;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                if (ReferenceEquals(_uiSettings, value)) return;
                _uiSettings.Changed -= OnUiSettingChanged;
                _uiSettings.ChatFiltersChanged -= OnChatFiltersChanged;
                _uiSettings = value;
                _uiSettings.Changed += OnUiSettingChanged;
                _uiSettings.ChatFiltersChanged += OnChatFiltersChanged;
                Menus.Settings = value;
                SyncUiSettingsFromServer();
            }
        }

        /// <summary>The inventory packet module (equipment, style lock, Auction House); null in sessions without one.</summary>
        public InventoryPacketModule? InventoryModule { get; set; }

        /// <summary>The progression packet module (events, key items, Mog House, Unity); null in sessions without one.</summary>
        public ProgressionPacketModule? ProgressionModule { get; set; }
        public TreasurePacketModule? TreasureModule { get; set; }
        public CraftingPacketModule? CraftingModule { get; set; }

        /// <summary>The social packet module (<c>/itemsearch</c>, <c>/blacklist</c>, delivery box, linkshell items); null in sessions without one.</summary>
        public SocialPacketModule? SocialModule { get; set; }

        /// <summary>The search (cache) server service (<c>/sea</c>); null in sessions without one.</summary>
        public Gordian.Core.Network.Search.SearchService? SearchService { get; set; }

        /// <summary>The everyday command packet module (<c>/heal</c>, <c>/sit</c>, <c>/random</c>, votes, wide scan); null in sessions without one.</summary>
        public PlayerCommandPacketModule? CommandModule { get; set; }

        /// <summary>The login-time data module (<c>/jobmasterdisp</c>, Blue Mage and automaton changes); null in sessions without one.</summary>
        public LoginDataPacketModule? LoginDataModule { get; set; }

        /// <summary>The configuration packet module (S2C 0x0B4, C2S 0x0DB / 0x0DC); null in sessions without one.</summary>
        public ConfigPacketModule? ConfigModule
        {
            get => _configModule;
            set
            {
                if (ReferenceEquals(_configModule, value)) return;
                if (_configModule != null) _configModule.State.Changed -= SyncUiSettingsFromServer;
                _configModule = value;
                if (value != null)
                {
                    value.State.Changed += SyncUiSettingsFromServer;
                    SyncUiSettingsFromServer();
                }
            }
        }

        private void SyncUiSettingsFromServer()
        {
            var state = _configModule?.State;
            if (state is not { Received: true }) return;
            _uiSettings.ApplyServer(
                state.IsSet(PlayerConfigFlags.AutoTargetOff),
                state.IsSet(PlayerConfigFlags.Anonymity),
                state.SystemMessageFilterLevel,
                state.MessageFilter1,
                state.MessageFilter2);
        }

        private void OnUiSettingChanged(StockUiSettingKey key, int value)
        {
            switch (key)
            {
                case StockUiSettingKey.AutoTarget:
                    _ = SendConfigFlagAsync(PlayerConfigFlags.AutoTargetOff, value == 0);
                    break;
                case StockUiSettingKey.CharacterInfoHidden:
                    _ = SendConfigFlagAsync(PlayerConfigFlags.Anonymity, value != 0);
                    break;
                case StockUiSettingKey.SystemMessageFilterLevel:
                    _ = SendSystemMessageFilterLevelAsync(value);
                    break;
            }
        }

        private void OnChatFiltersChanged(uint messageFilter1, uint messageFilter2) => _ = SendChatFiltersAsync(messageFilter1, messageFilter2);

        private async Task SendConfigFlagAsync(PlayerConfigFlags flag, bool on)
        {
            var module = _configModule;
            if (module == null) return;
            try
            {
                await module.SetFlagAsync(flag, on).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                GordianLog.Warning("UI", $"Config flag {flag} could not be sent: {ex.Message}");
            }
        }

        private async Task SendSystemMessageFilterLevelAsync(int level)
        {
            var module = _configModule;
            if (module == null) return;
            try
            {
                await module.SetSystemMessageFilterLevelAsync(level).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                GordianLog.Warning("UI", $"System message filter level could not be sent: {ex.Message}");
            }
        }

        private async Task SendChatFiltersAsync(uint messageFilter1, uint messageFilter2)
        {
            var module = _configModule;
            if (module == null) return;
            try
            {
                await module.SetChatFiltersAsync(messageFilter1, messageFilter2).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                GordianLog.Warning("UI", $"Chat filters could not be sent: {ex.Message}");
            }
        }

        public PlayerActionService(
            SessionProfile profile,
            WorldState world,
            LocalPlayerState localPlayer,
            CombatPacketModule combatModule,
            ChatPacketModule chatModule,
            PartyPacketModule partyModule,
            EntityPacketModule entityModule,
            LifecyclePacketModule lifecycleModule,
            Func<ReadOnlyMemory<byte>, bool, Task> sendChunkCallback)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _localPlayer = localPlayer ?? throw new ArgumentNullException(nameof(localPlayer));
            _combatModule = combatModule ?? throw new ArgumentNullException(nameof(combatModule));
            _chatModule = chatModule ?? throw new ArgumentNullException(nameof(chatModule));
            _partyModule = partyModule ?? throw new ArgumentNullException(nameof(partyModule));
            _entityModule = entityModule ?? throw new ArgumentNullException(nameof(entityModule));
            _lifecycleModule = lifecycleModule ?? throw new ArgumentNullException(nameof(lifecycleModule));
            _sendChunkCallback = sendChunkCallback ?? throw new ArgumentNullException(nameof(sendChunkCallback));

            Menus = new StockUiMenuController
            {
                LogoutRequested = shutdown => _lifecycleModule.RequestLogoutAsync(
                    shutdown ? ReqLogoutMode.ShutdownOn : ReqLogoutMode.LogoutOn,
                    shutdown ? ReqLogoutKind.Shutdown : ReqLogoutKind.Logout),
                CurrentWindowSkin = () => UiLayout.WindowSkin,
                WindowSkinSelected = skin => UiLayout.SetWindowSkin(skin),
                CurrentPartyIcons = () => UiLayout.ShowPartyStatusIcons,
                PartyIconsSelected = on => UiLayout.SetShowPartyStatusIcons(on),
                TargetCommand = RunMenuTargetCommandAsync,
                Clock = _world.Clock,
            };
            Menus.Settings = _uiSettings;
            DeathMenu = new StockUiDeathMenu(Menus, _localPlayer, HomePointAsync, AnswerDeathOfferAsync);
            _uiSettings.Changed += OnUiSettingChanged;
            _uiSettings.ChatFiltersChanged += OnChatFiltersChanged;

            _localPlayer.ServerStatusChanged += OnLocalServerStatusChanged;
            _world.EntityUpdated += OnEntityUpdated;
            _world.EntityDespawned += OnEntityDespawned;
            _combatModule.State.EngagementChanged += OnEngagementChanged;
            _combatModule.State.AssistTargetReceived += OnAssistTargetReceived;
        }

        /// <summary>
        /// The server picked the character's target (S2C 0x058: the <c>/assist</c> answer, or a new battle target): the
        /// cursor moves to it, as XiPackets describes the client's handler. PROVISIONAL: whether retail also locks on is
        /// not checked (LandSandBoat's comments say "lock on to the new target"); the lock-on state is left as it is. An
        /// id of 0 (no target) or an entity not in the zone changes nothing.
        /// </summary>
        private void OnAssistTargetReceived(uint serverId)
        {
            if (serverId == 0 || serverId == _localPlayer.ServerId) return;
            if (_world.TryGetByServerId(serverId, out var entity) && entity != null) SetTarget(entity);
        }

        /// <summary>
        /// Engaging locks on when the player enabled <see cref="StockUiSettingKey.AutoLockOnEngage"/> (#137). PROVISIONAL:
        /// retail has no confirmed auto-lock option; the turn toward the target is gradual (never on the engage frame).
        /// </summary>
        private void OnEngagementChanged()
        {
            var combat = Combat;
            if (combat is not { IsEngaged: true } || !_uiSettings.IsOn(StockUiSettingKey.AutoLockOnEngage)) return;
            if (CurrentTarget == null && _world.TryGetByServerId(combat.TargetServerId, out var fought) && fought != null) SetTarget(fought);
            if (CurrentTarget != null) SetLockOn(true);
        }

        #region Engagement End

        // LandSandBoat xi.animation status values (data/enums/animation.yaml).
        private const byte StatusEngaged = 1;
        private const byte StatusDespawning = 2;
        private const byte StatusDead = 3;

        /// <summary>
        /// The server ended the character's battle status (S2C 0x037 leaving "engaged", e.g. after the target died): the
        /// weapon goes away (the renderer plays the sheathe) and the engaged target is dropped, as the legacy client does.
        /// </summary>
        private void OnLocalServerStatusChanged(byte previous, byte current)
        {
            if (previous == StatusEngaged && current != StatusEngaged) EndEngagement();
        }

        /// <summary>The engaged target died: end the engagement and drop it as the target.</summary>
        private void OnEntityUpdated(WorldEntity entity)
        {
            if (entity.Type == EntityType.Player && entity.ServerId == _localPlayer.ServerId) return;
            if (entity.Hpp == 0 || entity.AnimationState is StatusDead or StatusDespawning)
            {
                if (Combat is { IsEngaged: true } combat && combat.TargetServerId == entity.ServerId) EndEngagement();
            }
        }

        /// <summary>The engaged or selected target despawned: end the engagement and drop the target.</summary>
        private void OnEntityDespawned(WorldEntity entity)
        {
            if (Combat is { IsEngaged: true } combat && combat.TargetServerId == entity.ServerId)
            {
                EndEngagement();
            }
            else if (CurrentTarget != null && CurrentTarget.ServerId == entity.ServerId)
            {
                ClearTarget();
            }
        }

        /// <summary>
        /// Ends the engagement locally (the server has already ended it, so no disengage request is sent) and clears the
        /// target if it is the one the character was fighting.
        /// </summary>
        private void EndEngagement()
        {
            var combat = Combat;
            if (combat == null) return;
            uint engagedTarget = combat.TargetServerId;
            combat.Disengage();
            if (CurrentTarget != null && (engagedTarget == 0 || CurrentTarget.ServerId == engagedTarget)) ClearTarget();
        }

        #endregion

        #region Targeting Subsystem

        /// <summary>
        /// Selects an entity as the active target.
        /// </summary>
        public void SetTarget(WorldEntity? target)
        {
            if (CurrentTarget != target)
            {
                // Retail cannot target anything else while a check window is open (in-game check 2026-10-07); clearing
                // the target (it went away) still goes through.
                if (target != null && Menus.IsCheckOpen) return;
                // The command menu is about the target it was opened on; it closes with it.
                if (Menus.CommandMenuTarget is { } open && open.TargetServerId != target?.ServerId) Menus.CloseCommandMenu();
                CurrentTarget = target;
                if (target == null)
                {
                    SetLockOn(false);
                }
                else
                {
                    TargetSelectedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                }
                TargetChanged?.Invoke(CurrentTarget);
            }
        }

        /// <summary>
        /// Attempts to target an entity by server ID.
        /// </summary>
        public bool SetTargetByServerId(uint serverId)
        {
            if (serverId == 0)
            {
                ClearTarget();
                return true;
            }

            if (_world.TryGetByServerId(serverId, out var entity) && entity != null)
            {
                SetTarget(entity);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to target an entity by zone target/actor index.
        /// </summary>
        public bool SetTargetByIndex(ushort targetIndex)
        {
            if (_world.TryGetByTargetIndex(targetIndex, out var entity) && entity != null)
            {
                SetTarget(entity);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to target an entity by name.
        /// </summary>
        public bool SetTargetByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                ClearTarget();
                return true;
            }

            if (_world.TryGetByName(name, out var entity) && entity != null)
            {
                SetTarget(entity);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Targets the member in <paramref name="slot"/> (1-5) of your own party, counting the other members in party
        /// window order (F2-F6 in retail; F1, yourself, is slot 0). Returns false when the slot is empty or the member
        /// is not in the zone.
        /// </summary>
        public bool SetTargetByPartySlot(int slot)
        {
            var members = _partyModule.State.Members;
            var self = members.FirstOrDefault(m => m.ServerId == _localPlayer.ServerId);
            byte ownParty = self?.PartyNumber ?? 0;
            var others = members.Where(m => m.PartyNumber == ownParty && m.ServerId != _localPlayer.ServerId)
                .OrderBy(m => m.MemberNumber).ToList();
            if (slot < 1 || slot > others.Count) return false;
            if (!_world.TryGetByServerId(others[slot - 1].ServerId, out var entity) || entity == null) return false;
            SetTarget(entity);
            return true;
        }

        /// <summary>
        /// Clears the current target.
        /// </summary>
        public void ClearTarget()
        {
            SetLockOn(false);
            SetTarget(null);
        }

        private (uint serverId, ushort targetIndex, string name) ResolveTarget(uint targetId, ushort targetIndex, string targetName)
        {
            // 1. Explicit target ID provided
            if (targetId != 0)
            {
                if (targetIndex == 0 && _world.TryGetByServerId(targetId, out var ent) && ent != null)
                {
                    return (targetId, ent.TargetIndex, ent.Name);
                }
                return (targetId, targetIndex, targetName);
            }

            // 2. Named target resolution
            if (!string.IsNullOrWhiteSpace(targetName) && targetName != "<t>" && targetName != "t")
            {
                if (_world.TryGetByName(targetName, out var namedEnt) && namedEnt != null)
                {
                    return (namedEnt.ServerId, namedEnt.TargetIndex, namedEnt.Name);
                }
            }

            // 3. Fallback to active CurrentTarget
            if (CurrentTarget != null)
            {
                return (CurrentTarget.ServerId, CurrentTarget.TargetIndex, CurrentTarget.Name);
            }

            return (0, 0, string.Empty);
        }

        #endregion

        #region Combat & Action Pipeline

        public async Task<PlayerActionResult> AttackAsync(uint targetId = 0, ushort targetIndex = 0)
        {
            var (resolvedId, resolvedIdx, resolvedName) = ResolveTarget(targetId, targetIndex, string.Empty);
            if (resolvedId == 0)
            {
                return PlayerActionResult.Warn("Cannot attack: No target selected.", ChatCommandResultKind.CombatAttack);
            }

            try
            {
                // Retail: attacking another monster while engaged switches the engaged target (0x01A ChangeTarget)
                // rather than engaging anew.
                bool switchTarget = Combat is { IsEngaged: true } combat && combat.TargetServerId != resolvedId;
                if (switchTarget)
                {
                    await _combatModule.RequestActionAsync(CliActionId.ChangeTarget, resolvedId, resolvedIdx).ConfigureAwait(false);
                }
                else
                {
                    await _combatModule.RequestAttackAsync(resolvedId, resolvedIdx).ConfigureAwait(false);
                }
                if (resolvedId != 0 && _world.TryGetByServerId(resolvedId, out var tgtEnt) && tgtEnt != null)
                {
                    SetTarget(tgtEnt);
                }
                return switchTarget
                    ? PlayerActionResult.Ok($"Switched target to {resolvedName} [ID: 0x{resolvedId:X8}].", ChatCommandResultKind.CombatAttack)
                    : PlayerActionResult.Ok($"Engaged in combat with {resolvedName} [ID: 0x{resolvedId:X8}].", ChatCommandResultKind.CombatAttack);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"Attack failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"Attack failed: {ex.Message}", ChatCommandResultKind.CombatAttack);
            }
        }

        /// <summary>
        /// Whether Confirm on the current target talks to it: NPCs and doors (retail talks to those; on players and
        /// monsters it opens the command menu instead, which is not built yet).
        /// </summary>
        public bool CanTalkToTarget => CurrentTarget is { Type: EntityType.Npc or EntityType.Door };

        /// <summary>
        /// Talks to the current target (Confirm on a targeted NPC or door): sends the 0x01A interaction, after which
        /// the server starts the NPC's event or prints its line.
        /// </summary>
        public async Task<PlayerActionResult> TalkToTargetAsync()
        {
            var target = CurrentTarget;
            if (target == null) return PlayerActionResult.Warn("Nothing is targeted.", ChatCommandResultKind.Talk);
            if (!CanTalkToTarget) return PlayerActionResult.Warn($"{target.Name} cannot be talked to.", ChatCommandResultKind.Talk);
            try
            {
                await _combatModule.RequestTalkAsync(target.ServerId, target.TargetIndex).ConfigureAwait(false);
                return PlayerActionResult.Ok($"Talking to {target.Name}.", ChatCommandResultKind.Talk);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"Talk failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"Talk failed: {ex.Message}", ChatCommandResultKind.Talk);
            }
        }

        /// <summary>
        /// Opens the target command menu for the current target (Confirm on yourself, another player, a monster, a
        /// pet or a trust; NPCs and doors are talked to instead): the menu's entries depend on the target's kind,
        /// whether you are engaged (with it) and whether you may invite. False when nothing is targeted, the
        /// target's kind has no menu, or a menu is already open.
        /// </summary>
        public bool OpenTargetCommandMenu()
        {
            var target = CurrentTarget;
            if (target == null) return false;
            var kind = target.Type switch
            {
                EntityType.Player => target.ServerId == _localPlayer.ServerId ? StockUiTargetKind.Self : StockUiTargetKind.Player,
                EntityType.Monster => StockUiTargetKind.Monster,
                EntityType.Pet => StockUiTargetKind.Pet,
                EntityType.Trust => StockUiTargetKind.Trust,
                _ => StockUiTargetKind.None,
            };
            if (kind == StockUiTargetKind.None) return false;
            var combat = Combat;
            bool engaged = combat?.IsEngaged == true;
            var party = _partyModule.State;
            bool canInvite = !party.IsInParty || party.IsLeader;
            if (canInvite)
            {
                foreach (var member in party.Members)
                {
                    if (member.ServerId == target.ServerId) canInvite = false;
                }
            }
            var context = new StockUiTargetContext(kind, target.ServerId, target.Name, engaged,
                EngagedWithTarget: engaged && combat!.TargetServerId == target.ServerId, CanInvite: canInvite);
            return Menus.OpenCommandMenu(context);
        }

        /// <summary>
        /// Names of the player characters within <paramref name="radius"/> yalms, nearest first (yourself included):
        /// the chat-mode list's tell candidates.
        /// </summary>
        public IReadOnlyList<string> NearbyPlayerNames(float radius = 50f)
        {
            if (!_world.TryGetByServerId(_localPlayer.ServerId, out var local) || local == null) return Array.Empty<string>();
            var players = new List<(float Distance, string Name)>();
            foreach (var entity in _world.GetEntitiesInRadius(local.Position, radius))
            {
                if (entity.Type != EntityType.Player || !entity.IsSpawned || string.IsNullOrEmpty(entity.Name)) continue;
                players.Add((Vector3.DistanceSquared(local.Position, entity.Position), entity.Name));
            }
            players.Sort((a, b) =>
            {
                int byDistance = a.Distance.CompareTo(b.Distance);
                return byDistance != 0 ? byDistance : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });
            var names = new List<string>(players.Count);
            foreach (var player in players) names.Add(player.Name);
            return names;
        }

        /// <summary>Runs a command menu entry on the target it was opened for (the controller's delegate).</summary>
        private async Task<PlayerActionResult> RunMenuTargetCommandAsync(StockUiMenuCommand command, StockUiTargetContext target)
        {
            if (!_world.TryGetByServerId(target.TargetServerId, out var entity) || entity == null)
            {
                return PlayerActionResult.Warn($"{target.TargetName} is no longer here.");
            }
            switch (command)
            {
                case StockUiMenuCommand.Attack:
                    var attack = await AttackAsync(entity.ServerId, entity.TargetIndex).ConfigureAwait(false);
                    // Retail re-opens the menu at once, now the engaged list (in-game check 2026-09-28).
                    if (attack.Success) OpenTargetCommandMenu();
                    return attack;
                case StockUiMenuCommand.Disengage:
                    return await DisengageAsync().ConfigureAwait(false);
                case StockUiMenuCommand.Invite:
                    await _partyModule.SendInviteAsync(entity.ServerId, entity.TargetIndex).ConfigureAwait(false);
                    return PlayerActionResult.Ok($"Invited {entity.Name} to party.", ChatCommandResultKind.PartyInvite);
                case StockUiMenuCommand.Check:
                    return await CheckAsync(entity.ServerId, entity.TargetIndex).ConfigureAwait(false);
                default:
                    return PlayerActionResult.Warn($"{command} is not available yet.");
            }
        }

        /// <summary>
        /// Examines a target (<c>/check</c>, the command menu's Check): sends 0x0DD, after which the server prints
        /// the check message (a monster's difficulty; a player's equipment is answered with 0x0C9, not shown yet).
        /// With no target given, the current target.
        /// </summary>
        public async Task<PlayerActionResult> CheckAsync(uint targetId = 0, ushort targetIndex = 0)
        {
            var (resolvedId, resolvedIdx, resolvedName) = ResolveTarget(targetId, targetIndex, string.Empty);
            if (resolvedId == 0) return PlayerActionResult.Warn("Nothing is targeted.", ChatCommandResultKind.Check);
            try
            {
                await _combatModule.RequestCheckAsync(resolvedId, resolvedIdx).ConfigureAwait(false);
                return PlayerActionResult.Ok($"Checking {resolvedName}.", ChatCommandResultKind.Check);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"Check failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"Check failed: {ex.Message}", ChatCommandResultKind.Check);
            }
        }

        public async Task<PlayerActionResult> DisengageAsync()
        {
            uint tid = CurrentTarget?.ServerId ?? 0;
            ushort tidx = CurrentTarget?.TargetIndex ?? 0;

            try
            {
                await _combatModule.RequestAttackOffAsync(tid, tidx).ConfigureAwait(false);
                SetLockOn(false);
                return PlayerActionResult.Ok("Disengaged from combat.", ChatCommandResultKind.CombatAttackOff);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"Disengage failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"Disengage failed: {ex.Message}", ChatCommandResultKind.CombatAttackOff);
            }
        }

        public async Task<PlayerActionResult> CastMagicAsync(ushort spellId, uint targetId = 0, ushort targetIndex = 0, Vector3 targetOffset = default)
        {
            var (resolvedId, resolvedIdx, resolvedName) = ResolveTarget(targetId, targetIndex, string.Empty);
            if (resolvedId == 0)
            {
                // If casting on self
                resolvedId = _localPlayer.ServerId;
                resolvedName = "self";
            }

            try
            {
                await _combatModule.RequestCastMagicAsync(resolvedId, resolvedIdx, spellId, targetOffset).ConfigureAwait(false);
                return PlayerActionResult.Ok($"Casting spell #{spellId} on {resolvedName}.", ChatCommandResultKind.CombatCast);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"CastMagic failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"CastMagic failed: {ex.Message}", ChatCommandResultKind.CombatCast);
            }
        }

        public async Task<PlayerActionResult> WeaponskillAsync(ushort wsId, uint targetId = 0, ushort targetIndex = 0)
        {
            var (resolvedId, resolvedIdx, resolvedName) = ResolveTarget(targetId, targetIndex, string.Empty);
            if (resolvedId == 0)
            {
                return PlayerActionResult.Warn("Cannot execute Weaponskill: No target selected.", ChatCommandResultKind.CombatWeaponskill);
            }

            try
            {
                await _combatModule.RequestWeaponskillAsync(resolvedId, resolvedIdx, wsId).ConfigureAwait(false);
                return PlayerActionResult.Ok($"Executed Weaponskill #{wsId} on {resolvedName}.", ChatCommandResultKind.CombatWeaponskill);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"Weaponskill failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"Weaponskill failed: {ex.Message}", ChatCommandResultKind.CombatWeaponskill);
            }
        }

        public async Task<PlayerActionResult> JobAbilityAsync(ushort abilityId, uint targetId = 0, ushort targetIndex = 0)
        {
            var (resolvedId, resolvedIdx, resolvedName) = ResolveTarget(targetId, targetIndex, string.Empty);
            if (resolvedId == 0)
            {
                resolvedId = _localPlayer.ServerId;
                resolvedName = "self";
            }

            try
            {
                await _combatModule.RequestJobAbilityAsync(resolvedId, resolvedIdx, abilityId).ConfigureAwait(false);
                return PlayerActionResult.Ok($"Used Job Ability #{abilityId} on {resolvedName}.", ChatCommandResultKind.CombatJobAbility);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"JobAbility failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"JobAbility failed: {ex.Message}", ChatCommandResultKind.CombatJobAbility);
            }
        }

        public async Task<PlayerActionResult> ShootAsync(uint targetId = 0, ushort targetIndex = 0)
        {
            var (resolvedId, resolvedIdx, resolvedName) = ResolveTarget(targetId, targetIndex, string.Empty);
            if (resolvedId == 0)
            {
                return PlayerActionResult.Warn("Cannot shoot: No target selected.", ChatCommandResultKind.CombatRanged);
            }

            try
            {
                await _combatModule.RequestShootAsync(resolvedId, resolvedIdx).ConfigureAwait(false);
                return PlayerActionResult.Ok($"Fired ranged attack at {resolvedName}.", ChatCommandResultKind.CombatRanged);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"Shoot failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"Shoot failed: {ex.Message}", ChatCommandResultKind.CombatRanged);
            }
        }

        public async Task<PlayerActionResult> AssistAsync(uint targetId = 0, ushort targetIndex = 0)
        {
            var (resolvedId, resolvedIdx, resolvedName) = ResolveTarget(targetId, targetIndex, string.Empty);
            if (resolvedId == 0)
            {
                return PlayerActionResult.Warn("Cannot assist: No target selected.", ChatCommandResultKind.CombatAssist);
            }

            try
            {
                await _combatModule.RequestAssistAsync(resolvedId, resolvedIdx).ConfigureAwait(false);
                return PlayerActionResult.Ok($"Assisting {resolvedName}.", ChatCommandResultKind.CombatAssist);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"Assist failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"Assist failed: {ex.Message}", ChatCommandResultKind.CombatAssist);
            }
        }

        /// <summary>
        /// Sends a C2S 0x01A action that targets the player itself (fish, sprint, chocobo dig, blockaid): the
        /// local player's id and index, with ActionBuf[0] = <paramref name="param"/>.
        /// </summary>
        private async Task<PlayerActionResult> SelfActionAsync(CliActionId action, uint param, string done, ChatCommandResultKind kind)
        {
            uint selfId = _localPlayer.ServerId;
            ushort selfIndex = _world.TryGetByServerId(selfId, out var self) && self != null ? self.TargetIndex : (ushort)0;
            try
            {
                await _combatModule.RequestActionAsync(action, selfId, selfIndex, param).ConfigureAwait(false);
                return PlayerActionResult.Ok(done, kind);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"{action} failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"{action} failed: {ex.Message}", kind);
            }
        }

        #region Death Menus (#103)

        /// <summary>The local player's target index, for the actions about itself (0 when its entity is not known).</summary>
        private ushort SelfIndex => _world.TryGetByServerId(_localPlayer.ServerId, out var self) && self != null ? self.TargetIndex : (ushort)0;

        /// <summary>
        /// Returns the dead character to its home point: C2S 0x01A HomepointMenu with ActionBuf[0] = 0 (the dead
        /// window's "Back to Home Point", and <c>/homepoint</c>). LandSandBoat ignores it unless the character is dead,
        /// then warps it to its home point (zone change).
        /// </summary>
        public async Task<PlayerActionResult> HomePointAsync()
        {
            const ChatCommandResultKind Kind = ChatCommandResultKind.HomePoint;
            if (!_localPlayer.IsDead) return PlayerActionResult.Warn("You can only return to your home point while unconscious.", Kind);
            try
            {
                await _combatModule.RequestDeathMenuAsync(CliActionId.HomepointMenu, (uint)HomepointMenuChoice.ReturnToHomePoint, SelfIndex).ConfigureAwait(false);
                return PlayerActionResult.Ok("Returning to your home point.", Kind);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"Home point failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"Home point failed: {ex.Message}", Kind);
            }
        }

        /// <summary>
        /// Answers the Raise (<see cref="DeathMenuType.Raise"/>) or Tractor offer of S2C 0x0F9: C2S 0x01A RaiseMenu or
        /// TractorMenu with ActionBuf[0] = 0 to accept, 1 to decline. The offer is cleared once answered. Sent only
        /// while dead (LandSandBoat rejects it otherwise); without a recorded offer it is still sent, since the server
        /// keeps its own (for testing a missed 0x0F9).
        /// </summary>
        public async Task<PlayerActionResult> AnswerDeathOfferAsync(DeathMenuType offer, bool accept)
        {
            var kind = offer == DeathMenuType.Tractor ? ChatCommandResultKind.TractorAnswer : ChatCommandResultKind.RaiseAnswer;
            string name = offer == DeathMenuType.Tractor ? "Tractor" : "Raise";
            if (offer == DeathMenuType.HomePoint) return PlayerActionResult.Warn("Nothing to answer.", kind);
            if (!_localPlayer.IsDead) return PlayerActionResult.Warn($"You can only answer a {name} while unconscious.", kind);
            var action = offer == DeathMenuType.Tractor ? CliActionId.TractorMenu : CliActionId.RaiseMenu;
            var answer = accept ? ReviveMenuAnswer.Accept : ReviveMenuAnswer.Decline;
            try
            {
                bool offered = _localPlayer.DeathMenu == offer;
                await _combatModule.RequestDeathMenuAsync(action, (uint)answer, SelfIndex).ConfigureAwait(false);
                if (offered) _localPlayer.ApplyDeathMenu(DeathMenuType.HomePoint);
                string done = accept ? $"{name} accepted." : $"{name} declined.";
                return offered ? PlayerActionResult.Ok(done, kind) : PlayerActionResult.Info($"{done} (no {name} was offered; the server decides)", kind);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"{name} answer failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"{name} answer failed: {ex.Message}", kind);
            }
        }

        #endregion

        /// <summary><c>/fish</c>: casts a line (0x01A Fish); the server starts the fishing mini-game (0x115).</summary>
        public Task<PlayerActionResult> FishAsync()
            => SelfActionAsync(CliActionId.Fish, 0, "Cast a line.", ChatCommandResultKind.Fish);

        /// <summary><c>/sprint</c> (0x01A Sprint). LandSandBoat accepts it but does nothing yet.</summary>
        public Task<PlayerActionResult> SprintAsync()
            => SelfActionAsync(CliActionId.Sprint, 0, "Sprint.", ChatCommandResultKind.Sprint);

        /// <summary>
        /// <c>/dig</c>: digs with the chocobo being ridden (0x01A ChocoboDig), spending a Gysahl Greens. The server
        /// answers with 0x02F, which <see cref="CombatPacketModule"/> acknowledges with C2S 0x063.
        /// </summary>
        public Task<PlayerActionResult> ChocoboDigAsync()
            => SelfActionAsync(CliActionId.ChocoboDig, 0, "Digging.", ChatCommandResultKind.ChocoboDig);

        /// <summary><c>/blockaid [on|off]</c> (0x01A Blockaid): refuses or accepts aid from outside the party; bare toggles.</summary>
        public Task<PlayerActionResult> BlockaidAsync(BlockaidMode mode)
            => SelfActionAsync(CliActionId.Blockaid, (uint)mode, $"Blockaid: {mode}.", ChatCommandResultKind.Blockaid);

        /// <summary>
        /// <c>/callforhelp</c>: calls for help against the engaged monster (0x01A Help), after which outsiders can
        /// join the fight but it yields no experience. The server answers with a battle message either way.
        /// </summary>
        public async Task<PlayerActionResult> CallForHelpAsync()
        {
            uint targetId = 0;
            ushort targetIndex = 0;
            if (Combat is { IsEngaged: true } combat)
            {
                targetId = combat.TargetServerId;
                targetIndex = combat.TargetIndex;
            }
            else if (CurrentTarget != null)
            {
                targetId = CurrentTarget.ServerId;
                targetIndex = CurrentTarget.TargetIndex;
            }

            try
            {
                await _combatModule.RequestActionAsync(CliActionId.Help, targetId, targetIndex).ConfigureAwait(false);
                return PlayerActionResult.Ok("Called for help.", ChatCommandResultKind.CallForHelp);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"Call for help failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"Call for help failed: {ex.Message}", ChatCommandResultKind.CallForHelp);
            }
        }

        /// <summary>
        /// <c>/monsterskill &lt;id&gt; [target]</c> (Monstrosity, 0x01A MonsterSkill): ActionBuf[0] is the skill id as
        /// the retail client sends it (the ability id less 1536).
        /// </summary>
        public async Task<PlayerActionResult> MonsterSkillAsync(ushort skillId, uint targetId = 0, ushort targetIndex = 0)
        {
            var (resolvedId, resolvedIdx, resolvedName) = ResolveTarget(targetId, targetIndex, string.Empty);
            if (resolvedId == 0)
            {
                resolvedId = _localPlayer.ServerId;
                resolvedIdx = _world.TryGetByServerId(resolvedId, out var self) && self != null ? self.TargetIndex : (ushort)0;
                resolvedName = "self";
            }

            try
            {
                await _combatModule.RequestActionAsync(CliActionId.MonsterSkill, resolvedId, resolvedIdx, skillId).ConfigureAwait(false);
                return PlayerActionResult.Ok($"Used monster skill #{skillId} on {resolvedName}.", ChatCommandResultKind.MonsterSkill);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"MonsterSkill failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"MonsterSkill failed: {ex.Message}", ChatCommandResultKind.MonsterSkill);
            }
        }

        /// <summary>The Trusts in the player's party that are spawned here, in party order.</summary>
        private List<WorldEntity> GetOwnTrusts()
        {
            var trusts = new List<WorldEntity>();
            foreach (var member in _partyModule.State.Members)
            {
                if (_world.TryGetByServerId(member.ServerId, out var ent) && ent is { Type: EntityType.Trust })
                {
                    trusts.Add(ent);
                }
            }
            return trusts;
        }

        /// <summary>
        /// <c>/refa &lt;name|all&gt;</c> (<c>/returnfaith</c>): releases one of the player's Trusts, or all of them, with a
        /// C2S 0x01A Talk on each. ActionBuf[0] is 1 for a single Trust; for <c>all</c> it counts up from 0, one Talk per
        /// Trust. Without an argument the current target is released if it is one of the player's Trusts.
        /// Packet usage referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/client/0x001A)
        /// and LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x01a_action.cpp),
        /// which releases any of the player's Trusts it is sent a Talk for and ignores ActionBuf.
        /// </summary>
        public async Task<PlayerActionResult> ReleaseTrustAsync(string args)
        {
            const ChatCommandResultKind kind = ChatCommandResultKind.ReleaseTrust;
            var trusts = GetOwnTrusts();
            if (trusts.Count == 0) return PlayerActionResult.Warn("You have no Trusts to release.", kind);

            string arg = (args ?? string.Empty).Trim();
            try
            {
                if (arg.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    for (int i = 0; i < trusts.Count; i++)
                    {
                        await _combatModule.RequestActionAsync(CliActionId.Talk, trusts[i].ServerId, trusts[i].TargetIndex, (uint)i).ConfigureAwait(false);
                    }
                    return PlayerActionResult.Ok($"Released {trusts.Count} Trust(s).", kind);
                }

                WorldEntity? trust = arg.Length == 0 || arg is "<t>" or "t"
                    ? trusts.Find(t => t.ServerId == CurrentTarget?.ServerId)
                    : trusts.Find(t => t.Name.Equals(arg, StringComparison.OrdinalIgnoreCase))
                      ?? trusts.Find(t => t.Name.StartsWith(arg, StringComparison.OrdinalIgnoreCase));
                if (trust == null)
                {
                    return arg.Length == 0
                        ? PlayerActionResult.Warn("Usage: /refa <name|all>", kind)
                        : PlayerActionResult.Warn($"No Trust named '{arg}' in your party.", kind);
                }

                await _combatModule.RequestActionAsync(CliActionId.Talk, trust.ServerId, trust.TargetIndex, 1).ConfigureAwait(false);
                return PlayerActionResult.Ok($"Released {trust.Name}.", kind);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"Release Trust failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"Release Trust failed: {ex.Message}", kind);
            }
        }

        public async Task<PlayerActionResult> CancelBuffAsync(ushort buffId)
        {
            try
            {
                await _combatModule.RequestBuffCancelAsync(buffId).ConfigureAwait(false);
                return PlayerActionResult.Ok($"Cancelled buff #{buffId}.", ChatCommandResultKind.CombatBuffCancel);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"CancelBuff failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"CancelBuff failed: {ex.Message}", ChatCommandResultKind.CombatBuffCancel);
            }
        }

        public async Task<PlayerActionResult> EmoteAsync(EmoteId emote, uint targetId = 0, ushort targetIndex = 0, ushort param = 0)
        {
            var (resolvedId, resolvedIdx, _) = ResolveTarget(targetId, targetIndex, string.Empty);
            try
            {
                await _combatModule.RequestEmoteAsync(resolvedId, resolvedIdx, emote, param: param).ConfigureAwait(false);
                // No line of our own: the server's echo (S2C 0x05A) prints "X waves." and plays the motion, as in retail.
                return PlayerActionResult.Ok(string.Empty, ChatCommandResultKind.Emote);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"Emote failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"Emote failed: {ex.Message}", ChatCommandResultKind.Emote);
            }
        }

        public async Task<PlayerActionResult> JumpAsync()
        {
            try
            {
                // The server checks the index against the character's own (c2s/0x11d_jump.cpp).
                ushort selfIndex = _world.TryGetByServerId(_localPlayer.ServerId, out var self) && self != null ? self.TargetIndex : (ushort)0;
                await _combatModule.RequestJumpAsync(selfIndex).ConfigureAwait(false);
                return PlayerActionResult.Ok("Jumped.", ChatCommandResultKind.CombatJump);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"Jump failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"Jump failed: {ex.Message}", ChatCommandResultKind.CombatJump);
            }
        }

        #endregion

        #region Locomotion & Inspection Subsystem

        /// <summary>
        /// Formats an internal position (Y = height) in FFXI/Windower display order: X, Y (north/south), Z (height).
        /// </summary>
        private static string FormatDisplayPosition(Vector3 pos, string format) =>
            $"X={pos.X.ToString(format)}, Y={pos.Z.ToString(format)}, Z={pos.Y.ToString(format)}";

        /// <summary>
        /// Moves towards target coordinates (internal axes, Y = height; a NaN Y keeps the current height).
        /// Gated by <see cref="FeatureRestrictions"/>.
        /// </summary>
        public async Task<PlayerActionResult> MoveToAsync(Vector3 targetPos)
        {
            if (_profile.IsRestricted(FeatureRestrictions.Movement))
            {
                return PlayerActionResult.Fail(
                    "Synthetic movement (/moveto) is blocked by server feature restrictions (Movement).",
                    ChatCommandResultKind.SyntheticMoveTo);
            }

            byte dir = 0;
            ushort targetIndex = 0;
            float dist = 0f;

            // Update local entity coordinates in WorldState if present
            bool hasLocal = _world.TryGetByServerId(_localPlayer.ServerId, out var localEnt) && localEnt != null;
            if (float.IsNaN(targetPos.Y))
            {
                // No height given: land on the floor there nearest the current height, else keep the current height.
                float currentHeight = hasLocal ? localEnt!.Position.Y : 0f;
                var collision = _world.Collision;
                targetPos = targetPos with
                {
                    Y = collision != null && collision.TryGetNearestGround(targetPos.X, targetPos.Z, currentHeight, out var ground)
                        ? ground.Height
                        : currentHeight
                };
            }

            if (hasLocal && localEnt != null)
            {
                dist = Vector3.Distance(localEnt.Position, targetPos);
                localEnt.Position = targetPos;
                dir = localEnt.Direction;
                byte activeSpeed = localEnt.Speed > 0 ? localEnt.Speed : (_localPlayer.Speed > 0 ? (byte)Math.Min((ushort)255, _localPlayer.Speed) : (byte)50);
                localEnt.Speed = dist > 0.05f ? activeSpeed : (byte)0;
            }
            else if (_localPlayer.ServerId != 0)
            {
                byte initialSpeed = _localPlayer.Speed > 0 ? (byte)Math.Min((ushort)255, _localPlayer.Speed) : (byte)50;
                localEnt = new PlayerEntity(_localPlayer.ServerId, 0)
                {
                    Position = targetPos,
                    IsSpawned = true,
                    Speed = initialSpeed
                };
                _world.UpsertEntity(localEnt);
            }

            LocalPlayerMoved?.Invoke(targetPos, dir);

            ushort slideFrames = dist > 0.05f ? SessionNetworkManager.InitialRunCount : SessionNetworkManager.StationaryRunCount;

            try
            {
                byte[] posPacket = LifecycleOutboundPackets.BuildPos(
                    sequenceId: 0,
                    x: targetPos.X,
                    y: targetPos.Y,
                    z: targetPos.Z,
                    dir: dir,
                    targetIndex: targetIndex,
                    moveFrame: slideFrames);

                await _sendChunkCallback(posPacket, false).ConfigureAwait(false);
                return PlayerActionResult.Ok(
                    $"Locomotion updated: {FormatDisplayPosition(targetPos, "F2")}",
                    ChatCommandResultKind.SyntheticMoveTo);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"MoveTo failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"MoveTo failed: {ex.Message}", ChatCommandResultKind.SyntheticMoveTo);
            }
        }

        /// <summary>
        /// Returns a formatted summary of current player position, heading, and zone.
        /// </summary>
        public string GetPositionSummary()
        {
            Vector3 pos = Vector3.Zero;
            byte dir = 0;
            float headingDeg = 0;

            if (_world.TryGetByServerId(_localPlayer.ServerId, out var localEnt) && localEnt != null)
            {
                pos = localEnt.Position;
                dir = localEnt.Direction;
                headingDeg = (dir / 256.0f) * 360.0f;
            }

            return $"[Position] {FormatDisplayPosition(pos, "F2")} | Dir={dir} ({headingDeg:F0}°) | ServerID=0x{_localPlayer.ServerId:X8}";
        }

        /// <summary>
        /// Returns a formatted summary of character vitals and jobs.
        /// </summary>
        public string GetVitalsSummary()
        {
            return $"[Vitals] HP: {_localPlayer.CurrentHp}/{_localPlayer.MaxHp} ({_localPlayer.Hpp}%) | MP: {_localPlayer.CurrentMp}/{_localPlayer.MaxMp} | TP: {_localPlayer.CurrentTp} | Job: {_localPlayer.MainJob} {_localPlayer.MainJobLevel} / {_localPlayer.SubJob} {_localPlayer.SubJobLevel}";
        }

        /// <summary>
        /// Returns a list of nearby entities formatted for console inspection.
        /// </summary>
        public string GetNearbySummary(float radius = 50.0f)
        {
            Vector3 center = Vector3.Zero;
            if (_world.TryGetByServerId(_localPlayer.ServerId, out var localEnt) && localEnt != null)
            {
                center = localEnt.Position;
            }

            var entities = _world.GetEntitiesInRadius(center, radius);
            if (entities.Count == 0)
            {
                return $"No entities found within {radius:F0} yalms.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"--- Nearby Entities within {radius:F0} yalms ({entities.Count} found) ---");
            foreach (var ent in entities)
            {
                float dist = Vector3.Distance(center, ent.Position);
                string name = string.IsNullOrWhiteSpace(ent.Name) ? "<Unknown>" : ent.Name;
                sb.AppendLine($" - [{ent.Type}] {name} (ID: 0x{ent.ServerId:X8}, Idx: {ent.TargetIndex}) Dist: {dist:F1}y HP: {ent.Hpp}% Pos: ({ent.Position.X:F1}, {ent.Position.Z:F1}, {ent.Position.Y:F1})");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Returns a formatted summary of the currently selected target.
        /// </summary>
        public string GetTargetInfoSummary()
        {
            if (CurrentTarget == null)
            {
                return "No entity currently targeted.";
            }

            var t = CurrentTarget;
            string name = string.IsNullOrWhiteSpace(t.Name) ? "<Unknown>" : t.Name;
            Vector3 myPos = Vector3.Zero;
            if (_world.TryGetByServerId(_localPlayer.ServerId, out var localEnt) && localEnt != null)
            {
                myPos = localEnt.Position;
            }

            float dist = Vector3.Distance(myPos, t.Position);
            return $"[Target] {name} | Type: {t.Type} | ID: 0x{t.ServerId:X8} | Index: {t.TargetIndex} | HP: {t.Hpp}% | Dist: {dist:F1}y | Pos: ({t.Position.X:F2}, {t.Position.Z:F2}, {t.Position.Y:F2})";
        }

        /// <summary>
        /// Checks whether the local active player holds Game Master (GM) administrative permissions.
        /// </summary>
        public bool IsLocalPlayerGm()
        {
            if (_localPlayer.IsGm) return true;
            if (_world.TryGetByServerId(_localPlayer.ServerId, out var ent) && ent is PlayerEntity pe && pe.GmLevel > 0)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Returns a formatted list of available client commands, dynamically filtered by <see cref="FeatureRestrictions"/>.
        /// </summary>
        public string GetStandardCommandsSummary(string? filter = null)
        {
            var restrictions = _profile.FeatureRestrictions;
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(filter))
            {
                string norm = filter.Trim().TrimStart('/').ToLowerInvariant();
                switch (norm)
                {
                    case "moveto" or "goto":
                        if (_profile.IsRestricted(FeatureRestrictions.Movement))
                            return "Command '/moveto' is blocked by server feature restrictions (Movement).";
                        return "Usage: /moveto <x> <y> [z] - Move to FFXI/Windower coordinates (z = height, optional).";
                    case "playsound":
                        return "Usage: /playsound <id> | stop - Debug: play sound effect <id> on this client (looped files loop until stop).";
                    case "playmusic":
                        return "Usage: /playmusic <n> | stop - Debug: play music track <n> on this client instead of the zone music.";
                    case "pos" or "where" or "loc":
                        return "Usage: /pos - Print current player coordinates, heading, and server ID.";
                    case "target" or "ta":
                        return "Usage: /target <name|id> - Target entity by name or server ID. Use without args to clear.";
                    case "targetinfo" or "ti":
                        return "Usage: /targetinfo - Display detailed stats, HP%, and distance for current target.";
                    case "vitals" or "hp" or "stats":
                        return "Usage: /vitals - Display HP, MP, TP, and job progression.";
                    case "nearby" or "scan" or "entities":
                        return "Usage: /nearby [radius] - Scan nearby entities within given radius (default 50 yalms).";
                    case "attack" or "a":
                        return "Usage: /attack [target] - Engage targeted or specified entity in melee combat.";
                    case "attackoff" or "disengage" or "aoff":
                        return "Usage: /attackoff - Disengage from combat.";
                    case "check":
                        return "Usage: /check [target] - Examine the targeted or specified entity.";
                    case "magic" or "ma" or "cast":
                        return "Usage: /magic <spell_id> [target] - Cast magic spell on target.";
                    case "ws" or "weaponskill":
                        return "Usage: /ws <ws_id> [target] - Execute weapon skill on target.";
                    case "ja" or "jobability":
                        return "Usage: /ja <ability_id> [target] - Use job ability on target.";
                    case "shoot" or "ra":
                        return "Usage: /shoot [target] - Perform ranged attack on target.";
                    case "assist" or "as":
                        return "Usage: /assist [target] - Assist targeted or named player.";
                    case "cancel":
                        return "Usage: /cancel <buff_id> - Cancel active player status effect.";
                    case "jump":
                        return "Usage: /jump - Perform jump action.";
                    case "fish":
                        return "Usage: /fish - Cast a fishing line (a rod and bait must be equipped).";
                    case "sprint":
                        return "Usage: /sprint - Sprint.";
                    case "dig":
                        return "Usage: /dig - Dig with your chocobo (uses one Gysahl Greens).";
                    case "blockaid":
                        return "Usage: /blockaid [on|off] - Refuse or accept aid from outside your party; toggles without an argument.";
                    case "callforhelp" or "cfh":
                        return "Usage: /callforhelp - Call for help against the monster you are fighting.";
                    case "monsterskill" or "ms":
                        return "Usage: /monsterskill <skill_id> [target] - Use a Monstrosity monster skill.";
                    case "homepoint":
                        return "Usage: /homepoint - While unconscious, return to your home point (the dead window's Back to Home Point). GordianXI command.";
                    case "acceptraise":
                        return "Usage: /acceptraise [decline] - While unconscious, accept (or decline) the Raise you were offered. GordianXI command.";
                    case "accepttractor":
                        return "Usage: /accepttractor [decline] - While unconscious, accept (or decline) the Tractor you were offered. GordianXI command.";
                    case "refa" or "returnfaith":
                        return "Usage: /refa <name|all> - Release one or all of your Trusts (the current target without an argument).";
                    case "say" or "s":
                        return "Usage: /say <message> - Send message to local Say channel.";
                    case "party" or "p":
                        return "Usage: /party <message> - Send message to Party channel.";
                    case "shout" or "sh":
                        return "Usage: /shout <message> - Send message to zone Shout channel.";
                    case "yell" or "y":
                        return "Usage: /yell <message> - Send message to Yell channel.";
                    case "tell" or "t" or "w":
                        return "Usage: /tell <player> <message> - Send private tell.";
                    case "linkshell" or "l":
                        return "Usage: /linkshell <message> - Send message to active Linkshell.";
                    case "echo":
                        return "Usage: /echo <message> - Print local message to console.";
                    case "invite":
                        return "Usage: /invite <player> - Invite player to party.";
                    case "join" or "accept":
                        return "Usage: /accept - Accept party invitation.";
                    case "decline" or "refuse":
                        return "Usage: /decline - Decline party invitation.";
                    case "leave" or "break":
                        return "Usage: /leave - Leave current party.";
                    case "disband" or "breakup":
                        return "Usage: /disband - Disband party (party leader only).";
                    case "kick":
                        return "Usage: /kick <player> - Remove player from party.";
                    case "uilayout" or "uil":
                        return UiLayoutUsage;
                    case "heal":
                        return "Usage: /heal [on|off] - Rest to recover HP and MP faster; send it again to stand up.";
                    case "sit":
                        return "Usage: /sit [on|off] - Sit down or stand up.";
                    case "logout":
                        return "Usage: /logout [on|off] - Log out to the character select screen after 30 seconds; again (or off) cancels.";
                    case "shutdown":
                        return "Usage: /shutdown [on|off] - Shut down after 30 seconds, closing the session; again (or off) cancels.";
                    case "sitchair":
                        return "Usage: /sitchair [chair 0-20] [on|off] - Sit in a chair (0 is the plain chair; 1-11 need the matching key item).";
                    case "random" or "rand":
                        return "Usage: /random - Roll a number from 0 to 999, shown to you and everyone near.";
                    case "nominate" or "propose":
                        return "Usage: /nominate [say|party|shout|linkshell|linkshell2] \"question\" \"option 1\" \"option 2\" ... - Start a vote (no text cancels yours).";
                    case "vote":
                        return "Usage: /vote <option 1-8> [proposer] - Vote in a proposal (the last one seen without a name).";
                    case "widescan" or "wide":
                        return "Usage: /widescan - List monsters and NPCs around you (Rangers and Beastmasters).";
                    case "track" or "untrack":
                        return "Usage: /track [index|name|off] - Track a Wide Scan target (your target without an argument); /untrack stops.";
                    case "jobmasterdisp":
                        return "Usage: /jobmasterdisp on|off - Show or hide the job mastery mark next to your name.";
                    case "lockstyle":
                        return LockstyleUsage;
                    case "lockstyleset":
                        return "Usage: /lockstyleset - Lock your equipment's appearance (equipment set numbers are not supported yet).";
                    case "help" or "commands":
                        return "Usage: /help [command] - Show available client commands or detailed help.";
                    case "gmhelp" or "gmcommands":
                        return "Usage: /gmhelp - Show Game Master administrative commands (requires GM status).";
                    default:
                        break;
                }
            }

            sb.AppendLine($"--- Available Client Commands [Restrictions: {restrictions}] ---");
            sb.AppendLine("[Navigation & Telemetry]");
            sb.AppendLine("  /pos                      - Current coordinates, heading, and server ID (/where, /loc)");
            sb.AppendLine("  /target <name|id>         - Target entity by name or server ID (/ta)");
            sb.AppendLine("  /targetinfo               - Detailed stats and distance of target (/ti)");
            sb.AppendLine("  /vitals                   - HP, MP, TP, and job levels (/hp, /stats)");
            sb.AppendLine("  /nearby [radius]          - Scan nearby entities within radius (default 50y)");
            if (!_profile.IsRestricted(FeatureRestrictions.Movement))
            {
                sb.AppendLine("  /moveto <x> <y> [z]       - Move to target coordinates (/goto)");
            }
            sb.AppendLine("  /collision [layer] [on|off] - Toggle ground, walls or entities collision (/col)");
            sb.AppendLine("  /anchor [on|off]          - Ignore knockback (off by default; the server can forbid it)");
            sb.AppendLine("  /uilayout [window] [...]  - Stock UI scale, move, hide or reset windows; unlock to drag them (/uil)");
            sb.AppendLine("  /lockstyle [on|off]       - Lock your equipment's appearance, or show whether it is locked");
            sb.AppendLine("  /lot [slot], /pass [slot] - Lot or pass on a treasure pool item (all undecided items without a slot)");
            sb.AppendLine("  /synth <crystal slot> <slot> [slot ...] - Synthesize with inventory slots (repeat a slot to use several of a stack)");
            sb.AppendLine("  /guild buylist | selllist | buy <item id> [n] | sell <slot> [n] - Guild shop requests (while a guild shop is open)");
            sb.AppendLine("[Debug: audio, client only]");
            sb.AppendLine("  /playsound <id> | stop    - Play sound effect <id> (seNNNNNN.spw) centred; looped files loop until stop");
            sb.AppendLine("  /playmusic <n> | stop     - Play musicNNN.bgw instead of the zone music; stop returns to it");
            sb.AppendLine("[Combat & Abilities]");
            sb.AppendLine("  /attack [target]          - Engage target in melee combat (/a)");
            sb.AppendLine("  /attackoff                - Disengage from combat (/disengage, /aoff)");
            sb.AppendLine("  /check [target]           - Examine a target (the command menu's Check)");
            sb.AppendLine("  /magic <spell_id> [target]- Cast magic spell (/ma, /cast)");
            sb.AppendLine("  /ws <ws_id> [target]      - Execute weapon skill (/weaponskill)");
            sb.AppendLine("  /ja <ability_id> [target] - Use job ability (/jobability)");
            sb.AppendLine("  /shoot [target]           - Perform ranged attack (/ra)");
            sb.AppendLine("  /assist [target]          - Assist target (/as)");
            sb.AppendLine("  /cancel <buff_id>         - Cancel active buff");
            sb.AppendLine("  /jump                     - Perform jump action");
            sb.AppendLine("  /callforhelp              - Call for help against your monster (/cfh)");
            sb.AppendLine("  /blockaid [on|off]        - Refuse aid from outside your party");
            sb.AppendLine("  /refa <name|all>          - Release Trusts (/returnfaith)");
            sb.AppendLine("  /monsterskill <id> [target] - Monstrosity monster skill (/ms)");
            sb.AppendLine("  /fish, /dig, /sprint      - Fish, dig with your chocobo, sprint");
            sb.AppendLine("  /homepoint                - While unconscious: return to your home point");
            sb.AppendLine("  /acceptraise [decline]    - While unconscious: answer a Raise (/accepttractor for a Tractor)");
            sb.AppendLine("[Emotes]");
            sb.AppendLine("  /emote <name>             - Perform emote (/em)");
            sb.AppendLine("  /cheer, /wave, /bow, ...  - Standard emote shortcuts");
            sb.AppendLine("  /heal [on|off]            - Rest to recover HP and MP; again to stand");
            sb.AppendLine("  /sit [on|off]             - Sit down or stand up");
            sb.AppendLine("  /sitchair [n] [on|off]    - Sit in a chair (0 plain, 1-11 unlocked chairs)");
            sb.AppendLine("  /random                   - Roll the dice (0-999), shown to everyone near");
            sb.AppendLine("  /nominate [scope] \"question\" \"option\" ... - Start a vote (/propose); /vote <n> [proposer] to answer");
            sb.AppendLine("  /widescan, /track [target|off] - Wide Scan (Ranger, Beastmaster) and tracking");
            sb.AppendLine("  /conquest                 - Conquest points and Imperial Standing (/cq)");
            sb.AppendLine("  /jobmasterdisp on|off     - Show or hide the job mastery mark");
            sb.AppendLine("  /logout [on|off]          - Log out to the character select screen (30 seconds)");
            sb.AppendLine("  /shutdown [on|off]        - Shut down and close the session (30 seconds)");
            sb.AppendLine("[Communication]");
            sb.AppendLine("  /say <msg>                - Send chat to Say (/s)");
            sb.AppendLine("  /party <msg>              - Send chat to Party (/p)");
            sb.AppendLine("  /shout <msg>              - Send chat to Shout (/sh)");
            sb.AppendLine("  /yell <msg>               - Send chat to Yell (/y)");
            sb.AppendLine("  /tell <player> <msg>      - Send private tell (/t, /w)");
            sb.AppendLine("  /linkshell <msg>          - Send chat to Linkshell (/l, /l1, /l2)");
            sb.AppendLine("  /echo <msg>               - Print local echo message");
            sb.AppendLine("[Party Management]");
            sb.AppendLine("  /invite <player>          - Invite player to party");
            sb.AppendLine("  /accept                   - Accept party invite (/join)");
            sb.AppendLine("  /decline                  - Decline party invite (/refuse)");
            sb.AppendLine("  /leave                    - Leave party (/break)");
            sb.AppendLine("  /disband                  - Disband party (/breakup)");
            sb.AppendLine("  /kick <player>            - Kick member from party");
            sb.AppendLine("[Discovery]");
            sb.AppendLine("  /help [command]           - Show available commands (/commands)");
            sb.AppendLine("  /gmhelp                   - Show Game Master commands (GM only)");

            return sb.ToString().TrimEnd();
        }

        #region Style Lock & Key Items

        private const string LockstyleUsage = "Usage: /lockstyle [on|off] - Lock your equipment's appearance; without an argument, show whether it is locked.";

        /// <summary>
        /// <c>/lockstyle</c>: C2S 0x053 mode Query without an argument, Enable for <c>on</c>, Disable for <c>off</c>,
        /// the modes retail sends for each form (XiPackets world/client/0x0053). The server replies with the
        /// on/off message or the new appearance.
        /// </summary>
        public async Task<PlayerActionResult> LockstyleAsync(string args)
        {
            LockstyleMode? mode = args.Trim().ToLowerInvariant() switch
            {
                "" => LockstyleMode.Query,
                "on" => LockstyleMode.Enable,
                "off" => LockstyleMode.Disable,
                _ => null,
            };
            if (mode is null) return PlayerActionResult.Warn(LockstyleUsage, ChatCommandResultKind.Lockstyle);

            var module = InventoryModule;
            if (module == null) return PlayerActionResult.Fail("Style lock is unavailable: no inventory module.", ChatCommandResultKind.Lockstyle);

            await module.SetLockstyleAsync(mode.Value).ConfigureAwait(false);
            return PlayerActionResult.Ok(mode switch
            {
                LockstyleMode.Enable => "Style lock on requested.",
                LockstyleMode.Disable => "Style lock off requested.",
                _ => "Style lock status requested.",
            }, ChatCommandResultKind.Lockstyle);
        }

        /// <summary>
        /// <c>/lockstyleset</c>: without a set number retail sends C2S 0x053 mode Enable. A numbered set needs the
        /// client-side equipment sets, which GordianXI does not store yet.
        /// </summary>
        public async Task<PlayerActionResult> LockstyleSetAsync(string args)
        {
            if (!string.IsNullOrWhiteSpace(args))
            {
                return PlayerActionResult.Warn("/lockstyleset <n> needs equipment sets, which are not supported yet. Use /lockstyle on to lock your current look.", ChatCommandResultKind.LockstyleSet);
            }
            return await LockstyleAsync("on").ConfigureAwait(false);
        }

        #region Everyday commands (/heal, /sit, /random, votes, wide scan)

        private async Task<PlayerActionResult> SendCommandAsync(ChatCommandResultKind kind, string name, string okMessage, Func<PlayerCommandPacketModule, Task> send)
        {
            var module = CommandModule;
            if (module == null) return PlayerActionResult.Fail($"/{name} is unavailable: no command module.", kind);
            try
            {
                await send(module).ConfigureAwait(false);
                return PlayerActionResult.Ok(okMessage, kind);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"/{name} failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"/{name} failed: {ex.Message}", kind);
            }
        }

        /// <summary>
        /// <c>/heal</c> (C2S 0x0E8): rests, or stops resting. The server puts the character in the Healing state (status 33,
        /// the Healing status effect) and refuses while engaged, dead, crafting or in an event.
        /// </summary>
        public Task<PlayerActionResult> HealAsync(RestMode mode = RestMode.Toggle)
            => SendCommandAsync(ChatCommandResultKind.Heal, "heal", "Heal requested.", m => m.SendHealAsync(mode));

        /// <summary>
        /// <c>/logout</c> and <c>/shutdown</c> (C2S 0x0E7): no argument toggles the request, <c>on</c> starts it, <c>off</c>
        /// cancels it. The server counts down 30 seconds (LandSandBoat's LEAVEGAME effect, with the character resting) and
        /// then sends S2C 0x00B; standing up cancels. Modes from XiPackets (https://github.com/atom0s/XiPackets,
        /// <c>world/client/0x00E7</c>).
        /// </summary>
        public async Task<PlayerActionResult> RequestLogoutAsync(bool shutdown, RestMode mode = RestMode.Toggle)
        {
            var kindResult = shutdown ? ChatCommandResultKind.Shutdown : ChatCommandResultKind.Logout;
            var kind = shutdown ? ReqLogoutKind.Shutdown : ReqLogoutKind.Logout;
            var reqMode = mode switch
            {
                RestMode.On => shutdown ? ReqLogoutMode.ShutdownOn : ReqLogoutMode.LogoutOn,
                RestMode.Off => ReqLogoutMode.Off,
                _ => ReqLogoutMode.Toggle
            };
            string name = shutdown ? "shutdown" : "logout";
            try
            {
                await _lifecycleModule.RequestLogoutAsync(reqMode, kind).ConfigureAwait(false);
                return PlayerActionResult.Ok(shutdown ? "Shutdown requested." : "Logout requested.", kindResult);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"/{name} failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"/{name} failed: {ex.Message}", kindResult);
            }
        }

        /// <summary><c>/sit</c> (C2S 0x0EA): sits down or stands up; it also stops resting.</summary>
        public Task<PlayerActionResult> SitAsync(RestMode mode = RestMode.Toggle)
            => SendCommandAsync(ChatCommandResultKind.Sit, "sit", "Sit requested.", m => m.SendSitAsync(mode));

        /// <summary><c>/sitchair [n]</c> (C2S 0x113): sits in chair <paramref name="chairId"/> (0 is the plain chair).</summary>
        public Task<PlayerActionResult> SitChairAsync(uint chairId, RestMode mode = RestMode.Toggle)
            => SendCommandAsync(ChatCommandResultKind.SitChair, "sitchair", $"Sit in chair {chairId} requested.", m => m.SendSitChairAsync(chairId, mode));

        /// <summary><c>/random</c> (C2S 0x0A2): the server rolls 0 to 999 and tells everyone near (S2C 0x009 message 88).</summary>
        public Task<PlayerActionResult> RandomAsync(string args = "")
        {
            uint.TryParse(args.Trim(), out uint typed);
            return SendCommandAsync(ChatCommandResultKind.Random, "random", "Dice rolled.", m => m.SendRandomAsync(typed));
        }

        /// <summary><c>/nominate</c> (C2S 0x0A0): proposes <paramref name="text"/> (a question and its options); empty text cancels your live proposal.</summary>
        public Task<PlayerActionResult> ProposeAsync(ProposalKind kind, string text)
            => SendCommandAsync(ChatCommandResultKind.Propose, "nominate",
                string.IsNullOrWhiteSpace(text) ? "Proposal cancel requested." : "Proposal sent.", m => m.SendProposalAsync(kind, text.Trim()));

        /// <summary><c>/vote</c> (C2S 0x0A1): votes for <paramref name="option"/> in <paramref name="proposer"/>'s proposal (the last one seen when empty).</summary>
        public async Task<PlayerActionResult> VoteAsync(byte option, string proposer)
        {
            string name = string.IsNullOrWhiteSpace(proposer) ? CommandModule?.State.Votes.LastProposer ?? string.Empty : proposer.Trim();
            if (name.Length == 0)
            {
                return PlayerActionResult.Warn("There is no proposal to vote on. Usage: /vote <option 1-8> [proposer]", ChatCommandResultKind.Vote);
            }
            return await SendCommandAsync(ChatCommandResultKind.Vote, "vote", $"Voted {option} in {name}'s proposal.", m => m.SendVoteAsync(option, name)).ConfigureAwait(false);
        }

        /// <summary>
        /// <c>/widescan</c> (C2S 0x0F4): asks for the wide scan list. LandSandBoat answers only Rangers and Beastmasters
        /// (or every job when its <c>ALL_JOBS_WIDESCAN</c> setting is on); the list then prints to the message log.
        /// </summary>
        public Task<PlayerActionResult> WideScanAsync()
            => SendCommandAsync(ChatCommandResultKind.WideScan, "widescan", "Wide Scan requested.", m => m.SendWideScanAsync());

        /// <summary>
        /// <c>/track [index|name|off]</c>: tracks a monster or NPC (C2S 0x0F5), the current target without an argument, or
        /// stops (0x0F6) with <c>off</c>. The server must have it within the wide scan range.
        /// </summary>
        public async Task<PlayerActionResult> TrackAsync(string args)
        {
            const ChatCommandResultKind Kind = ChatCommandResultKind.TrackTarget;
            string arg = args.Trim();
            if (arg.Equals("off", StringComparison.OrdinalIgnoreCase) || arg.Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                return await SendCommandAsync(Kind, "track", "Tracking stopped.", m => m.SendTrackingEndAsync()).ConfigureAwait(false);
            }

            WorldEntity? target = null;
            if (arg.Length == 0) target = CurrentTarget;
            else if (ushort.TryParse(arg, out ushort index)) _world.TryGetByTargetIndex(index, out target);
            else _world.TryGetByName(arg, out target);

            if (target == null || target.TargetIndex == 0)
            {
                return PlayerActionResult.Warn("Usage: /track <index|name|off> (or target something first)", Kind);
            }
            ushort actIndex = target.TargetIndex;
            return await SendCommandAsync(Kind, "track", $"Tracking {(target.Name.Length > 0 ? target.Name : "#" + actIndex)} requested.", m => m.SendTrackingStartAsync(actIndex)).ConfigureAwait(false);
        }

        /// <summary>
        /// <c>/jobmasterdisp on|off</c> (C2S 0x11B): shows or hides the job mastery mark. LandSandBoat saves it and, when it
        /// changed, sends S2C 0x037 and 0x067 back.
        /// </summary>
        public async Task<PlayerActionResult> JobMasterDisplayAsync(bool on)
        {
            const ChatCommandResultKind Kind = ChatCommandResultKind.JobMasterDisplay;
            var module = LoginDataModule;
            if (module == null) return PlayerActionResult.Fail("/jobmasterdisp is unavailable: no login data module.", Kind);
            try
            {
                await module.SendMasteryDisplayAsync(on).ConfigureAwait(false);
                return PlayerActionResult.Ok($"Job mastery display {(on ? "on" : "off")} requested.", Kind);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"/jobmasterdisp failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"/jobmasterdisp failed: {ex.Message}", Kind);
            }
        }

        /// <summary>How long <c>/itemsearch</c> and <c>/blacklist list</c> wait for the packet that answers them.</summary>
        public TimeSpan SocialReplyTimeout { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>
        /// <c>/itemsearch &lt;name&gt;</c> (C2S 0x02C): asks the server which item the name is and reports the containers that
        /// hold it from the S2C 0x049 that answers (the client's own inventory state does the container search).
        /// </summary>
        public async Task<PlayerActionResult> ItemSearchAsync(string itemName)
        {
            const ChatCommandResultKind Kind = ChatCommandResultKind.ItemSearch;
            var module = SocialModule;
            if (module == null) return PlayerActionResult.Fail("/itemsearch is unavailable: no social module.", Kind);
            if (string.IsNullOrWhiteSpace(itemName)) return PlayerActionResult.Warn("Usage: /itemsearch <item name>", Kind);

            var answered = new TaskCompletionSource<ItemSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnResult(ItemSearchResult r) => answered.TrySetResult(r);
            module.Social.ItemSearchReceived += OnResult;
            try
            {
                await module.SendItemSearchAsync(itemName.Trim()).ConfigureAwait(false);
                var finished = await Task.WhenAny(answered.Task, Task.Delay(SocialReplyTimeout)).ConfigureAwait(false);
                if (finished != answered.Task) return PlayerActionResult.Warn("Item search sent; the server did not answer.", Kind);

                var result = await answered.Task.ConfigureAwait(false);
                if (result.IsAsync) return PlayerActionResult.Info($"Searching for '{result.ItemName}'.", Kind);
                if (result.ItemId == 0) return PlayerActionResult.Info($"'{result.ItemName}' is not an item the server knows.", Kind);
                if (result.Containers.Count == 0) return PlayerActionResult.Info($"You do not have item {result.ItemId} ('{result.ItemName}').", Kind);
                var where = string.Join(", ", result.Containers.Select(c => $"{c.Container} ({c.Count})"));
                return PlayerActionResult.Info($"'{result.ItemName}' is in: {where}.", Kind);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"/itemsearch failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"/itemsearch failed: {ex.Message}", Kind);
            }
            finally
            {
                module.Social.ItemSearchReceived -= OnResult;
            }
        }

        /// <summary>
        /// <c>/sea [all] [name] [job] [level] [party] [friend]</c>: searches players on the search server (the current zone, or
        /// every zone with <c>all</c>). A three-letter job code picks the job, <c>75</c> or <c>70-75</c> (also <c>lv75</c>) the level,
        /// <c>party</c> players seeking a party, <c>friend</c> friends, and any other word is a name or name prefix. The full
        /// result is in <c>SearchService.State.LastSearch</c>; the message lists the first names.
        /// </summary>
        public async Task<PlayerActionResult> PlayerSearchAsync(string arguments)
        {
            const ChatCommandResultKind Kind = ChatCommandResultKind.PlayerSearch;
            var service = SearchService;
            if (service == null || !service.IsConfigured) return PlayerActionResult.Fail("/sea is unavailable: the search server address is not known yet.", Kind);

            var query = ParseSearchQuery(arguments, _world.CurrentZoneId);
            try
            {
                var result = await service.SearchAsync(query).ConfigureAwait(false);
                if (result.Players.Count == 0)
                {
                    return result.Complete
                        ? PlayerActionResult.Info("No players found.", Kind)
                        : PlayerActionResult.Warn("The search server did not answer.", Kind);
                }

                const int Shown = 12;
                var names = string.Join(", ", result.Players.Take(Shown).Select(p =>
                    p.IsAnonymous ? p.Name : $"{p.Name} ({(JobId)p.MainJob} {p.MainLevel})"));
                string more = result.Total > Shown ? $" and {result.Total - Shown} more" : string.Empty;
                return PlayerActionResult.Info($"{result.Total} player(s) found: {names}{more}.", Kind);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"/sea failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"/sea failed: {ex.Message}", Kind);
            }
        }

        /// <summary>Turns the arguments of <c>/sea</c> into a search; see <see cref="PlayerSearchAsync"/>.</summary>
        public static SearchQuery ParseSearchQuery(string arguments, ushort currentZone)
        {
            var query = new SearchQuery();
            foreach (string raw in (arguments ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string token = raw.ToLowerInvariant();
                if (token == "all") { query.AllAreas = true; continue; }
                if (token is "party" or "pt") { query.Flags1 = (ushort)SearchFlags.SeekingParty; continue; }
                if (token == "friend" || token == "friends") { query.FriendsOnly = true; continue; }

                string levelText = token.StartsWith("lv", StringComparison.Ordinal) ? token[2..] : token;
                if (TryParseLevelRange(levelText, out var range)) { query.Level = range; continue; }

                if (token.Length == 3 && SearchJobCodes.TryGetValue(token, out var job)) { query.Job = (byte)job; continue; }

                query.Name ??= raw;
            }
            if (!query.AllAreas && currentZone != 0) query.Areas.Add(currentZone);
            return query;
        }

        private static bool TryParseLevelRange(string text, out (byte Min, byte Max) range)
        {
            range = default;
            string[] parts = text.Split('-');
            if (parts.Length is < 1 or > 2) return false;
            if (!byte.TryParse(parts[0], out byte min) || min == 0 || min > 99) return false;
            byte max = min;
            if (parts.Length == 2 && (!byte.TryParse(parts[1], out max) || max < min || max > 99)) return false;
            range = (min, max);
            return true;
        }

        private static readonly Dictionary<string, JobId> SearchJobCodes = new()
        {
            ["war"] = JobId.Warrior, ["mnk"] = JobId.Monk, ["whm"] = JobId.WhiteMage, ["blm"] = JobId.BlackMage,
            ["rdm"] = JobId.RedMage, ["thf"] = JobId.Thief, ["pld"] = JobId.Paladin, ["drk"] = JobId.DarkKnight,
            ["bst"] = JobId.Beastmaster, ["brd"] = JobId.Bard, ["rng"] = JobId.Ranger, ["sam"] = JobId.Samurai,
            ["nin"] = JobId.Ninja, ["drg"] = JobId.Dragoon, ["smn"] = JobId.Summoner, ["blu"] = JobId.BlueMage,
            ["cor"] = JobId.Corsair, ["pup"] = JobId.Puppetmaster, ["dnc"] = JobId.Dancer, ["sch"] = JobId.Scholar,
            ["geo"] = JobId.Geomancer, ["run"] = JobId.RuneFencer
        };

        /// <summary>
        /// <c>/blacklist add|delete &lt;name&gt;</c> (C2S 0x03D) and <c>/blacklist [list]</c> (C2S 0x03C, then the S2C 0x041
        /// pages): edits or lists the character's blacklist.
        /// </summary>
        public async Task<PlayerActionResult> BlacklistAsync(string arguments)
        {
            const ChatCommandResultKind Kind = ChatCommandResultKind.Blacklist;
            var module = SocialModule;
            if (module == null) return PlayerActionResult.Fail("/blacklist is unavailable: no social module.", Kind);

            string[] parts = (arguments ?? string.Empty).Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : "list";
            string name = parts.Length > 1 ? parts[1] : string.Empty;

            try
            {
                switch (verb)
                {
                    case "add" when name.Length > 0:
                        await module.AddToBlacklistAsync(name).ConfigureAwait(false);
                        return PlayerActionResult.Ok($"Blacklist add requested for {name}.", Kind);
                    case "delete" or "remove" or "del" when name.Length > 0:
                        await module.RemoveFromBlacklistAsync(name).ConfigureAwait(false);
                        return PlayerActionResult.Ok($"Blacklist removal requested for {name}.", Kind);
                    case "list":
                    {
                        if (!module.Blacklist.IsComplete)
                        {
                            var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                            void OnChanged() { if (module.Blacklist.IsComplete) answered.TrySetResult(); }
                            module.Blacklist.Changed += OnChanged;
                            try
                            {
                                await module.RequestBlacklistAsync().ConfigureAwait(false);
                                await Task.WhenAny(answered.Task, Task.Delay(SocialReplyTimeout)).ConfigureAwait(false);
                            }
                            finally
                            {
                                module.Blacklist.Changed -= OnChanged;
                            }
                        }
                        var entries = module.Blacklist.Snapshot();
                        return entries.Count == 0
                            ? PlayerActionResult.Info("Your blacklist is empty.", Kind)
                            : PlayerActionResult.Info($"Blacklist ({entries.Count}): {string.Join(", ", entries.Select(e => e.Name))}.", Kind);
                    }
                    default:
                        return PlayerActionResult.Warn("Usage: /blacklist add <name> | delete <name> | list", Kind);
                }
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"/blacklist failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"/blacklist failed: {ex.Message}", Kind);
            }
        }

        /// <summary>How long <c>/conquest</c> waits for the S2C 0x05E that answers it.</summary>
        public TimeSpan ConquestReplyTimeout { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>
        /// <c>/conquest</c> (C2S 0x05A): asks for the conquest overview and reports the conquest points and Imperial
        /// Standing from the S2C 0x05E that answers. The conquest window does not exist yet; the full data is in
        /// <see cref="ProgressionState"/>.
        /// </summary>
        public async Task<PlayerActionResult> ConquestAsync()
        {
            const ChatCommandResultKind Kind = ChatCommandResultKind.ConquestRequest;
            var module = ProgressionModule;
            if (module == null) return PlayerActionResult.Fail("/conquest is unavailable: no progression module.", Kind);

            var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnConquest() => answered.TrySetResult();
            module.State.ConquestUpdated += OnConquest;
            try
            {
                await module.SendReqConquestAsync().ConfigureAwait(false);
                var finished = await Task.WhenAny(answered.Task, Task.Delay(ConquestReplyTimeout)).ConfigureAwait(false);
                if (finished != answered.Task) return PlayerActionResult.Warn("Conquest status requested; the server did not answer.", Kind);
                return PlayerActionResult.Info($"Conquest points: {module.State.ConquestPoints}, Imperial Standing: {module.State.ImperialStanding}.", Kind);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"/conquest failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"/conquest failed: {ex.Message}", Kind);
            }
            finally
            {
                module.State.ConquestUpdated -= OnConquest;
            }
        }

        #endregion

        #region Crafting and guild shops

        /// <summary>
        /// Debug: <c>/playroutine &lt;name&gt;</c> plays a motion routine of the character's own model (a four-character
        /// name such as <c>sit0</c> or <c>cabk</c>), on the client only. Used to look for routines, such as the synthesis
        /// motion (#112), that have not been identified.
        /// </summary>
        public PlayerActionResult PlayRoutineCommand(string args)
        {
            const ChatCommandResultKind kind = ChatCommandResultKind.DebugPlayRoutine;
            string name = args.Trim();
            if (name.Length is < 1 or > 4) return PlayerActionResult.Warn("Usage: /playroutine <routine name, e.g. sit0>", kind);
            if (!_world.TryGetByServerId(_localPlayer.ServerId, out var self) || self == null) return PlayerActionResult.Warn("There is no character to animate.", kind);
            self.Animation.EnqueueAction(new Gordian.Core.Animation.ActionRequest
            {
                ActorId = self.ServerId,
                Motion = Gordian.Core.Animation.ActionMotion.EventMotion,
                Routine = name,
                ReceivedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
            });
            return PlayerActionResult.Info($"Routine {name} queued.", kind);
        }

        private const string SynthUsage = "Usage: /synth <crystal slot> <ingredient slot> [ingredient slot ...] (inventory slots; repeat a slot to use several of a stack, 8 at most)";
        private const string GuildUsage = "Usage: /guild buylist | selllist | buy <item id> [count] | sell <inventory slot> [count]";

        /// <summary>
        /// <c>/synth</c>: reads the crystal and ingredient items from the inventory slots and sends C2S 0x096. The server
        /// answers with the animation (S2C 0x030) and the result (0x06F, 0x070). A slot repeated takes one more item of its
        /// stack, as retail's synthesis window spreads a stack over the eight entries.
        /// </summary>
        public async Task<PlayerActionResult> SynthesizeCommandAsync(string args)
        {
            const ChatCommandResultKind kind = ChatCommandResultKind.Synthesize;
            // Retail refuses commands while the character synthesizes, in its own words, and sends nothing.
            if (CraftingModule?.State.IsSynthesizing == true) return PlayerActionResult.Warn("You cannot use that command during synthesis.", kind);

            var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || parts.Length > 1 + CraftingPacketBuilders.MaxIngredients) return PlayerActionResult.Warn(SynthUsage, kind);

            var slots = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!byte.TryParse(parts[i], out slots[i]) || slots[i] == 0) return PlayerActionResult.Warn(SynthUsage, kind);
            }

            var module = CraftingModule;
            var inventory = InventoryModule?.State;
            if (module == null || inventory == null) return PlayerActionResult.Fail("Synthesis is unavailable.", kind);

            var bag = inventory.GetContainer(ContainerId.Inventory);
            if (!bag.TryGetItem(slots[0], out var crystal) || crystal.ItemId == 0) return PlayerActionResult.Warn($"Inventory slot {slots[0]} is empty.", kind);

            var used = new Dictionary<byte, int>();
            var ingredients = new (ushort ItemId, byte Slot)[slots.Length - 1];
            for (int i = 1; i < slots.Length; i++)
            {
                if (!bag.TryGetItem(slots[i], out var item) || item.ItemId == 0) return PlayerActionResult.Warn($"Inventory slot {slots[i]} is empty.", kind);
                used[slots[i]] = used.GetValueOrDefault(slots[i]) + 1;
                if (used[slots[i]] > item.Count) return PlayerActionResult.Warn($"Inventory slot {slots[i]} does not hold {used[slots[i]]} items.", kind);
                ingredients[i - 1] = (item.ItemId, slots[i]);
            }

            try
            {
                await module.SynthesizeAsync(crystal.ItemId, slots[0], ingredients).ConfigureAwait(false);
                // Retail prints nothing for the request; the animation and the result lines follow.
                return PlayerActionResult.Ok(string.Empty, kind);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"Synthesis failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"Synthesis failed: {ex.Message}", kind);
            }
        }

        /// <summary>
        /// Asks a crafting guild NPC for recipes (C2S 0x058); the answer arrives in <see cref="CraftingState"/>.
        /// </summary>
        public async Task<PlayerActionResult> RequestRecipeAsync(ushort skill, ushort level, RecipeRequestMode mode,
            ushort param0 = 0, ushort param1 = 0, ushort param2 = 0, ushort param3 = 0, ushort param4 = 0)
        {
            var module = CraftingModule;
            if (module == null) return PlayerActionResult.Fail("Recipes are unavailable.", ChatCommandResultKind.Synthesize);
            await module.RequestRecipeAsync(skill, level, mode, param0, param1, param2, param3, param4).ConfigureAwait(false);
            return PlayerActionResult.Info("Recipe requested.", ChatCommandResultKind.Synthesize);
        }

        /// <summary>
        /// <c>/guild</c>: the guild shop requests, for use while a guild shop is open (S2C 0x086). The answers fill
        /// <see cref="InventoryState"/> (the guild buy and sell lists and the last transaction).
        /// </summary>
        public async Task<PlayerActionResult> GuildShopCommandAsync(string args)
        {
            const ChatCommandResultKind kind = ChatCommandResultKind.GuildShop;
            var module = InventoryModule;
            if (module == null) return PlayerActionResult.Fail("The guild shop is unavailable.", kind);

            var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return PlayerActionResult.Warn(GuildUsage, kind);

            try
            {
                switch (parts[0].ToLowerInvariant())
                {
                    case "buylist" when parts.Length == 1:
                        await module.RequestGuildBuyListAsync().ConfigureAwait(false);
                        return PlayerActionResult.Info("Guild buy list requested.", kind);

                    case "selllist" when parts.Length == 1:
                        await module.RequestGuildSellListAsync().ConfigureAwait(false);
                        return PlayerActionResult.Info("Guild sell list requested.", kind);

                    case "buy" when parts.Length is 2 or 3:
                    {
                        byte count = 1;
                        if (!ushort.TryParse(parts[1], out ushort itemId) || (parts.Length == 3 && !byte.TryParse(parts[2], out count)) || count is < 1 or > 99)
                        {
                            return PlayerActionResult.Warn(GuildUsage, kind);
                        }
                        await module.BuyGuildItemAsync(itemId, count).ConfigureAwait(false);
                        return PlayerActionResult.Info($"Guild purchase of item {itemId} x{count} requested.", kind);
                    }

                    case "sell" when parts.Length is 2 or 3:
                    {
                        byte count = 1;
                        if (!byte.TryParse(parts[1], out byte slot) || slot == 0 || (parts.Length == 3 && !byte.TryParse(parts[2], out count)) || count is < 1 or > 99)
                        {
                            return PlayerActionResult.Warn(GuildUsage, kind);
                        }
                        if (!module.State.GetContainer(ContainerId.Inventory).TryGetItem(slot, out var item) || item.ItemId == 0)
                        {
                            return PlayerActionResult.Warn($"Inventory slot {slot} is empty.", kind);
                        }
                        await module.SellGuildItemAsync(item.ItemId, slot, count).ConfigureAwait(false);
                        return PlayerActionResult.Info($"Guild sale of slot {slot} x{count} requested.", kind);
                    }

                    default:
                        return PlayerActionResult.Warn(GuildUsage, kind);
                }
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"Guild shop request failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"Guild shop request failed: {ex.Message}", kind);
            }
        }

        #endregion

        #region Treasure pool

        /// <summary>
        /// <c>/lot [slot]</c> and <c>/pass [slot]</c>: lots or passes on treasure pool item <paramref name="args"/>
        /// (slot 0 to 9 as the pool window numbers them, the first item being 0), or on every item you have not yet
        /// entered when no slot is given. The server answers each with S2C 0x0D3, which prints the lot result.
        /// </summary>
        public async Task<PlayerActionResult> TreasureAsync(string args, bool lot)
        {
            var kind = lot ? ChatCommandResultKind.TreasureLot : ChatCommandResultKind.TreasurePass;
            string verb = lot ? "lot" : "pass";
            var module = TreasureModule;
            if (module == null) return PlayerActionResult.Fail("The treasure pool is unavailable.", kind);

            var pool = module.Pool;
            var slots = new List<byte>();
            if (string.IsNullOrWhiteSpace(args))
            {
                foreach (var slot in pool.Snapshot())
                {
                    if (slot.Entry == TreasureEntryKind.None) slots.Add(slot.Slot);
                }
                if (slots.Count == 0) return PlayerActionResult.Warn("There is nothing in the treasure pool to " + verb + " on.", kind);
            }
            else
            {
                if (!byte.TryParse(args.Trim(), out byte index) || index >= TreasurePoolState.SlotCount)
                {
                    return PlayerActionResult.Warn($"Usage: /{verb} [slot 0-{TreasurePoolState.SlotCount - 1}]", kind);
                }
                var slot = pool.GetSlot(index);
                if (slot == null) return PlayerActionResult.Warn($"Treasure pool slot {index} is empty.", kind);
                if (slot.Entry != TreasureEntryKind.None)
                {
                    return PlayerActionResult.Warn($"You have already {(slot.Entry == TreasureEntryKind.Lot ? "cast lots" : "passed")} on slot {index}.", kind);
                }
                slots.Add(index);
            }

            try
            {
                foreach (byte s in slots)
                {
                    await (lot ? module.SendLotAsync(s) : module.SendPassAsync(s)).ConfigureAwait(false);
                }
                return PlayerActionResult.Info($"{(lot ? "Lot" : "Pass")} sent for {slots.Count} treasure pool item{(slots.Count == 1 ? "" : "s")}.", kind);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"Treasure {verb} failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"Treasure {verb} failed: {ex.Message}", kind);
            }
        }

        #endregion

        /// <summary>
        /// Marks a held key item as read (C2S 0x064), as retail does when an unseen key item is first viewed.
        /// Returns false when the local player's actor index is unknown or the progression module is missing.
        /// </summary>
        public async Task<bool> MarkKeyItemSeenAsync(ushort keyItemId)
        {
            var module = ProgressionModule;
            if (module == null) return false;
            if (!_world.TryGetByServerId(_localPlayer.ServerId, out var self) || self == null) return false;

            await module.MarkKeyItemSeenAsync(keyItemId, self.TargetIndex).ConfigureAwait(false);
            return true;
        }

        #endregion

        /// <summary>
        /// Returns a formatted list of Game Master (GM) commands.
        /// Reports 'You are not a GM.' if the local player does not possess GM privileges.
        /// </summary>
        public string GetGmCommandsSummary(string? filter = null)
        {
            if (!IsLocalPlayerGm())
            {
                return "You are not a GM.";
            }

            var restrictions = _profile.FeatureRestrictions;
            var sb = new StringBuilder();
            sb.AppendLine($"--- Game Master (GM) Commands [Restrictions: {restrictions}] ---");
            sb.AppendLine("[Locomotion & Teleportation]");
            sb.AppendLine("  !pos [x y z [zone]]       - Query or set coordinates (Windower order, z = height)");
            sb.AppendLine("  !goto <player>            - Teleport to player");
            sb.AppendLine("  !bring <player>           - Teleport player to you");
            sb.AppendLine("  !zone <zone_id>           - Teleport to specified zone");
            sb.AppendLine("  !wall                     - Toggle noclip / collision");
            sb.AppendLine("  !speed <val>              - Set movement speed multiplier");
            sb.AppendLine("[Character & State]");
            sb.AppendLine("  !heal [hp] [mp]           - Restore or set vitals");
            sb.AppendLine("  !god                      - Toggle god mode (invulnerable)");
            sb.AppendLine("  !vanish / !hide           - Toggle GM invisibility");
            sb.AppendLine("  !level <1-99>             - Set main job level");
            sb.AppendLine("  !job <job_id>             - Change main job");
            sb.AppendLine("  !sjob <job_id>            - Change sub job");
            sb.AppendLine("  !dispel                   - Clear all buffs and status effects");
            sb.AppendLine("  !costume <id>             - Change model appearance");
            sb.AppendLine("[Inventory & World]");
            sb.AppendLine("  !additem <id> [qty]       - Add item to inventory");
            sb.AppendLine("  !addgil <qty>             - Add gil");
            sb.AppendLine("  !spawn <mob_id>           - Spawn entity/NPC");
            sb.AppendLine("  !kill                     - Kill targeted entity");
            sb.AppendLine("  !weather <id>             - Set zone weather");
            sb.AppendLine("  !time <hh:mm>             - Set world time");
            sb.AppendLine("  !reload                   - Reload server scripts");

            return sb.ToString().TrimEnd();
        }

        #endregion

        /// <summary>
        /// Handles <c>/collision [ground|walls|entities|all] [on|off]</c>: no arguments reports the state, a layer alone
        /// toggles it, and on/off alone applies to every layer. Layers the server protects stay on.
        /// </summary>
        public PlayerActionResult ApplyCollisionCommand(string args)
        {
            const ChatCommandResultKind Kind = ChatCommandResultKind.CollisionToggle;
            var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var layers = CollisionLayers.None;
            bool? turnOn = null;
            foreach (string part in parts)
            {
                string word = part.ToLowerInvariant();
                if (word is "on" or "enable") turnOn = true;
                else if (word is "off" or "disable") turnOn = false;
                else if (CollisionSettings.TryParseLayer(word, out var layer)) layers |= layer;
                else return PlayerActionResult.Warn("Usage: /collision [ground|walls|entities|all] [on|off]", Kind);
            }

            if (parts.Length > 0)
            {
                if (layers == CollisionLayers.None) layers = CollisionLayers.All;
                bool enable = turnOn ?? (Collision.Requested & layers) != layers;
                Collision.Requested = enable ? Collision.Requested | layers : Collision.Requested & ~layers;
            }

            var locked = CollisionSettings.GetLocked(_profile);
            var effective = Collision.GetEffective(_profile);
            string Describe(CollisionLayers layer, string name)
            {
                string state = (effective & layer) != 0 ? "on" : "off";
                return (locked & layer) != 0 && (Collision.Requested & layer) == 0 ? $"{name} {state} (server-enforced)" : $"{name} {state}";
            }

            string summary = $"Collision: {Describe(CollisionLayers.Ground, "ground")}, {Describe(CollisionLayers.Walls, "walls")}, " +
                             $"{Describe(CollisionLayers.Entities, "entities")}.";
            return (Collision.Requested | locked) != Collision.Requested && parts.Length > 0
                ? PlayerActionResult.Warn(summary, Kind)
                : PlayerActionResult.Ok(summary, Kind);
        }

        /// <summary>
        /// Handles <c>/anchor [on|off]</c>: no argument toggles, and the reply says whether knockback is ignored. The server's
        /// <see cref="FeatureRestrictions.KnockbackOverride"/> bit keeps knockback on whatever the player asks.
        /// </summary>
        public PlayerActionResult ApplyAnchorCommand(string args)
        {
            const ChatCommandResultKind Kind = ChatCommandResultKind.AnchorToggle;
            string word = args.Trim().ToLowerInvariant();
            if (word is "on" or "enable") Knockback.AnchorRequested = true;
            else if (word is "off" or "disable") Knockback.AnchorRequested = false;
            else if (word.Length == 0) Knockback.AnchorRequested = !Knockback.AnchorRequested;
            else return PlayerActionResult.Warn("Usage: /anchor [on|off]", Kind);

            if (Knockback.AnchorRequested && KnockbackSettings.IsAnchorLocked(_profile))
            {
                return PlayerActionResult.Warn("Anchor: off (the server keeps knockback on).", Kind);
            }
            return PlayerActionResult.Ok(Knockback.AnchorRequested ? "Anchor: on (knockback ignored)." : "Anchor: off.", Kind);
        }

        private const string UiLayoutUsage =
            "Usage: /uilayout [unlock | lock | scale <n> | skin <1-8> | tp <on|off> | buffs <on|off|left|right> | names <n|default> | reset [positions]] or /uilayout <window> <hide | show | reset | scale <n|default> | move <x> <y> [topleft|topright|bottomleft|bottomright]>. " +
            "Windows: log, chat, party, alliance1, alliance2, target, status, menu, query, command, shop, dead, check. Positions are 512x448 layout pixels, measured from the side of the window's anchor corner. " +
            "While unlocked, drag the outlined windows with the mouse.";

        private static readonly string[] UiWindowIds =
        {
            StockUiWindowIds.Log, StockUiWindowIds.ChatInput, StockUiWindowIds.Party, StockUiWindowIds.Alliance1, StockUiWindowIds.Alliance2,
            StockUiWindowIds.Target, StockUiWindowIds.StatusIcons, StockUiWindowIds.MainMenu, StockUiWindowIds.Query, StockUiWindowIds.CommandMenu,
            StockUiWindowIds.Shop, StockUiWindowIds.Death, StockUiWindowIds.Check,
        };

        /// <summary>
        /// Handles <c>/uilayout</c>: the global stock UI scale and per-window overrides (move, re-anchor, scale, hide,
        /// reset). No arguments reports the current layout. Changes save to the character's layout file at once.
        /// </summary>
        public PlayerActionResult ApplyUiLayoutCommand(string args)
        {
            const ChatCommandResultKind Kind = ChatCommandResultKind.UiLayout;
            var layout = UiLayout;
            var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            static bool TryFloat(string text, out float value) =>
                float.TryParse(text.Trim(',', '(', ')'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

            if (parts.Length == 0) return PlayerActionResult.Info(DescribeUiLayout(layout), Kind);

            string first = parts[0].ToLowerInvariant();
            if (first == "reset" && parts.Length == 1)
            {
                layout.ResetAll();
                return PlayerActionResult.Ok("Stock UI restored to the retail layout.", Kind);
            }
            if (first == "reset" && parts.Length == 2 && parts[1].Equals("positions", StringComparison.OrdinalIgnoreCase))
            {
                layout.ResetPositions();
                return PlayerActionResult.Ok("Every stock window is back at its retail position.", Kind);
            }
            if (first is "unlock" or "lock" && parts.Length == 1)
            {
                // Opt-in (not in the legacy client): while unlocked the HUD outlines the persistent windows and the
                // mouse drags them. The state is saved with the layout.
                UiDrag.Layout = layout;
                layout.SetUnlocked(first == "unlock");
                return PlayerActionResult.Ok(layout.Unlocked
                    ? "Stock UI unlocked: drag the outlined windows with the mouse; Default positions resets them; Esc or /uilayout lock when done."
                    : "Stock UI locked.", Kind);
            }
            if (first == "skin" && parts.Length == 2 && int.TryParse(parts[1], out int skin))
            {
                layout.SetWindowSkin(skin);
                return PlayerActionResult.Ok($"Window skin {layout.WindowSkin} (the config menu's Window Type).", Kind);
            }
            if (first == "tp" && parts.Length == 2 && parts[1].ToLowerInvariant() is "on" or "off")
            {
                layout.SetShowPartyTp(parts[1].Equals("on", StringComparison.OrdinalIgnoreCase));
                return PlayerActionResult.Ok($"Party window TP {(layout.ShowPartyTp ? "shown" : "hidden (retail)")}.", Kind);
            }
            if (first == "buffs" && parts.Length == 2 && parts[1].ToLowerInvariant() is "on" or "off" or "left" or "right")
            {
                // "left"/"right" turn the icons on and pick the side of the party window they are drawn on.
                string option = parts[1].ToLowerInvariant();
                PartyStatusIconSide? side = option switch { "left" => PartyStatusIconSide.Left, "right" => PartyStatusIconSide.Right, _ => null };
                layout.SetShowPartyStatusIcons(option != "off", side);
                return PlayerActionResult.Ok(layout.ShowPartyStatusIcons
                    ? $"Party member status icons shown on the {layout.PartyStatusIconSide.ToString().ToLowerInvariant()} of the party window."
                    : "Party member status icons hidden (retail).", Kind);
            }
            if (first == "names" && parts.Length == 2 && (parts[1].Equals("default", StringComparison.OrdinalIgnoreCase) || TryFloat(parts[1], out _)))
            {
                // The in-world name plates' size, as a multiple of the default.
                layout.SetNamePlateScale(TryFloat(parts[1], out float names) ? names : 1.0f);
                return PlayerActionResult.Ok($"Name plate size {layout.NamePlateScale:0.##}x.", Kind);
            }
            if (first == "scale" && parts.Length == 2 && TryFloat(parts[1], out float globalScale))
            {
                layout.SetScale(globalScale);
                return PlayerActionResult.Ok($"Stock UI scale {layout.Scale:0.##}.", Kind);
            }

            string? window = Array.Find(UiWindowIds, id => id == first);
            if (window == null || parts.Length < 2) return PlayerActionResult.Warn(UiLayoutUsage, Kind);

            switch (parts[1].ToLowerInvariant())
            {
                case "hide":
                    layout.SetHidden(window, true);
                    return PlayerActionResult.Ok($"Stock {window} window hidden.", Kind);
                case "show":
                    layout.SetHidden(window, false);
                    return PlayerActionResult.Ok($"Stock {window} window shown.", Kind);
                case "reset":
                    layout.Reset(window);
                    return PlayerActionResult.Ok($"Stock {window} window restored to its retail placement.", Kind);
                case "scale" when parts.Length == 3:
                    if (parts[2].Equals("default", StringComparison.OrdinalIgnoreCase))
                    {
                        layout.SetWindowScale(window, null);
                        return PlayerActionResult.Ok($"Stock {window} window uses the global scale.", Kind);
                    }
                    if (!TryFloat(parts[2], out float windowScale)) break;
                    layout.SetWindowScale(window, windowScale);
                    return PlayerActionResult.Ok($"Stock {window} window scale x{windowScale:0.##}.", Kind);
                case "move" when parts.Length is 4 or 5:
                    if (!TryFloat(parts[2], out float x) || !TryFloat(parts[3], out float y)) break;
                    UiAnchor? anchor = null;
                    if (parts.Length == 5)
                    {
                        if (!Enum.TryParse(parts[4], ignoreCase: true, out UiAnchor parsed) || !Enum.IsDefined(parsed)) break;
                        anchor = parsed;
                    }
                    layout.SetPosition(window, x, y, anchor);
                    string anchored = anchor != null ? $", anchored {anchor}" : string.Empty;
                    return PlayerActionResult.Ok($"Stock {window} window moved to ({x:0.#}, {y:0.#}){anchored}.", Kind);
            }
            return PlayerActionResult.Warn(UiLayoutUsage, Kind);
        }

        private static string DescribeUiLayout(StockUiLayout layout)
        {
            var sb = new StringBuilder($"Stock UI scale {layout.Scale:0.##}, window skin {layout.WindowSkin}{(layout.ShowPartyTp ? ", party TP shown" : string.Empty)}" +
                (layout.ShowPartyStatusIcons ? $", party status icons on the {layout.PartyStatusIconSide.ToString().ToLowerInvariant()}" : string.Empty) +
                (layout.NamePlateScale != 1.0f ? $", name plates {layout.NamePlateScale:0.##}x" : string.Empty));
            var overrides = layout.GetOverrides();
            if (overrides.Count == 0) return sb.Append("; every window at its retail placement.").ToString();
            foreach (var (id, o) in overrides)
            {
                var details = new List<string>();
                if (o.X != null || o.Y != null) details.Add($"at ({o.X:0.#}, {o.Y:0.#})");
                if (o.Anchor != null) details.Add($"anchored {o.Anchor}");
                if (o.Scale != null) details.Add($"scale x{o.Scale:0.##}");
                if (o.Hidden) details.Add("hidden");
                sb.Append($"; {id}: {string.Join(", ", details)}");
            }
            return sb.Append('.').ToString();
        }

        #region Unified Command Router Dispatcher

        /// <summary>
        /// Parses and executes a raw slash command, server !command, or speech line.
        /// </summary>
        public async Task<PlayerActionResult> ExecuteCommandAsync(string rawInput, ChatSendKind defaultSpeechKind = ChatSendKind.Say)
        {
            if (string.IsNullOrWhiteSpace(rawInput))
            {
                return PlayerActionResult.Warn("Input is empty.");
            }

            var cmd = ChatCommandRouter.Parse(rawInput, defaultSpeechKind, _world);

            switch (cmd.Kind)
            {
                // Inspection & Telemetry
                case ChatCommandResultKind.InspectPos:
                    return PlayerActionResult.Info(GetPositionSummary(), cmd.Kind);

                case ChatCommandResultKind.InspectVitals:
                    return PlayerActionResult.Info(GetVitalsSummary(), cmd.Kind);

                case ChatCommandResultKind.InspectTargetInfo:
                    return PlayerActionResult.Info(GetTargetInfoSummary(), cmd.Kind);

                case ChatCommandResultKind.InspectNearby:
                    return PlayerActionResult.Info(GetNearbySummary(cmd.ParamFloat), cmd.Kind);

                // Command Discovery
                case ChatCommandResultKind.DiscoverCommands:
                    return PlayerActionResult.Info(GetStandardCommandsSummary(cmd.Message), cmd.Kind);

                case ChatCommandResultKind.DiscoverGmCommands:
                {
                    if (!IsLocalPlayerGm())
                    {
                        return PlayerActionResult.Warn("You are not a GM.", cmd.Kind);
                    }
                    return PlayerActionResult.Info(GetGmCommandsSummary(cmd.Message), cmd.Kind);
                }

                // Targeting
                case ChatCommandResultKind.SetTarget:
                {
                    if (string.IsNullOrWhiteSpace(cmd.TargetName) && cmd.TargetServerId == 0)
                    {
                        ClearTarget();
                        return PlayerActionResult.Info("Target cleared.", cmd.Kind);
                    }

                    if (cmd.TargetServerId != 0 && SetTargetByServerId(cmd.TargetServerId))
                    {
                        return PlayerActionResult.Ok($"Targeted {CurrentTarget?.Name} [0x{cmd.TargetServerId:X8}].", cmd.Kind);
                    }

                    if (SetTargetByName(cmd.TargetName))
                    {
                        return PlayerActionResult.Ok($"Targeted {CurrentTarget?.Name}.", cmd.Kind);
                    }

                    return PlayerActionResult.Warn($"Target '{cmd.TargetName}' not found in area.", cmd.Kind);
                }

                // Targeting & Lock-On
                case ChatCommandResultKind.ToggleLockOn:
                    {
                        string lockArgs = (cmd.Message ?? string.Empty).Trim();
                        if (lockArgs.StartsWith("auto", StringComparison.OrdinalIgnoreCase))
                        {
                            string mode = lockArgs[4..].Trim();
                            bool on = mode.Length == 0 ? !_uiSettings.IsOn(StockUiSettingKey.AutoLockOnEngage) : mode.Equals("on", StringComparison.OrdinalIgnoreCase);
                            if (mode.Length != 0 && !on && !mode.Equals("off", StringComparison.OrdinalIgnoreCase))
                                return PlayerActionResult.Warn("Usage: /lockon [auto [on|off]]", ChatCommandResultKind.ToggleLockOn);
                            _uiSettings.SetValue(StockUiSettingKey.AutoLockOnEngage, on ? 1 : 0);
                            return PlayerActionResult.Ok(on ? "Auto lock-on when engaging: on." : "Auto lock-on when engaging: off.", ChatCommandResultKind.ToggleLockOn);
                        }
                    }
                    if (CurrentTarget == null)
                    {
                        return PlayerActionResult.Warn("Cannot lock on: No target selected.", ChatCommandResultKind.ToggleLockOn);
                    }
                    ToggleLockOn();
                    return PlayerActionResult.Ok(IsLockedOn ? $"Locked on to {CurrentTarget.Name}." : "Lock-on released.", ChatCommandResultKind.ToggleLockOn);

                case ChatCommandResultKind.CollisionToggle:
                    return ApplyCollisionCommand(cmd.Message ?? string.Empty);

                case ChatCommandResultKind.AnchorToggle:
                    return ApplyAnchorCommand(cmd.Message ?? string.Empty);

                case ChatCommandResultKind.UiLayout:
                    return ApplyUiLayoutCommand(cmd.Message ?? string.Empty);

                case ChatCommandResultKind.TreasureLot:
                    return await TreasureAsync(cmd.Message ?? string.Empty, lot: true).ConfigureAwait(false);

                case ChatCommandResultKind.TreasurePass:
                    return await TreasureAsync(cmd.Message ?? string.Empty, lot: false).ConfigureAwait(false);

                case ChatCommandResultKind.Synthesize:
                    return await SynthesizeCommandAsync(cmd.Message ?? string.Empty).ConfigureAwait(false);

                case ChatCommandResultKind.GuildShop:
                    return await GuildShopCommandAsync(cmd.Message ?? string.Empty).ConfigureAwait(false);

                case ChatCommandResultKind.Heal:
                    return await HealAsync(cmd.Rest).ConfigureAwait(false);

                case ChatCommandResultKind.Sit:
                    return await SitAsync(cmd.Rest).ConfigureAwait(false);

                case ChatCommandResultKind.Logout:
                    return await RequestLogoutAsync(shutdown: false, cmd.Rest).ConfigureAwait(false);

                case ChatCommandResultKind.Shutdown:
                    return await RequestLogoutAsync(shutdown: true, cmd.Rest).ConfigureAwait(false);

                case ChatCommandResultKind.SitChair:
                    return await SitChairAsync(cmd.ActionParam, cmd.Rest).ConfigureAwait(false);

                case ChatCommandResultKind.Random:
                    return await RandomAsync(cmd.Message ?? string.Empty).ConfigureAwait(false);

                case ChatCommandResultKind.Propose:
                    return await ProposeAsync((ProposalKind)cmd.ActionParam, cmd.Message ?? string.Empty).ConfigureAwait(false);

                case ChatCommandResultKind.Vote:
                    return await VoteAsync((byte)cmd.ActionParam, cmd.TargetName).ConfigureAwait(false);

                case ChatCommandResultKind.WideScan:
                    return await WideScanAsync().ConfigureAwait(false);

                case ChatCommandResultKind.TrackTarget:
                    return await TrackAsync(cmd.Message ?? string.Empty).ConfigureAwait(false);

                case ChatCommandResultKind.ConquestRequest:
                    return await ConquestAsync().ConfigureAwait(false);

                case ChatCommandResultKind.PlayerSearch:
                    return await PlayerSearchAsync(cmd.Message ?? string.Empty).ConfigureAwait(false);

                case ChatCommandResultKind.ItemSearch:
                    return await ItemSearchAsync(cmd.Message ?? string.Empty).ConfigureAwait(false);

                case ChatCommandResultKind.Blacklist:
                    return await BlacklistAsync(cmd.Message ?? string.Empty).ConfigureAwait(false);

                case ChatCommandResultKind.JobMasterDisplay:
                    return await JobMasterDisplayAsync(cmd.Rest == RestMode.On).ConfigureAwait(false);

                case ChatCommandResultKind.Lockstyle:
                    return await LockstyleAsync(cmd.Message ?? string.Empty).ConfigureAwait(false);

                case ChatCommandResultKind.DebugPlaySound:
                case ChatCommandResultKind.DebugPlayMusic:
                {
                    // Debug audio commands: handled by the client's audio engine, never sent to the server.
                    bool music = cmd.Kind == ChatCommandResultKind.DebugPlayMusic;
                    var handler = DebugAudioCommand;
                    return handler is null
                        ? PlayerActionResult.Warn("Audio is not available.", cmd.Kind)
                        : PlayerActionResult.Info(handler(music, cmd.Message ?? string.Empty), cmd.Kind);
                }

                case ChatCommandResultKind.LockstyleSet:
                    return await LockstyleSetAsync(cmd.Message ?? string.Empty).ConfigureAwait(false);

                case ChatCommandResultKind.DebugPlayRoutine:
                    return PlayRoutineCommand(cmd.Message ?? string.Empty);

                // Synthetic Locomotion
                case ChatCommandResultKind.SyntheticMoveTo:
                    return await MoveToAsync(new Vector3(cmd.MoveX, cmd.MoveY, cmd.MoveZ)).ConfigureAwait(false);

                // Combat Actions
                case ChatCommandResultKind.CombatAttack:
                    return await AttackAsync(cmd.TargetServerId, cmd.TargetIndex).ConfigureAwait(false);

                case ChatCommandResultKind.Check:
                    return await CheckAsync(cmd.TargetServerId, cmd.TargetIndex).ConfigureAwait(false);

                case ChatCommandResultKind.CombatAttackOff:
                    return await DisengageAsync().ConfigureAwait(false);

                case ChatCommandResultKind.CombatCast:
                    return await CastMagicAsync(cmd.ActionParam, cmd.TargetServerId, cmd.TargetIndex).ConfigureAwait(false);

                case ChatCommandResultKind.CombatWeaponskill:
                    return await WeaponskillAsync(cmd.ActionParam, cmd.TargetServerId, cmd.TargetIndex).ConfigureAwait(false);

                case ChatCommandResultKind.CombatJobAbility:
                    return await JobAbilityAsync(cmd.ActionParam, cmd.TargetServerId, cmd.TargetIndex).ConfigureAwait(false);

                case ChatCommandResultKind.CombatRanged:
                    return await ShootAsync(cmd.TargetServerId, cmd.TargetIndex).ConfigureAwait(false);

                case ChatCommandResultKind.CombatAssist:
                    return await AssistAsync(cmd.TargetServerId, cmd.TargetIndex).ConfigureAwait(false);

                case ChatCommandResultKind.CombatBuffCancel:
                    return await CancelBuffAsync(cmd.ActionParam).ConfigureAwait(false);

                case ChatCommandResultKind.CombatJump:
                    return await JumpAsync().ConfigureAwait(false);

                case ChatCommandResultKind.Fish:
                    return await FishAsync().ConfigureAwait(false);

                case ChatCommandResultKind.Sprint:
                    return await SprintAsync().ConfigureAwait(false);

                case ChatCommandResultKind.ChocoboDig:
                    return await ChocoboDigAsync().ConfigureAwait(false);

                case ChatCommandResultKind.Blockaid:
                    return await BlockaidAsync((BlockaidMode)cmd.ActionParam).ConfigureAwait(false);

                case ChatCommandResultKind.CallForHelp:
                    return await CallForHelpAsync().ConfigureAwait(false);

                case ChatCommandResultKind.MonsterSkill:
                    return await MonsterSkillAsync(cmd.ActionParam, cmd.TargetServerId, cmd.TargetIndex).ConfigureAwait(false);

                case ChatCommandResultKind.HomePoint:
                    return await HomePointAsync().ConfigureAwait(false);

                case ChatCommandResultKind.RaiseAnswer:
                case ChatCommandResultKind.TractorAnswer:
                    return await AnswerDeathOfferAsync(cmd.Kind == ChatCommandResultKind.TractorAnswer ? DeathMenuType.Tractor : DeathMenuType.Raise,
                        (ReviveMenuAnswer)cmd.ActionParam == ReviveMenuAnswer.Accept).ConfigureAwait(false);

                case ChatCommandResultKind.ReleaseTrust:
                    return await ReleaseTrustAsync(cmd.Message ?? string.Empty).ConfigureAwait(false);

                case ChatCommandResultKind.Emote:
                    return await EmoteAsync(cmd.Emote, cmd.TargetServerId, cmd.TargetIndex, cmd.ActionParam).ConfigureAwait(false);

                // Chat & Communication
                case ChatCommandResultKind.SendChat:
                    await _chatModule.SendChatAsync(cmd.SpeechKind, cmd.Message).ConfigureAwait(false);
                    return PlayerActionResult.Ok($"[{cmd.SpeechKind}] {cmd.Message}", cmd.Kind);

                case ChatCommandResultKind.SendTell:
                    await _chatModule.SendTellAsync(cmd.Recipient, cmd.Message).ConfigureAwait(false);
                    return PlayerActionResult.Ok($">> {cmd.Recipient}: {cmd.Message}", cmd.Kind);

                // Party Management
                case ChatCommandResultKind.PartyAccept:
                    await _partyModule.AcceptInviteAsync().ConfigureAwait(false);
                    return PlayerActionResult.Ok("Accepted party invite.", cmd.Kind);

                case ChatCommandResultKind.PartyDecline:
                    await _partyModule.DeclineInviteAsync().ConfigureAwait(false);
                    return PlayerActionResult.Ok("Declined party invite.", cmd.Kind);

                case ChatCommandResultKind.PartyInvite:
                    if (cmd.TargetServerId != 0)
                    {
                        await _partyModule.SendInviteAsync(cmd.TargetServerId, cmd.TargetIndex).ConfigureAwait(false);
                        return PlayerActionResult.Ok($"Invited {cmd.TargetName} to party.", cmd.Kind);
                    }
                    return PlayerActionResult.Warn($"Player '{cmd.TargetName}' not found in area.", cmd.Kind);

                case ChatCommandResultKind.PartyLeave:
                    await _partyModule.LeavePartyAsync().ConfigureAwait(false);
                    return PlayerActionResult.Ok("Left the party.", cmd.Kind);

                case ChatCommandResultKind.PartyDisband:
                    await _partyModule.DisbandPartyAsync().ConfigureAwait(false);
                    return PlayerActionResult.Ok("Disbanded the party.", cmd.Kind);

                case ChatCommandResultKind.PartyKick:
                    if (cmd.TargetServerId != 0)
                    {
                        await _partyModule.KickMemberAsync(cmd.TargetServerId, cmd.TargetIndex, cmd.TargetName).ConfigureAwait(false);
                        return PlayerActionResult.Ok($"Kicked {cmd.TargetName} from party.", cmd.Kind);
                    }
                    return PlayerActionResult.Warn($"Member '{cmd.TargetName}' not found in party.", cmd.Kind);

                // Server Command Passthrough (!pos, !zone, etc.)
                case ChatCommandResultKind.ServerCommand:
                    await _chatModule.SendChatAsync(ChatSendKind.Say, cmd.Message).ConfigureAwait(false);
                    return PlayerActionResult.Ok($"Sent server command: {cmd.Message}", cmd.Kind);

                case ChatCommandResultKind.LocalEcho:
                    return PlayerActionResult.Info(cmd.Message, cmd.Kind);

                case ChatCommandResultKind.LocalNotice:
                    return PlayerActionResult.Info(cmd.Message, cmd.Kind);

                case ChatCommandResultKind.Unrecognized:
                default:
                    return PlayerActionResult.Warn(cmd.Message, cmd.Kind);
            }
        }

        #endregion
    }
}
