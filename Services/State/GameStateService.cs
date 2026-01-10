using Microsoft.Extensions.Logging;
using SkillzBot.MODELS;
using SkillzBot.IllConfiguration;  
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Services.State
{
    public class GameStateService : IGameStateService
    {
        private readonly IPathProvider _paths;
        private readonly BotConfigModel _config;
        private readonly ILogger<GameStateService> _logger;

        // Locking mechanisms for thread safety
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        private readonly object _memLock = new object();

        // The actual data model
        public BotGameStateModel Current { get; private set; }

        public GameStateService(IPathProvider paths, BotConfigModel config, ILogger<GameStateService> logger)
        {
            _paths = paths;
            _config = config;
            _logger = logger;

            // Initialize with defaults from config
            Current = new BotGameStateModel
            {
                SummonerName = _config.SummonerName ?? "",
                SummonerRegion = "euw"
            };
        }

        public async Task LoadAsync()
        {
            var path = _paths.GetFullPath(_config.FilePaths.GameStateFileName);

            await _ioLock.WaitAsync();
            try
            {
                if (File.Exists(path))
                {
                    var json = await File.ReadAllTextAsync(path);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var loaded = JsonSerializer.Deserialize<BotGameStateModel>(json);
                        if (loaded != null)
                        {
                            lock (_memLock) Current = loaded;
                            _logger.LogInformation("GameState loaded successfully.");
                            return;
                        }
                    }
                }

                _logger.LogInformation("GameState file not found or empty. Creating default.");
                await SaveInternalAsync(path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load GameState");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task UpdateStateAsync(Action<BotGameStateModel> updateAction)
        {
            lock (_memLock)
            {
                updateAction(Current);
            }
            // Fire-and-forget save to prevent blocking the caller
            await Task.Run(SaveAsync);
        }

        public async Task SaveAsync()
        {
            var path = _paths.GetFullPath(_config.FilePaths.GameStateFileName);

            await _ioLock.WaitAsync();
            try
            {
                await SaveInternalAsync(path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save GameState");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private async Task SaveInternalAsync(string path)
        {
            string json;
            lock (_memLock)
            {
                json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(path, json);
        }
    }
}