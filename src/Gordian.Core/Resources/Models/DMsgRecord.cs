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
        /// <summary>Zone names by zone id (ROM/165/84.DAT, 300 entries: 230 = "Southern San d'Oria"; found 2026-09-28).</summary>
        ZoneNames,
        /// <summary>Short zone names by zone id (ROM/165/83.DAT: 230 = "S.San d'Oria").</summary>
        ZoneNamesShort,
        /// <summary>Compact zone names by zone id (ROM/165/85.DAT: 230 = "SSandOria"), the text retail's party window shows in parentheses for a member in another zone (#146).</summary>
        ZoneNamesCompact,
        /// <summary>
        /// Region names in the client's order (ROM/165/78.DAT, 55 entries: 0 "San d'Oria", 1 "Bastok" ... 28 "Empire of Aht
        /// Urhgan" ... 50 "The Adoulin Islands" ... 54 "Far East"; found 2026-10-25), the Map window's Change Map list.
        /// </summary>
        RegionNames,
        /// <summary>
        /// Weather names by weather id (ROM/165/79.DAT, 20 entries of two strings, noun and adjective: 0 "fine patches" /
        /// "fine", 1 "sunshine" / "sunny", 6 "rain" / "rainy", 19 "darkness" / "dark"). The forecast lines' 0x01 kind
        /// 0x18 names the noun, 0x17 the adjective (#125).
        /// </summary>
        WeatherNames,
        /// <summary>
        /// A table of 526 short UI strings (ROM/165/61.DAT; found 2026-10-03): race and sex marks first (0 = "全種"),
        /// the Unity leaders at rows 418-429 (418 = "No Unity", 419 = "Pieuje" ... 428 = "Yoran-Oran", 429 = "Sylvie").
        /// </summary>
        MiscStrings,
        /// <summary>
        /// The config menu's row texts (ROM/165/74.DAT, 206 entries; found 2026-09-28, #19): chat filter and Log page
        /// message types (36-62, 196), Font Colors samples (63-88, 197, 204-205), Effects rows (153-170).
        /// </summary>
        MenuConfigRows,
        /// <summary>
        /// The menus' help text by id (ROM/165/75.DAT, 1099 entries, #19): the <c>HelpTextId</c> of menu frames and buttons
        /// ("Select an item.", "Use an item."), shown in the help bar (<c>helpwind</c>).
        /// </summary>
        MenuHelp,
        /// <summary>
        /// The menus' own message texts (ROM/165/72.DAT): "Examine %s?" (95), "You do not possess any key items." (96), the
        /// equipment set editor's "Remove piece." / "Keep same piece." (275, 276) and its prompts (277-279) (#91, #92).
        /// </summary>
        MenuMessages,
        /// <summary>
        /// Mount names by client mount id (ROM/351/84.DAT, EN file id 0xD981, 39 entries: 0 "Chocobo", 1 "Raptor" ... 38
        /// "Bubble Crab"), the mount list's rows (#87). Table referenced from xi-tools (docs/mounts/mechanism.md).
        /// </summary>
        MountNames,
        /// <summary>
        /// Mount help lines by client mount id (ROM/351/85.DAT, EN file id 0xD982: 0 "Calls forth your personal chocobo."),
        /// shown in the help bar over the mount list (#87; retail capture Mounts.mp4).
        /// </summary>
        MountHelp,
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
        private readonly uint?[] _numbers;

        public string PrimaryText => SubStrings.Count > 0 ? SubStrings[0] : string.Empty;

        public DMsgRecord(int index, int offset, IReadOnlyList<string> subStrings, IReadOnlyDictionary<string, string>? namedFields = null, uint?[]? numbers = null)
        {
            Index = index;
            Offset = offset;
            SubStrings = subStrings ?? Array.Empty<string>();
            NamedFields = namedFields ?? new Dictionary<string, string>();
            _numbers = numbers ?? Array.Empty<uint?>();
        }

        /// <summary>
        /// Reads a numeric sub-entry (for example a key item's id in sub-entry 0). False when the sub-entry is text or missing.
        /// </summary>
        public bool TryGetNumber(int subIndex, out uint value)
        {
            if ((uint)subIndex < (uint)_numbers.Length && _numbers[subIndex] is uint number)
            {
                value = number;
                return true;
            }

            value = 0;
            return false;
        }

        public override string ToString() => $"[DMsg {Index}] {PrimaryText}";
    }
}
