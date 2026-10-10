// src/Gordian.Core/World/PlayerCommandState.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Network.Packets;

namespace Gordian.Core.World
{
    /// <summary>
    /// The unlocked job emotes (<c>/jobemote</c>) and chairs (<c>/sitchair</c>) of S2C 0x11A. Nothing reads it yet: the
    /// Main Menu, Communication page that shows them does not exist.
    /// </summary>
    public sealed class EmoteListState
    {
        public const int ChairCount = 11;

        private readonly object _sync = new();
        private uint _jobEmotes;
        private ushort _chairs;
        private bool _received;

        /// <summary>True once S2C 0x11A has arrived this session.</summary>
        public bool Received { get { lock (_sync) return _received; } }

        /// <summary>The raw job emote bits: bit 0 Warrior ... bit 21 Rune Fencer (job id - 1).</summary>
        public uint JobEmoteBits { get { lock (_sync) return _jobEmotes; } }

        /// <summary>The raw chair bits: bit 0 chair 1 ... bit 10 chair 11.</summary>
        public ushort ChairBits { get { lock (_sync) return _chairs; } }

        public event Action? Changed;

        public bool HasJobEmote(JobId job)
        {
            int bit = (int)job - 1;
            return bit is >= 0 and < 22 && (JobEmoteBits & (1u << bit)) != 0;
        }

        /// <summary>Whether chair <paramref name="chair"/> (1 to 11) is unlocked.</summary>
        public bool HasChair(int chair) => chair is >= 1 and <= ChairCount && (ChairBits & (1 << (chair - 1))) != 0;

        public void Apply(uint jobEmotes, ushort chairs)
        {
            lock (_sync)
            {
                _jobEmotes = jobEmotes;
                _chairs = chairs;
                _received = true;
            }
            Changed?.Invoke();
        }
    }

    /// <summary>One entry of a wide scan list (S2C 0x0F4).</summary>
    /// <param name="ActIndex">The entity's target index.</param>
    /// <param name="Level">The entity's level (0 when the server does not send it).</param>
    /// <param name="Type">0 player (blue), 1 NPC (green), 2 monster (red).</param>
    /// <param name="DeltaX">East-west distance from the player when scanned.</param>
    /// <param name="DeltaZ">North-south distance from the player when scanned.</param>
    /// <param name="Name">The name the packet carries (empty on LandSandBoat).</param>
    public readonly record struct WideScanEntry(ushort ActIndex, byte Level, byte Type, short DeltaX, short DeltaZ, string Name);

    /// <summary>The tracked entity of S2C 0x0F5.</summary>
    public readonly record struct WideScanTrack(ushort ActIndex, byte Level, Vector3 Position, TrackingPosState State);

    /// <summary>
    /// Wide scan (Ranger and Beastmaster): the list the server sends in answer to C2S 0x0F4 (S2C 0x0F6 start, 0x0F4
    /// entries, 0x0F6 end) and the position of the entity tracked with C2S 0x0F5. The map overlay that draws the dots
    /// does not exist yet; <c>/widescan</c> prints the list to the message log.
    /// </summary>
    public sealed class WideScanState
    {
        private readonly object _sync = new();
        private readonly List<WideScanEntry> _entries = new();
        private TrackingListState _listState;
        private WideScanTrack? _track;

        /// <summary>The state of the last list: <see cref="TrackingListState.ListStart"/> while entries arrive, <see cref="TrackingListState.ListEnd"/> when complete, <see cref="TrackingListState.Error"/> when the server refused.</summary>
        public TrackingListState ListState { get { lock (_sync) return _listState; } }

        /// <summary>The tracked entity, or null when none (or the server ended tracking).</summary>
        public WideScanTrack? Track { get { lock (_sync) return _track; } }

        /// <summary>Raised when a list finished (ListEnd), with its entries, on the network thread.</summary>
        public event Action<IReadOnlyList<WideScanEntry>>? ListCompleted;

        /// <summary>Raised when the server refused or ended a list (error state).</summary>
        public event Action<TrackingListState>? ListFailed;

        /// <summary>Raised after the tracked entity's position or state changed.</summary>
        public event Action<WideScanTrack>? TrackUpdated;

        public IReadOnlyList<WideScanEntry> Snapshot()
        {
            lock (_sync) return _entries.ToArray();
        }

        public void ApplyListState(TrackingListState state)
        {
            IReadOnlyList<WideScanEntry>? done = null;
            lock (_sync)
            {
                _listState = state;
                if (state == TrackingListState.ListStart) _entries.Clear();
                else if (state == TrackingListState.ListEnd) done = _entries.ToArray();
            }

            if (done != null) ListCompleted?.Invoke(done);
            else if (state is TrackingListState.Error or TrackingListState.End) ListFailed?.Invoke(state);
        }

        public void AddEntry(WideScanEntry entry)
        {
            lock (_sync) _entries.Add(entry);
        }

        public void ApplyTrack(WideScanTrack track)
        {
            lock (_sync) _track = track.State is TrackingPosState.Lose or TrackingPosState.End ? null : track;
            TrackUpdated?.Invoke(track);
        }

        /// <summary>Forgets the list and the tracked entity (a zone change).</summary>
        public void Clear()
        {
            lock (_sync)
            {
                _entries.Clear();
                _listState = TrackingListState.None;
                _track = null;
            }
        }
    }

    /// <summary>A party, linkshell or area proposal (<c>/nominate</c>) and its tally.</summary>
    /// <param name="ProposerName">Who made it; <c>/vote</c> names them.</param>
    /// <param name="Kind">The chat scope it was made in.</param>
    /// <param name="Question">The question, without the brackets.</param>
    /// <param name="Options">The options in order (option 1 first), without their numbers.</param>
    /// <param name="Votes">The votes per option, in the same order; all zero until a 0x079 arrives.</param>
    /// <param name="Closed">True once the final results arrived.</param>
    public sealed record VoteProposal(string ProposerName, ProposalKind Kind, string Question, IReadOnlyList<string> Options, IReadOnlyList<int> Votes, bool Closed);

    /// <summary>
    /// Proposals seen this session (S2C 0x078 start, 0x079 tally) and the last proposer, whom <c>/vote</c> defaults to.
    /// </summary>
    public sealed class VoteState
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, VoteProposal> _proposals = new(StringComparer.OrdinalIgnoreCase);
        private string _lastProposer = string.Empty;

        /// <summary>The proposer of the most recent proposal seen; empty when none.</summary>
        public string LastProposer { get { lock (_sync) return _lastProposer; } }

        /// <summary>Raised after a proposal started, changed its tally or closed, on the network thread.</summary>
        public event Action<VoteProposal>? Changed;

        public VoteProposal? Get(string proposerName)
        {
            lock (_sync) return _proposals.GetValueOrDefault(proposerName);
        }

        /// <summary>The proposal most recently started, or null.</summary>
        public VoteProposal? Latest
        {
            get
            {
                lock (_sync) return _lastProposer.Length == 0 ? null : _proposals.GetValueOrDefault(_lastProposer);
            }
        }

        public void ApplyStart(string proposer, ProposalKind kind, string text)
        {
            ParseText(text, out string question, out var options, out _);
            var proposal = new VoteProposal(proposer, kind, question, options, new int[options.Count], false);
            lock (_sync)
            {
                _proposals[proposer] = proposal;
                _lastProposer = proposer;
            }
            Changed?.Invoke(proposal);
        }

        /// <summary>Applies a tally. <paramref name="votes"/> is indexed by option number (entry 0 unused); <paramref name="text"/> is empty for a live update.</summary>
        public void ApplyProc(string proposer, ProposalKind kind, bool closed, int optionCount, ReadOnlySpan<int> votes, string text)
        {
            VoteProposal proposal;
            lock (_sync)
            {
                _proposals.TryGetValue(proposer, out var existing);
                string question = existing?.Question ?? string.Empty;
                IReadOnlyList<string> options = existing?.Options ?? Array.Empty<string>();
                if (text.Length > 0)
                {
                    ParseText(text, out question, out options, out _);
                }
                int count = Math.Max(optionCount, options.Count);
                var tally = new int[count];
                for (int i = 0; i < count && i + 1 < votes.Length; i++) tally[i] = votes[i + 1];
                proposal = new VoteProposal(proposer, existing?.Kind ?? kind, question, options, tally, closed);
                _proposals[proposer] = proposal;
            }
            Changed?.Invoke(proposal);
        }

        public void Clear()
        {
            lock (_sync)
            {
                _proposals.Clear();
                _lastProposer = string.Empty;
            }
        }

        /// <summary>
        /// Splits the wire text <c>[question]\n1:first\n2:second</c> (or <c>1[votes]:first</c> in the final results) into the
        /// question and options. Text that does not follow the format is taken as the question alone.
        /// </summary>
        public static void ParseText(string text, out string question, out IReadOnlyList<string> options, out IReadOnlyList<int> votes)
        {
            var optionList = new List<string>();
            var voteList = new List<int>();
            question = string.Empty;
            string[] lines = text.Split('\n');
            if (lines.Length > 0)
            {
                string first = lines[0].Trim();
                question = first.Length >= 2 && first[0] == '[' && first[^1] == ']' ? first[1..^1] : first;
            }
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string head = line[..colon];
                int open = head.IndexOf('[');
                int tally = 0;
                if (open >= 0 && head.EndsWith(']')) int.TryParse(head[(open + 1)..^1], out tally);
                optionList.Add(line[(colon + 1)..]);
                voteList.Add(tally);
            }
            options = optionList;
            votes = voteList;
        }
    }

    /// <summary>A system message to print (S2C 0x053): its id in the client's system message table and its two numbers.</summary>
    public readonly record struct SystemMessageInfo(ushort MessageId, uint Para, uint Para2);

    /// <summary>
    /// An emote someone in range made (S2C 0x05A), our own included: the caster, the target (0 / 0 without one), the
    /// emote id (the C2S 0x05D ids; job emotes from 74), its <c>Param</c> and how it plays.
    /// </summary>
    public readonly record struct EmoteEcho(uint CasterId, ushort CasterIndex, uint TargetId, ushort TargetIndex, ushort EmoteId, ushort Param, EmoteMode Mode)
    {
        /// <summary>Whether the emote was made at a target ("waves to X" rather than "waves").</summary>
        public bool HasTarget => TargetId != 0;

        /// <summary>Whether the log line shows (mode 0 or 1).</summary>
        public bool ShowsText => Mode != EmoteMode.Motion;

        /// <summary>Whether the motion plays (mode 0 or 2).</summary>
        public bool PlaysMotion => Mode != EmoteMode.Text;
    }

    /// <summary>A checked character's bazaar message and title (S2C 0x0CA).</summary>
    public sealed record InspectMessageInfo(string Name, string Message, bool HasBazaar, bool IsSelf, byte Race, uint TitleId);

    /// <summary>
    /// The bazaar messages the server sent (S2C 0x0CA): the last checked character's, and our own (LandSandBoat sends ours
    /// on zone-in). The check window (#64) shows the one that came with a check.
    /// </summary>
    public sealed class InspectMessageState
    {
        private readonly object _sync = new();
        private InspectMessageInfo? _last;
        private InspectMessageInfo? _own;

        /// <summary>The last message received, or null.</summary>
        public InspectMessageInfo? Last { get { lock (_sync) return _last; } }

        /// <summary>Our own bazaar message (the last one with <c>MyFlag</c> set), or null.</summary>
        public InspectMessageInfo? Own { get { lock (_sync) return _own; } }

        public event Action<InspectMessageInfo>? Received;

        public void Apply(InspectMessageInfo info)
        {
            ArgumentNullException.ThrowIfNull(info);
            lock (_sync)
            {
                _last = info;
                if (info.IsSelf) _own = info;
            }
            Received?.Invoke(info);
        }
    }

    /// <summary>
    /// The state behind the everyday command packets: the emote list (S2C 0x11A), emotes made in range (0x05A), wide scan
    /// (0x0F4-0x0F6), proposals (0x078 / 0x079), system messages (0x053), bazaar messages (0x0CA) and player checks (0x0C9). One per session.
    /// </summary>
    public sealed class PlayerCommandState
    {
        public EmoteListState Emotes { get; } = new();
        public WideScanState WideScan { get; } = new();
        public VoteState Votes { get; } = new();
        public InspectMessageState Inspect { get; } = new();

        /// <summary>Player checks (S2C 0x0C9): the checked character's equipment, jobs and linkshell.</summary>
        public EquipInspectState Equipment { get; } = new();

        /// <summary>The last emote made in range (S2C 0x05A), or null.</summary>
        public EmoteEcho? LastEmote { get; private set; }

        /// <summary>The last system message (S2C 0x053), or null.</summary>
        public SystemMessageInfo? LastSystemMessage { get; private set; }

        /// <summary>An emote was made in range (S2C 0x05A), ours included: the log line and the motion follow from it.</summary>
        public event Action<EmoteEcho>? EmotePerformed;

        /// <summary>A system message arrived (S2C 0x053).</summary>
        public event Action<SystemMessageInfo>? SystemMessageReceived;

        public void ApplyEmote(EmoteEcho emote)
        {
            LastEmote = emote;
            EmotePerformed?.Invoke(emote);
        }

        public void ApplySystemMessage(SystemMessageInfo message)
        {
            LastSystemMessage = message;
            SystemMessageReceived?.Invoke(message);
        }

        /// <summary>Forgets what does not survive a zone change: the wide scan list and the tracked entity.</summary>
        public void OnZoneChanged()
        {
            WideScan.Clear();
            Equipment.Clear();
        }
    }
}
