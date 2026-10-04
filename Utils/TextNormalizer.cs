using System.Globalization;
using System.Text;

namespace SkillzBot.Utils
{
    /// <summary>
    /// How Latin letters are folded into the canonical Cyrillic alphabet.
    /// </summary>
    public enum NormalizationMode
    {
        /// <summary>Latin letters are read by sound ("pidor" -> "пидор").</summary>
        Transliteration,
        /// <summary>Latin letters are read by shape ("nugop" -> "пидор").</summary>
        Lookalike
    }

    /// <summary>
    /// Folds chat text into a canonical lowercase Cyrillic form so that dictionary words
    /// and chat messages can be compared regardless of case, script mixing, diacritics,
    /// zalgo marks, leet digits or repeated letters.
    /// </summary>
    public static class TextNormalizer
    {
        private static readonly char[] TranslitTable = new char[char.MaxValue + 1];
        private static readonly char[] LookalikeTable = new char[char.MaxValue + 1];

        static TextNormalizer()
        {
            for (int i = 0; i < TranslitTable.Length; i++)
            {
                TranslitTable[i] = (char)i;
                LookalikeTable[i] = (char)i;
            }

            // Shared folds: other Cyrillic alphabets, Greek, leet digits, symbols,
            // and Latin letters that have no Unicode decomposition.
            var shared = new (char From, char To)[]
            {
                ('і', 'и'), ('ї', 'и'), ('є', 'е'), ('ґ', 'г'), ('ў', 'у'), ('ѓ', 'г'), ('ќ', 'к'),
                ('ђ', 'д'), ('ћ', 'ч'), ('ј', 'и'), ('ѕ', 'с'), ('ѐ', 'е'), ('ѝ', 'и'), ('ё', 'е'), ('й', 'и'),
                ('α', 'а'), ('β', 'в'), ('ε', 'е'), ('ι', 'и'), ('κ', 'к'), ('μ', 'м'), ('ο', 'о'),
                ('π', 'п'), ('ρ', 'р'), ('τ', 'т'), ('υ', 'у'), ('χ', 'х'),
                ('0', 'о'), ('1', 'и'), ('3', 'з'), ('4', 'ч'), ('6', 'б'), ('@', 'а'),
                ('ø', 'о'), ('ı', 'и'), ('ɑ', 'а'), ('ß', 'в'), ('æ', 'а'), ('đ', 'д'), ('ł', 'л'), ('ħ', 'н'), ('ŧ', 'т'),
            };
            foreach (var (from, to) in shared)
            {
                TranslitTable[from] = to;
                LookalikeTable[from] = to;
            }

            var translit = new (char From, char To)[]
            {
                ('a', 'а'), ('b', 'б'), ('c', 'с'), ('d', 'д'), ('e', 'е'), ('f', 'ф'), ('g', 'г'), ('h', 'х'),
                ('i', 'и'), ('j', 'и'), ('k', 'к'), ('l', 'л'), ('m', 'м'), ('n', 'н'), ('o', 'о'), ('p', 'п'),
                ('q', 'к'), ('r', 'р'), ('s', 'с'), ('t', 'т'), ('u', 'у'), ('v', 'в'), ('w', 'в'), ('x', 'х'),
                ('y', 'у'), ('z', 'з'),
            };
            foreach (var (from, to) in translit) TranslitTable[from] = to;

            var lookalike = new (char From, char To)[]
            {
                ('a', 'а'), ('b', 'в'), ('c', 'с'), ('d', 'д'), ('e', 'е'), ('g', 'д'), ('h', 'н'), ('i', 'и'),
                ('k', 'к'), ('m', 'м'), ('n', 'п'), ('o', 'о'), ('p', 'р'), ('r', 'г'), ('t', 'т'), ('u', 'и'),
                ('v', 'в'), ('w', 'ш'), ('x', 'х'), ('y', 'у'), ('z', 'з'),
            };
            foreach (var (from, to) in lookalike) LookalikeTable[from] = to;
        }

        /// <summary>
        /// Decomposes compatibility characters, strips combining marks and formatting
        /// characters, lowercases, and folds letters into Cyrillic. Punctuation and
        /// whitespace are preserved so the result can still be tokenized.
        /// </summary>
        public static string Canonicalize(string input, NormalizationMode mode)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            string decomposed;
            try
            {
                decomposed = input.Normalize(NormalizationForm.FormKD);
            }
            catch (System.ArgumentException)
            {
                // Invalid surrogate pairs; fall back to the raw text.
                decomposed = input;
            }

            var table = mode == NormalizationMode.Lookalike ? LookalikeTable : TranslitTable;
            var sb = new StringBuilder(decomposed.Length);
            foreach (char c in decomposed)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category == UnicodeCategory.NonSpacingMark ||
                    category == UnicodeCategory.EnclosingMark ||
                    category == UnicodeCategory.SpacingCombiningMark ||
                    category == UnicodeCategory.Format)
                {
                    continue;
                }
                // A leading '@' is a mention marker, not a letter: "@anotherPlate" must not glue into one word.
                // Inside a word it still reads as 'а' ("пид@р").
                if (c == '@' && (sb.Length == 0 || !char.IsLetterOrDigit(sb[sb.Length - 1])))
                {
                    sb.Append(' ');
                    continue;
                }
                sb.Append(table[char.ToLowerInvariant(c)]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Keeps only letters and digits and collapses runs of the same character
        /// ("п-и-и-д-о-о-р" -> "пидор").
        /// </summary>
        public static string Squash(string canonical)
        {
            if (string.IsNullOrEmpty(canonical)) return string.Empty;

            var sb = new StringBuilder(canonical.Length);
            char last = '\0';
            foreach (char c in canonical)
            {
                if (!char.IsLetterOrDigit(c)) continue;
                if (c == last) continue;
                sb.Append(c);
                last = c;
            }
            return sb.ToString();
        }

        /// <summary>Full pipeline for a single dictionary word.</summary>
        public static string NormalizeWord(string word, NormalizationMode mode) => Squash(Canonicalize(word, mode));
    }
}
