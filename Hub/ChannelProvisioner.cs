using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SkillzBot.Services.Twitch;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SkillzBot.Hub
{
    /// <summary>
    /// Creates a channel's folder, config and token store from the hub template, and imports channel folders that
    /// predate the hub (a single-channel install) into the registry on first start.
    /// </summary>
    public sealed class ChannelProvisioner
    {
        private readonly HubConfig _hub;
        private readonly ChannelRegistry _registry;
        private readonly string _channelsDir;
        private readonly ILogger<ChannelProvisioner> _logger;

        public ChannelProvisioner(HubConfig hub, ChannelRegistry registry, string channelsDir, ILogger<ChannelProvisioner> logger)
        {
            _hub = hub; _registry = registry; _channelsDir = channelsDir; _logger = logger;
        }

        public string DataDir(string login) => Path.Combine(_channelsDir, login, "DATA");
        public string ConfigPath(string login) => Path.Combine(DataDir(login), login + ".json");

        /// <summary>Folder + config from the template; existing keys in an existing config are kept (only missing ones are added).</summary>
        public ChannelEntry Provision(string login, string displayName, string broadcasterId, string addedBy)
        {
            login = login.ToLowerInvariant();
            var entry = _registry.Add(login, displayName, broadcasterId, addedBy);
            Directory.CreateDirectory(Path.Combine(DataDir(login), "logs"));
            string path = ConfigPath(login);
            JObject cfg = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();
            foreach (var kv in BuildConfig(entry))
                if (!cfg.ContainsKey(kv.Key)) cfg[kv.Key] = kv.Value;
            WriteJson(path, cfg, secret: true); // the channel config carries the app secret and the database password
            _logger.LogInformation("[Hub] channel {Login} provisioned (port {Port}, broadcaster {Id}).", login, entry.ApiPort, broadcasterId);
            return entry;
        }

        /// <summary>Writes the broadcaster token into the channel's own store so its process owns and refreshes it.</summary>
        public void WriteBroadcasterToken(string login, TwitchTokenGrant grant)
        {
            string path = Path.Combine(DataDir(login), TwitchTokenService.FileName);
            JObject store = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();
            store["broadcaster"] = new JObject
            {
                ["userId"] = grant.UserId, ["login"] = grant.Login, ["clientId"] = _hub.ApiClientId, ["accessToken"] = grant.AccessToken,
                ["refreshToken"] = grant.RefreshToken, ["scopes"] = new JArray(grant.Scopes), ["expiresUtc"] = DateTime.UtcNow.AddSeconds(Math.Max(60, grant.ExpiresIn)),
                ["obtainedUtc"] = DateTime.UtcNow,
            };
            WriteJson(path, store, secret: true);
        }

        private JObject BuildConfig(ChannelEntry entry)
        {
            var cfg = (JObject)_hub.ChannelTemplate.DeepClone();
            cfg["ChannelName"] = entry.Login;
            cfg["BrodcasterId"] = entry.BroadcasterId;
            cfg["RootUser"] = _hub.RootUser;
            cfg["BotTwitchName"] = _hub.BotTwitchName;
            cfg["ApiClientId"] = _hub.ApiClientId;
            cfg["TApiClientId"] = _hub.ApiClientId;
            cfg["TApiClientSecret"] = _hub.TApiClientSecret;
            cfg["ApiPublicUrl"] = _hub.ApiPublicUrl;
            cfg["ApiPort"] = entry.ApiPort;
            cfg["BotTwitchAuth"] = ""; cfg["TApiAccessToken"] = "";
            cfg["StreamElementsApiToken"] = cfg["StreamElementsApiToken"] ?? ""; cfg["StreamElementsID"] = cfg["StreamElementsID"] ?? "";
            cfg["Summoner_Name"] = cfg["Summoner_Name"] ?? "";
            if (cfg["ChatFilterLvl"] == null) cfg["ChatFilterLvl"] = 3;
            if (cfg["VipLimit"] == null) cfg["VipLimit"] = 100;
            return cfg;
        }

        /// <summary>
        /// First start of the hub on a box that already has channel folders: register each of them. The hub's own
        /// hub.json is written from the first channel's config when missing, so secrets are not retyped.
        /// </summary>
        public List<string> ImportExisting()
        {
            var imported = new List<string>();
            if (!Directory.Exists(_channelsDir)) return imported;
            foreach (var dir in Directory.GetDirectories(_channelsDir))
            {
                string login = Path.GetFileName(dir);
                if (login.StartsWith("_") || login.Equals("hub", StringComparison.OrdinalIgnoreCase)) continue;
                string path = ConfigPath(login);
                if (!File.Exists(path)) continue;
                if (_registry.Get(login) != null || _registry.IsRemoved(login)) continue;
                if (login != login.ToLowerInvariant())
                {
                    // the registry and ENV_CHANNEL_NAME are lower-case; a process could not find this folder
                    _logger.LogError("[Hub] channel folder {Login} is not lower-case; rename it to {Lower} to have it imported.", login, login.ToLowerInvariant());
                    continue;
                }
                try
                {
                    var cfg = JObject.Parse(File.ReadAllText(path));
                    string broadcasterId = cfg.Value<string>("BrodcasterId")?.Trim();
                    if (string.IsNullOrEmpty(broadcasterId) || !broadcasterId.All(char.IsAsciiDigit))
                    {
                        _logger.LogWarning("[Hub] channel {Login} has no valid BrodcasterId ({Value}); its owner claims it by adding the bot on the hub page.", login, broadcasterId ?? "");
                        broadcasterId = "";
                    }
                    var entry = _registry.Add(login, login, broadcasterId, "import");
                    // The hub owns the public URL, the Twitch application and the port; the process listens where the hub tells it to.
                    cfg["ApiPublicUrl"] = _hub.ApiPublicUrl; cfg["ApiPort"] = entry.ApiPort;
                    cfg["ApiClientId"] = _hub.ApiClientId; cfg["TApiClientSecret"] = _hub.TApiClientSecret;
                    WriteJson(path, cfg, secret: true);
                    imported.Add(login);
                    _logger.LogWarning("[Hub] imported existing channel {Login} (broadcaster {Id}) on port {Port}.", login, broadcasterId ?? "?", entry.ApiPort);
                }
                catch (Exception ex) { _logger.LogError(ex, "[Hub] could not import channel folder {Login}.", login); }
            }
            return imported;
        }

        /// <summary>
        /// Takes (reads and removes) a channel's bot token record, left by a pre-hub install that had authorized the bot
        /// account on the panel. The hub owns that token from now on; two refreshers would invalidate each other.
        /// </summary>
        public JObject TakeBotRecordFromChannel(string login)
        {
            string path = Path.Combine(DataDir(login), TwitchTokenService.FileName);
            if (!File.Exists(path)) return null;
            var store = JObject.Parse(File.ReadAllText(path));
            var bot = store["bot"] as JObject;
            if (bot == null) return null;
            store.Remove("bot");
            WriteJson(path, store, secret: true);
            return bot;
        }

        /// <summary>Records the real owner of an imported channel whose config carried no valid id.</summary>
        public void SetBroadcasterId(string login, string broadcasterId)
        {
            string path = ConfigPath(login);
            if (!File.Exists(path)) return;
            var cfg = JObject.Parse(File.ReadAllText(path));
            cfg["BrodcasterId"] = broadcasterId;
            WriteJson(path, cfg, secret: true);
        }

        public static void WriteJson(string path, JObject obj, bool secret = false)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, obj.ToString(Formatting.Indented));
            if (secret) { try { if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { } }
            File.Move(tmp, path, true);
        }
    }
}
