using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;

namespace SkillzBot.Hub
{
    /// <summary>
    /// Channels_Data/hub.json: what the hub itself needs (public URL, Twitch application, root, bot account, ports)
    /// plus ChannelTemplate, the keys copied into every new channel's config (database, Riot key, proxy, defaults).
    /// Secrets live here once; a channel config gets its own copy at provisioning time.
    /// </summary>
    public sealed class HubConfig
    {
        public const string FileName = "hub.json";
        public static readonly string[] TemplateKeys =
        {
            "RiotApiToken", "YouTubeApiToken", "GPTApiToken", "MySQL_IP", "MySQL_Port", "MySQL_User", "MySQL_password",
            "ProxyUrl", "ProxyCorePath", "ProxyApplyTo", "ChatFilterLvl", "VipLimit", "ChatTransport", "SummonerRegion", "DiscordBotToken"
        };

        public int HubPort { get; set; } = 8080;
        public string ApiPublicUrl { get; set; }
        public string ApiClientId { get; set; }
        public string TApiClientSecret { get; set; }
        public string RootUser { get; set; }
        public string BotTwitchName { get; set; }
        public int ChannelPortBase { get; set; } = 8100;
        public JObject ChannelTemplate { get; set; } = new JObject();
        public string Path { get; private set; }

        public static HubConfig Load(string path)
        {
            var root = JObject.Parse(File.ReadAllText(path));
            var cfg = new HubConfig
            {
                Path = path,
                HubPort = root.Value<int?>("HubPort") ?? 8080,
                ApiPublicUrl = root.Value<string>("ApiPublicUrl")?.Trim().TrimEnd('/'),
                ApiClientId = root.Value<string>("ApiClientId")?.Trim(),
                TApiClientSecret = root.Value<string>("TApiClientSecret")?.Trim(),
                RootUser = root.Value<string>("RootUser")?.Trim().ToLowerInvariant(),
                BotTwitchName = root.Value<string>("BotTwitchName")?.Trim().ToLowerInvariant(),
                ChannelPortBase = root.Value<int?>("ChannelPortBase") ?? 8100,
                ChannelTemplate = root["ChannelTemplate"] as JObject ?? new JObject(),
            };
            return cfg;
        }

        public List<string> Missing()
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(ApiPublicUrl)) missing.Add("ApiPublicUrl");
            if (string.IsNullOrWhiteSpace(ApiClientId)) missing.Add("ApiClientId");
            if (string.IsNullOrWhiteSpace(TApiClientSecret)) missing.Add("TApiClientSecret");
            if (string.IsNullOrWhiteSpace(RootUser)) missing.Add("RootUser");
            if (string.IsNullOrWhiteSpace(BotTwitchName)) missing.Add("BotTwitchName");
            if (string.IsNullOrWhiteSpace(ChannelTemplate.Value<string>("RiotApiToken"))) missing.Add("ChannelTemplate.RiotApiToken");
            if (string.IsNullOrWhiteSpace(ChannelTemplate.Value<string>("MySQL_IP"))) missing.Add("ChannelTemplate.MySQL_IP");
            return missing;
        }

        /// <summary>Builds hub.json from an existing channel config, so a single-channel install migrates without retyping secrets.</summary>
        public static JObject FromChannelConfig(JObject channel, int hubPort)
        {
            var template = new JObject();
            foreach (var key in TemplateKeys)
                if (channel.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out var v)) template[key] = v.DeepClone();
            string clientId = channel.Value<string>("ApiClientId");
            if (string.IsNullOrWhiteSpace(clientId)) clientId = channel.Value<string>("TApiClientId");
            return new JObject
            {
                ["HubPort"] = hubPort,
                ["ApiPublicUrl"] = channel.Value<string>("ApiPublicUrl") ?? "",
                ["ApiClientId"] = clientId ?? "",
                ["TApiClientSecret"] = channel.Value<string>("TApiClientSecret") ?? "",
                ["RootUser"] = channel.Value<string>("RootUser") ?? "",
                ["BotTwitchName"] = channel.Value<string>("BotTwitchName") ?? "",
                ["ChannelPortBase"] = 8100,
                ["ChannelTemplate"] = template,
            };
        }
    }
}
