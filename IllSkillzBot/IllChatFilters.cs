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
    internal class IllChatFilters
    {
        readonly private static string dataPath = IllSkillzBotMain.GetChannelName();
        readonly private static string pichkaList = Path.Combine(dataPath, "pichkaList.txt");
        readonly private static string dicDir = Path.Combine(dataPath, "dic.txt");
        readonly private static string dicDirWhite = Path.Combine(dataPath, "dicWhiteList.txt");
        readonly private static string userBlackListDir = Path.Combine(dataPath, "userblacklist.txt");
        readonly private static string mediaBlackList = Path.Combine(dataPath, "mediaList.txt");
        readonly private static string channelBlackList = Path.Combine(dataPath, "channelList.txt");

        readonly static int[] Arabic2 = Enumerable.Range('\ufb50', 687).ToArray();

        public static bool CheckBooB(string message)
        {
            IEnumerable<String> pichkaBlack = File.ReadLines(pichkaList);
            foreach (string pickString in pichkaBlack)
            {
                if (message.Contains(pickString))
                    return true;
            }
            return false;
        }                                                                  
        public static bool CheckTreck(string ID)
        {
            IEnumerable<String> mediaBlack = File.ReadLines(mediaBlackList);
            foreach (string id in mediaBlack)
            {
                if (id == ID)
                    return true;
            }
            return false;
        }                                                                       
        public static bool CheckChannel(string channelName)
        {
            IEnumerable<String> channelBlack = File.ReadLines(channelBlackList);
            foreach (string id in channelBlack)
            {
                if (id == channelName)
                    return true;
            }
            return false;
        }                                                            
        public static bool ZapCheck(string message, string name)
        {
            HashSet<string> dictionary = new HashSet<string>(File.ReadLines(dicDir));
            HashSet<string> whiteList = new HashSet<string>(File.ReadLines(dicDirWhite));
            var exact = message.Split(' ');
            string CleanMessage = StringUtil.Clean(message);
            foreach (string white in whiteList)
            {
                CleanMessage = CleanMessage.Replace(white, "");
            }
            CleanMessage = StringUtil.Clean(CleanMessage);
            foreach (string word in dictionary)
            {
                if (CleanMessage.Contains(word))
                {
                    try
                    {
                        FlagWriter.FlagWriterTask($"{DateTime.Now} {name} : {message} : {word}");
                    }
                    catch (Exception e)
                    {
                        Log.WriteLog(e, "zapCheck()");
                    }
                    return true;
                }
            }
            string check;
            foreach (string exactWord in exact)
            {
                check = StringUtil.Clean(exactWord);
                foreach (string word in dictionary)
                {
                    if (check == word)
                        return true;
                }
            }
            return false;
        }
        public static async Task<List<string>> YouTubeFilter(string ID)
        {
            List<string> output = new List<string>();
            var yRes = await YouTubeSearch.YouTubeSearchByIDTask(ID);
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
            IEnumerable<String> userBlackList = File.ReadLines(userBlackListDir);
            foreach (string user in userBlackList)
            {
                if (user == userID)
                    return true;
            }
            return false;
        }
        public static async Task DeleteLinks (UserObject user, OnMessageReceivedArgs e)
        {
            if (e.ChatMessage.CustomRewardId != IllSingleton.GetInstance().ZakazTrekaId & !e.ChatMessage.Message.Contains("clips"))
            {
                if (e.ChatMessage.Message.Contains("http"))
                    if (user.isMod != 1)
                        await TtvAPI.DeleteMessage(e.ChatMessage.Id).ConfigureAwait(false);
            }
        }
        public static bool FilterASCII(OnMessageReceivedArgs e)
        {            
            if (e.ChatMessage.CustomRewardId != IllSingleton.GetInstance().Pi4KaId)
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
