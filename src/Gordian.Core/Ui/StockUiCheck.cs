// src/Gordian.Core/Ui/StockUiCheck.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// What the check window shows about a checked player: the character's name (the world entity's, else the 0x0CA
    /// name) and the S2C 0x0C9 / 0x0CA data.
    /// </summary>
    public sealed record StockUiCheckData(string Name, EquipInspectInfo Info)
    {
        /// <summary>The bazaar message's lines (up to three), empty without one.</summary>
        public IReadOnlyList<string> CommentLines =>
            Info.Message is { Message.Length: > 0 } message ? message.Message.Split('\n') : Array.Empty<string>();

        /// <summary>Whether the character has a bazaar open (0x0CA BazaarFlag): View Wares is greyed otherwise.</summary>
        public bool HasBazaar => Info.Message?.HasBazaar == true;

        /// <summary>The jobs line ("WAR75/NIN37"); see <see cref="StockUiCheck.FormatJobs"/>.</summary>
        public string JobText => StockUiCheck.FormatJobs(Info);
    }

    /// <summary>
    /// The player check window (#64): the English menu DAT's <c>inspect</c> window (16,48 top-left, 182 x 190: the
    /// equipment window's 4 x 4 grid of 32 x 32 slots with the same slot labels, and a "View Wares" button, windowps #73,
    /// greyed #431, at (48,148)), opened when a check's S2C 0x0C9 general block arrives. Under it the client draws the
    /// selected item's <c>iteminfo</c> window, or the bazaar message in the <c>comment</c> window (16,240 bottom-left,
    /// 366 x 56: three log lines) when the cursor is not on an item. The grid buttons map to slots by their labels: the
    /// DAT gives buttons 1-4 the left column (Main, Head, Body, Back), 5-8 the second (Sub, Neck, Hands, Waist), 9-12 the
    /// third (Range, Ear1, Ring1, Legs) and 13-16 the right (Ammo, Ear2, Ring2, Feet).
    /// <para>
    /// PROVISIONAL (no retail capture of the check window yet): where retail prints the name, the jobs and the linkshell
    /// (drawn here in the 24 px under View Wares), the jobs format, the comment window's use, and what View Wares does
    /// (it posts that bazaars are not available yet).
    /// </para>
    /// </summary>
    public static class StockUiCheck
    {
        public const string MenuName = "inspect";
        public const string CommentMenu = "comment";
        public const string InfoMenu = "iteminfo";

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

        public static string JobAbbreviation(byte job) => job < Abbreviations.Length ? Abbreviations[job] : "???";

        /// <summary>
        /// The jobs line: main job and level, then the support job and level when there is one ("WAR75/NIN37"); a job
        /// with mastery unlocked shows its mastery level. An anonymous character (every field 0) reads "Anonymous".
        /// PROVISIONAL: the format retail uses here is not captured.
        /// </summary>
        public static string FormatJobs(EquipInspectInfo info)
        {
            if (info.IsAnonymous) return "Anonymous";
            int level = (info.MasteryFlags & 0x01) != 0 && info.MasteryJob == info.MainJob && info.MasteryLevel > 0 ? info.MasteryLevel : info.MainJobLevel;
            string main = $"{JobAbbreviation(info.MainJob)}{level}";
            return info.SubJob == 0 ? main : $"{main}/{JobAbbreviation(info.SubJob)}{info.SubJobLevel}";
        }
    }
}
