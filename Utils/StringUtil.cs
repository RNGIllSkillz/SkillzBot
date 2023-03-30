using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SkillzBot.Utils
{
    internal sealed class StringUtil
    {
        private readonly static Dictionary<string, double> rankValues;
        private readonly static char[] a;
        private readonly static char[] b;
        private readonly static char[] o;
        private readonly static char[] i;
        private readonly static char[] r;
        private readonly static char[] p;
        private readonly static char[] h;
        private readonly static char[] g;
        private readonly static char[] m;
        private readonly static char[] c;
        private readonly static char[] y;
        private readonly static char[] x;
        private readonly static char[] k;
        private readonly static char[] t;
        private readonly static char[] e;

        static StringUtil()
        {
            rankValues = new Dictionary<string, double>
            {
                { "challenger i", 27 }, { "grandmaster i", 26 }, { "master i", 25 }, { "diamond i", 24 },
                { "diamond ii", 23 }, { "diamond iii", 22 }, { "diamond iv", 21 }, { "platinum i", 20 },
                { "platinum ii", 19 }, { "platinum iii", 18 }, { "platinum iv", 17 }, { "gold i", 16 },
                { "gold ii", 15 }, { "gold iii", 14 }, { "gold iv", 13 }, { "silver i", 12 },
                { "silver ii", 11 }, { "silver iii", 10 }, { "silver iv", 9 }, { "bronze i", 8 },
                { "bronze ii", 7 }, { "bronze iii", 6 }, { "bronze iv", 5 }, { "iron i", 4 },
                { "iron ii", 3 }, { "iron iii", 2 }, { "iron iv", 1 }, { "unranked", 0 }
            };
            a = new char[] { 'a', 'а', '@', 'Ä', 'Â', 'Ⓐ', 'Å', 'ⓐ', '⒜', 'ḁ', 'ạ', 'ả', 'ầ', 'ấ', 'ẩ', 'ẫ', 'ặ', 'ẵ', 'ẳ', 'ằ', 'ắ', 'ậ', 'ẚ', 'ᾱ', 'ᾲ', 'ᾳ', 'ᾴ', 'ᾷ', 'ᾶ', 'ã', 'æ', 'å', 'ā', 'ă', 'ǎ', 'ą', 'ȁ', 'ȃ', 'ǡ', 'ǟ', 'ǻ', 'ȧ', 'ȁ' };
            b = new char[] { 'в', 'B', '฿', 'ᛒ', 'Ɓ', 'Ḅ', 'Ƃ', 'Ḇ', 'Ḃ', 'Ꞗ', 'Ƀ', 'ᛔ', 'v', 'ദ', '൫', 'ℬ', 'Ḇ' };
            o = new char[] { 'o', '0', 'ó', 'ô', 'õ', 'ò', 'ó', 'ø', 'ö', 'ō', 'ŏ', 'ő', 'ȯ', 'ȫ', 'ȭ', 'ơ', 'ờ', 'ớ', 'ở', 'ỡ', 'ợ', 'ọ', 'ø', 'ǫ', 'ǭ', 'ǿ', 'ȍ', 'ȏ', 'ⓞ', '⒪', '○', '◯', '◎', '◌', '◍', '◐', '◑', '⚪', 'ꝋ', 'Ꝍ' };
            i = new char[] { 'и', 'i', 'u', 'ⓤ', '⒰', 'υ', 'ṳ', 'ṵ', 'ṷ', 'ὓ', 'ὔ', 'ὕ', 'ὖ', 'ὗ' };
            r = new char[] { 'p', 'r', 'ρ', 'ℛ', 'ℙ', 'ℜ', 'ℝ', 'Ⓟ', 'Ⓡ', 'Ɽ', 'ᖇ', '℞', '℟', 'Ṙ', 'Ṗ', 'Ṕ', 'Ṛ', 'Ṝ', 'Ṟ', 'ᴘ' };
            p = new char[] { 'п', 'π', 'n', 'ń', 'ǹ', 'ṅ', 'ň', 'ñ', 'ņ', 'ƞ', 'ṇ', 'ṋ' };
            h = new char[] { 'н', 'H', 'ℋ', 'ℍ', 'Ḥ', 'Ḧ', 'Ḩ', 'Ἢ', 'Ἡ', 'Ἦ', 'Ἠ', 'Ḫ', 'Ἤ', 'Ἥ', 'Ἧ', 'ᾘ', 'ᾙ', 'ᾟ', 'ᾞ', 'ᾝ', 'H', 'ᾜ', 'ᾛ', 'ᾚ' };
            g = new char[] { 'д', 'g', 'D'};
            m = new char[] { 'м', 'm', 'ⓜ', '⒨', 'ṃ', 'ḿ', 'ṁ', 'm', '♏', 'Ḿ', 'Ṁ', 'Ṃ', 'ന' };
            c = new char[] { 'с', 'c', 'ⓒ', '⒞', 'ḉ', 'c', 'ℂ', '℃', '₡', '∁', 'C' };
            y = new char[] { 'у', 'y', 'ⓨ', 'ẙ', 'ỳ', 'ỵ', 'ỷ', 'ỹ', 'ẏ', 'y' };
            x = new char[] { 'х', 'x', 'ⓧ', '⒳', '✖', '✗', '✘', 'ẋ', 'ẍ', 'x', 'Ẍ', 'Ẋ', 'X', 'ⅹ', '乂','×','✕', '⨯', '⤫', '⤬'};            
            k = new char[] { 'k', 'к', 'ⓚ', '⒦', 'к', 'ḱ', 'ḳ', 'ḵ', 'k', '₭', 'Ḱ', 'Ḳ', 'Ḵ', 'K' };
            t = new char[] { 'T', 'Ṫ', 'Ṭ', 'Ť', 'Ţ', 'Ț', 'Ⱦ', 'Ƭ', 'ᴛ', 'Ｔ', 'т', '₮', 'Ṱ', 'Ṯ'};
            e = new char[] { 'е', 'e', 'ⓔ', '⒠', 'ℯ', '∊', 'ḕ', '€', 'ḗ', 'ḙ', 'ḛ', 'ḝ', 'ẹ', 'ẻ', 'ẽ', 'ế', 'ề', 'ể', 'ễ', 'ệ', 'e', 'Ẹ', 'Ḝ', 'Ḛ', 'Ḙ', 'Ḗ', 'Ḕ', 'Ẽ', 'Ế', 'Ề', 'Ể', 'Ễ', 'Ἑ', 'Ἒ', 'Ἐ', 'Έ', 'Ὲ' };
        }
        public static string GetUserNameFromInput(string userInput)
        {
            if (string.IsNullOrEmpty(userInput)) return null;
            string gName = userInput[(userInput.LastIndexOf('@') + 1)..];
            string[] subs2 = gName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return subs2.First().ToLower();
        }
        public static string[] SplitAllWords(string input)
        {
            string[] words = input.Split(' ', '|');
            return words;
        }
        public static string[] SplitAllWordsDiffSep(string input)
        {
            string[] words = input.Split('|');
            return words;
        }
        public static string[] SplitTwoWords(string inputString)
        {
            string[] words = inputString.Split(' ');
            string[] result = new string[2];
            result[0] = words[0];
            result[1] = string.Join(" ", words.Skip(1));
            return result;
        }
        public static string GetCommandFromUserInput(string[] wordsArray)
        {
            string[] wordsList = wordsArray.Skip(1).ToArray();
            string Command = string.Join(" ", wordsList);
            return Command;
        }
        public static string ExtractYouTubeVideoId(string input)
        {
            string videoId = null;
            string pattern = @"(?<=v=|\/)([a-zA-Z0-9_-]{11})(?=\&|\?|$)";
            Match match = Regex.Match(input, pattern);
            if (match.Success)
            {
                videoId = match.Groups[1].Value;
            }
            return videoId;
        }
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
            if (str.Length > 0)
            {
                str = str.ToLower();
                var chars = str.ToCharArray();
                var res = new char[chars.Length];
                var index = 0;
                res[0] = str[0];
                for (var i = 1; i < chars.Length; i++)
                {
                    if ((chars[i] != ' ' && chars[i] != '.' && chars[i] != ',' && chars[i] != '!' && chars[i] != '_' && chars[i] != '-') && res[index] != chars[i])
                    {
                        res[++index] = chars[i];
                    }
                }
                return new string(res, 0, index + 1);
            }
            else
                return string.Empty;
        }
        public static string RemoveWhitespace(string input)
        {
            return new string(input.ToCharArray()
                .Where(c => !Char.IsWhiteSpace(c))
                .ToArray());
        }
        public static string Shuffle(string input)
        {
            input = input.ToLower();
            string strOutput = "";
            foreach (var letter in input)
            {
                switch (letter)
                {
                    case 'а':
                        strOutput += a[IntUtil.Random(0, a.Length - 1)];
                        continue;
                    case 'в':
                        strOutput += b[IntUtil.Random(0, b.Length - 1)];
                        continue;
                    case 'о':
                        strOutput += o[IntUtil.Random(0, o.Length - 1)];
                        continue;
                    case 'и':
                        strOutput += i[IntUtil.Random(0, i.Length - 1)];
                        continue;
                    case 'р':
                        strOutput += r[IntUtil.Random(0, r.Length - 1)];
                        continue;
                    case 'п':
                        strOutput += p[IntUtil.Random(0, p.Length - 1)];
                        continue;
                    case 'н':
                        strOutput += h[IntUtil.Random(0, h.Length - 1)];
                        continue;
                    case 'д':
                        strOutput += g[IntUtil.Random(0, g.Length - 1)];
                        continue;
                    case 'м':
                        strOutput += m[IntUtil.Random(0, m.Length - 1)];
                        continue;
                    case 'с':
                        strOutput += c[IntUtil.Random(0, c.Length - 1)];
                        continue;
                    case 'у':
                        strOutput += y[IntUtil.Random(0, y.Length - 1)];
                        continue;
                    case 'х':
                        strOutput += x[IntUtil.Random(0, x.Length - 1)];
                        continue;
                    case 'к':
                        strOutput += k[IntUtil.Random(0, k.Length - 1)];
                        continue;
                    case 'т':
                        strOutput += t[IntUtil.Random(0, t.Length - 1)];
                        continue;
                    case 'е':
                        strOutput += e[IntUtil.Random(0, e.Length - 1)];
                        continue;
                    default:
                        strOutput += letter;
                        continue;
                }
            }
            return strOutput;
        }        
        public static string ConvertRank(string rank, bool direction)
        {
            rank = rank.ToLower();            
            if (direction)
            {
                if (rankValues.ContainsKey(rank))
                {
                    return rankValues[rank].ToString();
                }
                else
                {
                    return "0";
                }
            }
            else
            {
                if (double.TryParse(rank, out double rankValue))
                {
                    string[] rankNames = rankValues.Keys.ToArray();
                    double[] rankPoints = rankValues.Values.ToArray();
                    int index = 0;
                    double minDiff = Math.Abs(rankPoints[0] - rankValue);
                    for (int i = 1; i < rankPoints.Length; i++)
                    {
                        double diff = Math.Abs(rankPoints[i] - rankValue);
                        if (diff < minDiff)
                        {
                            minDiff = diff;
                            index = i;
                        }
                    }
                    return rankNames[index];
                }
                else
                {
                    return "Unknown Rank";
                }
            }
        }       
    }
}