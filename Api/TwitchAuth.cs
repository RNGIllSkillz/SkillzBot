using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SkillzBot.IllConfiguration;
using SkillzBot.Interfaces;
using SkillzBot.Services.Twitch;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace SkillzBot.Api
{
    /// <summary>
    /// Twitch OAuth (authorization code) against our own application, used for two things that share one callback:
    /// the panel login (no scopes, the token is dropped right after we learn who it is) and the scope grants
    /// (the streamer or the bot account authorizes the bot; the tokens go to <see cref="TwitchTokenService"/>).
    /// Who may log in is decided here: RootUser is root, the broadcaster is admin, logins in dbBotEditorTable are editors.
    /// </summary>
    public sealed class TwitchAuth
    {
        public const string RoleRoot = "root";
        public const string RoleAdmin = "admin";
        public const string RoleEditor = "editor";
        private const string StateCookie = "skillzbot.oauth";
        private const string GrantPrefix = "grant:";
        private const string GrantPage = "/twitch";
        public const string HttpClientName = "TwitchAuth";

        private readonly BotConfigModel _config;
        private readonly IAdminRepository _admin;
        private readonly IHttpClientFactory _http;
        private readonly TwitchTokenService _tokens;
        private readonly ILogger<TwitchAuth> _logger;

        public TwitchAuth(BotConfigModel config, IAdminRepository admin, IHttpClientFactory http, TwitchTokenService tokens, ILogger<TwitchAuth> logger)
        {
            _config = config;
            _admin = admin;
            _http = http;
            _tokens = tokens;
            _logger = logger;
        }

        public bool Configured => !string.IsNullOrWhiteSpace(_config.ApiClientId) && !string.IsNullOrWhiteSpace(_config.TApiClientSecret) && !string.IsNullOrWhiteSpace(_config.ApiPublicUrl);

        /// <summary>A channel run by the hub: the hub owns login and grants; this process only redirects there.</summary>
        public bool Managed => HubSignature.IsManagedProcess;
        private string HubUrl => (_config.ApiPublicUrl ?? "").TrimEnd('/');

        private string RedirectUri => _config.ApiPublicUrl.TrimEnd('/') + "/api/auth/callback";

        public Task Login(HttpContext ctx)
        {
            if (Managed)
            {
                string back = ctx.Request.Query["returnTo"].ToString();
                ctx.Response.Redirect(HubUrl + "/api/auth/login?returnTo=" + Uri.EscapeDataString(string.IsNullOrEmpty(back) ? "/c/" + _config.ChannelName + "/" : back));
                return Task.CompletedTask;
            }
            if (!Configured)
            {
                ctx.Response.StatusCode = 503;
                return ctx.Response.WriteAsync("Login is not configured: set ApiClientId, TApiClientSecret and ApiPublicUrl in the channel config.");
            }
            string returnTo = ctx.Request.Query["returnTo"].ToString();
            returnTo = SafeReturnPath(returnTo);
            string state = NewState();
            ctx.Response.Cookies.Append(StateCookie, state + "|" + returnTo, StateCookieOptions(ctx));
            ctx.Response.Redirect(AuthorizeUrl(state, ""));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Starts the scope grant for one identity. The callback stores the tokens instead of opening a session,
        /// and checks that the account which authorized is the one expected (the broadcaster, or the bot account).
        /// </summary>
        public Task Authorize(HttpContext ctx, TwitchIdentity identity)
        {
            if (Managed)
            {
                if (identity == TwitchIdentity.Bot)
                {
                    ctx.Response.StatusCode = 400;
                    return ctx.Response.WriteAsJsonAsync(new { error = "the bot account is authorized on the hub by root" });
                }
                ctx.Response.Redirect(HubUrl + "/api/hub/grant?channel=" + Uri.EscapeDataString(_config.ChannelName));
                return Task.CompletedTask;
            }
            if (!Configured)
            {
                ctx.Response.StatusCode = 503;
                return ctx.Response.WriteAsync("Set ApiClientId, TApiClientSecret and ApiPublicUrl in the channel config first.");
            }
            string state = NewState();
            ctx.Response.Cookies.Append(StateCookie, state + "|" + GrantPrefix + identity.ToString().ToLowerInvariant(), StateCookieOptions(ctx));
            string scopes = string.Join(" ", TwitchTokenService.RequiredScopes(identity));
            // force_verify makes Twitch show the account picker, so root can grant the bot identity from the bot account.
            ctx.Response.Redirect(AuthorizeUrl(state, scopes) + "&force_verify=true");
            return Task.CompletedTask;
        }

        public async Task Callback(HttpContext ctx)
        {
            string stateCookie = ctx.Request.Cookies[StateCookie];
            ctx.Response.Cookies.Delete(StateCookie, new CookieOptions { Path = "/api/auth" });
            string state = ctx.Request.Query["state"].ToString();
            string code = ctx.Request.Query["code"].ToString();
            if (string.IsNullOrEmpty(stateCookie) || string.IsNullOrEmpty(state) || !stateCookie.StartsWith(state + "|", StringComparison.Ordinal))
            {
                ctx.Response.Redirect("/?auth=state"); return;
            }
            string returnTo = stateCookie.Substring(state.Length + 1);
            bool grant = returnTo.StartsWith(GrantPrefix, StringComparison.Ordinal);
            TwitchIdentity grantIdentity = default;
            if (grant && !Enum.TryParse(returnTo.Substring(GrantPrefix.Length), true, out grantIdentity))
            {
                ctx.Response.Redirect(GrantPage + "?grant=state"); return;
            }
            string Fail(string reason) => grant ? GrantPage + "?grant=" + reason : "/?auth=" + reason;

            if (string.IsNullOrEmpty(code))
            {
                _logger.LogWarning("Twitch {Flow} denied: {Error}", grant ? "grant" : "login", ctx.Request.Query["error_description"].ToString());
                ctx.Response.Redirect(Fail("denied")); return;
            }

            try
            {
                var client = _http.CreateClient(HttpClientName);
                using var tokenResponse = await client.PostAsync("https://id.twitch.tv/oauth2/token", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _config.ApiClientId,
                    ["client_secret"] = _config.TApiClientSecret,
                    ["code"] = code,
                    ["grant_type"] = "authorization_code",
                    ["redirect_uri"] = RedirectUri,
                }));
                if (!tokenResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Twitch token exchange failed: {Status} {Body}", (int)tokenResponse.StatusCode, await tokenResponse.Content.ReadAsStringAsync());
                    ctx.Response.Redirect(Fail("token")); return;
                }
                using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
                var token = tokenJson.RootElement;
                string accessToken = token.GetProperty("access_token").GetString();
                string refreshToken = token.TryGetProperty("refresh_token", out var rt) && rt.ValueKind == JsonValueKind.String ? rt.GetString() : null;
                int expiresIn = token.TryGetProperty("expires_in", out var ei) && ei.TryGetInt32(out int e) ? e : 14400;

                using var validate = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
                validate.Headers.TryAddWithoutValidation("Authorization", "OAuth " + accessToken);
                using var validateResponse = await client.SendAsync(validate);
                if (!validateResponse.IsSuccessStatusCode) { ctx.Response.Redirect(Fail("validate")); return; }
                using var who = JsonDocument.Parse(await validateResponse.Content.ReadAsStringAsync());
                string login = who.RootElement.GetProperty("login").GetString()?.ToLowerInvariant();
                string userId = who.RootElement.GetProperty("user_id").GetString();
                var scopes = who.RootElement.TryGetProperty("scopes", out var sc) && sc.ValueKind == JsonValueKind.Array
                    ? sc.EnumerateArray().Select(s => s.GetString()).Where(s => s != null).ToList() : new List<string>();

                if (grant)
                {
                    await CompleteGrantAsync(ctx, client, grantIdentity, accessToken, refreshToken, expiresIn, scopes, userId, login);
                    return;
                }

                // The login token is only needed to learn who logged in; drop it right away.
                await RevokeQuietlyAsync(client, accessToken);

                string role = await RoleForAsync(userId, login);
                if (role == null)
                {
                    _logger.LogWarning("Web login refused for {Login} ({Id}): not an editor.", login, userId);
                    ctx.Response.Redirect("/?auth=forbidden"); return;
                }

                var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
                identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
                identity.AddClaim(new Claim(ClaimTypes.Name, login));
                identity.AddClaim(new Claim(ClaimTypes.Role, role));
                await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
                    new AuthenticationProperties { IsPersistent = true, IssuedUtc = DateTimeOffset.UtcNow });
                _logger.LogInformation("[API] {Login} logged in as {Role}.", login, role);
                ctx.Response.Redirect(returnTo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Twitch {Flow} callback failed", grant ? "grant" : "login");
                ctx.Response.Redirect(Fail("error"));
            }
        }

        private async Task CompleteGrantAsync(HttpContext ctx, HttpClient client, TwitchIdentity identity, string accessToken, string refreshToken,
            int expiresIn, List<string> scopes, string userId, string login)
        {
            string by = ctx.User?.Identity?.Name ?? "?";
            if (identity == TwitchIdentity.Broadcaster && userId != _config.BroadcasterId)
            {
                _logger.LogWarning("[API] {By} tried to grant the broadcaster identity from {Login} ({Id}), but the channel's broadcaster id is {Expected}.", by, login, userId, _config.BroadcasterId);
                await RevokeQuietlyAsync(client, accessToken);
                ctx.Response.Redirect(GrantPage + "?grant=wronguser&login=" + Uri.EscapeDataString(login ?? "")); return;
            }
            if (identity == TwitchIdentity.Bot && !string.IsNullOrWhiteSpace(_config.BotTwitchName) && !string.Equals(login, _config.BotTwitchName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[API] {By} tried to grant the bot identity from {Login}, but BotTwitchName is {Expected}.", by, login, _config.BotTwitchName);
                await RevokeQuietlyAsync(client, accessToken);
                ctx.Response.Redirect(GrantPage + "?grant=wrongbot&login=" + Uri.EscapeDataString(login ?? "")); return;
            }
            var missing = TwitchTokenService.RequiredScopes(identity).Where(s => !scopes.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
            await _tokens.StoreAuthorizationAsync(identity, accessToken, refreshToken, expiresIn, scopes, userId, login, _config.ApiClientId);
            _logger.LogInformation("[API] {By} completed the {Identity} grant as {Login}{Missing}.", by, identity, login, missing.Count > 0 ? " (missing scopes: " + string.Join(", ", missing) + ")" : "");
            ctx.Response.Redirect(GrantPage + "?grant=ok&identity=" + identity.ToString().ToLowerInvariant());
        }

        public async Task Logout(HttpContext ctx)
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            ctx.Response.StatusCode = 204;
        }

        /// <summary>root for RootUser, admin for the broadcaster, editor for promoted logins, null otherwise.</summary>
        public async Task<string> RoleForAsync(string userId, string login)
        {
            if (!string.IsNullOrEmpty(login) && login.Equals(_config.RootUser, StringComparison.OrdinalIgnoreCase)) return RoleRoot;
            if (!string.IsNullOrEmpty(userId) && userId == _config.BroadcasterId) return RoleAdmin;
            if (long.TryParse(userId, out long id) && await _admin.IsEditorAsync(id)) return RoleEditor;
            return null;
        }

        /// <summary>A returnTo the browser can only resolve inside this site: a plain absolute path, no scheme-relative or backslash tricks.</summary>
        public static string SafeReturnPath(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 2048 || value[0] != '/') return "/";
            if (value.Length > 1 && (value[1] == '/' || value[1] == '\\')) return "/";
            foreach (char c in value) if (c == '\\' || c <= ' ' || c == '\x7f') return "/";
            return value;
        }

        public static UserInfoDto Describe(ClaimsPrincipal user)
        {
            long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out long id);
            return new UserInfoDto(id, user.Identity?.Name, user.FindFirstValue(ClaimTypes.Role));
        }

        public object DescribeForPanel(ClaimsPrincipal user)
        {
            var dto = Describe(user);
            return new { twitchId = dto.TwitchId, login = dto.Login, role = dto.Role, managed = Managed, channel = _config.ChannelName };
        }

        private string AuthorizeUrl(string state, string scopes) =>
            "https://id.twitch.tv/oauth2/authorize?response_type=code&client_id=" + Uri.EscapeDataString(_config.ApiClientId) +
            "&redirect_uri=" + Uri.EscapeDataString(RedirectUri) + "&scope=" + Uri.EscapeDataString(scopes) + "&state=" + Uri.EscapeDataString(state);

        private static string NewState() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static CookieOptions StateCookieOptions(HttpContext ctx) => new CookieOptions
        {
            HttpOnly = true, Secure = ctx.Request.IsHttps, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromMinutes(10), Path = "/api/auth"
        };

        private async Task RevokeQuietlyAsync(HttpClient client, string accessToken)
        {
            try { await client.PostAsync("https://id.twitch.tv/oauth2/revoke", new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = _config.ApiClientId, ["token"] = accessToken })); } catch { }
        }
    }
}
