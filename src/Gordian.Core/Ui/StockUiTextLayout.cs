// src/Gordian.Core/Ui/StockUiTextLayout.cs
using System;
using System.Collections.Generic;
using System.Text;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// The one text layout every stock UI text box uses (item descriptions in the Items, Equipment, check, shop, guild shop,
    /// synthesis, Treasure Pool and Key Items windows, key item texts, prompt messages): text wrapped to a box's width as the
    /// font measures it, at spaces, keeping the text's own line breaks, and cut to the lines the box holds. A word wider than
    /// the box on its own is broken between characters, so no line is ever wider than the box. The renderer clips each box's
    /// drawing to the same bounds (<c>StockUiTextBoxes</c> in the app).
    /// <para>
    /// Before this each window wrapped on its own (five helpers), and the item descriptions by a fixed 42 characters: a job
    /// list of wide capitals ("Lv.7 WAR RDM THF PLD DRK BST BRD RNG SAM NIN DRG BLU COR DNC RUN", the Leather Bandana) is
    /// 42 characters on one line and ran past the window (the maintainer's round 4, 2026-10-19, image-17). Retail wraps
    /// (the retail captures show item descriptions on several lines inside the window, broken at spaces: "A white powder
    /// formed by applying / extreme heat to zinc ore."), with no smaller font and no text cut mid-glyph.
    /// </para>
    /// </summary>
    public static class StockUiTextLayout
    {
        /// <summary>
        /// Wraps <paramref name="text"/> to <paramref name="width"/> (in the units <paramref name="measure"/> returns) at spaces,
        /// starting a new line at each line break of the text; at most <paramref name="maxLines"/> lines. Blank lines are left
        /// out unless <paramref name="keepBlankLines"/> (then only those at the start and end are).
        /// </summary>
        public static List<string> Wrap(string? text, Func<string, float> measure, float width, int maxLines = int.MaxValue, bool keepBlankLines = false)
        {
            ArgumentNullException.ThrowIfNull(measure);
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text) || maxLines <= 0) return lines;
            foreach (string paragraph in text.Replace("\r", string.Empty).Split('\n'))
            {
                string line = string.Empty;
                foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    string candidate = line.Length == 0 ? word : line + " " + word;
                    if (measure(candidate) <= width)
                    {
                        line = candidate;
                        continue;
                    }
                    if (line.Length > 0) lines.Add(line);
                    line = word;
                    // A word wider than the box alone: break it between characters.
                    while (line.Length > 1 && measure(line) > width)
                    {
                        int fit = Fit(line, measure, width);
                        lines.Add(line[..fit]);
                        line = line[fit..];
                    }
                }
                if (line.Length > 0 || keepBlankLines) lines.Add(line);
            }
            while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
            while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
            return lines.Count > maxLines ? lines.GetRange(0, maxLines) : lines;
        }

        /// <summary>How many leading characters of <paramref name="text"/> fit <paramref name="width"/> (at least one).</summary>
        private static int Fit(string text, Func<string, float> measure, float width)
        {
            int n = 1;
            while (n < text.Length && measure(text[..(n + 1)]) <= width) n++;
            return n;
        }

        /// <summary>
        /// A stand-in measure for when no font is at hand (tests, the Core before the HUD has set one): every character
        /// <paramref name="advance"/> units wide.
        /// </summary>
        public static Func<string, float> FixedAdvance(float advance) => text => text.Length * advance;

        /// <summary>The lines a box of <paramref name="height"/> holds at <paramref name="lineHeight"/> per line (at least 0).</summary>
        public static int LinesIn(float height, float lineHeight) => lineHeight <= 0 ? 0 : Math.Max(0, (int)(height / lineHeight));

        /// <summary>Joins tokens with spaces (the item description's level and jobs line before it is wrapped).</summary>
        public static string Join(IEnumerable<string> tokens)
        {
            var sb = new StringBuilder();
            foreach (string t in tokens)
            {
                if (t.Length == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(t);
            }
            return sb.ToString();
        }
    }
}
