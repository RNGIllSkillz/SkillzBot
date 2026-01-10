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
    public class BotStateService : IBotStateService
    {
        private readonly IPathProvider _paths;
        private readonly BotConfigModel _config;
        private readonly ILogger<BotStateService> _logger;
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        private readonly object _memLock = new object();

        public BotStateModel Current { get; private set; } = new BotStateModel
        {
            // Default safe values
            AutoPred = true,
            ChatFilterLvl = 3,
            IsSubActive = true
        };

        public BotStateService(IPathProvider paths, BotConfigModel config, ILogger<BotStateService> logger)
        {
            _paths = paths;
            _config = config;
            _logger = logger;
        }

        public async Task LoadAsync()
        {
            var path = _paths.GetFullPath(_config.FilePaths.BotStateFileName);
            await _ioLock.WaitAsync();
            try
            {
                if (File.Exists(path))
                {
                    var json = await File.ReadAllTextAsync(path);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var loaded = JsonSerializer.Deserialize<BotStateModel>(json);
                        if (loaded != null)
                        {
                            lock (_memLock) Current = loaded;
                            return;
                        }
                    }
                }
                // If file doesn't exist or is empty, save defaults
                await SaveInternalAsync(path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load BotState");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task UpdateStateAsync(Action<BotStateModel> updateAction)
        {
            lock (_memLock)
            {
                updateAction(Current);
            }
            await Task.Run(SaveAsync);
        }

        public async Task SaveAsync()
        {
            var path = _paths.GetFullPath(_config.FilePaths.BotStateFileName);
            await _ioLock.WaitAsync();
            try
            {
                await SaveInternalAsync(path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save BotState");
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

            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(path, json);
        }
    }
}