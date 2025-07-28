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
        private static readonly HashSet<string> pichkaBlack;
        private static readonly HashSet<string> mediaBlack;
        private static readonly HashSet<string> channelBlack;
        private static readonly HashSet<string> dictionary;
        private static readonly HashSet<string> dictionaryGen;
        private static readonly BannedWordsTrie bannedWordsTrie = new BannedWordsTrie();
        private static HashSet<string> whiteList;
        private static HashSet<string> userBlackList;
        private static readonly int[] Arabic2;
        private static readonly int CharsInRow = 29;
        private static readonly int ArabCharsInRow = 4;
        private static readonly int RowsNum = 3;
        static IllChatFilters()
        {
            pichkaBlack = new HashSet<string>(File.ReadLines(Path.Combine(dataPath.sharedPath, IllSingleton.Config.FilePaths.PichkaListFileName)));
            mediaBlack = new HashSet<string>(File.ReadLines(Path.Combine(dataPath.sharedPath, IllSingleton.Config.FilePaths.MediaListFileName)));
            channelBlack = new HashSet<string>(File.ReadLines(Path.Combine(dataPath.sharedPath, IllSingleton.Config.FilePaths.ChannelListFileName)));
            dictionary = new HashSet<string>(File.ReadLines(Path.Combine(dataPath.sharedPath, IllSingleton.Config.FilePaths.DicFileName)));
            whiteList = new HashSet<string>(File.ReadLines(Path.Combine(dataPath.sharedPath, IllSingleton.Config.FilePaths.DicWhiteListFileName)));
            userBlackList = new HashSet<string>(File.ReadLines(Path.Combine(dataPath.uniquePath, IllSingleton.Config.FilePaths.UserBlacklistFileName)));
            Arabic2 = Enumerable.Range('\ufb50', 687).ToArray();
            dictionaryGen = StringUtil.GenerateDictionary(dictionary);
            bannedWordsTrie.BuildTrie(dictionaryGen);
        }
       

        public static bool CheckBooB(string message)
        {            
            foreach (var pickString in pichkaBlack)
            {
                if (message.Contains(pickString))
                    return true;
            }
            return false;
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
        public static bool ZapCheck(string message, string name)
        {
            var exact = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var CleanMessage = StringUtil.Clean(message);
            foreach (var white in whiteList)
            {
                CleanMessage = CleanMessage.Replace(white, "");
            }
            CleanMessage = StringUtil.Clean(CleanMessage);

            //parallel for exact words
            
            if(CheckExact(message, name))
                return true;

            //tire for substrings
            var bannedWord = bannedWordsTrie.FindBannedWord(CleanMessage);
            if (bannedWord != null)
            {
                FlagWriter.FlagWriterTask($"{name} : {message} : {bannedWord}");
                if (IllSingleton.State.Debug)
                    TtvIRCClient.SendMessage("substring trigger");
                return true;
            }                        
            return false;
        }
        private static bool CheckExact(string message, string name)
        {
            var messageWords = message.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => StringUtil.Clean(word))
                .Where(word => !string.IsNullOrEmpty(word))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var matchedWord = dictionaryGen.AsParallel()
                .FirstOrDefault(dictWord =>
                {
                    // Check exact word match first (fastest)
                    if (messageWords.Contains(dictWord))
                        return true;
                    return false;
                });

            if (matchedWord != null)
            {
                FlagWriter.FlagWriterTask($"{name} : {message} : exactWord: {matchedWord}");
                if (IllSingleton.State.Debug)
                    TtvIRCClient.SendMessage("exact trigger");
                return true;
            }
            return false;
        }
        /*public static bool ZapCheck(string message, string name)
        {
            var exact = message.Split(' ');
            var CleanMessage = StringUtil.Clean(message);
            foreach (var white in whiteList)
            {
                CleanMessage = CleanMessage.Replace(white, "");
            }
            CleanMessage = StringUtil.Clean(CleanMessage);
            foreach (var word in dictionaryGen)
                if (CleanMessage.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    FlagWriter.FlagWriterTask($"{name} : {message} : {word}");
                    return true;
                }
            foreach (var exactWord in exact)
                if (dictionaryGen.Contains(StringUtil.Clean(exactWord)))
                {
                    FlagWriter.FlagWriterTask($"{name} : {message} : exactWord: {exactWord}");
                    return true;
                }
            return false;
        }*/
        public static async Task<List<string>> YouTubeFilter(string ID)
        {
            List<string> output = new List<string>();
            var yRes = await YouTubeSearch.YouTubeSearchByIDTask(ID);
            if (yRes == null) return null;
            if (yRes[0] != "view" && yRes[0] != "duration" && yRes[0] != "age" && yRes[0] != "Embeddable")
            {
                if (ZapCheck(yRes[0], "YouTube"))
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
