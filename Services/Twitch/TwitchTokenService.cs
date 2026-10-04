using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkillzBot.IllConfiguration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Services.Twitch
{
    /// <summary>The two Twitch accounts the bot acts as.</summary>
    public enum TwitchIdentity
    {
        /// <summary>The streamer: predictions, polls, rewards, VIPs, moderation and EventSub run under this account.</summary>
        Broadcaster,
        /// <summary>The bot account: chat (IRC) and whispers.</summary>
        Bot
    }

    /// <summary>A token issued to our own application through the panel, persisted in DATA/twitch-tokens.json.</summary>
    public sealed class TwitchTokenRecord
    {
        public string UserId { get; set; }
        public string Login { get; set; }
        public string ClientId { get; set; }
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public List<string> Scopes { get; set; } = new List<string>();
        public DateTime ExpiresUtc { get; set; }
        public DateTime ObtainedUtc { get; set; }
        public DateTime? LastRefreshUtc { get; set; }
    }

    /// <summary>What a Twitch client needs to call Helix or IRC as one identity, plus where it came from.</summary>
    public sealed record TwitchCredential(TwitchIdentity Identity, string ClientId, string AccessToken, string UserId, string Login, IReadOnlyList<string> Scopes, string Source);

    /// <summary>Panel view of one identity.</summary>
    public sealed record TwitchTokenStatus(string Identity, string Source, string Login, string UserId, string ExpectedLogin, string ExpectedUserId,
        IReadOnlyList<string> Scopes, IReadOnlyList<string> RequiredScopes, IReadOnlyList<string> MissingScopes,
        DateTime? ExpiresUtc, double? ExpiresInSeconds, DateTime? LastRefreshUtc, string LastError, bool Refreshable, bool Valid);

    /// <summary>
    /// Owns the Twitch tokens of both identities. Tokens granted through the panel (authorization code flow against
    /// our application) are stored with their refresh token and renewed before they expire; the static tokens from the
    /// channel config (TApiAccessToken, BotTwitchAuth) remain as a fallback for an identity that was never authorized.
    /// Consumers attach a callback and receive the current credential immediately and after every change, so a
    /// refreshed token reaches TwitchLib without a restart.
    /// </summary>
    public sealed class TwitchTokenService
    {
        public const string HttpClientName = "TwitchTokens";
        public const string FileName = "twitch-tokens.json";

        /// <summary>Everything the bot does on the streamer's behalf, including the EventSub subscriptions it opens.</summary>
        public static readonly string[] BroadcasterScopes =
        {
            "channel:manage:predictions", "channel:manage:polls", "channel:manage:redemptions", "channel:read:redemptions",
            "channel:manage:vips", "channel:manage:moderators", "channel:moderate", "moderator:manage:banned_users",
            "moderator:manage:chat_messages", "moderator:read:chatters", "moderator:manage:announcements",
            "moderator:read:chat_settings", "user:read:chat", "bits:read", "clips:edit", "channel:bot"
        };

        /// <summary>Chat in and out, plus whispers, from the bot account.</summary>
        public static readonly string[] BotScopes =
        {
            "chat:read", "chat:edit", "user:read:chat", "user:write:chat", "user:bot", "user:manage:whispers"
        };

        private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(20);
        private static readonly TimeSpan UnauthorizedRefreshCooldown = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan RevalidateEvery = TimeSpan.FromHours(1);
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly BotConfigModel _config;
        private readonly IHttpClientFactory _http;
        private readonly ILogger<TwitchTokenService> _logger;
        private readonly string _filePath;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly Dictionary<TwitchIdentity, TwitchTokenRecord> _records = new Dictionary<TwitchIdentity, TwitchTokenRecord>();
        private readonly Dictionary<TwitchIdentity, TwitchCredential> _configCredentials = new Dictionary<TwitchIdentity, TwitchCredential>();
        private readonly Dictionary<TwitchIdentity, DateTime?> _configExpiresUtc = new Dictionary<TwitchIdentity, DateTime?>();
        private readonly Dictionary<TwitchIdentity, bool> _configValid = new Dictionary<TwitchIdentity, bool>();
        private readonly Dictionary<TwitchIdentity, string> _lastError = new Dictionary<TwitchIdentity, string>();
        private readonly Dictionary<TwitchIdentity, DateTime> _lastUnauthorizedRefresh = new Dictionary<TwitchIdentity, DateTime>();
        private readonly Dictionary<TwitchIdentity, List<Action<TwitchCredential>>> _listeners = new Dictionary<TwitchIdentity, List<Action<TwitchCredential>>>();
        private DateTime _lastRevalidationUtc = DateTime.MinValue;
        private bool _initialized;

        public TwitchTokenService(BotConfigModel config, IPathProvider paths, IHttpClientFactory http, ILogger<TwitchTokenService> logger)
        {
            _config = config;
            _http = http;
            _logger = logger;
            _filePath = Path.Combine(paths.DataPath, FileName);
            foreach (TwitchIdentity id in Enum.GetValues(typeof(TwitchIdentity))) _listeners[id] = new List<Action<TwitchCredential>>();
            BuildConfigFallbacks();
        }

        /// <summary>True when tokens can be refreshed, i.e. our application's id and secret are configured.</summary>
        public bool CanRefresh => !string.IsNullOrWhiteSpace(_config.ApiClientId) && !string.IsNullOrWhiteSpace(_config.TApiClientSecret);

        public static string[] RequiredScopes(TwitchIdentity identity) => identity == TwitchIdentity.Bot ? BotScopes : BroadcasterScopes;

        #region Current credential and listeners

        /// <summary>The credential to use right now: a panel-granted token first, the config token otherwise, null when there is neither.</summary>
        public TwitchCredential Current(TwitchIdentity identity)
        {
            lock (_records)
            {
                if (_records.TryGetValue(identity, out var r) && !string.IsNullOrEmpty(r.AccessToken))
                    return new TwitchCredential(identity, r.ClientId, r.AccessToken, r.UserId, r.Login, r.Scopes.AsReadOnly(), "oauth");
                return _configCredentials.TryGetValue(identity, out var c) ? c : null;
            }
        }

        public bool HasScope(TwitchIdentity identity, string scope)
        {
            var cred = Current(identity);
            return cred != null && cred.Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Registers a consumer: it is called with the current credential now (when there is one) and after every change.</summary>
        public void Attach(TwitchIdentity identity, Action<TwitchCredential> apply)
        {
            lock (_listeners) _listeners[identity].Add(apply);
            var current = Current(identity);
            if (current != null) apply(current);
        }

        private void Notify(TwitchIdentity identity)
        {
            var current = Current(identity);
            if (current == null) return;
            List<Action<TwitchCredential>> targets;
            lock (_listeners) targets = _listeners[identity].ToList();
            foreach (var t in targets)
            {
                try { t(current); }
                catch (Exception ex) { _logger.LogWarning(ex, "A token listener for {Identity} threw.", identity); }
            }
        }

        #endregion

        #region Startup and maintenance

        /// <summary>Loads the store, validates what is there (refreshing a dead access token), and learns what the config tokens are.</summary>
        public async Task InitializeAsync()
        {
            await _gate.WaitAsync();
            try
            {
                Load();
                foreach (TwitchIdentity id in Enum.GetValues(typeof(TwitchIdentity)))
                {
                    if (_records.TryGetValue(id, out var r))
                    {
                        var info = await ValidateAsync(r.AccessToken);
                        if (info == null)
                        {
                            _logger.LogWarning("[Tokens] {Identity}: stored access token is not valid any more; refreshing.", id);
                            await RefreshLockedAsync(id, "startup validation failed");
                        }
                        else if (info.Unreachable)
                        {
                            _logger.LogWarning("[Tokens] {Identity}: Twitch validate endpoint unreachable; keeping the stored token for now.", id);
                        }
                        else
                        {
                            r.Login = info.Login; r.UserId = info.UserId; r.Scopes = info.Scopes; r.ExpiresUtc = info.ExpiresUtc;
                            _logger.LogInformation("[Tokens] {Identity}: panel token for {Login}, {Left} left, {Count} scopes.", id, r.Login, Left(r.ExpiresUtc), r.Scopes.Count);
                        }
                    }
                    else if (_configCredentials.ContainsKey(id))
                    {
                        await RevalidateConfigAsync(id);
                    }
                    else
                    {
                        _logger.LogWarning("[Tokens] {Identity}: no token at all; authorize it on the panel's Twitch page.", id);
                    }
                }
                Save();
                _lastRevalidationUtc = DateTime.UtcNow;
                _initialized = true;
            }
            finally { _gate.Release(); }
            foreach (TwitchIdentity id in Enum.GetValues(typeof(TwitchIdentity))) Notify(id);
        }

        /// <summary>Called every minute: renews panel tokens that are close to expiry and re-checks config tokens hourly.</summary>
        public async Task MaintainAsync()
        {
            if (!_initialized) return;
            await _gate.WaitAsync();
            try
            {
                foreach (TwitchIdentity id in Enum.GetValues(typeof(TwitchIdentity)))
                {
                    if (_records.TryGetValue(id, out var r) && r.ExpiresUtc - DateTime.UtcNow < RefreshMargin)
                        await RefreshLockedAsync(id, $"expires in {Left(r.ExpiresUtc)}");
                }
                if (DateTime.UtcNow - _lastRevalidationUtc > RevalidateEvery)
                {
                    foreach (var id in _configCredentials.Keys.ToList())
                        if (!_records.ContainsKey(id)) await RevalidateConfigAsync(id);
                    _lastRevalidationUtc = DateTime.UtcNow;
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "[Tokens] maintenance pass failed."); }
            finally { _gate.Release(); }
        }

        /// <summary>A Helix call came back 401: refresh once, with a cooldown so a burst of failures does not hammer the token endpoint.</summary>
        public async Task<bool> HandleUnauthorizedAsync(TwitchIdentity identity)
        {
            lock (_records)
            {
                if (!_records.ContainsKey(identity)) return false;
                if (_lastUnauthorizedRefresh.TryGetValue(identity, out var last) && DateTime.UtcNow - last < UnauthorizedRefreshCooldown) return false;
                _lastUnauthorizedRefresh[identity] = DateTime.UtcNow;
            }
            await _gate.WaitAsync();
            try { return await RefreshLockedAsync(identity, "401 from Helix"); }
            finally { _gate.Release(); }
        }

        /// <summary>The credential, refreshed first when it is about to expire. For callers that connect once and keep the session, like IRC.</summary>
        public async Task<TwitchCredential> GetCredentialAsync(TwitchIdentity identity)
        {
            TwitchTokenRecord r;
            lock (_records) _records.TryGetValue(identity, out r);
            if (r != null && r.ExpiresUtc - DateTime.UtcNow < RefreshMargin)
            {
                await _gate.WaitAsync();
                try { await RefreshLockedAsync(identity, "requested near expiry"); }
                finally { _gate.Release(); }
            }
            return Current(identity);
        }

        #endregion

        #region Grants from the panel

        /// <summary>Stores a token the panel just obtained for this identity and pushes it to the consumers.</summary>
        public async Task StoreAuthorizationAsync(TwitchIdentity identity, string accessToken, string refreshToken, int expiresInSeconds, IEnumerable<string> scopes, string userId, string login, string clientId)
        {
            await _gate.WaitAsync();
            try
            {
                lock (_records)
                {
                    _records[identity] = new TwitchTokenRecord
                    {
                        UserId = userId, Login = login, ClientId = clientId, AccessToken = accessToken, RefreshToken = refreshToken,
                        Scopes = scopes?.ToList() ?? new List<string>(), ExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, expiresInSeconds)), ObtainedUtc = DateTime.UtcNow
                    };
                    _lastError.Remove(identity);
                }
                Save();
            }
            finally { _gate.Release(); }
            _logger.LogWarning("[Tokens] {Identity} authorized through the panel as {Login} ({UserId}) with {Count} scopes.", identity, login, userId, scopes?.Count() ?? 0);
            Notify(identity);
        }

        /// <summary>Revokes and forgets the panel token; the identity falls back to the config token, if any.</summary>
        public async Task RemoveAsync(TwitchIdentity identity, string by)
        {
            TwitchTokenRecord removed;
            await _gate.WaitAsync();
            try
            {
                lock (_records)
                {
                    _records.TryGetValue(identity, out removed);
                    _records.Remove(identity);
                    _lastError.Remove(identity);
                }
                Save();
            }
            finally { _gate.Release(); }
            if (removed != null)
            {
                try
                {
                    await _http.CreateClient(HttpClientName).PostAsync("https://id.twitch.tv/oauth2/revoke", new FormUrlEncodedContent(new Dictionary<string, string>
                    { ["client_id"] = removed.ClientId, ["token"] = removed.AccessToken }));
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Revoke failed; ignoring."); }
            }
            _logger.LogWarning("[Tokens] {By} removed the panel token of {Identity}; falling back to {Fallback}.", by, identity, _configCredentials.ContainsKey(identity) ? "the config token" : "nothing");
            Notify(identity);
        }

        #endregion

        #region Status

        public IReadOnlyList<TwitchTokenStatus> Describe()
        {
            var list = new List<TwitchTokenStatus>();
            foreach (TwitchIdentity id in Enum.GetValues(typeof(TwitchIdentity)))
            {
                var required = RequiredScopes(id);
                string expectedLogin = id == TwitchIdentity.Bot ? _config.BotTwitchName : _config.ChannelName;
                string expectedUserId = id == TwitchIdentity.Bot ? null : _config.BroadcasterId;
                TwitchTokenRecord r; TwitchCredential c; string err; DateTime? cfgExp; bool cfgValid;
                lock (_records)
                {
                    _records.TryGetValue(id, out r);
                    _configCredentials.TryGetValue(id, out c);
                    _lastError.TryGetValue(id, out err);
                    _configExpiresUtc.TryGetValue(id, out cfgExp);
                    _configValid.TryGetValue(id, out cfgValid);
                }
                if (r != null)
                {
                    var missing = required.Where(s => !r.Scopes.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
                    list.Add(new TwitchTokenStatus(id.ToString().ToLowerInvariant(), "oauth", r.Login, r.UserId, expectedLogin, expectedUserId,
                        r.Scopes.AsReadOnly(), required, missing, r.ExpiresUtc, Math.Max(0, (r.ExpiresUtc - DateTime.UtcNow).TotalSeconds), r.LastRefreshUtc,
                        err, CanRefresh && !string.IsNullOrEmpty(r.RefreshToken), r.ExpiresUtc > DateTime.UtcNow));
                }
                else if (c != null)
                {
                    var missing = required.Where(s => !c.Scopes.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
                    list.Add(new TwitchTokenStatus(id.ToString().ToLowerInvariant(), "config", c.Login, c.UserId, expectedLogin, expectedUserId,
                        c.Scopes, required, missing, cfgExp, cfgExp.HasValue ? Math.Max(0, (cfgExp.Value - DateTime.UtcNow).TotalSeconds) : null, null,
                        err, false, cfgValid));
                }
                else
                {
                    list.Add(new TwitchTokenStatus(id.ToString().ToLowerInvariant(), "none", null, null, expectedLogin, expectedUserId,
                        Array.Empty<string>(), required, required, null, null, null, err, false, false));
                }
            }
            return list;
        }

        /// <summary>One short token for the health line and the dashboard, e.g. "broadcaster=oauth 3h12m bot=config 2d".</summary>
        public string DescribeShort()
        {
            var parts = new List<string>();
            foreach (var s in Describe())
            {
                string left = s.ExpiresInSeconds.HasValue ? FormatLeft(TimeSpan.FromSeconds(s.ExpiresInSeconds.Value)) : "?";
                string flag = s.Source == "none" ? "NONE" : !s.Valid ? "INVALID" : s.MissingScopes.Count > 0 ? $"-{s.MissingScopes.Count}scopes" : "";
                parts.Add($"{s.Identity}={s.Source} {left}{(flag.Length > 0 ? " " + flag : "")}");
            }
            return string.Join(" ", parts);
        }

        #endregion

        #region Internals

        private void BuildConfigFallbacks()
        {
            if (!string.IsNullOrWhiteSpace(_config.TApiAccessToken))
                _configCredentials[TwitchIdentity.Broadcaster] = new TwitchCredential(TwitchIdentity.Broadcaster, _config.TApiClientId, _config.TApiAccessToken.Trim(), _config.BroadcasterId, _config.ChannelName, Array.Empty<string>(), "config");
            if (!string.IsNullOrWhiteSpace(_config.BotTwitchAuth))
            {
                string raw = _config.BotTwitchAuth.Trim();
                if (raw.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase)) raw = raw.Substring(6);
                _configCredentials[TwitchIdentity.Bot] = new TwitchCredential(TwitchIdentity.Bot, null, raw, null, _config.BotTwitchName, Array.Empty<string>(), "config");
            }
        }

        private async Task RevalidateConfigAsync(TwitchIdentity id)
        {
            TwitchCredential c;
            lock (_records) { if (!_configCredentials.TryGetValue(id, out c)) return; }
            var info = await ValidateAsync(c.AccessToken);
            if (info?.Unreachable == true) return;
            lock (_records)
            {
                if (info != null)
                {
                    _configCredentials[id] = c with { ClientId = info.ClientId ?? c.ClientId, UserId = info.UserId ?? c.UserId, Login = info.Login ?? c.Login, Scopes = info.Scopes.AsReadOnly() };
                    _configExpiresUtc[id] = info.ExpiresUtc;
                    _configValid[id] = true;
                }
                else
                {
                    _configValid[id] = false;
                    _lastError[id] = "the config token is not valid (expired or revoked); authorize this identity on the panel";
                }
            }
            if (info != null)
                _logger.LogInformation("[Tokens] {Identity}: config token for {Login}, {Left} left, {Count} scopes. Authorize it on the panel to get automatic renewal.", id, info.Login, Left(info.ExpiresUtc), info.Scopes.Count);
            else
                _logger.LogError("[Tokens] {Identity}: the config token is not valid. Authorize this identity on the panel's Twitch page.", id);
        }

        /// <summary>Must be called under _gate.</summary>
        private async Task<bool> RefreshLockedAsync(TwitchIdentity identity, string reason)
        {
            TwitchTokenRecord r;
            lock (_records) { if (!_records.TryGetValue(identity, out r)) return false; }
            if (!CanRefresh)
            {
                lock (_records) _lastError[identity] = "cannot refresh: ApiClientId / TApiClientSecret are not set";
                _logger.LogError("[Tokens] {Identity} needs a refresh ({Reason}) but ApiClientId/TApiClientSecret are not configured.", identity, reason);
                return false;
            }
            if (string.IsNullOrEmpty(r.RefreshToken))
            {
                lock (_records) _lastError[identity] = "no refresh token stored; authorize again";
                return false;
            }
            try
            {
                var client = _http.CreateClient(HttpClientName);
                using var response = await client.PostAsync("https://id.twitch.tv/oauth2/token", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = r.RefreshToken,
                    ["client_id"] = r.ClientId ?? _config.ApiClientId,
                    ["client_secret"] = _config.TApiClientSecret,
                }));
                string body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    lock (_records) _lastError[identity] = $"refresh failed: HTTP {(int)response.StatusCode} {Truncate(body)}";
                    _logger.LogError("[Tokens] {Identity} refresh failed ({Reason}): HTTP {Status} {Body}. Re-authorize it on the panel.", identity, reason, (int)response.StatusCode, Truncate(body));
                    return false;
                }
                using var json = JsonDocument.Parse(body);
                var root = json.RootElement;
                lock (_records)
                {
                    r.AccessToken = root.GetProperty("access_token").GetString();
                    if (root.TryGetProperty("refresh_token", out var rt) && rt.ValueKind == JsonValueKind.String) r.RefreshToken = rt.GetString();
                    int expiresIn = root.TryGetProperty("expires_in", out var ei) && ei.TryGetInt32(out int v) ? v : 14400;
                    r.ExpiresUtc = DateTime.UtcNow.AddSeconds(expiresIn);
                    if (root.TryGetProperty("scope", out var sc) && sc.ValueKind == JsonValueKind.Array)
                        r.Scopes = sc.EnumerateArray().Select(e => e.GetString()).Where(s => s != null).ToList();
                    r.LastRefreshUtc = DateTime.UtcNow;
                    _lastError.Remove(identity);
                }
                Save();
                _logger.LogInformation("[Tokens] {Identity} refreshed ({Reason}); valid for {Left}.", identity, reason, Left(r.ExpiresUtc));
                Notify(identity);
                return true;
            }
            catch (Exception ex)
            {
                lock (_records) _lastError[identity] = "refresh failed: " + ex.Message;
                _logger.LogError(ex, "[Tokens] {Identity} refresh threw ({Reason}).", identity, reason);
                return false;
            }
        }

        private sealed class ValidateInfo
        {
            public string ClientId; public string Login; public string UserId; public List<string> Scopes = new List<string>(); public DateTime ExpiresUtc;
            /// <summary>The validate endpoint could not be reached: say nothing about the token.</summary>
            public bool Unreachable;
        }

        private async Task<ValidateInfo> ValidateAsync(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken)) return null;
            try
            {
                var client = _http.CreateClient(HttpClientName);
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
                request.Headers.TryAddWithoutValidation("Authorization", "OAuth " + accessToken);
                using var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode) return null;
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = json.RootElement;
                var info = new ValidateInfo
                {
                    ClientId = root.TryGetProperty("client_id", out var cid) ? cid.GetString() : null,
                    Login = root.TryGetProperty("login", out var l) ? l.GetString()?.ToLowerInvariant() : null,
                    UserId = root.TryGetProperty("user_id", out var u) ? u.GetString() : null,
                };
                if (root.TryGetProperty("scopes", out var scopes) && scopes.ValueKind == JsonValueKind.Array)
                    info.Scopes = scopes.EnumerateArray().Select(e => e.GetString()).Where(s => s != null).ToList();
                int expiresIn = root.TryGetProperty("expires_in", out var ei) && ei.TryGetInt32(out int v) ? v : 0;
                info.ExpiresUtc = expiresIn > 0 ? DateTime.UtcNow.AddSeconds(expiresIn) : DateTime.UtcNow.AddYears(10);
                return info;
            }
            catch (Exception ex)
            {
                // Network trouble is not "invalid": keep whatever we have and let the next pass retry.
                _logger.LogWarning("[Tokens] validate call failed: {Message}", ex.Message);
                return new ValidateInfo { Unreachable = true };
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return;
                var data = JsonSerializer.Deserialize<Dictionary<string, TwitchTokenRecord>>(File.ReadAllText(_filePath), JsonOptions);
                if (data == null) return;
                lock (_records)
                {
                    foreach (var (key, rec) in data)
                        if (Enum.TryParse<TwitchIdentity>(key, true, out var id) && rec != null && !string.IsNullOrEmpty(rec.AccessToken)) _records[id] = rec;
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "[Tokens] could not read {File}; starting without panel tokens.", _filePath); }
        }

        private void Save()
        {
            try
            {
                Dictionary<string, TwitchTokenRecord> data;
                lock (_records) data = _records.ToDictionary(kv => kv.Key.ToString().ToLowerInvariant(), kv => kv.Value);
                string tmp = _filePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(data, JsonOptions));
                try { if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
                File.Move(tmp, _filePath, true);
            }
            catch (Exception ex) { _logger.LogError(ex, "[Tokens] could not write {File}.", _filePath); }
        }

        private static string Left(DateTime expiresUtc) => FormatLeft(expiresUtc - DateTime.UtcNow);
        private static string FormatLeft(TimeSpan t)
        {
            if (t <= TimeSpan.Zero) return "expired";
            if (t.TotalDays >= 365) return "no expiry";
            if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d{t.Hours}h";
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h{t.Minutes:D2}m";
            return $"{(int)t.TotalMinutes}m";
        }
        private static string Truncate(string s) => string.IsNullOrEmpty(s) ? "" : s.Length <= 200 ? s : s.Substring(0, 200) + "…";

        #endregion
    }

    /// <summary>Runs the token maintenance pass once a minute.</summary>
    public sealed class TwitchTokenRefresher : BackgroundService
    {
        private readonly TwitchTokenService _tokens;
        public TwitchTokenRefresher(TwitchTokenService tokens) { _tokens = tokens; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                    await _tokens.MaintainAsync();
            }
            catch (OperationCanceledException) { }
        }
    }
}
