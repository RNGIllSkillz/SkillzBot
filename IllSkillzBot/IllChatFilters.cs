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

namespace SkillzBot.IllSkillzBot
{
    sealed class IllChatFilters
    {
        private static readonly string dataPath = IllSkillzBotMain.GetChannelName();
        private static readonly HashSet<string> pichkaBlack;
        private static readonly HashSet<string> mediaBlack;
        private static readonly HashSet<string> channelBlack;
        private static readonly HashSet<string> dictionary;
        private static readonly HashSet<string> whiteList;
        private static readonly HashSet<string> userBlackList;
        private static readonly IllSingleton singleton = IllSingleton.GetInstance();
        private static readonly int[] Arabic2;

        static IllChatFilters()
        {
            pichkaBlack = new HashSet<string>(File.ReadLines(Path.Combine(dataPath, "pichkaList.txt")));
            mediaBlack = new HashSet<string>(File.ReadLines(Path.Combine(dataPath, "mediaList.txt")));
            channelBlack = new HashSet<string>(File.ReadLines(Path.Combine(dataPath, "channelList.txt")));
            dictionary = new HashSet<string>(File.ReadLines(Path.Combine(dataPath, "dic.txt")));
            whiteList = new HashSet<string>(File.ReadLines(Path.Combine(dataPath, "dicWhiteList.txt")));
            userBlackList = new HashSet<string>(File.ReadLines(Path.Combine(dataPath, "userblacklist.txt")));
            Arabic2 = Enumerable.Range('\ufb50', 687).ToArray();
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
        public static async Task DeleteLinks(UserObject user, OnMessageReceivedArgs e)
        {
            if (!e.ChatMessage.Message.Contains("http") || 
                e.ChatMessage.CustomRewardId == singleton.ZakazTrekaId || 
                e.ChatMessage.Message.Contains("clips") || 
                user.isMod == 1 || 
                user.IsBroadcaster == 1) return;
            await TtvAPI.DeleteMessage(e.ChatMessage.Id).ConfigureAwait(false);
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
    }
}
