using SkillzBot.IllConfiguration;
using SkillzBot.Readers;
using System;
using System.Collections.Generic;

namespace SkillzBot.Configuration 
{
    public static class BotConfigurationFactory
    {
        public static BotConfigModel Create(string configPath)
        {
            // 1. Read JSON (Synchronous)
            var configReader = new Config(configPath);
            var botConfigs = configReader.GetBotConfigs();

            if (botConfigs == null)
                throw new InvalidOperationException("Configuration file is empty or invalid JSON.");

            // 2. Validate
            var missingFields = new List<string>();
            if (string.IsNullOrWhiteSpace(botConfigs.BotTwitchAuth)) missingFields.Add("BotTwitchAuth");
            if (string.IsNullOrWhiteSpace(botConfigs.ChannelName)) missingFields.Add("ChannelName");
            if (string.IsNullOrWhiteSpace(botConfigs.RiotApiToken)) missingFields.Add("RiotApiToken");
            if (string.IsNullOrWhiteSpace(botConfigs.RootUser)) missingFields.Add("RootUser");

            if (missingFields.Count > 0)
            {
                throw new InvalidOperationException($"Critical configuration missing: {string.Join(", ", missingFields)}");
            }

            // 3. Map to Model
            return new BotConfigModel
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
                // Twitch logins are lowercase; normalize so access checks and reward rules match.
                RootUser = botConfigs.RootUser.Trim().TrimStart('@').ToLowerInvariant(),

                VipLimit = botConfigs.VipLimit ?? 100,
                TApiClientSecret = botConfigs.TApiClientSecret?.Trim(),
                ChatTransport = string.IsNullOrWhiteSpace(botConfigs.ChatTransport) ? "auto" : botConfigs.ChatTransport.Trim().ToLowerInvariant(),
                ApiClientId = string.IsNullOrWhiteSpace(botConfigs.ApiClientId) ? botConfigs.TApiClientId?.Trim() : botConfigs.ApiClientId.Trim(),
                ApiPort = botConfigs.ApiPort ?? 8080,
                ApiPublicUrl = botConfigs.ApiPublicUrl?.Trim().TrimEnd('/'),
                ProxyUrl = botConfigs.ProxyUrl?.Trim(),
                ProxyCorePath = botConfigs.ProxyCorePath?.Trim(),
                ProxyApplyTo = string.IsNullOrWhiteSpace(botConfigs.ProxyApplyTo)
                    ? new[] { "youtube" }
                    : botConfigs.ProxyApplyTo.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),

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
                    "BotState.txt",
                    "mediaqueue.txt",
                    "Subscription.txt"
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
        }
    }
}