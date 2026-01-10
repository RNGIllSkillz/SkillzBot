using Microsoft.Extensions.Logging;
using SkillzBot.IllSkillzBot;
using SkillzBot.IllSkillzBot.IllCommandsNest;
using SkillzBot.Interfaces;
using SkillzBot.Services.Infrastructure;
using System;
using System.Threading.Tasks;

namespace SkillzBot.QuartZ
{
    internal class BackGroundTasks
    {
        private readonly ILogger<BackGroundTasks> _logger;
        private readonly ITtvIRCClient _ircClient;
        private readonly IllChatMessageHandler _chatMessageHandler;
        private readonly IRiotApiService _riotApi;
        private readonly IllCommands _illCommands;
        private readonly IBotStateService _botState; 
        private readonly IGameStateService _gameState;
        private readonly MediaQueueService _mediaQueueService;
        private readonly SubscriptionService _subscriptionService;

        public BackGroundTasks(
            ILogger<BackGroundTasks> logger,
            ITtvIRCClient ircClient,
            IllChatMessageHandler chatMessageHandler,
            IRiotApiService riotApi,
            IllCommands illCommands,
            IBotStateService botState,
            IGameStateService gameState,
            MediaQueueService mediaQueueService,
            SubscriptionService subscriptionService)
        {
            _logger = logger;
            _ircClient = ircClient;
            _chatMessageHandler = chatMessageHandler;
            _riotApi = riotApi;
            _illCommands = illCommands;
            _botState = botState;
            _gameState = gameState;
            _mediaQueueService = mediaQueueService;
            _subscriptionService = subscriptionService;
        }

        public async Task RunDaily()
        {
            _logger.LogInformation("Running Daily Tasks...");
            var t = await _riotApi.GetRankBySummonerAsync();
            if (t != null)
            {
                if (int.TryParse(t[1], out int startLP))
                    _gameState.Current.StartLP = startLP;
                else
                    _gameState.Current.StartLP = 0;
                _gameState.Current.Elo = t[0];
                _gameState.Current.Tier = t[2];
            }            
            _gameState.Current.EarnedLP = 0;
            _gameState.Current.NumLosses = 0;
            _gameState.Current.NumGames = 0;
            _gameState.Current.NumWins = 0;

            await _gameState.SaveAsync();
        }

        public async Task RunEvery5Min()
        {
            _chatMessageHandler.PruneTrackers();
            await _chatMessageHandler.SaveBuffer(true);
            await _subscriptionService.CheckSubscriptionAsync();
        }

        public async Task TopRuleteTask()
        {
            if (_botState.Current.BroadcasterIsOnline)
            {
                try
                {
                    await _illCommands.TopRulete();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "TopRuleteTask failed");
                }
            }
        }

        public async Task MediaQueueFlush()
        {
            await _mediaQueueService.FlushQueueAsync();
        }

        public async Task CronTest()
        {
            await _ircClient.SendMessage("Cron test message.");
        }
    }
}