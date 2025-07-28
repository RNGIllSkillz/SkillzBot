namespace SkillzBot.Singleton
{
    public class BotConfigModel
    {
        public string BotTwitchName { get; init; }
        public string BotTwitchAuth { get; init; }
        public string ChannelName { get; init; }
        public string TApiAccessToken { get; init; }
        public string TApiClientId { get; init; }
        public string StreamElementsApiToken { get; init; }
        public string StreamElementsID { get; init; }
        public string SummonerName { get; set; }
        public string YouTubeApiToken { get; init; }
        public string RiotApiToken { get; init; }
        public string BroadcasterId { get; init; }
        public string GPTApiToken { get; init; }
        public string DiscordBotToken { get; init; }
        public string RootUser { get; init; }
        public ulong DiscordNoteID { get; init; }
        public ulong DiscordSpamID { get; init; }

        public DatabaseConfig Database { get; init; }
        public FilePathsConfig FilePaths { get; init; }
        public ChannelIdsConfig ChannelIds { get; init; }

    }
}
