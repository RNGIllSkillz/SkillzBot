using SkillzBot.Utils;
using SkillzBot.WRITERS;
using System;
using System.Collections.Generic;
using System.IO;
using IllSkillzBot;
using SkillzBot.API.YouTube;
using System.Threading.Tasks;
using SkillzBot.MODELS;
using TwitchLib.Client.Events;
using SkillzBot.API.Twitch;
using System.Linq;
using urldetector.detection;
using SkillzBot.Singleton;
using SkillzBot.IRC;

namespace SkillzBot.IllSkillzBot
{
    sealed class IllChatFilters
    {
        private static readonly ConfPathes dataPath = IllSkillzBotMain.GetDataPath();
        private static readonly AhoCorasick _pichkaMatcher;
        private static readonly HashSet<string> mediaBlack;
        private static readonly HashSet<string> channelBlack;
        private static readonly HashSet<string> dictionary;
        private static readonly BannedWordsTrie bannedWordsTrie = new BannedWordsTrie();
        private static HashSet<string> whiteList;
        private static HashSet<string> userBlackList;
        private static readonly int[] Arabic2;
        private static readonly int CharsInRow = 29;
        private static readonly int ArabCharsInRow = 4;
        private static readonly int RowsNum = 3;
        static IllChatFilters()
        {            
            mediaBlack = new HashSet<string>(File.ReadLines(Path.Combine(dataPath.sharedPath, IllSingleton.Config.FilePaths.MediaListFileName)));
            channelBlack = new HashSet<string>(File.ReadLines(Path.Combine(dataPath.sharedPath, IllSingleton.Config.FilePaths.ChannelListFileName)));
            dictionary = new HashSet<string>(File.ReadLines(Path.Combine(dataPath.sharedPath, IllSingleton.Config.FilePaths.DicFileName)));
            whiteList = new HashSet<string>(File.ReadLines(Path.Combine(dataPath.sharedPath, IllSingleton.Config.FilePaths.DicWhiteListFileName)));
            userBlackList = new HashSet<string>(File.ReadLines(Path.Combine(dataPath.uniquePath, IllSingleton.Config.FilePaths.UserBlacklistFileName)));
            Arabic2 = Enumerable.Range('\ufb50', 687).ToArray();
            bannedWordsTrie.BuildTrie(dictionary);

            // 1. Read pichka lines
            var pichkaLines = File.ReadLines(Path.Combine(dataPath.sharedPath, IllSingleton.Config.FilePaths.PichkaListFileName));
            // 2. Initialize the optimized matcher
            _pichkaMatcher = new AhoCorasick();
            foreach (var line in pichkaLines)
            {
                // Trim logic is optional, 
                // but usually raw lines are best for ASCII art.
                if (!string.IsNullOrWhiteSpace(line))
                {
                    _pichkaMatcher.AddPattern(line);
                }
            }
            _pichkaMatcher.Build(); //compiles the search tree
        }
       

        public static bool CheckBooB(string message)
        {
            // Fast fail: If the message doesn't contain Braille or Block elements, 
            // it's very likely not an ASCII art "pichka".
            // Braille range: \u2800-\u28FF
            // Block range: \u2580-\u259F
            bool hasSuspiciousChars = false;
            foreach (char c in message)
            {
                if ((c >= '\u2800' && c <= '\u28FF') || (c >= '\u2580' && c <= '\u259F'))
                {
                    hasSuspiciousChars = true;
                    break;
                }
            }

            // If no suspicious chars, skip the heavy check (unless you have pichkas made of purely latin text)
            if (!hasSuspiciousChars) return false;

            // Run the optimized Aho-Corasick check
            return _pichkaMatcher.ContainsAny(message);
        }                                                                  
        public static bool CheckTreck(string ID)
        {    
            if (mediaBlack.Contains(ID)) return true;
            return false;
        }                                                                       
        public static bool CheckChannel(string channelName)
        {      
            if (channelBlack.Contains(channelName)) return true;
            return false;
        }
        public static async Task<bool> ZapCheck(string message, string name)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;

            // STEP 1: Normalize but keep structure ("h_0_x_0_l word" -> "h o h o l word")
            // We do this so we can remove whitelisted phrases correctly.
            string processingMsg = StringUtil.Normalize(message);

            // STEP 2: Remove Whitelisted words/phrases
            // Example: if "bass" is whitelisted, we remove it before checking for "ass"
            if (whiteList != null)
            {
                foreach (var white in whiteList)
                {
                    // Ensure your whitelist items are normalized lower case strings!
                    processingMsg = processingMsg.Replace(white, " ");
                }
            }

            // STEP 3: Aggressive Squash
            // Take the remaining string and delete ALL non-letters.
            // "h o h o l word" -> "hoholword"
            // "gEGEGgh0x0l" -> "gegegghohol"
            string squashedMsg = StringUtil.GetAggressiveString(processingMsg);

            // STEP 4: Substring Search (Trie)
            // The Trie will look for "hohol" inside "gegegghohol"
            var bannedWord = bannedWordsTrie.FindBannedWord(squashedMsg);

            if (bannedWord != null)
            {
                FlagWriter.FlagWriterTask($"{name} : {message} (detected: {bannedWord})");
                if (IllSingleton.State.Debug)
                    await TtvIRCClient.SendMessage($"Filter: {bannedWord}").ConfigureAwait(false);
                return true;
            }
            return false;
        }
        public static async Task<List<string>> YouTubeFilter(string ID)
        {
            List<string> output = new List<string>();
            var yRes = await YouTubeSearch.YouTubeSearchByIDTask(ID);
            if (yRes == null) return null;
            if (yRes[0] != "view" && yRes[0] != "duration" && yRes[0] != "age" && yRes[0] != "Embeddable")
            {
                if (await ZapCheck(yRes[0], "YouTube").ConfigureAwait(false))
                {
                    output.Add("ZAP");
                    return output;
                }
                else
                {
                    output.Add("ok");
                    output.Add(yRes[1]); //chennel title
                    output.Add(yRes[0]); //title
                    return output;
                }
            }
            else
            {
                output.Add(yRes[0]);
                return output;
            }
        }
        public static bool IsUserBlacklisted(string userID)
        {
            if (userBlackList.Contains(userID)) return true;
            return false;
        }
        public static async Task<bool> DeleteLinks(UserObject user, OnMessageReceivedArgs e)
        {
            if (user.isMod == 1 || user.IsBroadcaster == 1) return false;
            if (!NetUtil.IsValidLink(e.ChatMessage.Message)) return false;
            UrlDetector parser = new UrlDetector(e.ChatMessage.Message, UrlDetectorOptions.Default);
            var detectedUrls = parser.Detect();
            if (detectedUrls.Count == 1)
            {
                var clipId = StringUtil.ExtractClipId(e.ChatMessage.Message);
                if (clipId == null || !await TtvAPI.CheckClipExistence(clipId).ConfigureAwait(false))
                {
                    await TtvAPI.DeleteMessage(e.ChatMessage.Id).ConfigureAwait(false);
                    return true;
                }
            }
            if (detectedUrls.Count > 1)
            {
                await TtvAPI.DeleteMessage(e.ChatMessage.Id).ConfigureAwait(false);
                return true;
            }
            return false;
        }
        public static bool FilterASCII(OnMessageReceivedArgs e)
        {            
            if (e.ChatMessage.CustomRewardId != IllSingleton.Config.ChannelIds.Pi4KaId)
            {
                int count = StringUtil.CheckASCII(e.ChatMessage.Message);
                if (count / CharsInRow >= RowsNum && e.ChatMessage.Message.Length / CharsInRow > RowsNum)
                    return true;
                var arabicCount = Arabic2.Select(b => e.ChatMessage.Message.Count(f => f == (char)b)).Sum();
                if (arabicCount / ArabCharsInRow >= RowsNum && e.ChatMessage.Message.Length / ArabCharsInRow >= RowsNum)
                    return true;
                if (e.ChatMessage.Message.Contains("ﱞﱞﱞﱞﱞﱞﱞﱞﱞﱞﱞﱞ"))
                    return true;
            }
            return false;
        }
        public static void EditUserBlackList(string UserTtvID)
        {
            userBlackList.Remove(UserTtvID);
        }
        public static void AddToWhiteList(string WordToAdd)
        {
            whiteList.Add(WordToAdd);
        }
    }
}
