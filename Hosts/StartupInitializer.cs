using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Core;
using Serilog.Events;
using SkillzBot.Interfaces;

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

        public StartupInitializer(
            IBotStateService botState,
            IGameStateService gameState,
            IPathProvider paths,
            QuartzBackgroundTaskManager quartz,
            ILogger<StartupInitializer> logger,
            IRiotApiService riotApi,
            LoggingLevelSwitch levelSwitch)
        {
            _botState = botState;
            _gameState = gameState;
            _paths = paths;
            _quartz = quartz;
            _logger = logger;
            _riotApi = riotApi;
            _levelSwitch = levelSwitch;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
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

            _logger.LogInformation("Initializing Riot API...");
            var apiResult = await _riotApi.InitializeAsync();
            if (apiResult)            
                _logger.LogInformation("Riot API ready.");            
            else            
                _logger.LogWarning("Riot API failed to initialize (Token might be missing or invalid).");            

            _logger.LogInformation("Scheduling Quartz Tasks...");
            await _quartz.ScheduleTasks();
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
                _paths.GetFullPath("mediaList.txt", false),
                _paths.GetFullPath("channelList.txt", false),
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