// src/Gordian.Engine/Actions/PlayerActionService.Actions.cs
// Spell, job ability and weapon skill use by name (#240, #353): /ma, /ja, /ws, the Magic and Abilities windows and the
// sub-target cursor. The action packet is C2S 0x01A (LandSandBoat c2s/0x01a_action.cpp; XiPackets world/client/0x001A).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Engine.Input;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Engine.Ui;
using Gordian.Core.World;

namespace Gordian.Engine.Actions
{
    public sealed partial class PlayerActionService
    {
        private ActionCatalog? _catalog;
        private volatile SubTargetSelection? _subTarget;

        /// <summary>
        /// The spells, abilities and weapon skills by name and id: set explicitly (tests), else the app's shared
        /// <see cref="ActionCatalog.Provider"/>. Null without the game's tables, when only numeric ids are understood.
        /// </summary>
        public ActionCatalog? Catalog
        {
            get => _catalog ?? ActionCatalog.Provider?.Invoke();
            set => _catalog = value;
        }

        /// <summary>The sub-target cursor while an action waits for its target, else null.</summary>
        public SubTargetSelection? SubTargeting => _subTarget;

        /// <summary>The entity last picked with the sub-target cursor (<c>&lt;lastst&gt;</c>).</summary>
        public WorldEntity? LastSubTarget { get; private set; }

        /// <summary>The entity the target cursor marks: the sub-target while one is being picked, else the current target.</summary>
        public WorldEntity? DisplayedTarget => _subTarget?.Current ?? CurrentTarget;

        /// <summary>Raised when the sub-target cursor starts, moves or ends.</summary>
        public event Action? SubTargetChanged;

        /// <summary>Connects the Magic and Abilities windows to the catalog and the character's state (from the constructor).</summary>
        private void WireActionMenus()
        {
            Menus.ActionRowsProvider = ComposeActionRows;
            Menus.ActionRequested = RunMenuActionAsync;
            Menus.InCombat = () => Combat?.IsEngaged == true;
            Action refresh = () =>
            {
                if (Menus.IsOpen) Menus.RefreshActionLists();
            };
            _localPlayer.MagicLearnedUpdated += refresh;
            _localPlayer.CommandsUpdated += refresh;
            _localPlayer.VitalsUpdated += refresh;
            _combatModule.State.RecastsUpdated += refresh;
        }

        private IReadOnlyList<StockUiActionRow>? ComposeActionRows(StockUiActionList list, int category, StockUiTargetContext? target)
        {
            if (list == StockUiActionList.Mounts)
            {
                // The mount list needs only the mount names (#87), not the spell and ability tables.
                var names = MountName;
                return names == null
                    ? Array.Empty<StockUiActionRow>()
                    : StockUiActionMenu.MountRows(UnlockedMounts, names, id => MountHelp?.Invoke(id),
                        _combatModule.State.GetMountRecastRemaining(), System.Diagnostics.Stopwatch.GetTimestamp());
            }
            var catalog = Catalog;
            if (catalog == null) return null;
            var combat = _combatModule.State;
            var state = new StockUiActionState
            {
                HasSpell = _localPlayer.HasSpell,
                HasJobAbility = _localPlayer.HasJobAbility,
                HasWeaponSkill = _localPlayer.HasWeaponSkill,
                HasTrait = _localPlayer.HasTrait,
                HasPetAbility = _localPlayer.HasPetAbility,
                Engaged = combat.IsEngaged,
                MainJob = _localPlayer.MainJob,
                MainJobLevel = _localPlayer.MainJobLevel,
                SubJob = _localPlayer.SubJob,
                SubJobLevel = _localPlayer.SubJobLevel,
                CurrentMp = _localPlayer.CurrentMp,
                CurrentTp = _localPlayer.CurrentTp,
                SpellRecast = combat.GetSpellRecastRemaining,
                AbilityRecast = combat.GetAbilityRecastRemaining,
            };
            return list switch
            {
                // The command menu's lists hold only what you can use (retail capture 2026-10-11).
                StockUiActionList.Spells => StockUiActionMenu.SpellRows(catalog, state, category, target, usableOnly: target != null),
                StockUiActionList.JobAbilities => StockUiActionMenu.JobAbilityRows(catalog, state, target),
                StockUiActionList.WeaponSkills => StockUiActionMenu.WeaponSkillRows(catalog, state, target),
                StockUiActionList.JobTraits => StockUiActionMenu.TraitRows(catalog, state),
                StockUiActionList.PetCommands => StockUiActionMenu.PetCommandRows(catalog, state, target),
                StockUiActionList.PetAbilities => StockUiActionMenu.PetAbilityRows(catalog, state, category),
                _ => Array.Empty<StockUiActionRow>(),
            };
        }

        /// <summary>
        /// An action chosen in a Magic or Abilities window: the sub-target cursor opens (even for an action only you can
        /// take: retail video 2026-10-11, Advancing March), on the command menu's target when it may be the target, while
        /// the menus step aside; they come back when the cursor ends.
        /// </summary>
        private Task<PlayerActionResult> RunMenuActionAsync(StockUiActionRequest request)
        {
            WorldEntity? preferred = null;
            if (request.Target is { } context && (!_world.TryGetByServerId(context.TargetServerId, out preferred) || preferred == null))
            {
                return Task.FromResult(PlayerActionResult.Warn($"{context.TargetName} is no longer here."));
            }
            if (request.Type == StockUiActionType.Mount)
            {
                // Calling a mount needs no target (retail capture Mounts.mp4: the mount comes at once).
                return MountAsync(request.Id);
            }
            if (request.Type == StockUiActionType.RangedAttack)
            {
                return Task.FromResult(StartSubTarget(request.Name, ActionTargetFlags.Enemy,
                    entity => ShootAsync(entity.ServerId, entity.TargetIndex), preferred, true, ActionKind.WeaponSkill, 0, default, 0));
            }
            var kind = request.Type switch
            {
                StockUiActionType.Spell => ActionKind.Spell,
                StockUiActionType.JobAbility => ActionKind.JobAbility,
                _ => ActionKind.WeaponSkill,
            };
            var allowed = request.TargetFlags == ActionTargetFlags.None ? ActionTargetFlags.Self : request.TargetFlags;
            ushort id = request.Id;
            return Task.FromResult(StartSubTarget(request.Name, allowed, entity => UseActionAsync(kind, id, entity), preferred, true, kind, id, default, 0));
        }

        /// <summary>
        /// <c>/ma</c>, <c>/ja</c>, <c>/ws</c>: the action by name (quoted or not, any case) or id, then the target: a name, a
        /// placeholder (<c>&lt;t&gt;</c>, <c>&lt;me&gt;</c>, <c>&lt;bt&gt;</c>, <c>&lt;p0&gt;</c>-<c>&lt;p5&gt;</c>,
        /// <c>&lt;lastst&gt;</c>, or the sub-target cursor <c>&lt;st&gt;</c>, <c>&lt;stpc&gt;</c>, <c>&lt;stnpc&gt;</c>,
        /// <c>&lt;stpt&gt;</c>, <c>&lt;stal&gt;</c>), or none, which uses yourself for an action only you can take and
        /// otherwise opens the sub-target cursor (as retail does for a macro without a target; PROVISIONAL). Without the
        /// game's tables, numeric ids only, and no target means the current target (else yourself).
        /// </summary>
        private async Task<PlayerActionResult> RunTypedActionAsync(ActionKind kind, ChatCommandResult cmd)
        {
            var resultKind = ResultKind(kind);
            string name = cmd.Message;
            string targetArg = cmd.TargetName;
            var catalog = Catalog;

            int? id = ushort.TryParse(name, out ushort numeric) ? numeric : catalog?.Find(kind, name);
            if (id == null && targetArg.Length == 0 && cmd.TargetServerId == 0 && name.LastIndexOf(' ') is > 0 and int split)
            {
                // "/ma Cure Gemini": the last word is the target when the whole text is not a known name.
                string shorter = name[..split].TrimEnd();
                if (catalog?.Find(kind, shorter) is { } found)
                {
                    id = found;
                    targetArg = name[(split + 1)..];
                    name = shorter;
                }
            }
            if (id == null)
            {
                return catalog == null
                    ? PlayerActionResult.Warn($"Cannot use \"{name}\" by name: the game's spell and ability tables are not loaded. Use its number.", resultKind)
                    : PlayerActionResult.Warn($"Unknown {Describe(kind)}: \"{name}\".", resultKind);
            }
            ushort actionId = (ushort)id.Value;
            string label = catalog?.Name(kind, actionId) ?? $"{Describe(kind)} #{actionId}";
            var flags = TargetFlagsOf(catalog, kind, actionId);

            // An explicit target: resolved by the router (a name), or a placeholder.
            if (cmd.TargetServerId != 0)
            {
                var (sid, sidx, _) = ResolveTarget(cmd.TargetServerId, cmd.TargetIndex, cmd.TargetName);
                return await SendActionAsync(kind, actionId, sid, sidx).ConfigureAwait(false);
            }
            if (targetArg.Length > 0)
            {
                if (ChatCommandRouter.IsTargetToken(targetArg))
                {
                    string token = targetArg[1..^1].Trim().ToLowerInvariant();
                    if (SubTargetFilter(token) is { } filter)
                    {
                        var allowed = flags == ActionTargetFlags.None ? filter : flags & filter;
                        return BeginSubTarget(kind, actionId, label, allowed);
                    }
                    var entity = ResolveTargetToken(token, out string? problem);
                    if (entity == null) return PlayerActionResult.Warn(problem ?? $"{targetArg}: no such target.", resultKind);
                    return await UseActionAsync(kind, actionId, entity).ConfigureAwait(false);
                }
                if (_world.TryGetByName(targetArg, out var named) && named != null)
                {
                    return await UseActionAsync(kind, actionId, named).ConfigureAwait(false);
                }
                return PlayerActionResult.Warn($"{targetArg} is not here.", resultKind);
            }
            if (catalog == null) return await LegacyNoTargetAsync(kind, actionId).ConfigureAwait(false);
            return await UseActionWithoutTargetAsync(kind, actionId, label, flags).ConfigureAwait(false);
        }

        /// <summary>No target given: yourself when only you may be the target, else the sub-target cursor.</summary>
        private async Task<PlayerActionResult> UseActionWithoutTargetAsync(ActionKind kind, ushort id, string name, ActionTargetFlags flags)
        {
            if (flags == ActionTargetFlags.Self || flags == ActionTargetFlags.None)
            {
                var self = LocalEntity();
                if (self == null) return PlayerActionResult.Warn("Your character is not in the zone yet.", ResultKind(kind));
                return await SendActionAsync(kind, id, self.ServerId, self.TargetIndex).ConfigureAwait(false);
            }
            return BeginSubTarget(kind, id, name, flags);
        }

        /// <summary>The behaviour before name lookup (no tables): the current target, else yourself; a weapon skill needs a target.</summary>
        private Task<PlayerActionResult> LegacyNoTargetAsync(ActionKind kind, ushort id) => kind switch
        {
            ActionKind.Spell => CastMagicAsync(id),
            ActionKind.JobAbility => JobAbilityAsync(id),
            _ => WeaponskillAsync(id),
        };

        /// <summary>Runs an action on an entity (no check of its target bits: the server decides).</summary>
        public Task<PlayerActionResult> UseActionAsync(ActionKind kind, ushort id, WorldEntity target)
        {
            ArgumentNullException.ThrowIfNull(target);
            return SendActionAsync(kind, id, target.ServerId, target.TargetIndex);
        }

        private Task<PlayerActionResult> SendActionAsync(ActionKind kind, ushort id, uint targetId, ushort targetIndex) => kind switch
        {
            ActionKind.Spell => CastMagicAsync(id, targetId, targetIndex),
            ActionKind.JobAbility => JobAbilityAsync(id, targetId, targetIndex),
            ActionKind.WeaponSkill => WeaponskillAsync(id, targetId, targetIndex),
            _ => Task.FromResult(PlayerActionResult.Warn("An item is used through BeginItemSubTarget (it needs its container and slot).")),
        };

        private static ChatCommandResultKind ResultKind(ActionKind kind) => kind switch
        {
            ActionKind.Spell => ChatCommandResultKind.CombatCast,
            ActionKind.JobAbility => ChatCommandResultKind.CombatJobAbility,
            _ => ChatCommandResultKind.CombatWeaponskill,
        };

        private static string Describe(ActionKind kind) => kind switch
        {
            ActionKind.Spell => "spell",
            ActionKind.JobAbility => "job ability",
            _ => "weapon skill",
        };

        /// <summary>The target bits of an action from the client's table; None when unknown.</summary>
        private static ActionTargetFlags TargetFlagsOf(ActionCatalog? catalog, ActionKind kind, ushort id)
        {
            if (catalog == null) return ActionTargetFlags.None;
            int flags = kind switch
            {
                ActionKind.Spell => catalog.GetSpell(id)?.TargetFlags ?? 0,
                ActionKind.JobAbility => catalog.GetJobAbility(id)?.TargetFlags ?? 0,
                _ => catalog.GetWeaponSkill(id)?.TargetFlags ?? 0,
            };
            return (ActionTargetFlags)(flags & 0x3F);
        }

        /// <summary>The kinds a sub-target placeholder allows (retail: <c>st</c> any, <c>stpc</c> players, <c>stnpc</c> NPCs and monsters, <c>stpt</c> party, <c>stal</c> alliance); null for other tokens.</summary>
        private static ActionTargetFlags? SubTargetFilter(string token) => token switch
        {
            "st" => (ActionTargetFlags)0x3F,
            "stpc" => ActionTargetFlags.Self | ActionTargetFlags.Player | ActionTargetFlags.Party | ActionTargetFlags.Alliance,
            "stnpc" => ActionTargetFlags.Npc | ActionTargetFlags.Enemy,
            "stpt" => ActionTargetFlags.Self | ActionTargetFlags.Party,
            "stal" => ActionTargetFlags.Self | ActionTargetFlags.Party | ActionTargetFlags.Alliance,
            _ => null,
        };

        /// <summary>A target placeholder without the brackets (<c>t</c>, <c>me</c>, <c>bt</c>, <c>p0</c>-<c>p5</c>, <c>lastst</c>).</summary>
        public WorldEntity? ResolveTargetToken(string token, out string? problem)
        {
            problem = null;
            switch (token)
            {
                case "t":
                    if (CurrentTarget == null) problem = "Nothing is targeted.";
                    return CurrentTarget;
                case "me":
                case "p0":
                    var self = LocalEntity();
                    if (self == null) problem = "Your character is not in the zone yet.";
                    return self;
                case "bt":
                    if (Combat is { TargetServerId: not 0 } combat && _world.TryGetByServerId(combat.TargetServerId, out var fought) && fought != null) return fought;
                    problem = "You are not fighting anything.";
                    return null;
                case "lastst":
                    if (LastSubTarget == null) problem = "No sub-target has been selected yet.";
                    return LastSubTarget;
            }
            switch (token)
            {
                case "pet":
                    // Your pet: the actor index S2C 0x037 / 0x067 report for it.
                    if (_localPlayer.PetActorIndex != 0 && _world.TryGetByTargetIndex(_localPlayer.PetActorIndex, out var pet) && pet != null) return pet;
                    problem = "You have no pet.";
                    return null;
                case "r":
                    // The last person you exchanged tells with (the chat's tell partner; retail: the last who sent you one).
                    if (Menus.TellTarget?.Invoke() is { Length: > 0 } teller && _world.TryGetByName(teller, out var tellEntity) && tellEntity != null) return tellEntity;
                    problem = "No one who sent you a tell is here.";
                    return null;
                case "scan":
                    // The entity the wide scan is tracking (S2C 0x0F5).
                    if (CommandModule?.State.WideScan.Track is { } track && _world.TryGetByTargetIndex(track.ActIndex, out var tracked) && tracked != null) return tracked;
                    problem = "Nothing is being tracked.";
                    return null;
                case "ht":
                    // The nearest monster someone called for help on (its name plate's CalledForHelp flag).
                    if (LocalEntity() is { } me)
                    {
                        WorldEntity? best = null;
                        float bestDistance = float.MaxValue;
                        foreach (var entity in _world.GetEntitiesInRadius(me.Position, TargetCycling.Range))
                        {
                            if (entity.Type != EntityType.Monster || (entity.NamePlate & NamePlateFlags.CalledForHelp) == 0) continue;
                            float d = System.Numerics.Vector3.DistanceSquared(me.Position, entity.Position);
                            if (d < bestDistance) { bestDistance = d; best = entity; }
                        }
                        if (best != null) return best;
                    }
                    problem = "No one has called for help nearby.";
                    return null;
                case "ft":
                case "focust":
                    problem = $"<{token}> is not supported yet (no adventuring fellow or focus target in this client).";
                    return null;
            }
            if (token.Length == 3 && token[0] == 'a' && token[1] is '1' or '2' && token[2] is >= '0' and <= '5')
            {
                var member = AllianceSlotEntity(token[1] - '0', token[2] - '0');
                if (member == null) problem = $"<{token}>: no alliance member in that slot here.";
                return member;
            }
            if (token.Length == 2 && token[0] == 'p' && token[1] is >= '1' and <= '5')
            {
                var member = PartySlotEntity(token[1] - '0');
                if (member == null) problem = $"<{token}>: no party member in that slot here.";
                return member;
            }
            problem = $"<{token}> is not a target this client understands.";
            return null;
        }

        private WorldEntity? LocalEntity() =>
            _world.TryGetByServerId(_localPlayer.ServerId, out var self) ? self : null;

        /// <summary>
        /// <c>&lt;a10&gt;</c>-<c>&lt;a25&gt;</c>: the member in a slot (0-5) of the alliance's other parties, the first (1) and
        /// second (2) in party-number order, as the alliance windows list them. PROVISIONAL: whether retail's first party is
        /// the first other party or the alliance's first party (possibly your own).
        /// </summary>
        private WorldEntity? AllianceSlotEntity(int party, int slot)
        {
            var members = _partyModule.State.Members;
            var me = members.FirstOrDefault(m => m.ServerId == _localPlayer.ServerId);
            byte ownParty = me?.PartyNumber ?? 0;
            var others = members.Where(m => m.PartyNumber != ownParty).GroupBy(m => m.PartyNumber).OrderBy(g => g.Key).ToList();
            if (party < 1 || party > others.Count) return null;
            var list = others[party - 1].OrderBy(m => m.MemberNumber).ToList();
            if (slot < 0 || slot >= list.Count) return null;
            return _world.TryGetByServerId(list[slot].ServerId, out var entity) ? entity : null;
        }

        /// <summary>The member in a slot (1-5) of your own party, in party window order, if in the zone.</summary>
        private WorldEntity? PartySlotEntity(int slot)
        {
            var members = _partyModule.State.Members;
            var me = members.FirstOrDefault(m => m.ServerId == _localPlayer.ServerId);
            byte ownParty = me?.PartyNumber ?? 0;
            var others = members.Where(m => m.PartyNumber == ownParty && m.ServerId != _localPlayer.ServerId).OrderBy(m => m.MemberNumber).ToList();
            if (slot < 1 || slot > others.Count) return null;
            return _world.TryGetByServerId(others[slot - 1].ServerId, out var entity) ? entity : null;
        }

        /// <summary>
        /// Whether an action with these target bits may land on an entity: yourself (Self), members of your party
        /// (Party; your trusts and pets count, PROVISIONAL) or alliance (Alliance), other players (Player), NPCs (Npc) and
        /// monsters (Enemy).
        /// </summary>
        public bool AcceptsTarget(ActionTargetFlags flags, WorldEntity entity)
        {
            if (entity.ServerId == _localPlayer.ServerId) return (flags & ActionTargetFlags.Self) != 0;
            switch (entity.Type)
            {
                case EntityType.Monster:
                    return (flags & ActionTargetFlags.Enemy) != 0;
                case EntityType.Npc:
                case EntityType.Door:
                    return (flags & ActionTargetFlags.Npc) != 0;
                case EntityType.Trust:
                case EntityType.Pet:
                    return (flags & (ActionTargetFlags.Party | ActionTargetFlags.Player)) != 0;
                case EntityType.Player:
                    var members = _partyModule.State.Members;
                    var me = members.FirstOrDefault(m => m.ServerId == _localPlayer.ServerId);
                    var them = members.FirstOrDefault(m => m.ServerId == entity.ServerId);
                    if (them != null && me != null && them.PartyNumber == me.PartyNumber) return (flags & (ActionTargetFlags.Party | ActionTargetFlags.Player)) != 0;
                    if (them != null) return (flags & (ActionTargetFlags.Alliance | ActionTargetFlags.Player)) != 0;
                    return (flags & ActionTargetFlags.Player) != 0;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Starts the sub-target cursor for a typed action (<c>&lt;st&gt;</c>, or no target given): see
        /// <see cref="BeginSubTarget(string, ActionTargetFlags, Func{WorldEntity, Task{PlayerActionResult}}, WorldEntity?, bool)"/>.
        /// </summary>
        public PlayerActionResult BeginSubTarget(ActionKind kind, ushort id, string name, ActionTargetFlags allowed) =>
            StartSubTarget(name, allowed, entity => UseActionAsync(kind, id, entity), null, false, kind, id, default, 0);

        /// <summary>
        /// Starts the sub-target cursor: the entry point for anything that needs a target picked on screen (spells,
        /// abilities, ranged attacks, and item use: Lane B's Items window calls this with the item's target bits and a
        /// callback that sends the use). <paramref name="allowed"/> filters what the cursor may land on;
        /// <paramref name="onConfirm"/> runs on the picked entity. The cursor starts on <paramref name="preferred"/> (a
        /// command menu's target) when allowed, else on the current target, else on yourself, else on the nearest allowed
        /// entity. With <paramref name="resumeMenus"/> the open menus step aside (<see cref="StockUiMenuController.Suspended"/>)
        /// and come back when the cursor ends, as retail's do; otherwise they close. Warns when nothing around may be the
        /// target.
        /// </summary>
        public PlayerActionResult BeginSubTarget(string name, ActionTargetFlags allowed, Func<WorldEntity, Task<PlayerActionResult>> onConfirm,
            WorldEntity? preferred = null, bool resumeMenus = false) =>
            StartSubTarget(name, allowed, onConfirm, preferred, resumeMenus, ActionKind.Spell, 0, default, 0);

        private PlayerActionResult StartSubTarget(string name, ActionTargetFlags allowed, Func<WorldEntity, Task<PlayerActionResult>> onConfirm,
            WorldEntity? preferred, bool resumeMenus, ActionKind kind, ushort id, ContainerId container, byte slot, Action? onFinished = null)
        {
            ArgumentNullException.ThrowIfNull(onConfirm);
            var self = LocalEntity();
            WorldEntity? start = null;
            if (preferred != null && AcceptsTarget(allowed, preferred)) start = preferred;
            else if (CurrentTarget is { } current && AcceptsTarget(allowed, current) && TargetCycling.IsTargetable(current)) start = current;
            else if (self != null && AcceptsTarget(allowed, self)) start = self;
            else if (self != null)
            {
                float best = float.MaxValue;
                foreach (var entity in _world.GetEntitiesInRadius(self.Position, TargetCycling.Range))
                {
                    if (!TargetCycling.IsTargetable(entity) || !AcceptsTarget(allowed, entity)) continue;
                    float d = System.Numerics.Vector3.DistanceSquared(self.Position, entity.Position);
                    if (d < best)
                    {
                        best = d;
                        start = entity;
                    }
                }
            }
            if (start == null) return PlayerActionResult.Warn($"{name}: there is no one here it can be used on.");
            CancelSubTarget();
            if (resumeMenus && Menus.IsOpen) Menus.Suspended = true;
            else Menus.CloseAll();
            // A lock-on lets go while the player picks, and comes back after (retail note C3, 2026-10-12); the engagement
            // and auto-attacks are not touched: the cursor never changes the battle target.
            WorldEntity? relock = IsLockedOn ? CurrentTarget : null;
            if (relock != null) SetLockOn(false);
            _subTarget = new SubTargetSelection(name, allowed, start, onConfirm)
            {
                ResumesMenus = resumeMenus,
                OnFinished = onFinished,
                RestoreLockOn = relock,
                Kind = kind,
                Id = id,
                ItemContainer = container,
                ItemSlot = slot,
            };
            GordianLog.Info("ACTION", $"Sub-target cursor for {name} (targets 0x{(int)allowed:X2}) on {start.Name}.");
            SubTargetChanged?.Invoke();
            return PlayerActionResult.Info($"{name}: select a target.");
        }

        /// <summary>Moves the sub-target cursor to an entity (one the action may land on).</summary>
        public bool MoveSubTarget(uint serverId)
        {
            var selection = _subTarget;
            if (selection == null || serverId == 0 || !_world.TryGetByServerId(serverId, out var entity) || entity == null) return false;
            if (!AcceptsTarget(selection.Allowed, entity) || entity.ServerId == selection.Current.ServerId) return false;
            selection.Current = entity;
            SubTargetChanged?.Invoke();
            return true;
        }

        /// <summary>Drops the sub-target cursor without acting; menus set aside for it come back.</summary>
        public void CancelSubTarget()
        {
            var selection = _subTarget;
            if (selection == null) return;
            _subTarget = null;
            EndSubTarget(selection);
            selection.OnFinished?.Invoke();
        }

        /// <summary>The cursor is over: menus set aside come back, an unlocked lock-on locks again.</summary>
        private void EndSubTarget(SubTargetSelection selection)
        {
            if (selection.ResumesMenus) Menus.Suspended = false;
            if (selection.RestoreLockOn is { } relock && CurrentTarget?.ServerId == relock.ServerId) SetLockOn(true);
            SubTargetChanged?.Invoke();
        }

        /// <summary>Confirm on the sub-target cursor: the action runs on the entity under it, and menus set aside for it come back.</summary>
        public async Task<PlayerActionResult> ConfirmSubTargetAsync()
        {
            var selection = _subTarget;
            if (selection == null) return PlayerActionResult.Warn("No target is being selected.");
            _subTarget = null;
            EndSubTarget(selection);
            var target = selection.Current;
            LastSubTarget = target;
            try
            {
                return await selection.Run(target).ConfigureAwait(false);
            }
            finally
            {
                selection.OnFinished?.Invoke();
            }
        }

        /// <summary>
        /// Item use through the sub-target cursor (Lane B's Items window, <c>/item ... &lt;st&gt;</c>): starts the cursor on
        /// the entities the item's <paramref name="validTargets"/> allow (the item DAT's bits, mapped by
        /// <see cref="ItemTargetFlags.ToActionTargets"/>), on <paramref name="preferred"/> (a command menu's target) when
        /// allowed; Confirm sends C2S 0x037 with count 0 (LandSandBoat rejects any other count) for the item at
        /// <paramref name="container"/> / <paramref name="slot"/>. Open menus step aside and come back afterwards (retail
        /// capture 2026-10-11: a potion from the command menu's Items goes to the sub-target cursor). Returns whether the
        /// cursor started; without a target allowed nearby, a notice is posted and nothing starts.
        /// </summary>
        public bool BeginItemSubTarget(ContainerId container, byte slot, ushort itemId, int validTargets, WorldEntity? preferred = null,
            Action? onFinished = null)
        {
            var allowed = ItemTargetFlags.ToActionTargets(validTargets);
            if (allowed == ActionTargetFlags.None) allowed = ActionTargetFlags.Self;
            string name = Menus.ItemLookup?.Invoke(itemId)?.Name is { Length: > 0 } itemName ? itemName : $"Item #{itemId}";
            var result = StartSubTarget(name, allowed, entity => UseItemOnAsync(container, slot, itemId, entity), preferred,
                resumeMenus: true, ActionKind.Item, itemId, container, slot, onFinished);
            if (!result.Success) Menus.PostNotice(result.Message);
            return result.Success;
        }

        /// <summary>Sends C2S 0x037 (use an item) on a target: count 0, the item's container and slot.</summary>
        private async Task<PlayerActionResult> UseItemOnAsync(ContainerId container, byte slot, ushort itemId, WorldEntity target)
        {
            var inventory = InventoryModule;
            if (inventory == null) return PlayerActionResult.Warn("Items are not available in this session.");
            try
            {
                await inventory.UseItemAsync(target.ServerId, target.TargetIndex, 0, slot, container).ConfigureAwait(false);
                return PlayerActionResult.Ok($"Using item #{itemId} on {target.Name}.");
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"Item use failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"Item use failed: {ex.Message}");
            }
        }

        /// <summary>
        /// The candidates the sub-target cursor moves through, seen from the camera: the allowed entities on screen within
        /// range, yourself included (at the screen's centre when not on it, e.g. in first person).
        /// </summary>
        public List<TargetCycling.Candidate> SubTargetCandidates(Graphics.ViewportCamera camera)
        {
            var selection = _subTarget;
            var self = LocalEntity();
            if (selection == null || self == null) return new List<TargetCycling.Candidate>();
            var nearby = _world.GetEntitiesInRadius(self.Position, TargetCycling.Range).Where(e => AcceptsTarget(selection.Allowed, e)).ToList();
            if (!nearby.Contains(selection.Current)) nearby.Add(selection.Current);
            var candidates = TargetCycling.Gather(nearby, _localPlayer.ServerId, selection.Current.ServerId, self.Position, camera);
            if (AcceptsTarget(selection.Allowed, self) && !candidates.Exists(c => c.ServerId == self.ServerId))
            {
                candidates.Add(new TargetCycling.Candidate(self.ServerId, 0.0f, 0.0f, true));
            }
            return candidates;
        }
    }
}
