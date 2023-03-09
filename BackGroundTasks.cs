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
            if (t != null)
            {
                try
                {
                    IllSingleton.GetInstance().startLP = Convert.ToInt32(t[1]);
                    IllSingleton.GetInstance().elo = t[0];
                    IllSingleton.GetInstance().tier = t[2];
                }
                catch (Exception e)
                {
                    Log.WriteLog(e, "null");
                }
            }
            IllSingleton.GetInstance().earnedLP = 0;
            IllSingleton.GetInstance().numLoose = 0;
            IllSingleton.GetInstance().numGames = 0;
            IllSingleton.GetInstance().numWins = 0;
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
                    foreach (var chatter in chatters.Chatters.Vips)
                    {
                        lChatters.Add(chatter);
                    }
                    foreach (var chatter in chatters.Chatters.Viewers)
                    {
                        lChatters.Add(chatter);
                    }
                    foreach (var chatter in chatters.Chatters.Moderators)
                    {
                        lChatters.Add(chatter);
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
