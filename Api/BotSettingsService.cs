using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog.Core;
using Serilog.Events;
using SkillzBot.IllConfiguration;
using SkillzBot.IllSkillzBot;
using SkillzBot.IllSkillzBot.Predictions;
using SkillzBot.Interfaces;
using SkillzBot.MODELS;
using SkillzBot.Services.Writers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace SkillzBot.Api
{
    /// <summary>
    /// Everything the web panel may change: bot state flags (with the same side effects as the
    /// chat commands), non-secret config keys, filter dictionaries, and the restart request.
    /// </summary>
    public sealed class BotSettingsService
    {
        /// <summary>Never returned by the API and never editable through it.</summary>
        public static readonly HashSet<string> SecretKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BotTwitchAuth", "TApiAccessToken", "TApiClientSecret", "StreamElementsApiToken", "YouTubeApiToken",
            "RiotApiToken", "GPTApiToken", "DiscordBotToken", "MySQL_password", "ProxyUrl"
        };
        /// <summary>Config keys that are bot settings rather than system settings; the only keys non-root roles see and may change.</summary>
        public static readonly HashSet<string> EditorConfigKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ChatFilterLvl", "VipLimit", "Summoner_Name", "SummonerRegion"
        };
        /// <summary>Config keys the running bot picks up without a restart.</summary>
        private static readonly HashSet<string> LiveConfigKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ChatFilterLvl" };

        private static readonly string[] StateReadOnly = { "QuizIsRunning", "InMatch", "BroadcasterIsOnline", "FirstQuizOfTheDay", "ActivePrediction", "ActivePoll", "LastPredictionPollUtc", "LiveSecondsBank", "LiveSinceUtc", "NextPollAfterLiveSec", "VipLastSyncUtc", "VipRegistrySeeded" };

        private readonly IBotStateService _botState;
        private readonly IGameStateService _gameState;
        private readonly ConfigWriterService _configWriter;
        private readonly LoggingLevelSwitch _levelSwitch;
        private readonly IllChatFilters _filters;
        private readonly IPathProvider _paths;
        private readonly BotConfigModel _config;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly ILogger<BotSettingsService> _logger;

        public BotSettingsService(IBotStateService botState, IGameStateService gameState, ConfigWriterService configWriter, LoggingLevelSwitch levelSwitch,
            IllChatFilters filters, IPathProvider paths, BotConfigModel config, IHostApplicationLifetime lifetime, ILogger<BotSettingsService> logger)
        {
            _botState = botState;
            _gameState = gameState;
            _configWriter = configWriter;
            _levelSwitch = levelSwitch;
            _filters = filters;
            _paths = paths;
            _config = config;
            _lifetime = lifetime;
            _logger = logger;
        }

        #region Bot state

        /// <summary>Applies a partial update. Returns the list of applied keys, or throws ArgumentException for a bad key or value.</summary>
        public async Task<List<string>> PatchStateAsync(Dictionary<string, JsonElement> patch, string by)
        {
            var applied = new List<string>();
            bool debugChanged = false, filterChanged = false;
            await _botState.UpdateStateAsync(s =>
            {
                foreach (var (key, value) in patch)
                {
                    if (StateReadOnly.Contains(key, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException($"{key} is read-only");
                    switch (key.ToLowerInvariant())
                    {
                        case "autopred": s.AutoPred = Bool(value, key); break;
                        case "predictionpollenabled": s.PredictionPollEnabled = Bool(value, key); break;
                        case "issilent": s.IsSilent = Bool(value, key); break;
                        case "issubactive": s.IsSubActive = Bool(value, key); break;
                        case "debug": s.Debug = Bool(value, key); debugChanged = true; break;
                        case "performancedebugmode": s.PerformanceDebugMode = Bool(value, key); break;
                        case "wisenabled": s.WisEnabled = Bool(value, key); break;
                        case "godmode": s.GodMode = Bool(value, key); break;
                        case "vipautorotate": s.VipAutoRotate = Bool(value, key); break;
                        case "chatfilterlvl": s.ChatFilterLvl = Int(value, key, 0, 5); filterChanged = true; break;
                        case "antibotprotectionlvl": s.AntiBotProtectionLvl = Int(value, key, 0, 2); break;
                        case "nextpredictionkey":
                            {
                                string k = value.ValueKind == JsonValueKind.Null ? null : value.GetString();
                                if (string.IsNullOrWhiteSpace(k) || k.Equals("winlose", StringComparison.OrdinalIgnoreCase)) s.NextPredictionKey = null;
                                else if (PredictionCatalog.Find(k) == null) throw new ArgumentException($"unknown prediction type {k}");
                                else s.NextPredictionKey = PredictionCatalog.Find(k).Key;
                                break;
                            }
                        default: throw new ArgumentException($"unknown or read-only state key {key}");
                    }
                    applied.Add(key);
                }
            });
            if (debugChanged) _levelSwitch.MinimumLevel = _botState.Current.Debug ? LogEventLevel.Debug : LogEventLevel.Information;
            if (filterChanged) await _configWriter.WriteAsync();
            _logger.LogInformation("[API] {By} changed state: {Keys}", by, string.Join(", ", patch.Select(kv => $"{kv.Key}={kv.Value}")));
            return applied;
        }

        private static bool Bool(JsonElement v, string key) => v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(v.GetString(), out var b) => b,
            _ => throw new ArgumentException($"{key} must be true or false")
        };

        private static int Int(JsonElement v, string key, int min, int max)
        {
            int n = v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i
                : v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var j) ? j
                : throw new ArgumentException($"{key} must be a number");
            if (n < min || n > max) throw new ArgumentException($"{key} must be between {min} and {max}");
            return n;
        }

        #endregion

        #region Config file

        /// <summary>
        /// Root sees every non-secret key (and which secrets are set); anyone else only the bot-setting keys in
        /// <see cref="EditorConfigKeys"/>, so system settings never leave the box for admins or editors.
        /// </summary>
        public async Task<ConfigViewDto> GetConfigAsync(bool isRoot)
        {
            var root = JObject.Parse(await File.ReadAllTextAsync(_paths.ConfigPath));
            var values = new Dictionary<string, object>();
            var secretsSet = new List<string>();
            foreach (var prop in root.Properties())
            {
                if (SecretKeys.Contains(prop.Name))
                {
                    if (isRoot && prop.Value.Type != JTokenType.Null && !string.IsNullOrEmpty(prop.Value.ToString())) secretsSet.Add(prop.Name);
                    continue;
                }
                if (!isRoot && !EditorConfigKeys.Contains(prop.Name)) continue;
                values[prop.Name] = prop.Value.Type switch
                {
                    JTokenType.Integer => (object)prop.Value.Value<long>(),
                    JTokenType.Float => prop.Value.Value<double>(),
                    JTokenType.Boolean => prop.Value.Value<bool>(),
                    JTokenType.Null => null,
                    _ => prop.Value.ToString()
                };
            }
            return new ConfigViewDto(values, isRoot ? SecretKeys.OrderBy(k => k).ToList() : Array.Empty<string>(), secretsSet, EditorConfigKeys.OrderBy(k => k).ToList());
        }

        /// <summary>Patches the channel config file. Returns true when a restart is needed for the change to apply.</summary>
        public async Task<bool> PatchConfigAsync(Dictionary<string, JsonElement> patch, bool isRoot, string by)
        {
            foreach (var key in patch.Keys)
            {
                if (SecretKeys.Contains(key)) throw new ArgumentException($"{key} is a secret and cannot be changed through the API");
                if (!isRoot && !EditorConfigKeys.Contains(key)) throw new ArgumentException($"{key} is a system setting; only root may change it");
            }
            var root = JObject.Parse(await File.ReadAllTextAsync(_paths.ConfigPath));
            bool restart = false;
            foreach (var (key, value) in patch)
            {
                root[key] = value.ValueKind switch
                {
                    JsonValueKind.Number when value.TryGetInt64(out var l) => new JValue(l),
                    JsonValueKind.Number => new JValue(value.GetDouble()),
                    JsonValueKind.True => new JValue(true),
                    JsonValueKind.False => new JValue(false),
                    JsonValueKind.Null => JValue.CreateNull(),
                    _ => new JValue(value.ToString().Trim('"'))
                };
                if (!LiveConfigKeys.Contains(key)) restart = true;
                if (key.Equals("ChatFilterLvl", StringComparison.OrdinalIgnoreCase) && value.TryGetInt32(out int lvl) && lvl >= 0 && lvl <= 5)
                    await _botState.UpdateStateAsync(s => s.ChatFilterLvl = lvl);
            }
            string tmp = _paths.ConfigPath + ".tmp";
            await File.WriteAllTextAsync(tmp, root.ToString(Formatting.Indented));
            File.Move(tmp, _paths.ConfigPath, true);
            _logger.LogWarning("[API] {By} changed config keys {Keys}{Restart}", by, string.Join(", ", patch.Keys), restart ? " (restart required)" : "");
            return restart;
        }

        #endregion

        #region Filter dictionaries

        /// <summary>The word lists behind the chat filter are root-only; other roles never see or save them.</summary>
        private static readonly HashSet<string> RootOnlyFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dic", "whitelist" };

        public static bool IsRootOnlyFilter(string name) => name != null && RootOnlyFilters.Contains(name);

        private IEnumerable<(string Name, string Title, bool Shared, string File)> FilterFiles()
        {
            var f = _config.FilePaths;
            yield return ("dic", "Запретные слова (dic.txt)", true, f.DicFileName);
            yield return ("whitelist", "Белый список слов", true, f.DicWhiteListFileName);
            yield return ("pichka", "Шаблоны ASCII/braille-арта", true, f.PichkaListFileName);
            yield return ("media", "Черный список треков (YouTube id)", true, f.MediaListFileName);
            yield return ("channels", "Черный список YouTube-каналов", true, f.ChannelListFileName);
            yield return ("userblacklist", "Пользователи без заказа треков (Twitch id)", false, f.UserBlacklistFileName);
        }

        public List<FilterListDto> GetFilters(bool isRoot)
        {
            var list = new List<FilterListDto>();
            foreach (var (name, title, shared, file) in FilterFiles())
            {
                if (!isRoot && IsRootOnlyFilter(name)) continue;
                string path = _paths.GetFullPath(file, shared);
                var lines = File.Exists(path) ? File.ReadAllLines(path).Select(l => l.TrimEnd('\r')).ToList() : new List<string>();
                list.Add(new FilterListDto(name, title, shared, lines));
            }
            return list;
        }

        public async Task<bool> SaveFilterAsync(string name, IEnumerable<string> lines, string by)
        {
            var entry = FilterFiles().FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (entry.Name == null) return false;
            string path = _paths.GetFullPath(entry.File, entry.Shared);
            var clean = lines.Select(l => (l ?? "").Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            string tmp = path + ".tmp";
            await File.WriteAllLinesAsync(tmp, clean);
            File.Move(tmp, path, true);
            _filters.ReloadFilters();
            _logger.LogInformation("[API] {By} saved filter list {Name} ({Count} lines).", by, entry.Name, clean.Count);
            return true;
        }

        public void ReloadFilters() => _filters.ReloadFilters();

        #endregion

        #region Logs and restart

        public async Task<string[]> TailLogAsync(string which, int lines)
        {
            string prefix = which == "errors" ? "errors-" : "bot-";
            string path = Path.Combine(_paths.DataPath, "logs", $"{prefix}{DateTime.Now:yyyyMMdd}.log");
            if (!File.Exists(path)) return Array.Empty<string>();
            lines = Math.Clamp(lines, 10, 5000);
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // Read only the tail of the file: enough bytes for the requested lines, generously.
            long want = Math.Min(fs.Length, (long)lines * 400);
            fs.Seek(fs.Length - want, SeekOrigin.Begin);
            using var reader = new StreamReader(fs);
            var text = await reader.ReadToEndAsync();
            var all = text.Split('\n');
            int start = Math.Max(want < fs.Length ? 1 : 0, all.Length - lines - 1);
            return all.Skip(start).Where(l => l.Length > 0).Select(l => l.TrimEnd('\r')).ToArray();
        }

        /// <summary>Stops the host cleanly; the launcher is expected to start a new instance.</summary>
        public void RequestRestart(string by)
        {
            _logger.LogWarning("[API] Restart requested by {By}; shutting down for the launcher to start a fresh instance.", by);
            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                _lifetime.StopApplication();
            });
        }

        #endregion
    }
}
