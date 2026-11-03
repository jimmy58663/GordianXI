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
        /// The menus' window titles by id (ROM/165/76.DAT, 201 entries, #19): the <c>TitleTextId</c> of menu frames ("Config",
        /// "Gameplay") and of the main menu's buttons (the title of the window each opens), shown in the help bar's title box
        /// (<c>titlewin</c>, #313).
        /// </summary>
        MenuTitles,
        /// <summary>
        /// The menus' own message texts (ROM/165/72.DAT): "Examine %s?" (95), "You do not possess any key items." (96), the
        /// equipment set editor's "Remove piece." / "Keep same piece." (275, 276) and its prompts (277-279) (#91, #92).
        /// </summary>
        MenuMessages,
        /// <summary>
        /// The menus' short strings (ROM/165/77.DAT, 528 entries; read 2026-10-31, #487): the currency names of the Status
        /// window's Currencies pages (136-219 and 350-524, not in packet order: 136 "Conquest Points (San d'Oria)", 191
        /// "Bayld", 443 "Unity Accolades"), the Records of Eminence texts (108-135) and the Unity texts (395-453).
        /// </summary>
        MenuStrings,
        /// <summary>
        /// The job points' jobs and categories (ROM/314/62.DAT, read 2026-11-01, #487): job n's name at 2n and its line at
        /// 2n + 1 from 2 ("Warrior", "Adjust various abilities for WAR lv. 99 or above."), the categories after them.
        /// </summary>
        JobPointStrings,
        /// <summary>
        /// The alter ego points' categories and items (ROM/384/121.DAT, read 2026-11-01, #487): 2-7 "HP/MP", "Attributes",
        /// "Skills" with "Valid from Lv. 99 or higher.", 16-37 the eleven S2C 0x08E items with their lines ("Uses 1 alter ego
        /// point to increase the alter ego's STR by 1."); the rest are Japanese placeholders.
        /// </summary>
        AlterEgoStrings,
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
        /// <summary>
        /// The rest of the quest log's name tables (#488), each record an id (the quest's bit in its area's S2C 0x056 block),
        /// a name and the Client / Summary text: Outlands (ROM/176/65, file 55711), Aht Urhgan (ROM/176/66, 55712), Abyssea
        /// (ROM/242/64, 55713), Crystal War (ROM/196/6, 55722), Adoulin (ROM/293/70, 55739) and Coalition assignments
        /// (ROM/293/71, 55740). Other Areas is <see cref="QuestsOther"/> (ROM/176/64, 55710). Found by file id 2026-10-31.
        /// </summary>
        QuestsOutlands,
        QuestsAhtUrhgan,
        QuestsAbyssea,
        QuestsCrystalWar,
        QuestsAdoulin,
        QuestsCoalition,
        /// <summary>
        /// The rest of the mission name tables (#488): Assault (ROM/176/72, 55720), Campaign (ROM/196/8, 55724, 512 rows), A
        /// Crystalline Prophecy (ROM/222/18, 55735), A Moogle Kupo d'Etat (ROM/223/12, 55736), A Shantotto Ascension
        /// (ROM/223/13, 55737) and The Voracious Resurgence (ROM/364/36, 55742). Found by file id 2026-10-31.
        /// </summary>
        MissionsAssault,
        MissionsCampaign,
        MissionsAcp,
        MissionsAmk,
        MissionsAsa,
        MissionsTvr,
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
