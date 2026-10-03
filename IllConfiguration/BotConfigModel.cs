namespace SkillzBot.IllConfiguration
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

        /// <summary>http://, socks5://, vless:// or hysteria2:// link; empty disables the proxy.</summary>
        public string ProxyUrl { get; init; }
        /// <summary>Path to the xray or hysteria binary (or its folder) for links that need a core.</summary>
        public string ProxyCorePath { get; init; }
        /// <summary>Which outbound clients use the proxy: youtube, riot, streamelements, mmr, all.</summary>
        public string[] ProxyApplyTo { get; init; }
        /// <summary>Number of VIP slots Twitch gives this channel; the bot frees one when it is reached.</summary>
        public int VipLimit { get; init; }

        public DatabaseConfig Database { get; init; }
        public FilePathsConfig FilePaths { get; init; }
        public ChannelIdsConfig ChannelIds { get; init; }

    }
}