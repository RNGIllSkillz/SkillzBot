using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SkillzBot.Utils
{
    internal sealed class StringUtil
    {
        // ==========================================
        // DATA SOURCES
        // ==========================================
        private static readonly Dictionary<string, double> rankValues;

        // Compiled Regex is much faster for repeated use
        private static readonly Regex _youTubeRegex = new Regex(@"(?<=v=|\/)([a-zA-Z0-9_-]{11})(?=\&|\?|$)", RegexOptions.Compiled);
        private static readonly Regex _twitchClipRegex = new Regex(@"https?:\/\/clips\.twitch\.tv\/[A-Za-z0-9_-]+", RegexOptions.Compiled);
        private static readonly Regex _apiTokenRegex = new Regex(@"^(oauth:|RGAPI-|)[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

        private static readonly HashSet<char> CharsToRemove = new HashSet<char> { ' ', '.', ',', '!', '_', '-' };

        // Lookup Tables
        private static readonly char[] _normalizationMap = new char[char.MaxValue + 1];
        private static readonly char[][] _obfuscationMap = new char[char.MaxValue + 1][];

        private static readonly Dictionary<char, char[]> Mappings = new Dictionary<char, char[]>
        {
            { 'a', new[] { 'a','а', 'а', '@', 'Ä', 'Â', 'Ⓐ', 'Å', 'ⓐ', '⒜', 'ḁ', 'ạ', 'ả', 'ầ', 'ấ', 'ẩ', 'ẫ', 'ặ', 'ẵ', 'ẳ', 'ằ', 'ắ', 'ậ', 'ẚ', 'ᾱ', 'ᾲ', 'ᾳ', 'ᾴ', 'ᾷ', 'ᾶ', 'ã', 'æ', 'å', 'ā', 'ă', 'ǎ', 'ą', 'ȁ', 'ȃ', 'ǡ', 'ǟ', 'ǻ', 'ȧ', 'ȁ' } },
            { 'b', new[] { 'в', 'B', '฿', 'ᛒ', 'Ɓ', 'Ḅ', 'Ƃ', 'Ḇ', 'Ḃ', 'Ꞗ', 'Ƀ', 'ᛔ', 'v', 'ദ', '൫', 'ℬ', 'Ḇ' } },
            { 'o', new[] { 'о', 'o', '0', 'ó', 'ô', 'õ', 'ò', 'ó', 'ø', 'ö', 'ō', 'ŏ', 'ő', 'ȯ', 'ȫ', 'ȭ', 'ơ', 'ờ', 'ớ', 'ở', 'ỡ', 'ợ', 'ọ', 'ø', 'ǫ', 'ǭ', 'ǿ', 'ȍ', 'ȏ', 'ⓞ', '⒪', '○', '◯', '◎', '◌', '◍', '◐', '◑', '⚪', 'ꝋ', 'Ꝍ' } },
            { 'i', new[] { 'и', 'i', 'u', 'ⓤ', '⒰', 'υ', 'ṳ', 'ṵ', 'ṷ', 'ὓ', 'ὔ', 'ὕ', 'ὖ', 'ὗ', '1', '!' } },
            { 'r', new[] { 'р', 'p', 'r', 'ρ', 'ℛ', 'ℙ', 'ℜ', 'ℝ', 'Ⓟ', 'Ⓡ', 'Ɽ', 'ᖇ', '℞', '℟', 'Ṙ', 'Ṗ', 'Ṕ', 'Ṛ', 'Ṝ', 'Ṟ', 'ᴘ', '☈' } },
            { 'p', new[] { 'п', 'π', 'n', 'ń', 'ǹ', 'ṅ', 'ň', 'ñ', 'ņ', 'ƞ', 'ṇ', 'ṋ', 'p', 'ρ', 'ℙ', 'Ⓟ', 'Ṗ', 'Ṕ', 'ᴘ', '♫' } },
            { 'h', new[] { 'н', 'H', 'ℋ', 'ℍ', 'Ḥ', 'Ḧ', 'Ḩ', 'Ἢ', 'Ἡ', 'Ἦ', 'Ἠ', 'Ḫ', 'Ἤ', 'Ἥ', 'Ἧ', 'ᾘ', 'ᾙ', 'ᾟ', 'ᾞ', 'ᾝ', 'H', 'ᾜ', 'ᾛ', 'ᾚ' } },
            { 'd', new[] { 'д', 'g', 'D' } },
            { 'm', new[] { 'м', 'm', 'ⓜ', '⒨', 'ṃ', 'ḿ', 'ṁ', 'm', '♏', 'Ḿ', 'Ṁ', 'Ṃ', 'ന' } },
            { 'c', new[] { 'с', 'c', 'ⓒ', '⒞', 'ḉ', 'c', 'ℂ', '℃', '₡', '∁', 'C' } },
            { 'y', new[] { 'у', 'y', 'ⓨ', 'ẙ', 'ỳ', 'ỵ', 'ỷ', 'ỹ', 'ẏ', 'y' } },
            { 'x', new[] { 'х', 'x', 'ⓧ', '⒳', '✖', '✗', '✘', 'ẋ', 'ẍ', 'x', 'Ẍ', 'Ẋ', 'X', 'ⅹ', '乂', '×', '✕', '⨯', '⤫', '⤬' } },
            { 'k', new[] { 'k', 'к', 'ⓚ', '⒦', 'к', 'ḱ', 'ḳ', 'ḵ', 'k', '₭', 'Ḱ', 'Ḳ', 'Ḵ', 'K' } },
            { 't', new[] { 'T', 'Ṫ', 'Ṭ', 'Ť', 'Ţ', 'Ț', 'Ⱦ', 'Ƭ', 'ᴛ', 'Ｔ', 'т', '₮', 'Ṱ', 'Ṯ' } },
            { 'e', new[] { 'е', 'e', 'ⓔ', '⒠', 'ℯ', '∊', 'ḕ', '€', 'ḗ', 'ḙ', 'ḛ', 'ḝ', 'ẹ', 'ẻ', 'ẽ', 'ế', 'ề', 'ể', 'ễ', 'ệ', 'e', 'Ẹ', 'Ḝ', 'Ḛ', 'Ḙ', 'Ḗ', 'Ḕ', 'Ẽ', 'Ế', 'Ề', 'Ể', 'Ễ', 'Ἑ', 'Ἒ', 'Ἐ', 'Έ', 'Ὲ' } }
        };

        static StringUtil()
        {
            // Initialize Ranks
            rankValues = new Dictionary<string, double>
            {
                { "challenger i", 31 }, { "grandmaster i", 30 }, { "master i", 29 }, { "diamond i", 28 },
                { "diamond ii", 27 }, { "diamond iii", 26 }, { "diamond iv", 25 },
                { "emerald i", 24 }, { "emerald ii", 23 }, { "emerald iii", 22 }, { "emerald iv", 21 },
                { "platinum i", 20 }, { "platinum ii", 19 }, { "platinum iii", 18 }, { "platinum iv", 17 },
                { "gold i", 16 }, { "gold ii", 15 }, { "gold iii", 14 }, { "gold iv", 13 },
                { "silver i", 12 }, { "silver ii", 11 }, { "silver iii", 10 }, { "silver iv", 9 },
                { "bronze i", 8 }, { "bronze ii", 7 }, { "bronze iii", 6 }, { "bronze iv", 5 },
                { "iron i", 4 }, { "iron ii", 3 }, { "iron iii", 2 }, { "iron iv", 1 },
                { "unranked", 0 }
            };

            // Initialize Normalization Map (Default 1:1)
            for (int i = 0; i < _normalizationMap.Length; i++)
                _normalizationMap[i] = (char)i;

            // Fill Maps from Source of Truth
            foreach (var entry in Mappings)
            {
                char targetBase = entry.Key;

                // 1. Populate Obfuscation (Shuffle)
                _obfuscationMap[targetBase] = entry.Value;

                // 2. Populate Normalization (Filter)
                foreach (char variant in entry.Value)
                {
                    _normalizationMap[variant] = targetBase;
                }
            }
        }

        // ==========================================
        // NORMALIZATION & OBFUSCATION
        // ==========================================

        /// <summary>
        /// Converts obfuscated chars to base chars, but preserves structure.
        /// "H0x0l is bad" -> "hohol is bad"
        /// </summary>
        public static string Normalize(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            char[] result = new char[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                result[i] = char.ToLowerInvariant(_normalizationMap[input[i]]);
            }
            return new string(result);
        }

        public static string GetAggressiveString(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            // Stack allocation for speed
            Span<char> result = input.Length <= 1024
                ? stackalloc char[input.Length]
                : new char[input.Length];

            int pos = 0;
            char lastChar = '\0'; // Tracker for deduplication

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                // 1. Normalize the char (get base version, e.g. '0' -> 'o')
                char normalizedChar = _normalizationMap[c];

                // 2. Filter: Only keep Letters and Digits.
                if (char.IsLetterOrDigit(normalizedChar))
                {
                    char lower = char.ToLowerInvariant(normalizedChar);

                    // 3. Deduplicate: Only add if different from the previous char
                    if (lower != lastChar)
                    {
                        result[pos++] = lower;
                        lastChar = lower;
                    }
                }
            }
            return result.Slice(0, pos).ToString();
        }
        public static bool IsZalgo(string input)
        {
            if (string.IsNullOrEmpty(input)) return false;

            int markCount = 0;
            int nastySymbolCount = 0;

            foreach (char c in input)
            {
                if (c >= '\u20D0' && c <= '\u20FF')
                {
                    nastySymbolCount++;
                }

                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category == UnicodeCategory.NonSpacingMark ||
                    category == UnicodeCategory.EnclosingMark ||
                    category == UnicodeCategory.SpacingCombiningMark)
                {
                    markCount++;
                }
            }
            if (nastySymbolCount > 3) return true;
            if (markCount > 10) return true;
            if (input.Length > 5 && (double)markCount / input.Length > 0.35) return true;

            return false;
        }
        public static string Shuffle(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            char[] result = new char[input.Length];
            Random rng = Random.Shared;

            for (int i = 0; i < input.Length; i++)
            {
                char current = char.ToLowerInvariant(input[i]);
                char[] variants = _obfuscationMap[current];

                if (variants != null && variants.Length > 0)
                {
                    result[i] = variants[rng.Next(variants.Length)];
                }
                else
                {
                    result[i] = current;
                }
            }
            return new string(result);
        }

        // ==========================================
        // PARSING & EXTRACTION
        // ==========================================

        public static string GetUserNameFromInput(string userInput)
        {
            if (string.IsNullOrEmpty(userInput)) return null;

            // Safer parsing
            int atIndex = userInput.LastIndexOf('@');
            if (atIndex == -1 && userInput.Length > 0)
            {
                // Fallback if no @ symbol, assume first word is name
                string[] parts = userInput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length > 0 ? parts[0].ToLower() : null;
            }

            string gName = userInput.Substring(atIndex + 1);
            string[] subs2 = gName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return subs2.Length > 0 ? subs2[0].ToLower() : null;
        }

        public static string[] SplitAllWords(string input)
        {
            if (string.IsNullOrEmpty(input)) return Array.Empty<string>();
            return input.Split(new[] { ' ', '|' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static string[] SplitAllWordsDiffSep(string input)
        {
            if (string.IsNullOrEmpty(input)) return Array.Empty<string>();
            return input.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static string[] SplitTwoWords(string inputString)
        {
            if (string.IsNullOrEmpty(inputString)) return Array.Empty<string>();

            // Optimized to not scan the whole string
            int firstSpace = inputString.IndexOf(' ');
            if (firstSpace == -1) return new string[] { inputString, "" };

            return new string[]
            {
                inputString.Substring(0, firstSpace),
                inputString.Substring(firstSpace + 1)
            };
        }

        public static string GetCommandFromUserInput(string[] wordsArray)
        {
            if (wordsArray == null || wordsArray.Length <= 1) return string.Empty;
            // Re-joining an array is slow
            return string.Join(" ", wordsArray.Skip(1));
        }

        public static string ExtractYouTubeVideoId(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;
            Match match = _youTubeRegex.Match(input);
            return match.Success ? match.Groups[1].Value : null;
        }

        public static string ExtractClipId(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;
            Match match = _twitchClipRegex.Match(input);

            if (match.Success && match.Value == input)
            {
                if (Uri.TryCreate(match.Value, UriKind.Absolute, out Uri uri) && uri.Host == "clips.twitch.tv")
                {
                    string[] segments = uri.Segments;
                    if (segments.Length > 1)
                    {
                        return segments[1].Trim('/');
                    }
                }
            }
            return null;
        }

        public static bool IsValidApiToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            return _apiTokenRegex.IsMatch(token);
        }

        // ==========================================
        // TEXT CLEANING & ANALYSIS
        // ==========================================

        public static int CheckASCII(string message)
        {
            int i = 0;
            foreach (char ch in message)
            {
                if (!((int)ch >= 32 && (int)ch <= 173) && !((int)ch >= 1024 && (int)ch <= 1279))
                {
                    i++;
                }
            }
            return i;
        }

        public static string Clean(string str)
        {
            if (string.IsNullOrEmpty(str)) return string.Empty;

            var sb = new StringBuilder(str.Length);
            string lowerStr = str.ToLower();
            if (lowerStr.Length > 0 && !CharsToRemove.Contains(lowerStr[0]))
            {
                sb.Append(lowerStr[0]);
            }

            for (int i = 1; i < lowerStr.Length; i++)
            {
                char currentChar = lowerStr[i];

                if (!CharsToRemove.Contains(currentChar))
                {
                    if (sb.Length == 0 || sb[sb.Length - 1] != currentChar)
                    {
                        sb.Append(currentChar);
                    }
                }
            }
            return sb.ToString();
        }
        
        public static string RemoveWhitespace(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            int len = input.Length;
            char[] src = input.ToCharArray(); 
            int dstIdx = 0;

            for (int i = 0; i < len; i++)
            {
                char c = src[i];
                if (!char.IsWhiteSpace(c))
                {
                    src[dstIdx++] = c;
                }
            }
            return new string(src, 0, dstIdx);
        }

        public static int CountUpperCaseLetters(string input)
        {
            if (string.IsNullOrEmpty(input)) return 0;
            int count = 0;
            foreach (char c in input)
            {
                if (char.IsUpper(c)) count++;
            }
            return count;
        }

        public static string ConvertRank(string rank, bool direction)
        {
            rank = rank.ToLower();
            if (direction) // Get Points from Name
            {
                return rankValues.TryGetValue(rank, out double val) ? val.ToString() : "0";
            }
            else // Get Name from Points
            {
                if (double.TryParse(rank, out double rankValue))
                {
                    string bestMatch = "Unknown Rank";
                    double minDiff = double.MaxValue;

                    foreach (var kvp in rankValues)
                    {
                        double diff = Math.Abs(kvp.Value - rankValue);
                        if (diff < minDiff)
                        {
                            minDiff = diff;
                            bestMatch = kvp.Key;
                        }
                    }
                    return bestMatch;
                }
                return "Unknown Rank";
            }
        }
    }
}