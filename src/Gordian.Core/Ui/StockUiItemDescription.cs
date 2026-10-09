// src/Gordian.Core/Ui/StockUiItemDescription.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Ui
{
    /// <summary>One page of an equipment description: its lines (name first) and the footer at the bottom right.</summary>
    public sealed record StockUiItemDescriptionPage(IReadOnlyList<string> Lines, string Footer, bool HasMore)
    {
        /// <summary>The lines the window holds: the text lines plus the footer's line when there is a footer or a next page.</summary>
        public int LineCount => Lines.Count + (Footer.Length > 0 || HasMore ? 1 : 0);
    }

    /// <summary>
    /// The equipment description retail shows in the item info window (the maintainer's retail screenshots of a check,
    /// 2026-10-07: "Bihu roundlet +3" / "[Head]All Races" / the description's lines / "Lv.99 BRD" / "&lt;Item
    /// Level:119&gt;" and a page arrow at the bottom right). The window grows with the lines up to twelve
    /// (<c>item4inf</c> ... <c>item9inf</c>, <c>item10in</c> ... <c>item12in</c> in the English menu DAT: 8 + 16 n px tall,
    /// 366 wide; three lines or fewer use <c>iteminfo</c>), and a longer description continues on further pages, which the
    /// gamepad's X turns. The first page carries the name, the slot and races, up to eight description lines, the level
    /// and jobs, and the footer; the next pages repeat the name and slot line and carry up to nine more description lines
    /// (the screenshots' Bihu roundlet +3: eight lines then two).
    /// <para>
    /// PROVISIONAL (from two screenshots of one item): the weapon line ("(Hand-to-Hand)All Races"), the race wording
    /// other than "All Races", how retail wraps a long job list, and the Rare / Ex marks (not drawn).
    /// </para>
    /// </summary>
    public static class StockUiItemDescription
    {
        /// <summary>The tallest item info window's line count (<c>item12in</c>).</summary>
        public const int MaxLines = 12;

        /// <summary>Description lines on the first page (it also holds the level line) and on the pages after it.</summary>
        public const int FirstPageBodyLines = 8, NextPageBodyLines = 9;

        /// <summary>Characters a line holds before the job list wraps (the log font's ~7.3 px over the window's ~310 px of text).</summary>
        private const int WrapColumns = 42;

        /// <summary>The item info window for a line count: <c>iteminfo</c> up to three lines, then <c>item4inf</c> ... <c>item12in</c>.</summary>
        public static string FrameFor(int lines)
        {
            int n = Math.Clamp(lines, 3, MaxLines);
            if (n <= 3) return "iteminfo";
            return n <= 9 ? $"item{n}inf" : $"item{n}in";
        }

        /// <summary>
        /// The item's display name as retail writes it in the info window: the log name with its first letter capitalized
        /// ("Bihu roundlet +3", where the short name is "Bihu Roundlet +3" and abbreviations like "Behem. Cesti +1" occur).
        /// </summary>
        public static string DisplayName(ItemRecord record)
        {
            string name = string.IsNullOrWhiteSpace(record.LogName) ? record.Name : record.LogName;
            return name.Length > 0 && char.IsLower(name[0]) ? char.ToUpperInvariant(name[0]) + name[1..] : name;
        }

        private static readonly string[] SlotNames =
        {
            "Main", "Sub", "Range", "Ammo", "Head", "Body", "Hands", "Legs", "Feet", "Neck", "Waist", "Ear", "Ear", "Ring", "Ring", "Back",
        };

        /// <summary>Weapon skill names by skill id (LandSandBoat <c>SKILLTYPE</c>), for the weapon line.</summary>
        private static readonly Dictionary<byte, string> SkillNames = new()
        {
            [1] = "Hand-to-Hand", [2] = "Dagger", [3] = "Sword", [4] = "Great Sword", [5] = "Axe", [6] = "Great Axe",
            [7] = "Scythe", [8] = "Polearm", [9] = "Katana", [10] = "Great Katana", [11] = "Club", [12] = "Staff",
            [25] = "Archery", [26] = "Marksmanship", [27] = "Throwing", [41] = "String Instrument", [42] = "Wind Instrument",
            [45] = "Handbell", [48] = "Fishing",
        };

        /// <summary>The slot and races line: "[Head]All Races"; a weapon names its skill in parentheses.</summary>
        public static string SlotLine(ItemRecord record)
        {
            string slot;
            if (record.Skill != 0 && SkillNames.TryGetValue(record.Skill, out var skill)) slot = $"({skill})";
            else if (record.ShieldSize > 0) slot = "[Shield]";
            else
            {
                slot = string.Empty;
                for (int i = 0; i < SlotNames.Length; i++)
                {
                    if (record.CanEquipSlot(i))
                    {
                        slot = $"[{SlotNames[i]}]";
                        break;
                    }
                }
            }
            return slot + Races(record.RacesMask);
        }

        /// <summary>"All Races", or the races the item fits (both sexes: the race; one: "Hume Male").</summary>
        public static string Races(ushort mask)
        {
            const ushort all = 0x1FE;
            if ((mask & all) == all) return "All Races";
            var parts = new List<string>(4);
            void Pair(int male, int female, string race)
            {
                bool m = (mask & (1 << male)) != 0, f = (mask & (1 << female)) != 0;
                if (m && f) parts.Add(race);
                else if (m) parts.Add(race + " Male");
                else if (f) parts.Add(race + " Female");
            }
            Pair(1, 2, "Hume");
            Pair(3, 4, "Elvaan");
            Pair(5, 6, "Tarutaru");
            if ((mask & (1 << 7)) != 0) parts.Add("Mithra");
            if ((mask & (1 << 8)) != 0) parts.Add("Galka");
            return string.Join(' ', parts);
        }

        /// <summary>The level and jobs line(s): "Lv.99 BRD", "All Jobs" when every job may equip it; wrapped at about 42 columns.</summary>
        public static List<string> LevelLines(ItemRecord record)
        {
            var lines = new List<string>(2);
            if (record.Level == 0 || record.Level == ushort.MaxValue || record.JobsMask == 0) return lines;
            const uint allJobs = 0x7FFFFE; // jobs 1-22
            var line = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"Lv.{record.Level}"));
            if ((record.JobsMask & allJobs) == allJobs)
            {
                line.Append(" All Jobs");
            }
            else
            {
                for (int job = 1; job <= 22; job++)
                {
                    if (!record.CanEquipJob(job)) continue;
                    string abbreviation = StockUiCheck.JobAbbreviation((byte)job);
                    if (line.Length + 1 + abbreviation.Length > WrapColumns)
                    {
                        lines.Add(line.ToString());
                        line.Clear();
                        line.Append(abbreviation);
                        continue;
                    }
                    line.Append(' ').Append(abbreviation);
                }
            }
            lines.Add(line.ToString());
            return lines;
        }

        /// <summary>The footer: "&lt;Item Level:119&gt;" for an item with an item level, else empty.</summary>
        public static string Footer(ItemRecord record) =>
            record.ItemLevel > 0 ? string.Create(CultureInfo.InvariantCulture, $"<Item Level:{record.ItemLevel}>") : string.Empty;

        /// <summary>The description's pages (at least one).</summary>
        public static List<StockUiItemDescriptionPage> Pages(ItemRecord record)
        {
            string name = DisplayName(record);
            string slotLine = SlotLine(record);
            var body = new List<string>();
            foreach (string line in (record.Description ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0) body.Add(trimmed);
            }
            var level = LevelLines(record);
            string footer = Footer(record);

            var pages = new List<StockUiItemDescriptionPage>();
            int at = 0;
            bool first = true;
            do
            {
                int take = Math.Min(body.Count - at, first ? FirstPageBodyLines : NextPageBodyLines);
                var lines = new List<string>(MaxLines) { name, slotLine };
                lines.AddRange(body.GetRange(at, take));
                at += take;
                if (first) lines.AddRange(level);
                first = false;
                pages.Add(new StockUiItemDescriptionPage(lines, footer, at < body.Count));
            }
            while (at < body.Count);
            return pages;
        }
    }
}
