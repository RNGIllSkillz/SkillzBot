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
using SkillzBot.Singleton;
using System.Linq;
using System.Text.RegularExpressions;
using urldetector.detection;
using System.Text;

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
        private static HashSet<string> whiteList;
        private static HashSet<string> userBlackList;
        private static readonly IllSingleton singleton = IllSingleton.GetInstance();
        private static readonly int[] Arabic2;
        static IllChatFilters()
        {
            pichkaBlack = new HashSet<string>(File.ReadLines(Path.Combine(dataPath.sharedPath, singleton.PichkaListFileName)));
            mediaBlack = new HashSet<string>(File.ReadLines(Path.Combine(dataPath.sharedPath, singleton.MediaListFileName)));
            channelBlack = new HashSet<string>(File.ReadLines(Path.Combine(dataPath.sharedPath, singleton.ChannelListFileName)));
            dictionary = new HashSet<string>(File.ReadLines(Path.Combine(dataPath.sharedPath, singleton.DicFileName)));
            whiteList = new HashSet<string>(File.ReadLines(Path.Combine(dataPath.sharedPath, singleton.DicWhiteListFileName)));
            userBlackList = new HashSet<string>(File.ReadLines(Path.Combine(dataPath.uniquePath, singleton.UserblacklistFileName)));
            Arabic2 = Enumerable.Range('\ufb50', 687).ToArray();
            dictionaryGen = StringUtil.GenerateDictionary(dictionary);   
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
            var exact = message.Split(' ');
            var CleanMessage = StringUtil.Clean(message);
            foreach (var white in whiteList)
            {
                CleanMessage = CleanMessage.Replace(white, "");
            }
            CleanMessage = StringUtil.Clean(CleanMessage);
            foreach (var word in dictionaryGen)
                if (CleanMessage.Contains(word))
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
        }        
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
            if (e.ChatMessage.CustomRewardId != singleton.Pi4KaId)
            {
                int count = StringUtil.CheckASCII(e.ChatMessage.Message);
                if (count / 29 >= 3 && e.ChatMessage.Message.Length / 29 > 3)
                    return true;
                var arabicCount = Arabic2.Select(b => e.ChatMessage.Message.Count(f => f == (char)b)).Sum();
                if (arabicCount / 4 >= 3 && e.ChatMessage.Message.Length / 4 >= 3)
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


        /////
        /*
        public static bool ZapCheck_DEPRICATED(string message, string name)
        {
            var exact = message.Split(' ');
            var CleanMessage = StringUtil.Clean(message);
            foreach (var white in whiteList)
            {
                CleanMessage = CleanMessage.Replace(white, "");
            }
            CleanMessage = StringUtil.Clean(CleanMessage);
            foreach (var word in dictionary)
                if (CleanMessage.Contains(word))
                {
                    FlagWriter.FlagWriterTask($"{name} : {message} : {word}");
                    return true;
                }

            foreach (var exactWord in exact)
                if (dictionary.Contains(StringUtil.Clean(exactWord)))
                {
                    FlagWriter.FlagWriterTask($"{name} : {message} : exactWord: {exactWord}");
                    return true;
                }
            return false;
        }


        */
        /////
    }
}
