// src/Gordian.Core/Ui/CraftingLog.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Events;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// The message log lines for synthesis results (S2C 0x06F and 0x070), in the client's own wording.
    /// <list type="bullet">
    /// <item>The "synthesized" and "lost" lines come from the system message table, <c>ROM/27/76.DAT</c> (file id 7031):
    /// 160-163 "You synthesized ..." by grade, 164 "... was lost", and for others 200-203 "&lt;name&gt; synthesized ...",
    /// 204 "&lt;name&gt; lost ...", 205 "&lt;name&gt; obtained ..." (message ids from XiPackets world/server/0x006F and 0x0070;
    /// the items are the message's own item codes). Without the table (tests, a broken install) the same texts are built here.</item>
    /// <item>The result lines ("Synthesis failed. You lost the crystal you were using.") are the client's string table
    /// <c>ROM/0/49.DAT</c> (file id 49); the texts here were checked against that file and a retail capture
    /// (2026-10-07).</item>
    /// <item>Skill-ups are not printed here: the server sends them as S2C 0x029 messages 38 and 53, which
    /// <see cref="CombatLogFormatter"/> words ("Gemini's bonecraft skill rises 0.1 points."). Printing the 0x06F
    /// <c>UpKind</c> / <c>UpLevel</c> as well would show them twice.</item>
    /// </list>
    /// </summary>
    public static class CraftingLog
    {
        /// <summary>Mangled Mess, the item the server names when a synthesis fails and loses items.</summary>
        public const ushort MangledMessItemId = 29695;

        /// <summary>System message ids (ROM/27/76): own "synthesized" 160 + grade, own "lost" 164, others' 200 + grade, 204, 205.</summary>
        public const int OwnSynthesizedMessage = 160, OwnLostMessage = 164, OtherSynthesizedMessage = 200, OtherLostMessage = 204, OtherObtainedMessage = 205;

        /// <summary>The system line for a result id that has its own text; null on success.</summary>
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

        /// <summary>The grade as a table offset: 0 to 3 (an out-of-range grade, such as the failure's -1, prints as 0).</summary>
        private static int GradeOffset(sbyte grade) => grade is >= 0 and <= 3 ? grade : 0;

        /// <summary>Lines for the character's own synthesis.</summary>
        public static IEnumerable<string> FormatOwn(SynthesisOutcome outcome, Func<ushort, ItemRecord?>? itemLookup, ClientMessageController? messages = null)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            if (outcome.IsSuccess)
            {
                if (outcome.ItemId == 0) yield break;
                byte count = Math.Max(outcome.Count, (byte)1);
                var lines = messages?.FormatTableMessage(OwnSynthesizedMessage + GradeOffset(outcome.Grade), new[] { (int)outcome.ItemId, count });
                if (lines is { Count: > 0 })
                {
                    foreach (string line in lines) yield return line;
                }
                else
                {
                    yield return $"You synthesized {StockUiShop.DescribeCount(StockUiShop.Lookup(itemLookup, outcome.ItemId), count)}.";
                }
                yield break;
            }

            string? text = ResultText(outcome.Result, outcome.ResultId);
            if (text != null) yield return text;
            foreach (ushort lost in outcome.LostItemIds)
            {
                if (lost == 0) continue;
                var lines = messages?.FormatTableMessage(OwnLostMessage, new[] { (int)lost });
                if (lines is { Count: > 0 })
                {
                    foreach (string line in lines) yield return line;
                }
                else
                {
                    yield return Capitalize($"{StockUiShop.DescribeCount(StockUiShop.Lookup(itemLookup, lost), 1)} was lost.");
                }
            }
        }

        /// <summary>Lines for another character's synthesis.</summary>
        public static IEnumerable<string> FormatOther(OtherSynthesisOutcome outcome, Func<ushort, ItemRecord?>? itemLookup, ClientMessageController? messages = null)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            var strings = new[] { string.Empty, string.Empty, outcome.Name };
            if (outcome.IsSuccess)
            {
                if (outcome.ItemId == 0) yield break;
                byte count = Math.Max(outcome.Count, (byte)1);
                // Result 0x0C shows the "obtained" line, any other success the grade's "synthesized" line.
                int id = outcome.Result == SynthesisAnswer.SuccessDesynth ? OtherObtainedMessage : OtherSynthesizedMessage + GradeOffset(outcome.Grade);
                var lines = messages?.FormatTableMessage(id, new[] { (int)outcome.ItemId, count }, strings);
                if (lines is { Count: > 0 })
                {
                    foreach (string line in lines) yield return line;
                }
                else
                {
                    string verb = outcome.Result == SynthesisAnswer.SuccessDesynth ? "obtained" : "synthesized";
                    yield return $"{outcome.Name} {verb} {StockUiShop.DescribeCount(StockUiShop.Lookup(itemLookup, outcome.ItemId), count)}.";
                }
                yield break;
            }

            foreach (ushort lost in outcome.LostItemIds)
            {
                if (lost == 0) continue;
                var lines = messages?.FormatTableMessage(OtherLostMessage, new[] { (int)lost }, strings);
                if (lines is { Count: > 0 })
                {
                    foreach (string line in lines) yield return line;
                }
                else
                {
                    yield return $"{outcome.Name} lost {StockUiShop.DescribeCount(StockUiShop.Lookup(itemLookup, lost), 1)}.";
                }
            }
        }

        private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }
}
