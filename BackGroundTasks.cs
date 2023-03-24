using SkillzBot.IllSkillzBot;
using SkillzBot.Singleton;
using SkillzBot.WRITERS;
using SkillzBot.API.Riot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SkillzBot.MYSQL;
using SkillzBot.API.Twitch;
using SkillzBot.IRC;

namespace SkillzBot.Tasks
{
    internal class BackGroundTasks
    {        
        public static async Task RunDaily()
        {
            var t = await RiotAPI.GetRankBySummonerAsync().ConfigureAwait(false);
            var singleton = IllSingleton.GetInstance();
            if (t != null)
            {
                try
                {
                    singleton.startLP = int.Parse(t[1]);
                    singleton.elo = t[0];
                    singleton.tier = t[2];
                }
                catch (Exception e)
                {
                    Log.WriteLog(e, "null");
                }
            }
            singleton.earnedLP = 0;
            singleton.numLoose = 0;
            singleton.numGames = 0;
            singleton.numWins = 0;
            IllCommands.SaveGameStats();
        }
        public static async Task CalculatePoints()
        {
            //run every 5 min
            //ToDo: Add timestamp to online status            
            try
            {
                //var Chatters = await TtvAPI.GetChatters().ConfigureAwait(false);
                var chatters = await TtvAPI.GetChattersAsync().ConfigureAwait(false);
                if (chatters != null)
                {
                    List<string> lChatters = new List<string>();
                    foreach (var chatter in chatters.Data)
                    {
                        lChatters.Add(chatter.UserLogin);
                    }

                    await MySQL.UpdateOnlineStatus(lChatters).ConfigureAwait(false);
                    if (IllSingleton.GetInstance().BroadcasterIsOnline)
                    {
                        await MySQL.AddPoints(10).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "CalculatePoints()");
            }
        }
        public static async Task TopRuleteTask()
        {
            if (IllSingleton.GetInstance().BroadcasterIsOnline)
            {
                try
                {
                    await IllCommands.TopRulete().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.WriteLog(ex, "null");
                }
            }
        }
        public static async Task MediaQueueFlush()
        {
           await MediaqueueWriter.MediaQueueFlush().ConfigureAwait(false);
        }
        public static async Task CronTest()
        {
            TtvIRCClient.SendMessage("cron await test. 10s");
            await Task.Delay(10000);
        }
    }
}
