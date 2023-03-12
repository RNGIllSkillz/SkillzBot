using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using static Mysqlx.Crud.Order.Types;

namespace SkillzBot.Utils
{
    internal sealed class StringUtil
    {
        public static string GetUserNameFromInput(string userInput)
        {
            if (string.IsNullOrEmpty(userInput)) return null;
            string gName = userInput[(userInput.LastIndexOf('@') + 1)..];
            string[] subs2 = gName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return subs2.First().ToLower();
        }
        public static string[] SplitAllWords(string input)
        {
            string[] words = input.Split(' ');
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
                        strOutput += RandomA();
                        continue;
                    case 'в':
                        strOutput += RandomB();
                        continue;
                    case 'о':
                        strOutput += RandomO();
                        continue;
                    case 'и':
                        strOutput += RandomI();
                        continue;
                    case 'р':
                        strOutput += RandomR();
                        continue;
                    case 'п':
                        strOutput += RandomP();
                        continue;
                    case 'н':
                        strOutput += RandomH();
                        continue;
                    case 'д':
                        strOutput += RandomG();
                        continue;
                    case 'м':
                        strOutput += RandomM();
                        continue;
                    case 'с':
                        strOutput += RandomС();
                        continue;
                    case 'у':
                        strOutput += RandomY();
                        continue;
                    case 'х':
                        strOutput += RandomX();
                        continue;
                    case 'к':
                        strOutput += RandomK();
                        continue;
                    case 'т':
                        strOutput += RandomT();
                        continue;
                    case 'е':
                        strOutput += RandomE();
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
            Dictionary<string, double> rankValues = new Dictionary<string, double>
            {
                { "challenger i", 27 }, { "grandmaster i", 26 }, { "master i", 25 }, { "diamond i", 24 },
                { "diamond ii", 23 }, { "diamond iii", 22 }, { "diamond iv", 21 }, { "platinum i", 20 },
                { "platinum ii", 19 }, { "platinum iii", 18 }, { "platinum iv", 17 }, { "gold i", 16 },
                { "gold ii", 15 }, { "gold iii", 14 }, { "gold iv", 13 }, { "silver i", 12 },
                { "silver ii", 11 }, { "silver iii", 10 }, { "silver iv", 9 }, { "bronze i", 8 },
                { "bronze ii", 7 }, { "bronze iii", 6 }, { "bronze iv", 5 }, { "iron i", 4 },
                { "iron ii", 3 }, { "iron iii", 2 }, { "iron iv", 1 }, { "unranked", 0 }
            };
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
        private static char RandomA()
        {
            char[] a = { 'a', 'а', '@', 'Ä', 'Â', 'Ⓐ', 'Å', 'ⓐ', '⒜', 'ḁ', 'ạ', 'ả', 'ầ', 'ấ', 'ẩ', 'ẫ', 'ặ', 'ẵ', 'ẳ', 'ằ', 'ắ', 'ậ', 'ẚ', 'ᾱ', 'ᾲ', 'ᾳ', 'ᾴ', 'ᾷ', 'ᾶ' };
            return a[IntUtil.Random(0, a.Length - 1)];
        }
        private static char RandomB()
        {
            char[] a = { 'в', 'B', 'v', '฿', 'ദ', '൫', 'ℬ', 'Ḃ', 'Ḅ', 'B', 'Ḇ' };
            return a[IntUtil.Random(0, a.Length - 1)];
        }
        private static char RandomO()
        {
            char[] a = {'о', 'o', '0', 'Ө', 'Ö', 'Ø', 'Ꝋ', 'ⓞ', '⒪', 'ọ', 'ṓ', 'ṑ', 'ṏ', 'ṍ', 'ố', 'ỏ', 'ồ', 'ổ', 'ỗ', 'ớ', 'ὁ', 'ὀ', 'ợ', 'ờ', 'ở', 'ὂ', 'ὃ', 'ὄ', 'ὅ', 'ộ', 'o', 'Ỏ', 'Ọ', 'Ṓ', 'Ṑ', 'Ṏ', 'Ṍ'};
            return a[IntUtil.Random(0, a.Length - 1)];
        }
        private static char RandomI()
        {
            char[] a = {'и', 'i', 'u', 'ⓤ', '⒰', 'υ', 'ṳ', 'ṵ', 'ṷ', 'ὓ', 'ὔ', 'ὕ', 'ὖ', 'ὗ'};
            return a[IntUtil.Random(0, a.Length - 1)];
        }
        private static char RandomR()
        {
            char[] a = {'р', 'p', 'r', 'ⓟ', '⒫', 'ṗ', 'ṕ', 'ῥ', 'ῤ', 'ℙ', 'Ṗ', 'Ῥ', 'Ṕ'};
            return a[IntUtil.Random(0, a.Length - 1)];
        }
        private static char RandomP()
        {
            char[] a = {'п', 'π', 'n', 'ń', 'ǹ', 'ṅ', 'ň', 'ñ', 'ņ', 'ƞ', 'ṇ', 'ṋ'};
            return a[IntUtil.Random(0, a.Length - 1)];
        }
        private static char RandomH()
        {
            char[] a = {'н', 'H', 'ℋ', 'ℍ', 'Ḥ', 'Ḧ', 'Ḩ', 'Ἢ', 'Ἡ', 'Ἦ', 'Ἠ', 'Ḫ', 'Ἤ', 'Ἥ', 'Ἧ', 'ᾘ', 'ᾙ', 'ᾟ', 'ᾞ', 'ᾝ', 'H', 'ᾜ', 'ᾛ', 'ᾚ'};
            return a[IntUtil.Random(0, a.Length - 1)];
        }
        private static char RandomG()
        {
            char[] a = {'д', 'g'};
            return a[IntUtil.Random(0, a.Length - 1)];
        }
        private static char RandomM()
        {
            char[] a = {'м', 'm', 'ⓜ', '⒨', 'ṃ', 'ḿ', 'ṁ', 'm', '♏', 'Ḿ', 'Ṁ', 'Ṃ', 'ന'};
            return a[IntUtil.Random(0, a.Length - 1)];
        }
        private static char RandomС()
        {
            char[] a = {'с', 'c' , 'ⓒ', '⒞', 'ḉ', 'c', 'ℂ', '℃', '₡', '∁', 'C'};
            return a[IntUtil.Random(0, a.Length - 1)];
        }
        private static char RandomY()
        {
            char[] a = {'у', 'y', 'ⓨ', 'ẙ', 'ỳ', 'ỵ', 'ỷ', 'ỹ', 'ẏ', 'y'};
            return a[IntUtil.Random(0, a.Length - 1)];
        }
        private static char RandomX()
        {
            char[] a = { 'х', 'x', 'ⓧ', '⒳', '✖', '✗', '✘', 'ẋ', 'ẍ', 'x', 'Ẍ', 'Ẋ', 'X' };
            return a[IntUtil.Random(0, a.Length - 1)];
        }
        private static char RandomK()
        {
            char[] a = {'k','к', 'ⓚ', '⒦', 'к', 'ḱ', 'ḳ', 'ḵ', 'k', '₭', 'Ḱ', 'Ḳ', 'Ḵ', 'K'};
            return a[IntUtil.Random(0, a.Length - 1)];
        }
        private static char RandomT()
        {
            char[] a = { 'т','T', '₮', 'Ṫ', 'T', 'Ṱ', 'Ṯ', 'Ṭ'};
            return a[IntUtil.Random(0, a.Length - 1)];
        }
        private static char RandomE()
        {
            char[] a = { 'е', 'e', 'ⓔ', '⒠', 'ℯ', '∊', 'ḕ', '€', 'ḗ', 'ḙ', 'ḛ', 'ḝ', 'ẹ', 'ẻ', 'ẽ', 'ế', 'ề', 'ể', 'ễ', 'ệ', 'e', 'Ẹ', 'Ḝ', 'Ḛ', 'Ḙ', 'Ḗ', 'Ḕ', 'Ẽ', 'Ế', 'Ề', 'Ể', 'Ễ', 'Ἑ', 'Ἒ', 'Ἐ', 'Έ','Ὲ' };
            return a[IntUtil.Random(0, a.Length - 1)];
        }
    }
}
