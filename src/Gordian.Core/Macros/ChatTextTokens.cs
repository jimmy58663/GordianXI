// src/Gordian.Core/Macros/ChatTextTokens.cs
using System;
using System.Globalization;
using System.Text;

namespace Gordian.Engine.Macros
{
    /// <summary>
    /// The placeholders the retail client fills in when chat text is sent (BG-Wiki "Macro", display tokens; #362):
    /// <c>&lt;hp&gt;</c>, <c>&lt;mp&gt;</c>, <c>&lt;hpp&gt;</c>, <c>&lt;mpp&gt;</c>, <c>&lt;tp&gt;</c>, <c>&lt;job&gt;</c>,
    /// <c>&lt;mjob&gt;</c>, <c>&lt;sjob&gt;</c>, the pet's <c>&lt;pethpp&gt;</c> / <c>&lt;petmpp&gt;</c> / <c>&lt;pettp&gt;</c>,
    /// <c>&lt;recast=Name&gt;</c>, and the target placeholders (<c>&lt;t&gt;</c>, <c>&lt;me&gt;</c>, <c>&lt;bt&gt;</c>,
    /// <c>&lt;p0&gt;</c>-<c>&lt;p5&gt;</c> ...) as the entity's name. <c>&lt;wait n&gt;</c> is a macro's pause
    /// (<see cref="MacroRunner"/>) and is removed from the text. A placeholder nothing can fill (no pet, nothing targeted,
    /// an unknown word) stays as typed.
    /// <para>The output formats are PROVISIONAL (BG-Wiki gives usage, not output): <c>&lt;hp&gt;</c> "current/max",
    /// <c>&lt;hpp&gt;</c> "n%", <c>&lt;tp&gt;</c> the number, <c>&lt;job&gt;</c> "WAR75/NIN37", <c>&lt;mjob&gt;</c> "WAR75",
    /// <c>&lt;sjob&gt;</c> "NIN37", <c>&lt;recast=X&gt;</c> "m:ss". To check against retail.</para>
    /// </summary>
    public static class ChatTextTokens
    {
        /// <summary>
        /// Replaces every placeholder <paramref name="resolve"/> knows. It gets the word between the brackets, lower-cased
        /// and trimmed (for <c>&lt;recast="Provoke"&gt;</c>: <c>recast="provoke"</c> with the original case kept after the
        /// '='), and returns the text to put in its place, or null to leave it as typed.
        /// </summary>
        public static string Expand(string text, Func<string, string?> resolve)
        {
            ArgumentNullException.ThrowIfNull(resolve);
            if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0) return text;
            var sb = new StringBuilder(text.Length);
            bool removedWait = false;
            int i = 0;
            while (i < text.Length)
            {
                int open = text.IndexOf('<', i);
                if (open < 0)
                {
                    sb.Append(text, i, text.Length - i);
                    break;
                }
                int close = text.IndexOf('>', open + 1);
                if (close < 0)
                {
                    sb.Append(text, i, text.Length - i);
                    break;
                }
                sb.Append(text, i, open - i);
                string inner = text.Substring(open + 1, close - open - 1);
                string word = NormalizeWord(inner);
                bool wait = IsWait(word, out _);
                removedWait |= wait;
                string? value = wait ? string.Empty : resolve(word);
                if (value == null) sb.Append(text, open, close - open + 1);
                else sb.Append(value);
                i = close + 1;
            }
            // Removing a <wait n> can leave a double or trailing space.
            string result = sb.ToString();
            return removedWait ? result.Replace("  ", " ").TrimEnd() : result;
        }

        /// <summary>The placeholder's word: lower case and trimmed, the name after <c>recast=</c> kept as typed (quotes removed).</summary>
        public static string NormalizeWord(string inner)
        {
            string trimmed = inner.Trim();
            int eq = trimmed.IndexOf('=');
            if (eq < 0) return trimmed.ToLowerInvariant();
            return trimmed[..eq].Trim().ToLowerInvariant() + "=" + trimmed[(eq + 1)..].Trim().Trim('"').Trim();
        }

        /// <summary>Whether a placeholder word is a pause (<c>wait</c>, <c>wait 2</c>, <c>wait2.5</c>) and how many seconds.</summary>
        public static bool IsWait(string word, out double seconds)
        {
            seconds = 0;
            if (!word.StartsWith("wait", StringComparison.OrdinalIgnoreCase)) return false;
            string rest = word[4..].Trim();
            if (rest.Length == 0)
            {
                seconds = 1;
                return true;
            }
            return double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && seconds >= 0;
        }

        /// <summary>"current/max", as <c>&lt;hp&gt;</c> and <c>&lt;mp&gt;</c> show it.</summary>
        public static string CurrentOfMax(int current, int max) => string.Create(CultureInfo.InvariantCulture, $"{current}/{max}");

        /// <summary>"n%", as <c>&lt;hpp&gt;</c> and the other percentages show it.</summary>
        public static string Percent(int value) => string.Create(CultureInfo.InvariantCulture, $"{value}%");

        /// <summary>A job and level: "WAR75"; empty for no job.</summary>
        public static string JobLevel(string? abbreviation, int level) =>
            string.IsNullOrEmpty(abbreviation) || level <= 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"{abbreviation}{level}");

        /// <summary>Main and support job: "WAR75/NIN37", or "WAR75" without a support job.</summary>
        public static string Jobs(string? main, int mainLevel, string? sub, int subLevel)
        {
            string m = JobLevel(main, mainLevel), s = JobLevel(sub, subLevel);
            return s.Length == 0 ? m : m + "/" + s;
        }

        /// <summary>A recast as "m:ss" ("0:00" when ready).</summary>
        public static string Recast(TimeSpan left)
        {
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            int total = (int)Math.Ceiling(left.TotalSeconds);
            return string.Create(CultureInfo.InvariantCulture, $"{total / 60}:{total % 60:00}");
        }
    }
}
