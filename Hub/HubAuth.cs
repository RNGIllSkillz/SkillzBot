using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SkillzBot.Api;
using SkillzBot.Services.Twitch;
using System;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace SkillzBot.Hub
{
    /// <summary>
    /// The hub's Twitch OAuth flows, all through one callback: panel login (no scopes), a streamer onboarding or
    /// re-authorizing their channel (broadcaster scopes), and root authorizing the bot account (bot scopes).
    /// Hub roles are only root and user; a channel decides admin/editor for itself behind the proxy.
    /// </summary>
    public sealed class HubAuth
    {
        public const string RoleRoot = "root";
        public const string RoleUser = "user";
        private const string StateCookie = "skillzbot.hub.oauth";

        private readonly HubConfig _hub;
        private readonly ChannelRegistry _registry;
        private readonly ChannelProvisioner _provisioner;
        private readonly ChannelSupervisor _supervisor;
        private readonly HubProxy _proxy;
        private readonly TwitchTokenService _tokens;
        private readonly IHttpClientFactory _http;
        private readonly ILogger<HubAuth> _logger;

        public HubAuth(HubConfig hub, ChannelRegistry registry, ChannelProvisioner provisioner, ChannelSupervisor supervisor, HubProxy proxy, TwitchTokenService tokens, IHttpClientFactory http, ILogger<HubAuth> logger)
        {
            _hub = hub; _registry = registry; _provisioner = provisioner; _supervisor = supervisor; _proxy = proxy; _tokens = tokens; _http = http; _logger = logger;
        }

        private string RedirectUri => _hub.ApiPublicUrl + "/api/auth/callback";
        public static bool IsRoot(ClaimsPrincipal user) => user?.IsInRole(RoleRoot) == true;

        public Task Login(HttpContext ctx) => Start(ctx, "login", "", SafeReturn(ctx.Request.Query["returnTo"]), "", false);
        public Task Onboard(HttpContext ctx) => Start(ctx, "onboard", "", "/", string.Join(" ", TwitchTokenService.BroadcasterScopes), true);
        public Task AuthorizeBot(HttpContext ctx) => Start(ctx, "bot", "", "/", string.Join(" ", TwitchTokenService.BotScopes), true);

        public Task Grant(HttpContext ctx)
        {
            string channel = ctx.Request.Query["channel"].ToString().ToLowerInvariant();
            var entry = _registry.Get(channel);
            if (entry == null) { ctx.Response.StatusCode = 404; return ctx.Response.WriteAsJsonAsync(new { error = "unknown channel" }); }
            string userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!IsRoot(ctx.User) && userId != entry.BroadcasterId) { ctx.Response.StatusCode = 403; return ctx.Response.WriteAsJsonAsync(new { error = "only the channel's broadcaster or root may re-authorize it" }); }
            return Start(ctx, "grant", channel, $"/c/{channel}/twitch", string.Join(" ", TwitchTokenService.BroadcasterScopes), true);
        }

        private Task Start(HttpContext ctx, string kind, string param, string returnTo, string scopes, bool forceVerify)
        {
            string state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            ctx.Response.Cookies.Append(StateCookie, $"{state}|{kind}|{param}|{returnTo}", new CookieOptions
            {
                HttpOnly = true, Secure = ctx.Request.IsHttps, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromMinutes(10), Path = "/api/auth"
            });
            ctx.Response.Redirect(TwitchOAuthClient.AuthorizeUrl(_hub.ApiClientId, RedirectUri, scopes, state, forceVerify));
            return Task.CompletedTask;
        }

        public async Task Callback(HttpContext ctx)
        {
            string cookie = ctx.Request.Cookies[StateCookie];
            ctx.Response.Cookies.Delete(StateCookie, new CookieOptions { Path = "/api/auth" });
            string state = ctx.Request.Query["state"].ToString(), code = ctx.Request.Query["code"].ToString();
            if (string.IsNullOrEmpty(cookie) || string.IsNullOrEmpty(state) || !cookie.StartsWith(state + "|", StringComparison.Ordinal)) { ctx.Response.Redirect("/?auth=state"); return; }
            var parts = cookie.Split('|', 4);
            string kind = parts[1], param = parts[2], returnTo = parts.Length > 3 ? parts[3] : "/";
            string Fail(string reason) => kind == "login" ? "/?auth=" + reason : (kind == "grant" ? returnTo + "?grant=" + reason : "/?grant=" + reason);
            if (string.IsNullOrEmpty(code)) { ctx.Response.Redirect(Fail("denied")); return; }

            try
            {
                var client = _http.CreateClient(TwitchAuth.HttpClientName);
                var (grant, error) = await TwitchOAuthClient.ExchangeAsync(client, _hub.ApiClientId, _hub.TApiClientSecret, code, RedirectUri);
                if (grant == null) { _logger.LogWarning("[Hub] {Kind} failed: {Error}", kind, error); ctx.Response.Redirect(Fail("token")); return; }

                switch (kind)
                {
                    case "login":
                        await TwitchOAuthClient.RevokeQuietlyAsync(client, _hub.ApiClientId, grant.AccessToken);
                        await SignInAsync(ctx, grant.UserId, grant.Login);
                        ctx.Response.Redirect(returnTo);
                        return;

                    case "bot":
                        if (!IsRoot(ctx.User)) { ctx.Response.Redirect("/?grant=forbidden"); return; }
                        if (!string.Equals(grant.Login, _hub.BotTwitchName, StringComparison.OrdinalIgnoreCase))
                        {
                            await TwitchOAuthClient.RevokeQuietlyAsync(client, _hub.ApiClientId, grant.AccessToken);
                            ctx.Response.Redirect("/?grant=wrongbot&login=" + Uri.EscapeDataString(grant.Login ?? "")); return;
                        }
                        await _tokens.StoreAuthorizationAsync(TwitchIdentity.Bot, grant.AccessToken, grant.RefreshToken, grant.ExpiresIn, grant.Scopes, grant.UserId, grant.Login, _hub.ApiClientId);
                        ctx.Response.Redirect("/?grant=bot-ok");
                        return;

                    case "grant":
                    {
                        var entry = _registry.Get(param);
                        if (entry == null) { ctx.Response.Redirect("/?grant=unknown"); return; }
                        if (grant.UserId != entry.BroadcasterId)
                        {
                            await TwitchOAuthClient.RevokeQuietlyAsync(client, _hub.ApiClientId, grant.AccessToken);
                            ctx.Response.Redirect(returnTo + "?grant=wronguser&login=" + Uri.EscapeDataString(grant.Login ?? "")); return;
                        }
                        await DeliverBroadcasterTokenAsync(entry, grant);
                        ctx.Response.Redirect(returnTo + "?grant=ok&identity=broadcaster");
                        return;
                    }

                    case "onboard":
                    {
                        string login = (grant.Login ?? "").ToLowerInvariant();
                        if (!IsValidChannelLogin(login) || !IsTwitchId(grant.UserId))
                        {
                            // "hub" and "_shared" are the hub's own folders; anything else odd is not worth a folder either
                            await TwitchOAuthClient.RevokeQuietlyAsync(client, _hub.ApiClientId, grant.AccessToken);
                            _logger.LogWarning("[Hub] onboarding refused for login {Login} ({Id}): reserved or malformed.", grant.Login, grant.UserId);
                            ctx.Response.Redirect("/?grant=reserved&login=" + Uri.EscapeDataString(grant.Login ?? "")); return;
                        }
                        var existing = _registry.GetByBroadcaster(grant.UserId);
                        if (existing == null)
                        {
                            var byLogin = _registry.Get(login);
                            if (byLogin != null && IsTwitchId(byLogin.BroadcasterId))
                            {
                                // the same login registered for another Twitch user id: a recycled name or a wrong import; root sorts it out by hand
                                await TwitchOAuthClient.RevokeQuietlyAsync(client, _hub.ApiClientId, grant.AccessToken);
                                _logger.LogWarning("[Hub] onboarding refused: channel {Login} belongs to broadcaster {Owner}, but {Id} authorized.", login, byLogin.BroadcasterId, grant.UserId);
                                ctx.Response.Redirect("/?grant=conflict&login=" + Uri.EscapeDataString(login)); return;
                            }
                            if (byLogin != null)
                            {
                                // imported before the hub knew the owner's id (empty or placeholder BrodcasterId): the login's owner claims it
                                _registry.SetBroadcasterId(byLogin.Login, grant.UserId);
                                _provisioner.SetBroadcasterId(byLogin.Login, grant.UserId);
                                _logger.LogWarning("[Hub] channel {Login} claimed by its broadcaster {Id} (the import had no valid id).", login, grant.UserId);
                                existing = _registry.Get(login);
                                if (existing.Enabled) _ = _supervisor.RestartAsync(existing.Login); // the running process still has the old id in its config
                            }
                        }
                        if (existing != null)
                        {
                            await DeliverBroadcasterTokenAsync(existing, grant);
                            if (!existing.Enabled)
                            {
                                if (!IsRoot(ctx.User)) { ctx.Response.Redirect("/?grant=disabled&login=" + Uri.EscapeDataString(existing.Login)); return; } // root switched it off; only root switches it on
                                _registry.SetEnabled(existing.Login, true); _supervisor.Ensure(existing.Login);
                            }
                            ctx.Response.Redirect($"/c/{existing.Login}/twitch?grant=ok&identity=broadcaster");
                            return;
                        }
                        string by = ctx.User?.Identity?.Name ?? grant.Login;
                        var entry = _provisioner.Provision(login, grant.Login, grant.UserId, by);
                        _provisioner.WriteBroadcasterToken(entry.Login, grant);
                        _supervisor.Ensure(entry.Login);
                        _logger.LogWarning("[Hub] channel {Login} onboarded by {By}.", entry.Login, by);
                        if (ctx.User?.Identity?.IsAuthenticated != true) await SignInAsync(ctx, grant.UserId, grant.Login);
                        ctx.Response.Redirect($"/c/{entry.Login}/?onboarded=1");
                        return;
                    }
                }
                ctx.Response.Redirect("/?auth=state");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Hub] OAuth callback ({Kind}) failed", kind);
                ctx.Response.Redirect(Fail("error"));
            }
        }

        /// <summary>
        /// Writes the token into the channel's store and tells the running process about it. A process that is still
        /// starting (port closed) gets the call retried in the background for a minute; it also picks the file up by
        /// itself within a minute, so the grant is never lost.
        /// </summary>
        private async Task DeliverBroadcasterTokenAsync(ChannelEntry entry, TwitchTokenGrant grant)
        {
            _provisioner.WriteBroadcasterToken(entry.Login, grant);
            if (await PushBroadcasterTokenAsync(entry, grant, 1)) return;
            _ = Task.Run(async () =>
            {
                for (int attempt = 2; attempt <= 20; attempt++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3));
                    if (await PushBroadcasterTokenAsync(entry, grant, attempt)) return;
                }
                _logger.LogWarning("[Hub] channel {Login} did not take the new broadcaster token over the API; it is in the channel's token file and will be picked up from there.", entry.Login);
            });
        }

        private async Task<bool> PushBroadcasterTokenAsync(ChannelEntry entry, TwitchTokenGrant grant, int attempt)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{entry.ApiPort}/api/internal/tokens/broadcaster");
                req.Headers.TryAddWithoutValidation(HubSignature.Header, _proxy.HubToken());
                req.Headers.TryAddWithoutValidation(ApiHost.CsrfHeader, ApiHost.CsrfValue);
                req.Content = new StringContent(JsonSerializer.Serialize(new { accessToken = grant.AccessToken, refreshToken = grant.RefreshToken, expiresIn = grant.ExpiresIn, scopes = grant.Scopes, userId = grant.UserId, login = grant.Login, clientId = _hub.ApiClientId }), System.Text.Encoding.UTF8, "application/json");
                using var resp = await _http.CreateClient(HubProxy.HttpClientName).SendAsync(req);
                if (resp.IsSuccessStatusCode) { _logger.LogInformation("[Hub] broadcaster token delivered to channel {Login} (attempt {Attempt}).", entry.Login, attempt); return true; }
                string body = await resp.Content.ReadAsStringAsync();
                _logger.LogWarning("[Hub] channel {Login} rejected the broadcaster token: HTTP {Status} {Body}", entry.Login, (int)resp.StatusCode, body.Length > 200 ? body.Substring(0, 200) : body);
                return (int)resp.StatusCode < 500 && resp.StatusCode != System.Net.HttpStatusCode.Unauthorized; // a 400 will not change by retrying; a 401 (secret rotated) or 5xx may
            }
            catch (Exception ex)
            {
                if (attempt == 1) _logger.LogInformation("[Hub] channel {Login} not reachable yet ({Message}); retrying in the background.", entry.Login, ex.Message);
                return false;
            }
        }

        private async Task SignInAsync(HttpContext ctx, string userId, string login)
        {
            var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
            identity.AddClaim(new Claim(ClaimTypes.Name, login));
            identity.AddClaim(new Claim(ClaimTypes.Role, string.Equals(login, _hub.RootUser, StringComparison.OrdinalIgnoreCase) ? RoleRoot : RoleUser));
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true, IssuedUtc = DateTimeOffset.UtcNow });
            _logger.LogInformation("[Hub] {Login} logged in.", login);
        }

        public async Task Logout(HttpContext ctx)
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            ctx.Response.StatusCode = 204;
        }

        private static string SafeReturn(string value) => TwitchAuth.SafeReturnPath(value);
        private static bool IsTwitchId(string id) => !string.IsNullOrEmpty(id) && id.All(char.IsAsciiDigit);
        private static bool IsValidChannelLogin(string login) => !string.IsNullOrEmpty(login) && login.Length <= 25 && login != "hub" && !login.StartsWith('_') && login.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');
    }
}
