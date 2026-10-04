using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SkillzBot.IllConfiguration;
using SkillzBot.Interfaces;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace SkillzBot.Api
{
    /// <summary>
    /// Twitch OAuth (authorization code) login for the web panel. Who may log in is decided here:
    /// RootUser is root, the broadcaster is admin, logins in dbBotEditorTable are editors.
    /// </summary>
    public sealed class TwitchAuth
    {
        public const string RoleRoot = "root";
        public const string RoleAdmin = "admin";
        public const string RoleEditor = "editor";
        private const string StateCookie = "skillzbot.oauth";
        public const string HttpClientName = "TwitchAuth";

        private readonly BotConfigModel _config;
        private readonly IAdminRepository _admin;
        private readonly IHttpClientFactory _http;
        private readonly ILogger<TwitchAuth> _logger;

        public TwitchAuth(BotConfigModel config, IAdminRepository admin, IHttpClientFactory http, ILogger<TwitchAuth> logger)
        {
            _config = config;
            _admin = admin;
            _http = http;
            _logger = logger;
        }

        public bool Configured => !string.IsNullOrWhiteSpace(_config.ApiClientId) && !string.IsNullOrWhiteSpace(_config.TApiClientSecret) && !string.IsNullOrWhiteSpace(_config.ApiPublicUrl);

        private string RedirectUri => _config.ApiPublicUrl.TrimEnd('/') + "/api/auth/callback";

        public Task Login(HttpContext ctx)
        {
            if (!Configured)
            {
                ctx.Response.StatusCode = 503;
                return ctx.Response.WriteAsync("Login is not configured: set TApiClientSecret and ApiPublicUrl in the channel config.");
            }
            string returnTo = ctx.Request.Query["returnTo"].ToString();
            if (string.IsNullOrEmpty(returnTo) || !returnTo.StartsWith('/') || returnTo.StartsWith("//")) returnTo = "/";
            string state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            ctx.Response.Cookies.Append(StateCookie, state + "|" + returnTo, new CookieOptions
            {
                HttpOnly = true, Secure = ctx.Request.IsHttps, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromMinutes(10), Path = "/api/auth"
            });
            string url = "https://id.twitch.tv/oauth2/authorize?response_type=code&client_id=" + Uri.EscapeDataString(_config.ApiClientId) +
                         "&redirect_uri=" + Uri.EscapeDataString(RedirectUri) + "&scope=&state=" + Uri.EscapeDataString(state);
            ctx.Response.Redirect(url);
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
            if (string.IsNullOrEmpty(code))
            {
                _logger.LogWarning("Twitch login denied: {Error}", ctx.Request.Query["error_description"].ToString());
                ctx.Response.Redirect("/?auth=denied"); return;
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
                    ctx.Response.Redirect("/?auth=token"); return;
                }
                using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
                string accessToken = tokenJson.RootElement.GetProperty("access_token").GetString();

                using var validate = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
                validate.Headers.TryAddWithoutValidation("Authorization", "OAuth " + accessToken);
                using var validateResponse = await client.SendAsync(validate);
                if (!validateResponse.IsSuccessStatusCode) { ctx.Response.Redirect("/?auth=validate"); return; }
                using var who = JsonDocument.Parse(await validateResponse.Content.ReadAsStringAsync());
                string login = who.RootElement.GetProperty("login").GetString()?.ToLowerInvariant();
                string userId = who.RootElement.GetProperty("user_id").GetString();

                // The user token is only needed to learn who logged in; drop it right away.
                try { await client.PostAsync("https://id.twitch.tv/oauth2/revoke", new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = _config.ApiClientId, ["token"] = accessToken })); } catch { }

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
                _logger.LogError(ex, "Twitch login callback failed");
                ctx.Response.Redirect("/?auth=error");
            }
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

        public static UserInfoDto Describe(ClaimsPrincipal user)
        {
            long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out long id);
            return new UserInfoDto(id, user.Identity?.Name, user.FindFirstValue(ClaimTypes.Role));
        }
    }
}
