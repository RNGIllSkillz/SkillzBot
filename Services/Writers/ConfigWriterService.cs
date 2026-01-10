using Newtonsoft.Json;
using SkillzBot.JSON.Settings; 
using SkillzBot.IllConfiguration; 
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Services.Writers
{
    public class ConfigWriterService
    {
        private readonly IPathProvider _paths;
        private readonly BotConfigModel _config;
        private readonly IBotStateService _botState;
        private readonly IGameStateService _gameState;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public ConfigWriterService(
            IPathProvider paths,
            BotConfigModel config,
            IBotStateService botState,
            IGameStateService gameState)
        {
            _paths = paths;
            _config = config;
            _botState = botState;
            _gameState = gameState;
        }

        public async Task WriteAsync()
        {
            await _lock.WaitAsync();
            try
            {
                var settings = new SettingsJson
                {
                    SummonerName = _gameState.Current.SummonerName, 
                    SummonerRegion = _gameState.Current.SummonerRegion,
                    ChannelName = _config.ChannelName,
                    BotTwitchName = _config.BotTwitchName,
                    BotTwitchAuth = _config.BotTwitchAuth,
                    TApiAccessToken = _config.TApiAccessToken,
                    TApiClientId = _config.TApiClientId,
                    YouTubeApiToken = _config.YouTubeApiToken,
                    RiotApiToken = _config.RiotApiToken,
                    BrodcasterId = _config.BroadcasterId,
                    ChatFilterLvl = _botState.Current.ChatFilterLvl, 
                    DiscordBotToken = _config.DiscordBotToken,
                    DiscordNoteID = _config.DiscordNoteID,
                    DiscordSpamID = _config.DiscordSpamID,

                    // Database mapping
                    MySQL_IP = _config.Database.Host,
                    MySQL_Port = _config.Database.Port,
                    MySQL_User = _config.Database.Username,
                    MySQL_password = _config.Database.Password,

                    // IDs
                    ZakazTrekaId = _config.ChannelIds.ZakazTrekaId,
                    Pi4KaId = _config.ChannelIds.Pi4KaId,
                    UvalId = _config.ChannelIds.UvalId,
                    UvalSabId = _config.ChannelIds.UvalSabId,
                    UvalVipId = _config.ChannelIds.UvalVipId,
                    EmoteModeId = _config.ChannelIds.EmoteModeId,
                    CenceleUval = _config.ChannelIds.CenceleUval,
                    uvalMod = _config.ChannelIds.UvalMod,

                    StreamElementsApiToken = _config.StreamElementsApiToken,
                    StreamElementsID = _config.StreamElementsID,
                    GPTApiToken = _config.GPTApiToken
                };

                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                await File.WriteAllTextAsync(_paths.ConfigPath, json);
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}