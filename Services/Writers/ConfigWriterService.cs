using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Services.Writers
{
    /// <summary>
    /// Persists the few runtime-editable settings back into the channel config file.
    /// The file is patched in place so unknown keys and secrets are never dropped or rewritten.
    /// </summary>
    public class ConfigWriterService
    {
        private readonly IPathProvider _paths;
        private readonly IBotStateService _botState;
        private readonly IGameStateService _gameState;
        private readonly ILogger<ConfigWriterService> _logger;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public ConfigWriterService(
            IPathProvider paths,
            IBotStateService botState,
            IGameStateService gameState,
            ILogger<ConfigWriterService> logger)
        {
            _paths = paths;
            _botState = botState;
            _gameState = gameState;
            _logger = logger;
        }

        public async Task WriteAsync()
        {
            await _lock.WaitAsync();
            try
            {
                var path = _paths.ConfigPath;
                if (!File.Exists(path))
                {
                    _logger.LogWarning("Config file {Path} not found; skipping config update.", path);
                    return;
                }

                var root = JObject.Parse(await File.ReadAllTextAsync(path));
                root["Summoner_Name"] = _gameState.Current.SummonerName;
                root["SummonerRegion"] = _gameState.Current.SummonerRegion;
                root["ChatFilterLvl"] = _botState.Current.ChatFilterLvl;

                await File.WriteAllTextAsync(path, root.ToString(Formatting.Indented));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update config file");
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
