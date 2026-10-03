using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Core;
using Serilog.Events;
using SkillzBot.Discord;
using SkillzBot.IllConfiguration;
using SkillzBot.Interfaces;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SkillzBot.Hosts
{
    public class StartupInitializer : IHostedService
    {
        private readonly IBotStateService _botState;
        private readonly IGameStateService _gameState;
        private readonly IPathProvider _paths;
        private readonly QuartzBackgroundTaskManager _quartz;
        private readonly ILogger<StartupInitializer> _logger;
        private readonly IRiotApiService _riotApi;
        private readonly LoggingLevelSwitch _levelSwitch;
        private readonly DiscordClient _discord;
        private readonly ITwitchService _twitchService;
        private readonly BotConfigModel _config;

        public StartupInitializer(
            IBotStateService botState,
            IGameStateService gameState,
            IPathProvider paths,
            QuartzBackgroundTaskManager quartz,
            ILogger<StartupInitializer> logger,
            IRiotApiService riotApi,
            LoggingLevelSwitch levelSwitch,
            DiscordClient discord,
            ITwitchService twitchService,
            BotConfigModel config)
        {
            _twitchService = twitchService;
            _config = config;
            _botState = botState;
            _gameState = gameState;
            _paths = paths;
            _quartz = quartz;
            _logger = logger;
            _riotApi = riotApi;
            _levelSwitch = levelSwitch;
            _discord = discord;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
            _logger.LogInformation("SkillzBot {Version} starting for channel {Channel} on {Runtime}; data path {DataPath}",
                version, _config.ChannelName, RuntimeInformation.FrameworkDescription, _paths.DataPath);

            _logger.LogInformation("Creating default files if missing...");
            await EnsureDefaultFilesExistAsync();

            _logger.LogInformation("Loading Bot and Game State...");
            await _botState.LoadAsync();
            await _gameState.LoadAsync();

            if (_botState.Current.InMatch || _botState.Current.QuizIsRunning)
            {
                _logger.LogWarning("Detected leftover active state (InMatch/QuizRunning). Resetting to FALSE to prevent lockups.");
                await _botState.UpdateStateAsync(s =>
                {
                    s.InMatch = false;
                    s.QuizIsRunning = false;
                });
            }

            if (_botState.Current.Debug)
            {
                _levelSwitch.MinimumLevel = LogEventLevel.Debug;
                _logger.LogWarning("DEBUG MODE ENABLED via BotState.txt");
            }
            else
            {
                _levelSwitch.MinimumLevel = LogEventLevel.Information;
            }

            // EventSub only reports transitions, so learn the current live state once at startup.
            try
            {
                bool live = await _twitchService.GetStreamStatus();
                if (live != _botState.Current.BroadcasterIsOnline)
                {
                    _logger.LogInformation("Stream status synced at startup: online={Live}", live);
                    await _botState.SetBroadcasterOnlineAsync(live);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not determine stream status at startup.");
            }

            _logger.LogInformation("Initializing Riot API...");
            try
            {
                var apiResult = await _riotApi.InitializeAsync();
                if (apiResult)
                    _logger.LogInformation("Riot API ready.");
                else
                    _logger.LogWarning("Riot API failed to initialize (Token might be missing or invalid).");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Riot API initialization threw; it will retry on first use.");
            }

            _logger.LogInformation("Scheduling Quartz Tasks...");
            await _quartz.ScheduleTasks();

            _logger.LogInformation("Starting Discord client...");
            try
            {
                await _discord.InitializeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Discord failed to start; continuing without it.");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private async Task EnsureDefaultFilesExistAsync()
        {
            var filesToCreate = new[]
            {
                _paths.GetFullPath("dic.txt", true),
                _paths.GetFullPath("dicWhiteList.txt", true),
                _paths.GetFullPath("pichkaList.txt", true),
                _paths.GetFullPath("mediaqueue.txt", false),
                _paths.GetFullPath("userblacklist.txt", false),
                _paths.GetFullPath("mediaList.txt", true),
                _paths.GetFullPath("channelList.txt", true),
                _paths.GetFullPath("Subscription.txt", false),
            };

            foreach (string filePath in filesToCreate)
            {
                if (!File.Exists(filePath))
                {
                    await File.WriteAllTextAsync(filePath, string.Empty);
                }
            }
        }
    }
}
