using Microsoft.Extensions.Logging;
using SkillzBot.IllSkillzBot;
using SkillzBot.IllSkillzBot.IllCommandsNest;
using SkillzBot.Interfaces;
using SkillzBot.Services.Infrastructure;
using SkillzBot.Utils;
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
        private readonly CooldownManager _cooldownManager;
        private readonly ITwitchService _twitchService;

        public BackGroundTasks(
            ILogger<BackGroundTasks> logger,
            ITtvIRCClient ircClient,
            IllChatMessageHandler chatMessageHandler,
            IRiotApiService riotApi,
            IllCommands illCommands,
            IBotStateService botState,
            IGameStateService gameState,
            MediaQueueService mediaQueueService,
            SubscriptionService subscriptionService,
            CooldownManager cooldownManager,
            ITwitchService twitchService)
        {
            _twitchService = twitchService;
            _logger = logger;
            _ircClient = ircClient;
            _chatMessageHandler = chatMessageHandler;
            _riotApi = riotApi;
            _illCommands = illCommands;
            _botState = botState;
            _gameState = gameState;
            _mediaQueueService = mediaQueueService;
            _subscriptionService = subscriptionService;
            _cooldownManager = cooldownManager;
        }

        public async Task RunDaily()
        {
            _logger.LogInformation("Running Daily Tasks...");
            var t = await _riotApi.GetRankBySummonerAsync();

            await _gameState.UpdateStateAsync(s =>
            {
                if (t != null)
                {
                    s.StartLP = int.TryParse(t[1], out int startLP) ? startLP : 0;
                    s.Elo = t[0];
                    s.Tier = t[2];
                }
                s.EarnedLP = 0;
                s.NumLosses = 0;
                s.NumGames = 0;
                s.NumWins = 0;
            });
        }

        public async Task RunEvery5Min()
        {
            // Each step is isolated so one failure (for example the database being down)
            // does not skip the others.
            await RunStep("PruneTrackers", () => { _chatMessageHandler.PruneTrackers(); return Task.CompletedTask; });
            await RunStep("PruneCooldowns", () => { _cooldownManager.PruneExpiredCooldowns(); return Task.CompletedTask; });
            await RunStep("SaveBuffer", () => _chatMessageHandler.SaveBuffer(true));
            await RunStep("CheckSubscription", () => _subscriptionService.CheckSubscriptionAsync());
            await RunStep("SyncStreamStatus", SyncStreamStatusAsync);
        }

        /// <summary>
        /// Safety net for a missed stream.online/offline event: quietly realigns the flag
        /// that gates the periodic roulette top with what Helix reports.
        /// </summary>
        private async Task SyncStreamStatusAsync()
        {
            if (!_twitchService.IsReady()) return;
            bool live = await _twitchService.GetStreamStatus();
            if (live != _botState.Current.BroadcasterIsOnline)
            {
                _logger.LogInformation("Stream status drifted; correcting online={Live}", live);
                await _botState.UpdateStateAsync(s => s.BroadcasterIsOnline = live);
            }
        }

        private async Task RunStep(string name, Func<Task> step)
        {
            try
            {
                await step();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Periodic step {Step} failed", name);
            }
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
