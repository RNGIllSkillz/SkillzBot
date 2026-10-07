using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkillzBot.Services.Twitch;
using System;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace SkillzBot.Api
{
    /// <summary>
    /// Authentication for a channel process run by the hub: the hub proxies panel requests with a signed
    /// X-SkillzBot-Auth header naming the Twitch user; this handler verifies it and lets the channel decide the
    /// role (root / admin / editor) exactly as the cookie login does. The hub itself (internal calls) is root.
    /// </summary>
    public sealed class HubHeaderAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "hub";
        public const string RefusedLoginItem = "SkillzBot.HubRefusedLogin";
        public const string RoleErrorItem = "SkillzBot.HubRoleError";
        private static readonly ConcurrentDictionary<string, (string Role, DateTime Until)> RoleCache = new ConcurrentDictionary<string, (string, DateTime)>();
        private readonly TwitchAuth _auth;
        private readonly string _secret = Environment.GetEnvironmentVariable(HubSignature.EnvSecret);

        public HubHeaderAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TwitchAuth auth)
            : base(options, logger, encoder) { _auth = auth; }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string token = Request.Headers[HubSignature.Header].ToString();
            if (string.IsNullOrEmpty(token)) return AuthenticateResult.NoResult();
            var who = HubSignature.Verify(_secret, token);
            if (who == null) return AuthenticateResult.Fail("invalid hub signature");
            string role;
            if (who.IsHub) role = TwitchAuth.RoleRoot;
            else
            {
                // root and the broadcaster are decided from the config; editors need the database, so cache the answer briefly
                string key = who.UserId + "|" + who.Login;
                if (RoleCache.TryGetValue(key, out var cached) && cached.Until > DateTime.UtcNow) role = cached.Role;
                else
                {
                    try { role = await _auth.RoleForAsync(who.UserId, who.Login); }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("[Hub] role lookup for {Login} failed: {Message}", who.Login, ex.Message);
                        Context.Items[RefusedLoginItem] = who.Login;
                        Context.Items[RoleErrorItem] = true; // the database, not the user: the panel says "try again" instead of "no access"
                        return AuthenticateResult.Fail("role lookup failed");
                    }
                    RoleCache[key] = (role, DateTime.UtcNow.AddSeconds(role == null ? 30 : 120));
                }
            }
            if (role == null)
            {
                Context.Items[RefusedLoginItem] = who.Login; // known to the hub, but not an editor of this channel: the 401 body says so
                return AuthenticateResult.NoResult();
            }
            var identity = new ClaimsIdentity(SchemeName);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, who.UserId ?? "0"));
            identity.AddClaim(new Claim(ClaimTypes.Name, who.Login ?? "hub"));
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties) { Response.StatusCode = 401; return Task.CompletedTask; }
        protected override Task HandleForbiddenAsync(AuthenticationProperties properties) { Response.StatusCode = 403; return Task.CompletedTask; }
    }
}
