using SkillzBot.IllSkillzBot;
using SkillzBot.WRITERS;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SkillzBot.MYSQL;
using SkillzBot.API.Twitch;
using SkillzBot.IRC;
using SkillzBot.SubUtils;
using SkillzBot.API.RiotGames;
using SkillzBot.IllSkillzBot.IllCommandsNest;
using SkillzBot.Hosts;
using Microsoft.Extensions.Logging;
using SkillzBot.Singleton;

namespace SkillzBot.QuartZ
{
    internal class BackGroundTasks
    {
        private static readonly ILogger<BackGroundTasks> _logger = IllServiceProvider.GetLogger<BackGroundTasks>();
        public static async Task RunDaily()
        {
            var t = await RiotAPI.GetRankBySummonerAsync().ConfigureAwait(false);
            if (t != null)
            {
                if (int.TryParse(t[1], out int startLP))
                    IllSingleton.Game.StartLP = startLP;
                else
                    IllSingleton.Game.StartLP = 0;
                IllSingleton.Game.Elo = t[0];
                IllSingleton.Game.Tier = t[2];
            }
            IllSingleton.Game.EarnedLP = 0;
            IllSingleton.Game.NumLosses = 0;
            IllSingleton.Game.NumGames = 0;
            IllSingleton.Game.NumWins = 0;
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
            if (IllSingleton.State.BroadcasterIsOnline)
            {
                try
                {
                    await IllCommands.TopRulete().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "");
                }
            }
        }
        public static async Task MediaQueueFlush()
        {
            await MediaqueueWriter.MediaQueueFlush().ConfigureAwait(false);
        }
        public static async Task CronTest()
        {
            await TtvIRCClient.SendMessage("cron await test. 10s");
            await Task.Delay(10000);
        }
        public static async Task UserUntimeoutTrigger(string UserName)
        {
            await Task.Delay(2000).ConfigureAwait(false); // wait for PubSub time out event
            while (true)
            {
                var user = await IllServiceProvider.Database.GetUserAsync(UserName).ConfigureAwait(false);
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
