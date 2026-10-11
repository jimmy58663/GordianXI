// src/Gordian.Core/Actions/PlayerActionService.Actions.cs
// Spell, job ability and weapon skill use by name (#240, #353): /ma, /ja, /ws, the Magic and Abilities windows and the
// sub-target cursor. The action packet is C2S 0x01A (LandSandBoat c2s/0x01a_action.cpp; XiPackets world/client/0x001A).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Input;
using Gordian.Core.Network;
using Gordian.Core.Ui;
using Gordian.Core.World;

namespace Gordian.Core.Actions
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
            var catalog = Catalog;
            if (catalog == null) return null;
            var combat = _combatModule.State;
            var state = new StockUiActionState
            {
                HasSpell = _localPlayer.HasSpell,
                HasJobAbility = _localPlayer.HasJobAbility,
                HasWeaponSkill = _localPlayer.HasWeaponSkill,
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
                StockUiActionList.Spells => StockUiActionMenu.SpellRows(catalog, state, category, target),
                StockUiActionList.JobAbilities => StockUiActionMenu.JobAbilityRows(catalog, state, target),
                _ => StockUiActionMenu.WeaponSkillRows(catalog, state, target),
            };
        }

        /// <summary>An action chosen in a Magic or Abilities window: on the command menu's target, else picked as a typed command without a target is.</summary>
        private async Task<PlayerActionResult> RunMenuActionAsync(StockUiActionRequest request)
        {
            WorldEntity? target = null;
            if (request.Target is { } context)
            {
                if (!_world.TryGetByServerId(context.TargetServerId, out target) || target == null)
                {
                    return PlayerActionResult.Warn($"{context.TargetName} is no longer here.");
                }
            }
            if (request.Type == StockUiActionType.RangedAttack)
            {
                target ??= CurrentTarget;
                if (target == null) return BeginSubTarget(ActionKind.WeaponSkill, 0, request.Name, ActionTargetFlags.Enemy, rangedAttack: true);
                return await ShootAsync(target.ServerId, target.TargetIndex).ConfigureAwait(false);
            }
            var kind = request.Type switch
            {
                StockUiActionType.Spell => ActionKind.Spell,
                StockUiActionType.JobAbility => ActionKind.JobAbility,
                _ => ActionKind.WeaponSkill,
            };
            if (target != null) return await UseActionAsync(kind, request.Id, target).ConfigureAwait(false);
            return await UseActionWithoutTargetAsync(kind, request.Id, request.Name, request.TargetFlags).ConfigureAwait(false);
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
            _ => WeaponskillAsync(id, targetId, targetIndex),
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
        /// Starts the sub-target cursor for an action (closing the menus): on the current target when allowed, else on
        /// yourself, else on the nearest allowed entity. Warns when nothing around may be the target.
        /// </summary>
        public PlayerActionResult BeginSubTarget(ActionKind kind, ushort id, string name, ActionTargetFlags allowed, bool rangedAttack = false)
        {
            var self = LocalEntity();
            WorldEntity? start = null;
            if (CurrentTarget is { } current && AcceptsTarget(allowed, current) && TargetCycling.IsTargetable(current)) start = current;
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
            if (start == null) return PlayerActionResult.Warn($"{name}: there is no one here it can be used on.", ResultKind(kind));
            Menus.CloseAll();
            _subTarget = new SubTargetSelection(kind, id, name, allowed, start) { IsRangedAttack = rangedAttack };
            GordianLog.Info("ACTION", $"Sub-target cursor for {name} (targets 0x{(int)allowed:X2}) on {start.Name}.");
            SubTargetChanged?.Invoke();
            return PlayerActionResult.Info($"{name}: select a target.", ResultKind(kind));
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

        /// <summary>Drops the sub-target cursor without acting.</summary>
        public void CancelSubTarget()
        {
            if (_subTarget == null) return;
            _subTarget = null;
            SubTargetChanged?.Invoke();
        }

        /// <summary>Confirm on the sub-target cursor: the action runs on the entity under it.</summary>
        public async Task<PlayerActionResult> ConfirmSubTargetAsync()
        {
            var selection = _subTarget;
            if (selection == null) return PlayerActionResult.Warn("No target is being selected.");
            _subTarget = null;
            SubTargetChanged?.Invoke();
            var target = selection.Current;
            LastSubTarget = target;
            if (selection.IsRangedAttack) return await ShootAsync(target.ServerId, target.TargetIndex).ConfigureAwait(false);
            return await UseActionAsync(selection.Kind, selection.Id, target).ConfigureAwait(false);
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
