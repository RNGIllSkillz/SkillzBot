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

        // Singleton Guard
        private bool _isInitialized = false;
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

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
            if (_isInitialized) return;

            var path = _paths.GetFullPath(_config.FilePaths.GameStateFileName);

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (File.Exists(path))
                {
                    var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        try
                        {
                            var loaded = JsonSerializer.Deserialize<BotGameStateModel>(json, _jsonOptions);
                            if (loaded != null)
                            {
                                lock (_memLock) Current = loaded;
                                _isInitialized = true;
                                _logger.LogInformation("GameState loaded successfully.");
                                return;
                            }
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogError(ex, "GameState.txt corrupted. Using defaults.");
                        }
                    }
                }

                _logger.LogInformation("GameState file not found or empty. Creating default.");
                await SaveInternalAsync(path);
                _isInitialized = true;
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
            await SaveAsync();
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
            BotGameStateModel snapshot;
            lock (_memLock)
            {
                var tempJson = JsonSerializer.Serialize(Current, _jsonOptions);
                snapshot = JsonSerializer.Deserialize<BotGameStateModel>(tempJson, _jsonOptions);
            }

            var json = JsonSerializer.Serialize(snapshot, _jsonOptions);

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
        }
    }
}