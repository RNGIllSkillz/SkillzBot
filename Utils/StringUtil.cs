using System;
using System.Collections.Generic;
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