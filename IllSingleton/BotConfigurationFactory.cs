using SkillzBot.Readers;
using System;
using System.Threading.Tasks;

namespace SkillzBot.Singleton
{
    public static class BotConfigurationFactory
    {
        public static async Task<BotConfigModel> CreateAsync(string configPath)
        {
            var configReader = new Config(configPath);
            var botConfigs = configReader.GetBotConfigs();

            if (botConfigs == null)
                throw new InvalidOperationException("Configuration file is empty or invalid JSON.");

            var missingFields = new System.Collections.Generic.List<string>();
            if (string.IsNullOrWhiteSpace(botConfigs.BotTwitchAuth)) missingFields.Add("BotTwitchAuth");
            if (string.IsNullOrWhiteSpace(botConfigs.ChannelName)) missingFields.Add("ChannelName");
            if (string.IsNullOrWhiteSpace(botConfigs.RiotApiToken)) missingFields.Add("RiotApiToken");

            if (missingFields.Count > 0)
            {
                throw new InvalidOperationException($"Critical configuration missing: {string.Join(", ", missingFields)}");
            }

            var configuration = new BotConfigModel
            {
                BotTwitchName = botConfigs.BotTwitchName,
                BotTwitchAuth = botConfigs.BotTwitchAuth,
                ChannelName = botConfigs.ChannelName,
                TApiAccessToken = botConfigs.TApiAccessToken,
                TApiClientId = botConfigs.TApiClientId,
                StreamElementsApiToken = botConfigs.StreamElementsApiToken,
                StreamElementsID = botConfigs.StreamElementsID,
                SummonerName = botConfigs.SummonerName,
                YouTubeApiToken = botConfigs.YouTubeApiToken,
                RiotApiToken = botConfigs.RiotApiToken,
                BroadcasterId = botConfigs.BrodcasterId,
                GPTApiToken = botConfigs.GPTApiToken,
                DiscordBotToken = botConfigs.DiscordBotToken,
                DiscordNoteID = botConfigs.DiscordNoteID,
                DiscordSpamID = botConfigs.DiscordSpamID,
                RootUser = "rng_backtrack", 

                Database = new DatabaseConfig(
                    botConfigs.MySQL_IP,
                    botConfigs.MySQL_Port,
                    botConfigs.MySQL_User,
                    botConfigs.MySQL_password
                ),

                FilePaths = new FilePathsConfig(
                    "pichkaList.txt",
                    "mediaList.txt",
                    "channelList.txt",
                    "dic.txt",
                    "dicWhiteList.txt",
                    "userblacklist.txt",
                    "GameState.txt",
                    "BotState.txt"
                ),

                ChannelIds = new ChannelIdsConfig(
                    botConfigs.CenceleUval,
                    botConfigs.EmoteModeId,
                    botConfigs.uvalMod,
                    botConfigs.UvalId,
                    botConfigs.Pi4KaId,
                    botConfigs.ZakazTrekaId,
                    botConfigs.UvalSabId,
                    botConfigs.UvalVipId
                )
            };

            await Task.CompletedTask;
            return configuration;
        }
    }
}