// src/Gordian.Core/Resources/Tables/ElementGlyphs.cs
using System;
using System.Text;

namespace Gordian.Core.Resources.Tables
{
    /// <summary>
    /// The element icons inline in FFXI text: the two-byte codes 0xEF 0x1F ... 0xEF 0x26 stand for Fire, Ice, Wind, Earth,
    /// Lightning, Water, Light and Dark (the code range referenced from xi-tools, https://github.com/vekien/xi-tools,
    /// <c>docs/dialog/format.md</c>, "EF1F…EF26"; the element order checked against the
    /// retail item DAT: the Carrier's Sash lists all eight as "Fire+15 Ice+15 Wind+15 Earth+15 Lightning+15 Water+15 Light+15
    /// Dark+15", in the order retail gives elements everywhere). Retail draws each as a small element icon, so an item
    /// description reads "[fire icon]+20" where a word-for-word decode reads "Fire+20" (#417).
    /// <para>
    /// A string decoded with icons (<see cref="DMsgStringTable.DecodeTextWithGlyphs(ReadOnlySpan{byte}, bool)"/>) carries
    /// each icon as one private-use character, <see cref="IconChar"/> (U+E01F ... U+E026, the code's second byte in the
    /// private-use block), which the stock UI's text boxes draw as the icon; <see cref="ToPlainText"/> turns them back into
    /// the element words for anything that reads the text as words.
    /// </para>
    /// </summary>
    public static class ElementGlyphs
    {
        /// <summary>The second byte of the first element code (0xEF 0x1F = Fire).</summary>
        public const byte FirstCode = 0x1F;

        /// <summary>The element names in code order (0xEF 0x1F ... 0xEF 0x26).</summary>
        public static readonly string[] Names = { "Fire", "Ice", "Wind", "Earth", "Lightning", "Water", "Light", "Dark" };

        private const char FirstIcon = (char)(0xE000 + FirstCode);

        /// <summary>The private-use character of element <paramref name="element"/> (0 = Fire ... 7 = Dark).</summary>
        public static char IconChar(int element) =>
            (uint)element < (uint)Names.Length ? (char)(FirstIcon + element) : throw new ArgumentOutOfRangeException(nameof(element));

        /// <summary>The element of an icon character (0 = Fire ... 7 = Dark), or -1 for any other character.</summary>
        public static int ElementOf(char c) => c >= FirstIcon && c < FirstIcon + Names.Length ? c - FirstIcon : -1;

        /// <summary>Whether <paramref name="text"/> holds an element icon.</summary>
        public static bool HasIcons(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text) if (ElementOf(c) >= 0) return true;
            return false;
        }

        /// <summary>The text with each element icon written as its element word ("Fire+20").</summary>
        public static string ToPlainText(string text)
        {
            if (!HasIcons(text)) return text;
            var sb = new StringBuilder(text.Length + 16);
            foreach (char c in text)
            {
                int element = ElementOf(c);
                if (element >= 0) sb.Append(Names[element]);
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
