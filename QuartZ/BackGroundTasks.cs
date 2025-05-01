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
using SkillzBot.SubUtils;

namespace SkillzBot.QuartZ
{
    internal class BackGroundTasks
    {
        public static async Task RunDaily()
        {
            var t = await RiotAPI.GetRankBySummonerAsync().ConfigureAwait(false);
            var singleton = IllSingleton.GetInstance();
            if (t != null)
            {
                if (int.TryParse(t[1], out int startLP))
                    singleton.startLP = startLP;
                else
                    singleton.startLP = 0;
                singleton.elo = t[0];
                singleton.tier = t[2];
            }
            singleton.earnedLP = 0;
            singleton.numLoose = 0;
            singleton.numGames = 0;
            singleton.numWins = 0;
            IllCommands.SaveGameStats();
        }
        public static async Task RunEvery5Min()
        {
            //Save MessageBuffer
            await IllChatMessageHandler.SaveBuffer(true).ConfigureAwait(false);

            //Check Subscription
            SubCheck.RunChecker();
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
        public static async Task UserUntimeoutTrigger(string UserName)
        {
            await Task.Delay(2000).ConfigureAwait(false); // wait for PubSub time out event
            while (true)
            {
                var user = await MySQL.GetUser(UserName).ConfigureAwait(false);
                if (user.UvalTimer <= DateTimeOffset.Now.ToUnixTimeSeconds())
                {
                    while (!await TtvAPI.AddChannelModerator(user.TwitchID.ToString()).ConfigureAwait(false))
                        await Task.Delay(1000).ConfigureAwait(false);
                    return;
                }
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }
    }
}
