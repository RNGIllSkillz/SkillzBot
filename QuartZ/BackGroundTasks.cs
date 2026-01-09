using Microsoft.Extensions.Logging;
using SkillzBot.API.RiotGames;
using SkillzBot.IllSkillzBot;
using SkillzBot.IllSkillzBot.IllCommandsNest;
using SkillzBot.Interfaces;
using SkillzBot.Singleton;
using SkillzBot.SubUtils;
using SkillzBot.WRITERS;
using System;
using System.Threading.Tasks;

namespace SkillzBot.QuartZ
{
    internal class BackGroundTasks
    {
        private readonly ILogger<BackGroundTasks> _logger;
        private readonly IDatabaseService _database;
        private readonly ITtvIRCClient _ircClient;
        private readonly IllChatMessageHandler _chatMessageHandler;
        private readonly IRiotApiService _riotApi;
        private readonly IllCommands _illCommands;

        public BackGroundTasks(
            ILogger<BackGroundTasks> logger,
            IDatabaseService database,
            ITtvIRCClient ircClient,
            IllChatMessageHandler chatMessageHandler,
            IRiotApiService riotApi,
            IllCommands illCommands)
        {
            _logger = logger;
            _database = database;
            _ircClient = ircClient;
            _chatMessageHandler = chatMessageHandler;
            _riotApi = riotApi;
            _illCommands = illCommands;
        }

        public async Task RunDaily()
        {
            _logger.LogInformation("Running Daily Tasks...");
            var t = await _riotApi.GetRankBySummonerAsync().ConfigureAwait(false);
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

            await IllSingleton.Game.SaveAsync().ConfigureAwait(false);
        }

        public async Task RunEvery5Min()
        {
            await _chatMessageHandler.SaveBuffer(true).ConfigureAwait(false);
            SubCheck.RunChecker();
        }

        public async Task TopRuleteTask()
        {
            if (IllSingleton.State.BroadcasterIsOnline)
            {
                try
                {
                    await _illCommands.TopRulete().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "TopRuleteTask failed");
                }
            }
        }

        public async Task MediaQueueFlush()
        {
            await MediaqueueWriter.MediaQueueFlush().ConfigureAwait(false);
        }

        public async Task CronTest()
        {
            await _ircClient.SendMessage("Cron test message.").ConfigureAwait(false);
        }
    }
}