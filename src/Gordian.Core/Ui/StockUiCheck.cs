// src/Gordian.Core/Ui/StockUiCheck.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// What the check window shows about a checked player: the character's name (the world entity's, else the 0x0CA
    /// name), the S2C 0x0C9 / 0x0CA data, and whether the character has items for sale in a bazaar.
    /// </summary>
    /// <param name="HasBazaar">
    /// Whether the character's bazaar holds items: the BazaarFlag of its entity update (S2C 0x00D flags1 bit 31,
    /// <c>PlayerEntity.HasBazaar</c>), which LandSandBoat sets only while items are for sale. 0x0CA's own BazaarFlag is
    /// set on every packet LandSandBoat sends, so it says nothing.
    /// </param>
    /// <param name="JobText">The help bar's jobs line (<see cref="StockUiCheck.FormatJobs"/>), filled in when the window opens.</param>
    public sealed record StockUiCheckData(string Name, EquipInspectInfo Info, bool HasBazaar = false, string? JobText = null)
    {
        /// <summary>The bazaar message's lines (up to three), empty without one.</summary>
        public IReadOnlyList<string> CommentLines =>
            Info.Message is { Message.Length: > 0 } message ? message.Message.Split('\n') : Array.Empty<string>();
    }

    /// <summary>
    /// The player check window (#64): the English menu DAT's <c>inspect</c> window (16,48 top-left, 182 x 190: the
    /// equipment window's 4 x 4 grid of 32 x 32 slots with the same slot labels, and a "View Wares" button, windowps #73,
    /// greyed #431, at (48,148)), opened when a check's S2C 0x0C9 general block arrives. The grid buttons map to slots by
    /// their labels: buttons 1-4 are the left column (Main, Head, Body, Back), 5-8 the second (Sub, Neck, Hands, Waist),
    /// 9-12 the third (Range, Ear1, Ring1, Legs) and 13-16 the right (Ammo, Ear2, Ring2, Feet).
    /// <para>
    /// From the maintainer's retail screenshots (2026-10-07): the name and jobs ("Lv.99 Bard / Lv.49 White Mage", "???"
    /// when anonymous) are in the help bar at the top of the screen (<c>titlewin</c> and <c>helpwind</c>); the linkshell's
    /// rank icon and name sit under View Wares; the cursor starts on View Wares, where the bazaar message shows in the
    /// <c>comment</c> window; on an item, its description fills an <c>item{N}inf</c> window under the grid, paged with the
    /// gamepad's X (<see cref="StockUiItemDescription"/>).
    /// </para>
    /// </summary>
    public static class StockUiCheck
    {
        public const string MenuName = "inspect";
        public const string CommentMenu = "comment";

        /// <summary>The help bar's two windows: the title box on the left (the checked name) and the help line (the jobs).</summary>
        public const string TitleMenu = "titlewin", HelpMenu = "helpwind";

        /// <summary>The "View Wares" button (opens the checked character's bazaar in retail).</summary>
        public const int ViewWaresButton = 17;

        private static readonly EquipSlotId[] ButtonSlots =
        {
            EquipSlotId.Main, EquipSlotId.Head, EquipSlotId.Body, EquipSlotId.Back,
            EquipSlotId.Sub, EquipSlotId.Neck, EquipSlotId.Hands, EquipSlotId.Waist,
            EquipSlotId.Ranged, EquipSlotId.Ear1, EquipSlotId.Ring1, EquipSlotId.Legs,
            EquipSlotId.Ammo, EquipSlotId.Ear2, EquipSlotId.Ring2, EquipSlotId.Feet,
        };

        /// <summary>The slot a grid button (1-16) shows, by its DAT label; false for View Wares and anything else.</summary>
        public static bool TryGetSlot(int buttonId, out EquipSlotId slot)
        {
            if (buttonId >= 1 && buttonId <= ButtonSlots.Length)
            {
                slot = ButtonSlots[buttonId - 1];
                return true;
            }
            slot = default;
            return false;
        }

        /// <summary>The grid button (1-16) that shows a slot.</summary>
        public static int ButtonOf(EquipSlotId slot) => Array.IndexOf(ButtonSlots, slot) + 1;

        /// <summary>
        /// Job abbreviations by job id, as the client's job abbreviation table (ROM/165/87: 0 "NON", 1 "WAR" ... 22
        /// "RUN") holds them.
        /// </summary>
        private static readonly string[] Abbreviations =
        {
            "NON", "WAR", "MNK", "WHM", "BLM", "RDM", "THF", "PLD", "DRK", "BST", "BRD", "RNG",
            "SAM", "NIN", "DRG", "SMN", "BLU", "COR", "PUP", "DNC", "SCH", "GEO", "RUN",
        };

        /// <summary>Job names by job id, as the client's job name table (ROM/165/86: 1 "Warrior" ... 22 "Rune Fencer") holds them.</summary>
        private static readonly string[] Names =
        {
            "None", "Warrior", "Monk", "White Mage", "Black Mage", "Red Mage", "Thief", "Paladin", "Dark Knight", "Beastmaster",
            "Bard", "Ranger", "Samurai", "Ninja", "Dragoon", "Summoner", "Blue Mage", "Corsair", "Puppetmaster", "Dancer",
            "Scholar", "Geomancer", "Rune Fencer",
        };

        public static string JobAbbreviation(byte job) => job < Abbreviations.Length ? Abbreviations[job] : "???";

        public static string JobName(byte job) => job < Names.Length ? Names[job] : "???";

        /// <summary>
        /// The help bar's jobs line, as retail writes it: "Lv.99 Bard / Lv.49 White Mage", the main job alone without a
        /// support job, and "???" for an anonymous character (every job field 0). A job with mastery unlocked shows its
        /// mastery level (XiPackets). <paramref name="jobName"/> reads the client's job name table; null or a missing
        /// entry falls back to the English names.
        /// </summary>
        public static string FormatJobs(EquipInspectInfo info, Func<byte, string?>? jobName = null)
        {
            if (info.IsAnonymous) return "???";
            string Name(byte job) => jobName?.Invoke(job) is { Length: > 0 } name ? name : JobName(job);
            int level = (info.MasteryFlags & 0x01) != 0 && info.MasteryJob == info.MainJob && info.MasteryLevel > 0 ? info.MasteryLevel : info.MainJobLevel;
            string main = string.Create(CultureInfo.InvariantCulture, $"Lv.{level} {Name(info.MainJob)}");
            return info.SubJob == 0 ? main : string.Create(CultureInfo.InvariantCulture, $"{main} / Lv.{info.SubJobLevel} {Name(info.SubJob)}");
        }
    }
}
