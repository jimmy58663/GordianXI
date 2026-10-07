// src/Gordian.Core/Ui/CraftingLog.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// The message log lines for synthesis results (S2C 0x06F and 0x070). Retail prints them from a message DAT
    /// (<c>ROM/27/76.DAT</c>, ids 160 and 200 plus the grade); the texts here follow the templates XiPackets documents
    /// for them. Skill-ups are decoded into <see cref="SynthesisOutcome.SkillUps"/> but not printed, as the text of
    /// that line has not been captured.
    /// </summary>
    public static class CraftingLog
    {
        /// <summary>Mangled Mess, the item the server names when a synthesis fails and loses items.</summary>
        public const ushort MangledMessItemId = 29695;

        /// <summary>The system line for a result id that has its own text (XiPackets S2C 0x06F table); null on success.</summary>
        public static string? ResultText(SynthesisAnswer result, byte resultId) => result switch
        {
            SynthesisAnswer.Success or SynthesisAnswer.SuccessDesynth => null,
            SynthesisAnswer.Failed => "Synthesis failed. You lost the crystal you were using.",
            SynthesisAnswer.Interrupted or SynthesisAnswer.InterruptedCritical => "Synthesis interrupted. You lost the crystal and materials you were using.",
            SynthesisAnswer.CancelBadRecipe => "Synthesis canceled. That combination of materials cannot be synthesized.",
            SynthesisAnswer.Cancel => "Synthesis canceled.",
            SynthesisAnswer.CancelSkillTooLow => "Synthesis canceled. That formula is beyond your current craft skill level.",
            SynthesisAnswer.CancelRareItem => "Synthesis canceled. You cannot hold more than one item of that type.",
            SynthesisAnswer.MustWaitLonger => "You must wait longer before repeating that action.",
            // Retail treats every other id as a failure that lost the crystal.
            _ => "Synthesis failed. You lost the crystal you were using.",
        };

        /// <summary>Lines for the character's own synthesis.</summary>
        public static IEnumerable<string> FormatOwn(SynthesisOutcome outcome, Func<ushort, ItemRecord?>? itemLookup)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            if (outcome.IsSuccess)
            {
                if (outcome.ItemId != 0) yield return $"You synthesized {StockUiShop.DescribeCount(StockUiShop.Lookup(itemLookup, outcome.ItemId), Math.Max(outcome.Count, (byte)1))}.";
                yield break;
            }

            string? text = ResultText(outcome.Result, outcome.ResultId);
            if (text != null) yield return text;
            foreach (ushort lost in outcome.LostItemIds)
            {
                if (lost != 0) yield return $"{StockUiShop.DescribeCount(StockUiShop.Lookup(itemLookup, lost), 1)} was lost.".Capitalize();
            }
        }

        /// <summary>Lines for another character's synthesis.</summary>
        public static IEnumerable<string> FormatOther(OtherSynthesisOutcome outcome, Func<ushort, ItemRecord?>? itemLookup)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            if (outcome.IsSuccess)
            {
                if (outcome.ItemId != 0) yield return $"{outcome.Name} synthesized {StockUiShop.DescribeCount(StockUiShop.Lookup(itemLookup, outcome.ItemId), Math.Max(outcome.Count, (byte)1))}.";
                yield break;
            }

            foreach (ushort lost in outcome.LostItemIds)
            {
                if (lost != 0) yield return $"{outcome.Name} lost {StockUiShop.Lookup(itemLookup, lost).Name}.";
            }
        }

        private static string Capitalize(this string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }
}
