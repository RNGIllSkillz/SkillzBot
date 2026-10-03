using System;
using System.Collections.Generic;

namespace SkillzBot.Utils
{
    /// <summary>
    /// Dictionary-based slur detector that is robust to case, script mixing,
    /// homoglyphs, repeated letters and spaced-out spelling, while ignoring
    /// innocent words from a whitelist and innocent word boundaries
    /// ("не грусти" must not read as "негр").
    /// </summary>
    public sealed class ProfanityDetector
    {
        private sealed class ModeIndex
        {
            public NormalizationMode Mode;
            public readonly WordTrie Banned = new WordTrie();
            public readonly WordTrie Whitelist = new WordTrie();
        }

        private readonly struct Token
        {
            public readonly int Start;
            public readonly int Length;
            public int End => Start + Length;
            public Token(int start, int length) { Start = start; Length = length; }
        }

        private readonly ModeIndex[] _modes;

        public int BannedWordCount => _modes[0].Banned.Count;
        public int WhitelistCount => _modes[0].Whitelist.Count;

        private ProfanityDetector(ModeIndex[] modes)
        {
            _modes = modes;
        }

        public static ProfanityDetector Build(IEnumerable<string> bannedWords, IEnumerable<string> whitelistWords)
        {
            var modes = new[]
            {
                new ModeIndex { Mode = NormalizationMode.Transliteration },
                new ModeIndex { Mode = NormalizationMode.Lookalike },
            };

            foreach (var raw in bannedWords ?? Array.Empty<string>())
            {
                var word = raw?.Trim();
                if (string.IsNullOrEmpty(word)) continue;
                foreach (var mode in modes)
                {
                    var normalized = TextNormalizer.NormalizeWord(word, mode.Mode);
                    if (normalized.Length > 1) mode.Banned.Insert(normalized, word);
                }
            }

            var detector = new ProfanityDetector(modes);
            foreach (var raw in whitelistWords ?? Array.Empty<string>())
                detector.AddWhitelist(raw);
            return detector;
        }

        public void AddWhitelist(string rawWord)
        {
            var word = rawWord?.Trim();
            if (string.IsNullOrEmpty(word)) return;
            foreach (var mode in _modes)
            {
                var normalized = TextNormalizer.NormalizeWord(word, mode.Mode);
                if (normalized.Length > 1) mode.Whitelist.Insert(normalized, word);
            }
        }

        /// <summary>
        /// Returns the original spelling of the first banned word found, or null.
        /// </summary>
        public string Find(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return null;

            string previousCanonical = null;
            foreach (var mode in _modes)
            {
                if (mode.Banned.Count == 0) continue;

                var canonical = TextNormalizer.Canonicalize(message, mode.Mode);
                // Pure Cyrillic text canonicalizes identically in every mode; skip the duplicate pass.
                if (previousCanonical != null && string.Equals(previousCanonical, canonical, StringComparison.Ordinal))
                    continue;
                previousCanonical = canonical;

                var hit = FindInMode(mode, canonical);
                if (hit != null) return hit;
            }
            return null;
        }

        private static string FindInMode(ModeIndex mode, string canonical)
        {
            var tokens = new List<Token>();
            var squashed = Tokenize(canonical, tokens);
            if (squashed.Length == 0) return null;

            int from = 0;
            while (from < squashed.Length)
            {
                if (!mode.Banned.FindFirst(squashed, from, out int index, out int length, out string word))
                    break;

                if (IsAcceptedMatch(mode.Whitelist, squashed, index, length, tokens))
                    return word;

                from = index + 1;
            }
            return null;
        }

        /// <summary>
        /// Splits canonical text into letter/digit runs, squashes each run, and concatenates
        /// them into one string while recording each token's span inside it.
        /// </summary>
        private static string Tokenize(string canonical, List<Token> tokens)
        {
            var sb = new System.Text.StringBuilder(canonical.Length);
            int i = 0;
            while (i < canonical.Length)
            {
                if (!char.IsLetterOrDigit(canonical[i])) { i++; continue; }

                int tokenStart = sb.Length;
                char last = '\0';
                while (i < canonical.Length && char.IsLetterOrDigit(canonical[i]))
                {
                    char c = canonical[i++];
                    if (c != last) { sb.Append(c); last = c; }
                }
                tokens.Add(new Token(tokenStart, sb.Length - tokenStart));
            }
            return sb.ToString();
        }

        /// <summary>
        /// A match inside one token is accepted unless a whitelisted word in that token fully
        /// contains it ("книга" contains "нига"; "spider" contains "пидер"). A match that spans
        /// several tokens is accepted only when every touched token is mostly consumed by it,
        /// which catches "п и д о р" and "пи дор" but not "не грусти" or "выспи дорого".
        /// </summary>
        private static bool IsAcceptedMatch(WordTrie whitelist, string text, int index, int length, List<Token> tokens)
        {
            int matchEnd = index + length;
            int touched = 0;
            bool allConsumed = true;
            Token single = default;

            foreach (var token in tokens)
            {
                if (token.End <= index) continue;
                if (token.Start >= matchEnd) break;

                touched++;
                single = token;
                int overlapStart = Math.Max(index, token.Start);
                int overlapEnd = Math.Min(matchEnd, token.End);
                int consumed = overlapEnd - overlapStart;

                bool fullyConsumed = consumed == token.Length;
                bool mostlyConsumed = consumed * 2 >= token.Length && consumed >= 2;
                if (!fullyConsumed && !mostlyConsumed) allConsumed = false;
            }

            if (touched == 1)
                return !IsCoveredByWhitelist(whitelist, text, index, matchEnd, single);

            return touched > 1 && allConsumed;
        }

        /// <summary>
        /// True when some whitelisted word occurring inside <paramref name="token"/> starts at or
        /// before the match and ends at or after it.
        /// </summary>
        private static bool IsCoveredByWhitelist(WordTrie whitelist, string text, int matchStart, int matchEnd, Token token)
        {
            if (whitelist.Count == 0) return false;
            for (int start = token.Start; start <= matchStart; start++)
            {
                int len = whitelist.LongestMatchAt(text, start, token.End);
                if (len > 0 && start + len >= matchEnd) return true;
            }
            return false;
        }
    }
}
