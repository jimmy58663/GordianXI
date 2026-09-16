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
using Gordian.Core.World;

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
    /// Enforces <see cref="ServerAutomationPolicy"/> and acts as the unified bridge
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
        public event Action<WorldEntity?>? TargetChanged;
        public event Action<Vector3, byte>? LocalPlayerMoved;

        public WorldState World => _world;
        public LocalPlayerState LocalPlayer => _localPlayer;
        public SessionProfile Profile => _profile;

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
        }

        #region Targeting Subsystem

        /// <summary>
        /// Selects an entity as the active target.
        /// </summary>
        public void SetTarget(WorldEntity? target)
        {
            if (CurrentTarget != target)
            {
                CurrentTarget = target;
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
        /// Clears the current target.
        /// </summary>
        public void ClearTarget()
        {
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
                await _combatModule.RequestAttackAsync(resolvedId, resolvedIdx).ConfigureAwait(false);
                return PlayerActionResult.Ok($"Engaged in combat with {resolvedName} [ID: 0x{resolvedId:X8}].", ChatCommandResultKind.CombatAttack);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"Attack failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"Attack failed: {ex.Message}", ChatCommandResultKind.CombatAttack);
            }
        }

        public async Task<PlayerActionResult> DisengageAsync()
        {
            uint tid = CurrentTarget?.ServerId ?? 0;
            ushort tidx = CurrentTarget?.TargetIndex ?? 0;

            try
            {
                await _combatModule.RequestAttackOffAsync(tid, tidx).ConfigureAwait(false);
                return PlayerActionResult.Ok("Disengaged from combat.", ChatCommandResultKind.CombatAttackOff);
            }
            catch (Exception ex)
            {
                GordianLog.Error("ACTION", $"Disengage failed: {ex.Message}", ex);
                return PlayerActionResult.Fail($"Disengage failed: {ex.Message}", ChatCommandResultKind.CombatAttackOff);
            }
        }

        public async Task<PlayerActionResult> CastMagicAsync(ushort spellId, uint targetId = 0, ushort targetIndex = 0, Vector3 targetPos = default)
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
                await _combatModule.RequestCastMagicAsync(resolvedId, resolvedIdx, spellId, targetPos).ConfigureAwait(false);
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

        public async Task<PlayerActionResult> EmoteAsync(EmoteId emote, uint targetId = 0, ushort targetIndex = 0)
        {
            var (resolvedId, resolvedIdx, _) = ResolveTarget(targetId, targetIndex, string.Empty);
            try
            {
                await _combatModule.RequestEmoteAsync(resolvedId, resolvedIdx, emote).ConfigureAwait(false);
                return PlayerActionResult.Ok($"Emote: {emote}", ChatCommandResultKind.Emote);
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
                await _combatModule.RequestJumpAsync().ConfigureAwait(false);
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
        /// Moves towards target coordinates. Gated by <see cref="ServerAutomationPolicy"/>.
        /// </summary>
        public async Task<PlayerActionResult> MoveToAsync(Vector3 targetPos)
        {
            if (_profile.AutomationPolicy == ServerAutomationPolicy.StrictVanilla)
            {
                return PlayerActionResult.Fail(
                    "Synthetic movement (/moveto) is blocked by server automation policy (StrictVanilla).",
                    ChatCommandResultKind.SyntheticMoveTo);
            }

            byte dir = 0;
            ushort targetIndex = 0;

            // Update local entity coordinates in WorldState if present
            if (_world.TryGetByServerId(_localPlayer.ServerId, out var localEnt) && localEnt != null)
            {
                localEnt.Position = targetPos;
                dir = localEnt.Direction;
                targetIndex = localEnt.TargetIndex;
            }
            else if (_localPlayer.ServerId != 0)
            {
                localEnt = new PlayerEntity(_localPlayer.ServerId, 0)
                {
                    Position = targetPos,
                    IsSpawned = true
                };
                _world.UpsertEntity(localEnt);
            }

            LocalPlayerMoved?.Invoke(targetPos, dir);

            try
            {
                byte[] posPacket = LifecycleOutboundPackets.BuildPos(
                    sequenceId: 0,
                    x: targetPos.X,
                    y: targetPos.Y,
                    z: targetPos.Z,
                    dir: dir,
                    targetIndex: targetIndex);

                await _sendChunkCallback(posPacket, false).ConfigureAwait(false);
                return PlayerActionResult.Ok(
                    $"Locomotion updated: X={targetPos.X:F2}, Y={targetPos.Y:F2}, Z={targetPos.Z:F2}",
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

            return $"[Position] X={pos.X:F2}, Y={pos.Y:F2}, Z={pos.Z:F2} | Dir={dir} ({headingDeg:F0}°) | ServerID=0x{_localPlayer.ServerId:X8}";
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
                sb.AppendLine($" - [{ent.Type}] {name} (ID: 0x{ent.ServerId:X8}, Idx: {ent.TargetIndex}) Dist: {dist:F1}y HP: {ent.Hpp}% Pos: ({ent.Position.X:F1}, {ent.Position.Y:F1}, {ent.Position.Z:F1})");
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
            return $"[Target] {name} | Type: {t.Type} | ID: 0x{t.ServerId:X8} | Index: {t.TargetIndex} | HP: {t.Hpp}% | Dist: {dist:F1}y | Pos: ({t.Position.X:F2}, {t.Position.Y:F2}, {t.Position.Z:F2})";
        }

        #endregion

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

                // Synthetic Locomotion
                case ChatCommandResultKind.SyntheticMoveTo:
                    return await MoveToAsync(new Vector3(cmd.MoveX, cmd.MoveY, cmd.MoveZ)).ConfigureAwait(false);

                // Combat Actions
                case ChatCommandResultKind.CombatAttack:
                    return await AttackAsync(cmd.TargetServerId, cmd.TargetIndex).ConfigureAwait(false);

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

                case ChatCommandResultKind.Emote:
                    return await EmoteAsync(cmd.Emote, cmd.TargetServerId, cmd.TargetIndex).ConfigureAwait(false);

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
