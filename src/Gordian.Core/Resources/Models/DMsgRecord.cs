// src/Gordian.Core/Resources/Models/DMsgRecord.cs
using System;
using System.Collections.Generic;

namespace Gordian.Core.Resources.Models
{
    /// <summary>
    /// Well-known categories of FFXI d_msg string resource tables.
    /// </summary>
    public enum DMsgCategory
    {
        Spells,
        SpellHelp,
        Abilities,
        AbilityHelp,
        StatusNames,
        Titles,
        Jobs,
        KeyItems,
        QuestsSandoria,
        QuestsBastok,
        QuestsWindurst,
        QuestsJeuno,
        QuestsOther,
        MissionsSandoria,
        MissionsBastok,
        MissionsWindurst,
        MissionsZilart,
        MissionsCop,
        MissionsToau,
        MissionsWotg,
        MissionsAdoulin,
        MissionsRov,
        Custom
    }

    /// <summary>
    /// Represents one decoded row from a d_msg string table containing an array of sub-strings or numeric parameters.
    /// </summary>
    public sealed class DMsgRecord
    {
        public int Index { get; }
        public int Offset { get; }
        public IReadOnlyList<string> SubStrings { get; }
        public IReadOnlyDictionary<string, string> NamedFields { get; }

        public string PrimaryText => SubStrings.Count > 0 ? SubStrings[0] : string.Empty;

        public DMsgRecord(int index, int offset, IReadOnlyList<string> subStrings, IReadOnlyDictionary<string, string>? namedFields = null)
        {
            Index = index;
            Offset = offset;
            SubStrings = subStrings ?? Array.Empty<string>();
            NamedFields = namedFields ?? new Dictionary<string, string>();
        }

        public override string ToString() => $"[DMsg {Index}] {PrimaryText}";
    }
}
