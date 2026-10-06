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
                        var existing = _registry.GetByBroadcaster(grant.UserId);
                        if (existing != null)
                        {
                            await DeliverBroadcasterTokenAsync(existing, grant);
                            if (!existing.Enabled) { _registry.SetEnabled(existing.Login, true); _supervisor.Ensure(existing.Login); }
                            ctx.Response.Redirect($"/c/{existing.Login}/twitch?grant=ok&identity=broadcaster");
                            return;
                        }
                        string by = ctx.User?.Identity?.Name ?? grant.Login;
                        var entry = _provisioner.Provision(grant.Login, grant.Login, grant.UserId, by);
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

        /// <summary>Writes the token into the channel's store and tells a running process about it (it keeps the file otherwise).</summary>
        private async Task DeliverBroadcasterTokenAsync(ChannelEntry entry, TwitchTokenGrant grant)
        {
            _provisioner.WriteBroadcasterToken(entry.Login, grant);
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{entry.ApiPort}/api/internal/tokens/broadcaster");
                req.Headers.TryAddWithoutValidation(HubSignature.Header, _proxy.HubToken());
                req.Headers.TryAddWithoutValidation(ApiHost.CsrfHeader, ApiHost.CsrfValue);
                req.Content = new StringContent(JsonSerializer.Serialize(new { accessToken = grant.AccessToken, refreshToken = grant.RefreshToken, expiresIn = grant.ExpiresIn, scopes = grant.Scopes, userId = grant.UserId, login = grant.Login, clientId = _hub.ApiClientId }), System.Text.Encoding.UTF8, "application/json");
                using var resp = await _http.CreateClient(HubProxy.HttpClientName).SendAsync(req);
                _logger.LogInformation("[Hub] broadcaster token delivered to channel {Login}: HTTP {Status}.", entry.Login, (int)resp.StatusCode);
            }
            catch (Exception ex) { _logger.LogWarning("[Hub] channel {Login} not reachable to apply the new token now ({Message}); it will read it at the next start.", entry.Login, ex.Message); }
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

        private static string SafeReturn(string value) => string.IsNullOrEmpty(value) || !value.StartsWith('/') || value.StartsWith("//") ? "/" : value;
    }
}
