// src/Gordian.Engine/Ui/StockUiSocialActions.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Network.Packets;
using Gordian.Core.Network.Search;
using Gordian.Core.World;
using Gordian.Engine.Actions;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// The session's side of the Party, Search, Linkshell and Communication windows (#489, #490): reads the party state, the
    /// server's config copy (S2C 0x0B4) and the search server, and runs the windows' commands through the same chat commands
    /// the player can type (<c>/pcmd</c>, <c>/acmd</c>, <c>/blacklist</c>) or, where no command exists, the packet modules.
    /// </summary>
    /// <remarks>
    /// Packets: C2S 0x0DC (config flags InviteFlg, AutoPartyFlg, RecruitFlg; XiPackets <c>world/client/0x00DC</c>), C2S 0x0DB
    /// kind 1 (party languages; XiPackets <c>world/client/0x00DB</c>), C2S 0x077 GROUP_CHANGE2 kind 0 with change kinds 4
    /// SetQuartermaster, 5 SetLottery, 6 SetLevelSync and 7 DisableLevelSync (LandSandBoat
    /// <c>packets/c2s/0x077_group_change2.h</c> and <c>CParty::AssignPartyRole</c>) and C2S 0x11C PARTY_REQUEST (XiPackets
    /// <c>world/client/0x011C</c>; LandSandBoat's handler only logs it). https://github.com/atom0s/XiPackets,
    /// https://github.com/LandSandBoat/server.
    /// </remarks>
    public sealed class StockUiSocialActions : IStockUiSocial
    {
        private const byte PartyKindParty = 0, SetQuartermaster = 4, SetLottery = 5, SetLevelSync = 6, DisableLevelSync = 7;
        private const byte RequestAdd = 0, RequestRemove = 1;

        private readonly PlayerActionService _actions;
        private readonly PartyState _party;
        private readonly PartyPacketModule _partyModule;
        private readonly ChatPacketModule _chatModule;
        private readonly Func<uint> _selfId;
        private readonly Func<string> _selfName;
        private readonly Func<ushort> _zone;

        public StockUiSocialActions(PlayerActionService actions, PartyState party, PartyPacketModule partyModule, ChatPacketModule chatModule,
            Func<uint> selfId, Func<string> selfName, Func<ushort> zone)
        {
            _chatModule = chatModule ?? throw new ArgumentNullException(nameof(chatModule));
            _actions = actions ?? throw new ArgumentNullException(nameof(actions));
            _party = party ?? throw new ArgumentNullException(nameof(party));
            _partyModule = partyModule ?? throw new ArgumentNullException(nameof(partyModule));
            _selfId = selfId;
            _selfName = selfName;
            _zone = zone;
        }

        private SearchService? Search => _actions.SearchService;

        private Func<byte, string?>? JobName => _actions.Menus.JobName;

        private Func<int, string?>? ZoneName => _actions.Menus.ZoneName;

        public ushort CurrentZone => _zone();

        public StockUiSocialState State
        {
            get
            {
                var config = _actions.ConfigModule?.State;
                var members = _party.Members;
                string? quartermaster = members.FirstOrDefault(m => m.IsQuartermaster)?.Name;
                return new StockUiSocialState(
                    config?.IsSet(PlayerConfigFlags.Invite) == true,
                    config?.IsSet(PlayerConfigFlags.AutoParty) == true,
                    config?.IsSet(PlayerConfigFlags.Recruit) == true,
                    (int)(config?.PartyLanguages ?? PartyLanguages.None),
                    string.IsNullOrEmpty(quartermaster) ? null : quartermaster,
                    members.Any(m => m.IsLevelSynced));
            }
        }

        public IReadOnlyList<StockUiPerson> PartyMembers(bool includeSelf)
        {
            uint self = _selfId();
            var people = new List<StockUiPerson>();
            // Your own party's members only (alliance parties are not yours to lead), in slot order.
            foreach (var member in _party.Members.Where(m => m.PartyNumber == 0).OrderBy(m => m.MemberNumber))
            {
                if (!includeSelf && member.ServerId == self) continue;
                string detail = StockUiPeople.Detail((byte)member.MainJob, member.MainJobLevel, (byte)member.SubJob, member.SubJobLevel,
                    member.ZoneId != 0 ? ZoneName?.Invoke(member.ZoneId) : null, JobName);
                people.Add(new StockUiPerson(member.Name, member.ServerId, member.IsInOtherZone(_zone()) ? (ushort)0 : member.TargetIndex,
                    Detail: detail.Length > 0 ? detail : null));
            }
            return people;
        }

        public IReadOnlyList<StockUiPerson> JoinRequests() =>
            _party.SnapshotJoinRequests().Select(r => new StockUiPerson(r.Name, r.ServerId, r.TargetIndex)).ToList();

        public async Task<StockUiPeopleResult> MemberListAsync()
        {
            var search = Search;
            if (search is { IsConfigured: true })
            {
                try
                {
                    if (await search.RefreshPartyListAsync().ConfigureAwait(false) is { Players.Count: > 0 } list)
                        return new StockUiPeopleResult(list.Players.Select(p => StockUiPeople.FromSearch(p, JobName, ZoneName)).ToList());
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Gordian.Core.Diagnostics.GordianLog.Warning("UI", $"Member List: the search server failed ({ex.Message}); listing the party state.");
                }
            }
            // No search server answer: the members the world server told us about.
            return new StockUiPeopleResult(PartyMembers(includeSelf: true));
        }

        public async Task<StockUiPeopleResult> SearchAsync(SearchQuery query)
        {
            var search = Search;
            if (search is not { IsConfigured: true }) return StockUiPeopleResult.Failed("Search is unavailable: the search server address is not known yet.");
            var result = await search.SearchAsync(query).ConfigureAwait(false);
            if (result.Players.Count == 0 && !result.Complete) return StockUiPeopleResult.Failed("The search server did not answer.");
            return new StockUiPeopleResult(result.Players.Select(p => StockUiPeople.FromSearch(p, JobName, ZoneName)).ToList());
        }

        public async Task<StockUiPeopleResult> LinkshellMembersAsync(int slot)
        {
            var search = Search;
            if (search is not { IsConfigured: true }) return StockUiPeopleResult.Failed("Linkshell: the search server address is not known yet.");
            var result = await search.RefreshLinkshellListAsync(slot).ConfigureAwait(false);
            if (result == null) return StockUiPeopleResult.Failed("Linkshell: no linkshell is equipped there.");
            if (result.Players.Count == 0 && !result.Complete) return StockUiPeopleResult.Failed("The search server did not answer.");
            return new StockUiPeopleResult(result.Players.Select(p => StockUiPeople.FromSearch(p, JobName, ZoneName)).ToList());
        }

        public async Task<StockUiPeopleResult> BlacklistAsync()
        {
            var module = _actions.SocialModule;
            if (module == null) return StockUiPeopleResult.Failed("Blacklist is unavailable in this session.");
            if (!module.Blacklist.IsComplete)
            {
                var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnChanged() { if (module.Blacklist.IsComplete) answered.TrySetResult(); }
                module.Blacklist.Changed += OnChanged;
                try
                {
                    await module.RequestBlacklistAsync().ConfigureAwait(false);
                    await Task.WhenAny(answered.Task, Task.Delay(_actions.SocialReplyTimeout)).ConfigureAwait(false);
                }
                finally
                {
                    module.Blacklist.Changed -= OnChanged;
                }
            }
            return new StockUiPeopleResult(module.Blacklist.Snapshot().Select(e => new StockUiPerson(e.Name, CharacterId: e.Id)).ToList());
        }

        public async Task<string?> RunAsync(StockUiSocialCommand command, StockUiPerson person = default, int argument = 0)
        {
            switch (command)
            {
                case StockUiSocialCommand.SetSeekParty:
                    return await SetFlagAsync(PlayerConfigFlags.Invite, argument != 0, "Party flag on.", "Party flag off.").ConfigureAwait(false);
                case StockUiSocialCommand.SetAutogroup:
                    return await SetFlagAsync(PlayerConfigFlags.AutoParty, argument != 0, "Autogroup on.", "Autogroup off.").ConfigureAwait(false);
                case StockUiSocialCommand.SetRecruit:
                    return await SetFlagAsync(PlayerConfigFlags.Recruit, argument != 0,
                        "You are now accepting party requests.", "You are no longer accepting party requests.").ConfigureAwait(false);
                case StockUiSocialCommand.SetPartyLanguages:
                    if (_actions.ConfigModule is not { } config) return "Languages are unavailable in this session.";
                    await config.SetPartyLanguagesAsync((PartyLanguages)argument).ConfigureAwait(false);
                    return null;
                case StockUiSocialCommand.PassLeader:
                    return await CommandAsync($"/pcmd leader {person.Name}").ConfigureAwait(false);
                case StockUiSocialCommand.Kick:
                    return await CommandAsync($"/pcmd kick {person.Name}").ConfigureAwait(false);
                case StockUiSocialCommand.Invite:
                    return await CommandAsync($"/pcmd add {person.Name}").ConfigureAwait(false);
                case StockUiSocialCommand.AllianceInviteTarget:
                    if (_actions.CurrentTarget is not { } target || target.ServerId == _selfId()) return "Target the leader of the party to invite to the alliance.";
                    return await CommandAsync("/acmd add <t>").ConfigureAwait(false);
                case StockUiSocialCommand.AllianceDissolve:
                    return await CommandAsync("/acmd breakup").ConfigureAwait(false);
                case StockUiSocialCommand.LevelSync:
                    await _partyModule.SendGroupChange2Async(person.Name, PartyKindParty, SetLevelSync).ConfigureAwait(false);
                    return null;
                case StockUiSocialCommand.DisableLevelSync:
                    await _partyModule.SendGroupChange2Async(_selfName(), PartyKindParty, DisableLevelSync).ConfigureAwait(false);
                    return null;
                case StockUiSocialCommand.SetLottery:
                    await _partyModule.SendGroupChange2Async(_selfName(), PartyKindParty, SetLottery).ConfigureAwait(false);
                    return null;
                case StockUiSocialCommand.SetQuartermaster:
                    await _partyModule.SendGroupChange2Async(person.Name, PartyKindParty, SetQuartermaster).ConfigureAwait(false);
                    return null;
                case StockUiSocialCommand.RequestJoin or StockUiSocialCommand.CancelRequest:
                    uint id = person.ServerId != 0 ? person.ServerId : person.CharacterId;
                    if (id == 0) return "That player cannot be found.";
                    await _partyModule.SendPartyRequestAsync(id, person.TargetIndex, command == StockUiSocialCommand.RequestJoin ? RequestAdd : RequestRemove).ConfigureAwait(false);
                    return command == StockUiSocialCommand.RequestJoin ? $"You request to join {person.Name}'s party." : $"You withdraw your request to join {person.Name}'s party.";
                case StockUiSocialCommand.Target:
                    return await CommandAsync($"/target {person.Name}").ConfigureAwait(false);
                case StockUiSocialCommand.BlacklistRemove:
                    return await CommandAsync($"/blacklist delete {person.Name}").ConfigureAwait(false);
                default:
                    return null;
            }
        }

        public async Task<string?> SetSearchCommentAsync(string text, uint commentType)
        {
            await _chatModule.SetSearchMessageAsync(text, (SearchMessageType)commentType).ConfigureAwait(false);
            return null;
        }

        private async Task<string?> SetFlagAsync(PlayerConfigFlags flag, bool on, string onNotice, string offNotice)
        {
            if (_actions.ConfigModule is not { } config) return "That is unavailable in this session.";
            await config.SetFlagAsync(flag, on).ConfigureAwait(false);
            // PROVISIONAL: LandSandBoat sends no message for these flags; retail's wording is not captured.
            return on ? onNotice : offNotice;
        }

        /// <summary>Runs a chat command as typed, and returns what it printed.</summary>
        private async Task<string?> CommandAsync(string line)
        {
            var result = await _actions.ExecuteCommandAsync(line).ConfigureAwait(false);
            return result.Message is { Length: > 0 } message ? message : null;
        }
    }
}
