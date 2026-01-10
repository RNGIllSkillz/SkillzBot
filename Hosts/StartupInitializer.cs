using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkillzBot.Interfaces;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Hosts
{
    public class StartupInitializer : IHostedService
    {
        private readonly IBotStateService _botState;
        private readonly IGameStateService _gameState;
        private readonly IPathProvider _paths;
        private readonly QuartzBackgroundTaskManager _quartz;
        private readonly ILogger<StartupInitializer> _logger;

        public StartupInitializer(
            IBotStateService botState,
            IGameStateService gameState,
            IPathProvider paths,
            QuartzBackgroundTaskManager quartz,
            ILogger<StartupInitializer> logger)
        {
            _botState = botState;
            _gameState = gameState;
            _paths = paths;
            _quartz = quartz;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Creating default files if missing...");
            await EnsureDefaultFilesExistAsync();

            _logger.LogInformation("Loading Bot and Game State...");
            await _botState.LoadAsync();
            await _gameState.LoadAsync();

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