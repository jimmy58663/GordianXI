// src/Gordian.Engine/Actions/PlayerActionService.Macros.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Search;
using Gordian.Engine.Input;
using Gordian.Engine.Macros;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Engine.Ui;
using Gordian.Core.World;

namespace Gordian.Engine.Actions
{
    /// <summary>
    /// The macro commands (#339, #362; BG-Wiki "Macro"): <c>/macro</c>, <c>/wait</c>, <c>/recast</c>, <c>/targetnpc</c>,
    /// <c>/targetbnpc</c>, <c>/lastsynth</c>, <c>/acmd</c>, and the chat text placeholders (<see cref="ChatTextTokens"/>).
    /// </summary>
    public sealed partial class PlayerActionService
    {
        /// <summary>The character's macros (<c>/macro book</c> and <c>/macro set</c> change the current set).</summary>
        public MacroLibrary? Macros { get; set; }

        /// <summary>The local player's pet, for <c>&lt;pethpp&gt;</c>, <c>&lt;petmpp&gt;</c> and <c>&lt;pettp&gt;</c>.</summary>
        public Func<LocalPet?>? PetSnapshot { get; set; }

        private (ushort Crystal, ushort[] Ingredients)? _lastSynthesis;

        private void RememberSynthesis(ushort crystal, (ushort ItemId, byte Slot)[] ingredients)
        {
            var ids = new ushort[ingredients.Length];
            for (int i = 0; i < ids.Length; i++) ids[i] = ingredients[i].ItemId;
            _lastSynthesis = (crystal, ids);
        }

        private async Task<PlayerActionResult> ExecuteMacroCommandAsync(ChatCommandResult cmd)
        {
            switch (cmd.Kind)
            {
                case ChatCommandResultKind.Macro:
                    return MacroCommand(cmd.Message ?? string.Empty);
                case ChatCommandResultKind.Wait:
                    // Outside a macro a pause has nothing to hold (the macro runner takes /wait lines itself).
                    return PlayerActionResult.Ok(string.Empty, cmd.Kind);
                case ChatCommandResultKind.Recast:
                    return RecastCommand(cmd.Message ?? string.Empty);
                case ChatCommandResultKind.TargetNpc:
                case ChatCommandResultKind.TargetBattleNpc:
                    return TargetClosest(cmd.Kind == ChatCommandResultKind.TargetBattleNpc);
                case ChatCommandResultKind.LastSynth:
                    return await LastSynthAsync().ConfigureAwait(false);
                case ChatCommandResultKind.AllianceCommand:
                    return await AllianceCommandAsync(cmd.Message ?? string.Empty).ConfigureAwait(false);
                case ChatCommandResultKind.PartyLeader:
                    return await PartyLeaderAsync(cmd.TargetName).ConfigureAwait(false);
                default:
                    return PlayerActionResult.Warn(cmd.Message ?? string.Empty, cmd.Kind);
            }
        }

        private const string MacroUsage = "Usage: /macro book <1-40> | /macro set <1-10>";

        /// <summary><c>/macro book n</c> (1-40) and <c>/macro set n</c> (1-10) change the current book or set (BG-Wiki).</summary>
        public PlayerActionResult MacroCommand(string args)
        {
            const ChatCommandResultKind kind = ChatCommandResultKind.Macro;
            var library = Macros;
            if (library == null) return PlayerActionResult.Warn("Macros are not available.", kind);
            var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
            {
                return PlayerActionResult.Warn(MacroUsage, kind);
            }
            switch (parts[0].ToLowerInvariant())
            {
                case "book":
                    if (number is < 1 or > MacroLibrary.BookCount) return PlayerActionResult.Warn(MacroUsage, kind);
                    library.SelectBook(number - 1);
                    return PlayerActionResult.Ok(string.Empty, kind);
                case "set":
                    if (number is < 1 or > MacroLibrary.SetsPerBook) return PlayerActionResult.Warn(MacroUsage, kind);
                    library.SelectSet(library.CurrentBook, number - 1);
                    return PlayerActionResult.Ok(string.Empty, kind);
                default:
                    return PlayerActionResult.Warn(MacroUsage, kind);
            }
        }

        /// <summary>
        /// <c>/recast "name"</c>: a spell's or job ability's time to ready (BG-Wiki). The wording is GordianXI's
        /// (PROVISIONAL: retail's reply is not documented).
        /// </summary>
        public PlayerActionResult RecastCommand(string name)
        {
            const ChatCommandResultKind kind = ChatCommandResultKind.Recast;
            if (name.Length == 0) return PlayerActionResult.Warn("Usage: /recast \"<spell or ability>\"", kind);
            if (FindRecast(name) is not { } found) return PlayerActionResult.Warn($"Unknown spell or ability: \"{name}\".", kind);
            // Retail: "[Provoke] 0:00" (maintainer retail capture 2026-10-13).
            return PlayerActionResult.Info($"[{found.Name}] {ChatTextTokens.Recast(found.Left)}", kind);
        }

        /// <summary>A spell's or job ability's recast by name (spells first), or null when the name is neither.</summary>
        public (string Name, TimeSpan Left)? FindRecast(string name)
        {
            var catalog = Catalog;
            var combat = Combat;
            if (catalog == null || combat == null) return null;
            if (catalog.Find(ActionKind.Spell, name) is { } spell)
            {
                return (catalog.Name(ActionKind.Spell, spell) ?? name, combat.GetSpellRecastRemaining((ushort)spell));
            }
            if (catalog.Find(ActionKind.JobAbility, name) is { } ability && catalog.GetJobAbility(ability) is { } record)
            {
                return (catalog.Name(ActionKind.JobAbility, ability) ?? name, combat.GetAbilityRecastRemaining(record.RecastId));
            }
            return null;
        }

        /// <summary>
        /// <c>/targetnpc</c> (the closest NPC) and <c>/targetbnpc</c> (the closest enemy) (BG-Wiki), among the entities the
        /// targeting keys could pick, within <see cref="TargetCycling.Range"/> yalms.
        /// </summary>
        public PlayerActionResult TargetClosest(bool enemy)
        {
            var kind = enemy ? ChatCommandResultKind.TargetBattleNpc : ChatCommandResultKind.TargetNpc;
            var self = LocalEntity();
            if (self == null) return PlayerActionResult.Warn("Your character is not in the zone yet.", kind);
            WorldEntity? best = null;
            float bestDistance = float.MaxValue;
            foreach (var entity in _world.GetEntitiesInRadius(self.Position, TargetCycling.Range))
            {
                if (!TargetCycling.IsTargetable(entity)) continue;
                bool matches = enemy ? entity.Type == EntityType.Monster && !entity.IsDeadBattleEntity : entity.Type is EntityType.Npc or EntityType.Door;
                if (!matches) continue;
                float distance = System.Numerics.Vector3.DistanceSquared(entity.Position, self.Position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = entity;
                }
            }
            if (best == null) return PlayerActionResult.Warn(enemy ? "There is no enemy nearby." : "There is no one nearby.", kind);
            SetTargetByServerId(best.ServerId);
            return PlayerActionResult.Ok(string.Empty, kind);
        }

        /// <summary>
        /// <c>/lastsynth</c>: the last synthesis again with the same items (BG-Wiki), found anew in the inventory: the crystal
        /// and each ingredient from the first slots that hold enough.
        /// </summary>
        public async Task<PlayerActionResult> LastSynthAsync()
        {
            const ChatCommandResultKind kind = ChatCommandResultKind.LastSynth;
            if (CraftingModule?.State.IsSynthesizing == true) return PlayerActionResult.Warn("You cannot use that command during synthesis.", kind);
            if (_lastSynthesis is not { } last) return PlayerActionResult.Warn("You have not synthesized anything yet.", kind);
            var inventory = InventoryModule?.State;
            if (inventory == null || CraftingModule == null) return PlayerActionResult.Fail("Synthesis is unavailable.", kind);

            var items = inventory.SnapshotItems(ContainerId.Inventory);
            var used = new Dictionary<byte, uint>();
            byte? Find(ushort itemId)
            {
                InventoryItem? pick = null;
                foreach (var item in items)
                {
                    if (item.ItemId != itemId || item.Slot == 0 || item.Count <= used.GetValueOrDefault(item.Slot)) continue;
                    if (pick == null || item.Slot < pick.Slot) pick = item;
                }
                if (pick == null) return null;
                used[pick.Slot] = used.GetValueOrDefault(pick.Slot) + 1;
                return pick.Slot;
            }

            if (Find(last.Crystal) is not { } crystalSlot) return PlayerActionResult.Warn("You do not have the crystal for your last synthesis.", kind);
            var slots = new List<string> { crystalSlot.ToString(CultureInfo.InvariantCulture) };
            foreach (ushort ingredient in last.Ingredients)
            {
                if (Find(ingredient) is not { } slot) return PlayerActionResult.Warn("You do not have the materials for your last synthesis.", kind);
                slots.Add(slot.ToString(CultureInfo.InvariantCulture));
            }
            var result = await SynthesizeCommandAsync(string.Join(' ', slots)).ConfigureAwait(false);
            return result.Success ? PlayerActionResult.Ok(string.Empty, kind) : result;
        }

        /// <summary>
        /// <c>/pcmd leader name</c>: passes party leadership (C2S 0x077 kind 0 Party, change kind 0 SetPartyLeader; LandSandBoat
        /// <c>packets/c2s/0x077_group_change2.h</c>, https://github.com/LandSandBoat/server). A placeholder (<c>&lt;t&gt;</c>) names its entity.
        /// </summary>
        public async Task<PlayerActionResult> PartyLeaderAsync(string name)
        {
            const ChatCommandResultKind kind = ChatCommandResultKind.PartyLeader;
            if (ChatCommandRouter.IsTargetToken(name) && ResolveTargetToken(name[1..^1].ToLowerInvariant(), out _) is { } entity) name = entity.Name;
            else if (_world.TryGetByName(name, out var found) && found != null) name = found.Name;
            if (name.Length == 0) return PlayerActionResult.Warn("Usage: /pcmd leader <player>", kind);
            await _partyModule.SendGroupChange2Async(name, 0, 0).ConfigureAwait(false);
            return PlayerActionResult.Ok($"Passed party leadership to {name}.", kind);
        }

        /// <summary>
        /// Invites a player who is not in this zone (<c>/pcmd add</c>, <c>/invite</c>, <c>/acmd add</c>). C2S 0x06E carries no
        /// name, only the invitee's character id (<c>UniqueNo</c>) and, in the same zone only, their target index; with the
        /// index 0 the server finds the character by id, in its own zone or through the message server on another map server
        /// (XiPackets <c>world/client/0x006E</c>: "ActIndex ... is only set if the target player is within the same zone";
        /// LandSandBoat <c>packets/c2s/0x06e_group_solicit_req.cpp</c>: <c>zoneutils::GetChar</c>, else <c>ipc::PartyInvite</c>;
        /// https://github.com/atom0s/XiPackets, https://github.com/LandSandBoat/server). The id comes from the search server: a
        /// name search across every area (the same query as <c>/sea all name</c>), whose entries carry the character id.
        /// </summary>
        public async Task<PlayerActionResult> InviteOutOfZoneAsync(string name, PartyKind partyKind, ChatCommandResultKind kind)
        {
            name = name.Trim().Trim('"');
            if (name.Length == 0) return PlayerActionResult.Warn("Usage: /pcmd add <player>", kind);
            var service = SearchService;
            if (service == null || !service.IsConfigured) return PlayerActionResult.Warn($"{name} is not in this area, and the search server is not known yet to find them.", kind);
            try
            {
                var query = new SearchQuery { AllAreas = true, Name = name };
                var result = await service.SearchAsync(query).ConfigureAwait(false);
                var match = result.Players.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (match == null || match.CharacterId == 0)
                {
                    return PlayerActionResult.Warn(result.Complete ? $"{name} is not online." : "The search server did not answer.", kind);
                }
                await _partyModule.SendInviteAsync(match.CharacterId, 0, partyKind).ConfigureAwait(false);
                return PlayerActionResult.Ok(partyKind == PartyKind.Alliance ? $"Invited {match.Name}'s party to the alliance." : $"Invited {match.Name} to party.", kind);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                GordianLog.Warning("PARTY", $"Out-of-zone invite of {name} failed: {ex.Message}");
                return PlayerActionResult.Fail($"Could not invite {name}: {ex.Message}", kind);
            }
        }

        private const string AllianceUsage = "Usage: /acmd <add|leave|breakup|kick|leader> [name]";

        /// <summary>
        /// <c>/acmd</c> (BG-Wiki): <c>add name</c> invites a party leader's party into the alliance (C2S 0x06E kind 5),
        /// <c>leave</c> (0x06F kind 5), <c>breakup</c> (0x070 kind 5), <c>kick name</c> (0x071 kind 5) and <c>leader name</c>
        /// (0x077 kind 5, change kind 1 SetAllianceLeader). Kinds from LandSandBoat <c>packets/c2s/0x071_group_strike.h</c> and
        /// <c>0x077_group_change2.h</c> (https://github.com/LandSandBoat/server).
        /// </summary>
        public async Task<PlayerActionResult> AllianceCommandAsync(string args)
        {
            const ChatCommandResultKind kind = ChatCommandResultKind.AllianceCommand;
            const byte AllianceKind = (byte)PartyKind.Alliance, SetAllianceLeader = 1;
            var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return PlayerActionResult.Warn(AllianceUsage, kind);
            string name = parts.Length > 1 ? parts[1].Trim('"') : string.Empty;
            WorldEntity? named = null;
            if (name.Length > 0 && ChatCommandRouter.IsTargetToken(name)) named = ResolveTargetToken(name[1..^1].ToLowerInvariant(), out _);
            else if (name.Length > 0 && _world.TryGetByName(name, out var found)) named = found;
            if (named != null) name = named.Name;

            switch (parts[0].ToLowerInvariant())
            {
                case "add" or "invite":
                    if (name.Length == 0) return PlayerActionResult.Warn(AllianceUsage, kind);
                    if (named == null) return await InviteOutOfZoneAsync(name, PartyKind.Alliance, kind).ConfigureAwait(false);
                    await _partyModule.SendInviteAsync(named.ServerId, named.TargetIndex, PartyKind.Alliance).ConfigureAwait(false);
                    return PlayerActionResult.Ok($"Invited {name}'s party to the alliance.", kind);
                case "leave":
                    await _partyModule.LeavePartyAsync(PartyKind.Alliance).ConfigureAwait(false);
                    return PlayerActionResult.Ok("Left the alliance.", kind);
                case "breakup" or "disband":
                    await _partyModule.DisbandPartyAsync(PartyKind.Alliance).ConfigureAwait(false);
                    return PlayerActionResult.Ok("Disbanded the alliance.", kind);
                case "kick":
                    if (name.Length == 0) return PlayerActionResult.Warn(AllianceUsage, kind);
                    await _partyModule.KickMemberAsync(named?.ServerId ?? 0, named?.TargetIndex ?? 0, name, AllianceKind).ConfigureAwait(false);
                    return PlayerActionResult.Ok($"Removed {name}'s party from the alliance.", kind);
                case "leader":
                    if (name.Length == 0) return PlayerActionResult.Warn(AllianceUsage, kind);
                    await _partyModule.SendGroupChange2Async(name, AllianceKind, SetAllianceLeader).ConfigureAwait(false);
                    return PlayerActionResult.Ok($"Passed alliance leadership to {name}.", kind);
                default:
                    return PlayerActionResult.Warn(AllianceUsage, kind);
            }
        }

        /// <summary>
        /// Fills the chat text placeholders (<see cref="ChatTextTokens"/>) from the character's state: vitals, jobs, the pet,
        /// recasts and target names. Placeholders it cannot fill stay as typed.
        /// </summary>
        public string ExpandChatTokens(string text) => ChatTextTokens.Expand(text, ResolveChatToken);

        private string? ResolveChatToken(string word)
        {
            var player = _localPlayer;
            switch (word)
            {
                case "hp": return ChatTextTokens.CurrentOfMax(player.CurrentHp, player.MaxHp);
                case "mp": return ChatTextTokens.CurrentOfMax(player.CurrentMp, player.MaxMp);
                case "hpp": return ChatTextTokens.Percent(player.Hpp);
                case "mpp": return ChatTextTokens.Percent(player.Mpp);
                case "tp": return player.CurrentTp.ToString(CultureInfo.InvariantCulture);
                case "job":
                    return ChatTextTokens.Jobs(JobText(player.MainJob), player.MainJobLevel, JobText(player.SubJob), player.SubJobLevel) is { Length: > 0 } jobs ? jobs : null;
                case "mjob":
                    return ChatTextTokens.JobLevel(JobText(player.MainJob), player.MainJobLevel) is { Length: > 0 } main ? main : null;
                case "sjob":
                    return ChatTextTokens.JobLevel(JobText(player.SubJob), player.SubJobLevel) is { Length: > 0 } sub ? sub : null;
                case "pethpp": return PetSnapshot?.Invoke() is { } p1 ? ChatTextTokens.Percent(p1.Hpp) : null;
                case "petmpp": return PetSnapshot?.Invoke() is { } p2 ? ChatTextTokens.Percent(p2.Mpp) : null;
                case "pettp": return PetSnapshot?.Invoke() is { } p3 ? p3.Tp.ToString(CultureInfo.InvariantCulture) : null;
            }
            if (word.StartsWith("recast=", StringComparison.Ordinal))
            {
                return FindRecast(word["recast=".Length..]) is { } recast ? ChatTextTokens.Recast(recast.Left) : null;
            }
            // Target placeholders become the entity's name; the sub-target cursor ones (<st> ...) stay as typed.
            if (SubTargetFilter(word) != null) return null;
            return ResolveTargetToken(word, out _)?.Name is { Length: > 0 } name ? name : null;
        }

        private static string? JobText(JobId job) => job == JobId.None ? null : StockUiCheck.JobAbbreviation((byte)job);
    }
}
