// src/Gordian.Core/Ui/StockUiPlayerCommands.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Gordian.Core.World;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// The message log lines for the everyday command results: the wide scan list, proposals and their tallies, and the
    /// <c>/random</c> roll. The server sends no text for the first two (the client builds the wide scan window and the
    /// vote text itself), so the wording here is GordianXI's own until a retail capture settles it; the dice line is the
    /// LandSandBoat message 88 text (<c>msg_std.h</c>: "Dice Roll! player rolls roll").
    /// </summary>
    public static class StockUiPlayerCommands
    {
        /// <summary>Most entries printed for one wide scan list.</summary>
        public const int MaxWideScanLines = 40;

        /// <summary>
        /// The lines for a finished wide scan: a header, then the nearest entries first with their kind, level, name when
        /// known, and distance. <paramref name="resolveName"/> gives the name of an entity by target index (the server does
        /// not send names on LandSandBoat).
        /// </summary>
        public static IEnumerable<string> FormatWideScan(IReadOnlyList<WideScanEntry> entries, Func<ushort, string?> resolveName)
        {
            if (entries.Count == 0)
            {
                yield return "Wide Scan: nothing found.";
                yield break;
            }

            yield return $"Wide Scan: {entries.Count} found.";
            var ordered = entries
                .OrderBy(e => (double)e.DeltaX * e.DeltaX + (double)e.DeltaZ * e.DeltaZ)
                .ToList();
            foreach (var entry in ordered.Take(MaxWideScanLines))
            {
                string kind = entry.Type switch { 0 => "Player", 1 => "NPC", 2 => "Monster", _ => "Entity" };
                string name = entry.Name.Length > 0 ? entry.Name : resolveName(entry.ActIndex) ?? $"#{entry.ActIndex}";
                string level = entry.Level > 0 ? $" Lv.{entry.Level}" : string.Empty;
                double distance = Math.Sqrt((double)entry.DeltaX * entry.DeltaX + (double)entry.DeltaZ * entry.DeltaZ);
                yield return $"  {kind}{level} {name}: {distance:0} yalms ({entry.DeltaX:+0;-0;0}, {entry.DeltaZ:+0;-0;0})";
            }
            if (ordered.Count > MaxWideScanLines) yield return $"  ...and {ordered.Count - MaxWideScanLines} more.";
        }

        /// <summary>The lines for a proposal that just started: who, the question, and the numbered options.</summary>
        public static IEnumerable<string> FormatProposalStart(VoteProposal proposal)
        {
            yield return $"{proposal.ProposerName} proposes: {proposal.Question}";
            for (int i = 0; i < proposal.Options.Count; i++)
            {
                yield return $"  {i + 1}: {proposal.Options[i]}";
            }
            if (proposal.Options.Count > 0) yield return $"Vote with /vote <number> {proposal.ProposerName}";
        }

        /// <summary>The lines for the final results of a proposal.</summary>
        public static IEnumerable<string> FormatProposalResult(VoteProposal proposal)
        {
            yield return $"Results of {proposal.ProposerName}'s proposal: {proposal.Question}";
            for (int i = 0; i < proposal.Options.Count; i++)
            {
                int votes = i < proposal.Votes.Count ? proposal.Votes[i] : 0;
                yield return $"  {i + 1}: {proposal.Options[i]} ({votes} vote{(votes == 1 ? string.Empty : "s")})";
            }
        }

        /// <summary>
        /// The <c>/random</c> line from S2C 0x009 message 88, whose data is <c>string2 NAME string3 N</c>; null when the data
        /// does not have that shape.
        /// </summary>
        public static string? FormatDiceRoll(string data)
        {
            const string NameTag = "string2 ", RollTag = " string3 ";
            if (!data.StartsWith(NameTag, StringComparison.Ordinal)) return null;
            int rollAt = data.LastIndexOf(RollTag, StringComparison.Ordinal);
            if (rollAt < 0) return null;
            string name = data[NameTag.Length..rollAt];
            string roll = data[(rollAt + RollTag.Length)..].Trim();
            return name.Length == 0 || roll.Length == 0 ? null : $"{name} rolls {roll}.";
        }
    }
}
