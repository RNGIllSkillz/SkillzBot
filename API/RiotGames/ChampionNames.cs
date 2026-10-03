using Camille.Enums;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.API.RiotGames
{
    /// <summary>
    /// Champion id to display name ("Nunu &amp; Willump"), loaded from Riot's public Data Dragon,
    /// cached on disk for offline starts, and refreshed daily so new champions resolve.
    /// Falls back to a humanized enum identifier when nothing else is available.
    /// </summary>
    public sealed class ChampionNames
    {
        public const string HttpClientName = "DataDragon";
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);
        private static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);

        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<ChampionNames> _logger;
        private readonly string _cachePath;
        private readonly SemaphoreSlim _loadLock = new SemaphoreSlim(1, 1);

        private volatile Dictionary<int, string> _names;
        private DateTime _loadedAtUtc = DateTime.MinValue;
        private DateTime _lastAttemptUtc = DateTime.MinValue;

        public ChampionNames(IHttpClientFactory httpFactory, IPathProvider paths, ILogger<ChampionNames> logger)
        {
            _httpFactory = httpFactory;
            _logger = logger;
            _cachePath = Path.Combine(paths.SharedPath, "champions.json");
            LoadCacheFile();
        }

        public int Count => _names?.Count ?? 0;

        public async ValueTask<string> GetNameAsync(Champion champion, CancellationToken ct = default)
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            int id = (int)champion;
            var names = _names;
            if (names != null && names.TryGetValue(id, out var name)) return name;
            return Humanize(champion.ToString());
        }

        public async Task EnsureLoadedAsync(CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            if (_names != null && now - _loadedAtUtc < RefreshInterval) return;
            if (now - _lastAttemptUtc < RetryInterval) return;

            if (!await _loadLock.WaitAsync(0, ct).ConfigureAwait(false)) return;
            try
            {
                _lastAttemptUtc = DateTime.UtcNow;
                var client = _httpFactory.CreateClient(HttpClientName);
                var versions = JArray.Parse(await client.GetStringAsync("https://ddragon.leagueoflegends.com/api/versions.json", ct).ConfigureAwait(false));
                string version = versions[0].ToString();
                var json = JObject.Parse(await client.GetStringAsync($"https://ddragon.leagueoflegends.com/cdn/{version}/data/en_US/champion.json", ct).ConfigureAwait(false));

                var map = new Dictionary<int, string>();
                foreach (var champ in json["data"].Children<JProperty>())
                {
                    var key = champ.Value["key"]?.ToString();
                    var name = champ.Value["name"]?.ToString();
                    if (int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && !string.IsNullOrEmpty(name))
                        map[id] = name;
                }
                if (map.Count < 100) throw new InvalidOperationException($"Data Dragon returned only {map.Count} champions");

                _names = map;
                _loadedAtUtc = DateTime.UtcNow;
                File.WriteAllText(_cachePath, new JObject(map.Select(kv => new JProperty(kv.Key.ToString(CultureInfo.InvariantCulture), kv.Value))).ToString());
                _logger.LogInformation("Champion names loaded from Data Dragon {Version}: {Count} champions.", version, map.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not refresh champion names from Data Dragon; using {Count} cached names.", Count);
            }
            finally
            {
                _loadLock.Release();
            }
        }

        private void LoadCacheFile()
        {
            try
            {
                if (!File.Exists(_cachePath)) return;
                var json = JObject.Parse(File.ReadAllText(_cachePath));
                var map = new Dictionary<int, string>();
                foreach (var prop in json.Properties())
                    if (int.TryParse(prop.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int id))
                        map[id] = prop.Value.ToString();
                if (map.Count > 0) _names = map;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Champion name cache at {Path} is unreadable.", _cachePath);
            }
        }

        /// <summary>"NUNUWILLUMP" -> "Nunuwillump"; good enough as a last resort.</summary>
        public static string Humanize(string enumName)
        {
            if (string.IsNullOrEmpty(enumName)) return "?";
            var lower = enumName.ToLowerInvariant();
            return char.ToUpperInvariant(lower[0]) + lower.Substring(1);
        }
    }
}
