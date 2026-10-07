using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SkillzBot.Api;
using SkillzBot.IllConfiguration;
using SkillzBot.Services.Infrastructure;
using SkillzBot.Services.Twitch;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Hub
{
    /// <summary>Services and endpoints of the hub process (see <see cref="ChannelSupervisor"/>, <see cref="HubProxy"/>, <see cref="HubAuth"/>).</summary>
    public static class HubHost
    {
        public static readonly DateTime StartedUtc = DateTime.UtcNow;

        /// <summary>Resolves (or bootstraps) hub.json. Returns null with a logged reason when the hub cannot run.</summary>
        public static HubConfig LoadOrBootstrap(string channelsDir, ILogger logger)
        {
            Directory.CreateDirectory(channelsDir);
            string path = Path.Combine(channelsDir, HubConfig.FileName);
            if (!File.Exists(path))
            {
                // A single-channel install becoming a hub: take the secrets from the first channel config found.
                var source = Directory.GetDirectories(channelsDir).Select(Path.GetFileName)
                    .Where(n => !n.StartsWith("_") && !n.Equals("hub", StringComparison.OrdinalIgnoreCase))
                    .Select(n => Path.Combine(channelsDir, n, "DATA", n + ".json")).FirstOrDefault(File.Exists);
                if (source == null)
                {
                    logger.LogError("[Hub] {File} is missing and there is no channel config to build it from. Create it from hub.example.json.", path);
                    return null;
                }
                var sourceCfg = JObject.Parse(File.ReadAllText(source));
                int port = sourceCfg.Value<int?>("ApiPort") ?? 0; // nginx already points at this port; the hub takes it over
                var generated = HubConfig.FromChannelConfig(sourceCfg, port > 0 ? port : 8080);
                ChannelProvisioner.WriteJson(path, generated, secret: true);
                logger.LogWarning("[Hub] {File} created from {Source}; review it (HubPort must be the port nginx proxies to, ApiPublicUrl, ChannelTemplate).", path, source);
            }
            var cfg = HubConfig.Load(path);
            var missing = cfg.Missing();
            if (missing.Count > 0) logger.LogError("[Hub] hub.json is missing: {Keys}. Login and onboarding will not work until they are set.", string.Join(", ", missing));
            return cfg;
        }

        public static void ConfigureServices(IServiceCollection services, HubConfig hub, string channelsDir, IPathProvider paths)
        {
            services.AddSingleton(hub);
            services.AddSingleton(new HubSecret(Path.Combine(paths.DataPath, "hub-secret")));
            services.AddSingleton(new ChannelRegistry(Path.Combine(channelsDir, ChannelRegistry.FileName), hub.ChannelPortBase, hub.HubPort));
            services.AddSingleton(sp => new ChannelProvisioner(hub, sp.GetRequiredService<ChannelRegistry>(), channelsDir, sp.GetRequiredService<ILogger<ChannelProvisioner>>()));
            services.AddSingleton<ChannelSupervisor>();
            services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(45)); // the channels get 25s to exit cleanly
            // The hub's own token service holds the bot identity; the config it sees is the hub's view of the application.
            services.AddSingleton(new BotConfigModel { ChannelName = "hub", RootUser = hub.RootUser, BotTwitchName = hub.BotTwitchName, BotTwitchAuth = hub.BotTwitchAuth, ApiClientId = hub.ApiClientId, TApiClientId = hub.ApiClientId, TApiClientSecret = hub.TApiClientSecret, ApiPublicUrl = hub.ApiPublicUrl });
            services.AddSingleton<TwitchTokenService>();
            services.AddHostedService<TwitchTokenRefresher>();
            services.AddHostedService<HubBootstrap>(); // imports folders and publishes the bot token...
            services.AddHostedService(sp => sp.GetRequiredService<ChannelSupervisor>()); // ...before the first channel process starts
            services.AddHttpClient(TwitchTokenService.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15));
            services.AddHttpClient(TwitchAuth.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15));
            // a proxy passes redirects and cookies through to the browser; it never follows or keeps them itself
            services.AddHttpClient(HubProxy.HttpClientName, c => c.Timeout = Timeout.InfiniteTimeSpan)
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
            services.AddSingleton<HubProxy>();
            services.AddSingleton<HubAuth>();
            services.AddRouting();
            services.AddDataProtection().SetApplicationName("SkillzBotHub").PersistKeysToFileSystem(ApiHost.PrivateKeyDirectory(Path.Combine(paths.DataPath, "keys")));
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
            {
                o.Cookie.Name = "skillzbot.hub";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromDays(30);
                o.SlidingExpiration = true;
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            });
            services.AddAuthorization(o =>
            {
                o.AddPolicy("user", p => p.RequireAuthenticatedUser());
                o.AddPolicy("root", p => p.RequireRole(HubAuth.RoleRoot));
            });
        }

        public static void Configure(IApplicationBuilder app)
        {
            app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.Use(async (ctx, next) =>
            {
                var path = ctx.Request.Path;
                bool mutating = !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method);
                bool isApi = path.StartsWithSegments("/api") || (path.StartsWithSegments("/c") && path.Value.Contains("/api/"));
                if (mutating && isApi && !path.StartsWithSegments("/api/auth") && ctx.Request.Headers[ApiHost.CsrfHeader] != ApiHost.CsrfValue)
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await ctx.Response.WriteAsJsonAsync(new { error = $"missing {ApiHost.CsrfHeader}: {ApiHost.CsrfValue} header" });
                    return;
                }
                await next();
            });
            app.UseEndpoints(MapEndpoints);
        }

        private static T S<T>(HttpContext ctx) => ctx.RequestServices.GetRequiredService<T>();
        private static string Who(HttpContext ctx) => ctx.User?.Identity?.Name ?? "anonymous";

        private static object Describe(ChannelEntry c, ChannelSupervisor sup) => new
        {
            c.Login, c.DisplayName, c.BroadcasterId, c.ApiPort, c.Enabled, c.CreatedUtc, c.AddedBy, panelUrl = $"/c/{c.Login}/", process = sup.StatusOf(c.Login)
        };

        private static void MapEndpoints(IEndpointRouteBuilder e)
        {
            // ---- hub login and OAuth flows (one callback) ----
            e.MapGet("/api/auth/login", ctx => S<HubAuth>(ctx).Login(ctx));
            e.MapGet("/api/auth/callback", ctx => S<HubAuth>(ctx).Callback(ctx));
            e.MapPost("/api/auth/logout", ctx => S<HubAuth>(ctx).Logout(ctx));
            e.MapGet("/api/auth/me", ctx =>
            {
                var hub = S<HubConfig>(ctx);
                if (ctx.User?.Identity?.IsAuthenticated != true)
                    return Results.Json(new { error = "unauthenticated", hub = true, loginConfigured = hub.Missing().Count == 0 }, statusCode: 401).ExecuteAsync(ctx);
                string id = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
                bool root = HubAuth.IsRoot(ctx.User);
                var registry = S<ChannelRegistry>(ctx);
                var mine = registry.All().Where(c => root || c.BroadcasterId == id).Select(c => new { c.Login, c.DisplayName, c.Enabled, role = root ? "root" : "admin", panelUrl = $"/c/{c.Login}/" }).ToList();
                long.TryParse(id, out long tid);
                return Results.Json(new { twitchId = tid, login = ctx.User.Identity.Name, role = root ? "root" : "user", hub = true, channels = mine, botTwitchName = hub.BotTwitchName, canOnboard = !mine.Any() || root }).ExecuteAsync(ctx);
            });

            // ---- channels ----
            e.MapGet("/api/hub/channels", ctx =>
            {
                string id = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
                bool root = HubAuth.IsRoot(ctx.User);
                var sup = S<ChannelSupervisor>(ctx);
                var list = S<ChannelRegistry>(ctx).All().Where(c => root || c.BroadcasterId == id).Select(c => Describe(c, sup)).ToList();
                return Results.Json(list).ExecuteAsync(ctx);
            }).RequireAuthorization("user");
            e.MapPost("/api/hub/channels/{login}/restart", async ctx =>
            {
                string login = ctx.Request.RouteValues["login"]?.ToString();
                var c = S<ChannelRegistry>(ctx).Get(login ?? "");
                if (c == null) { await Results.Json(new { error = "unknown channel" }, statusCode: 404).ExecuteAsync(ctx); return; }
                if (!HubAuth.IsRoot(ctx.User) && ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) != c.BroadcasterId) { await Results.Json(new { error = "not your channel" }, statusCode: 403).ExecuteAsync(ctx); return; }
                S<ILogger<HubAuth>>(ctx).LogWarning("[Hub] {By} restarts channel {Login}.", Who(ctx), c.Login);
                await S<ChannelSupervisor>(ctx).RestartAsync(c.Login);
                await Results.Json(Describe(S<ChannelRegistry>(ctx).Get(c.Login), S<ChannelSupervisor>(ctx))).ExecuteAsync(ctx);
            }).RequireAuthorization("user");
            foreach (var (action, enabled) in new[] { ("enable", true), ("disable", false) })
            {
                e.MapPost($"/api/hub/channels/{{login}}/{action}", async ctx =>
                {
                    string login = ctx.Request.RouteValues["login"]?.ToString() ?? "";
                    if (!S<ChannelRegistry>(ctx).SetEnabled(login, enabled)) { await Results.Json(new { error = "unknown channel" }, statusCode: 404).ExecuteAsync(ctx); return; }
                    S<ILogger<HubAuth>>(ctx).LogWarning("[Hub] {By} {Action}s channel {Login}.", Who(ctx), action, login);
                    if (enabled) S<ChannelSupervisor>(ctx).Ensure(login); else await S<ChannelSupervisor>(ctx).StopAsync(login);
                    await Results.Json(Describe(S<ChannelRegistry>(ctx).Get(login), S<ChannelSupervisor>(ctx))).ExecuteAsync(ctx);
                }).RequireAuthorization("root");
            }
            e.MapDelete("/api/hub/channels/{login}", async ctx =>
            {
                string login = ctx.Request.RouteValues["login"]?.ToString() ?? "";
                bool removed = S<ChannelRegistry>(ctx).Remove(login); // out of the registry first, so the supervisor tick cannot start it again
                await S<ChannelSupervisor>(ctx).StopAsync(login);
                S<ILogger<HubAuth>>(ctx).LogWarning("[Hub] {By} removed channel {Login} from the registry (data folder kept).", Who(ctx), login);
                await Results.Json(new { removed }).ExecuteAsync(ctx);
            }).RequireAuthorization("root");

            // ---- grants ----
            e.MapGet("/api/hub/onboard", ctx => S<HubAuth>(ctx).Onboard(ctx)).RequireAuthorization("user");
            e.MapGet("/api/hub/grant", ctx => S<HubAuth>(ctx).Grant(ctx)).RequireAuthorization("user");
            e.MapGet("/api/hub/bot/authorize", ctx => S<HubAuth>(ctx).AuthorizeBot(ctx)).RequireAuthorization("root");
            e.MapGet("/api/hub/bot", ctx => Results.Json(S<TwitchTokenService>(ctx).Describe().First(t => t.Identity == "bot")).ExecuteAsync(ctx)).RequireAuthorization("root");
            e.MapDelete("/api/hub/bot", async ctx =>
            {
                await S<TwitchTokenService>(ctx).RemoveAsync(TwitchIdentity.Bot, Who(ctx));
                if (S<TwitchTokenService>(ctx).Current(TwitchIdentity.Bot) == null)
                {
                    // nothing to publish any more: the channels see a missing file as "no bot token"
                    string published = Path.Combine(S<IPathProvider>(ctx).SharedPath, TwitchTokenService.HubBotTokenFileName);
                    try { File.Delete(published); } catch (Exception ex) { S<ILogger<HubAuth>>(ctx).LogWarning("[Hub] could not remove {File}: {Message}", published, ex.Message); }
                }
                await Results.Json(S<TwitchTokenService>(ctx).Describe().First(t => t.Identity == "bot")).ExecuteAsync(ctx);
            }).RequireAuthorization("root");

            // ---- status ----
            e.MapGet("/api/hub/status", ctx =>
            {
                var registry = S<ChannelRegistry>(ctx); var sup = S<ChannelSupervisor>(ctx);
                var all = registry.All();
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
                return Results.Json(new
                {
                    version, startedUtc = StartedUtc, uptimeSeconds = (DateTime.UtcNow - StartedUtc).TotalSeconds,
                    channels = all.Count, running = all.Count(c => sup.StatusOf(c.Login).State == "running"),
                    bot = S<TwitchTokenService>(ctx).DescribeShort(), publicUrl = S<HubConfig>(ctx).ApiPublicUrl, missing = S<HubConfig>(ctx).Missing()
                }).ExecuteAsync(ctx);
            }).RequireAuthorization("user");

            // ---- channel panels ----
            e.Map("/c/{login}/api/{**rest}", ctx => S<HubProxy>(ctx).Forward(ctx));
        }
    }

    /// <summary>First-start work: import pre-hub channel folders, initialize the bot token, keep the published copy current.</summary>
    public sealed class HubBootstrap : IHostedService
    {
        private readonly ChannelProvisioner _provisioner;
        private readonly ChannelRegistry _registry;
        private readonly TwitchTokenService _tokens;
        private readonly IPathProvider _paths;
        private readonly HubConfig _hub;
        private readonly ILogger<HubBootstrap> _logger;

        public HubBootstrap(ChannelProvisioner provisioner, ChannelRegistry registry, TwitchTokenService tokens, IPathProvider paths, HubConfig hub, ILogger<HubBootstrap> logger)
        {
            _provisioner = provisioner; _registry = registry; _tokens = tokens; _paths = paths; _hub = hub; _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var imported = _provisioner.ImportExisting();
            try { await _tokens.InitializeAsync(); } // the hub's own store first, so a stale record from a folder never replaces a live token
            catch (Exception ex) { _logger.LogError(ex, "[Hub] token initialization failed."); }
            // A bot token a pre-hub channel had obtained on its panel moves to the hub, which refreshes it from now on.
            foreach (var login in imported)
            {
                var bot = _provisioner.TakeBotRecordFromChannel(login); // removed from the channel either way: one refresher per token
                if (bot == null) continue;
                if (_tokens.Current(TwitchIdentity.Bot)?.Source == "oauth") { _logger.LogWarning("[Hub] channel {Login} had a bot token too; the hub keeps its own.", login); continue; }
                if (!string.Equals(bot.Value<string>("clientId"), _hub.ApiClientId, StringComparison.Ordinal))
                { _logger.LogWarning("[Hub] bot token of channel {Login} was issued by another Twitch application ({ClientId}); it cannot be refreshed here and is dropped.", login, bot.Value<string>("clientId")); continue; }
                await _tokens.StoreAuthorizationAsync(TwitchIdentity.Bot, bot.Value<string>("accessToken"), bot.Value<string>("refreshToken"),
                    Math.Max(60, (int)((bot.Value<DateTime?>("expiresUtc") ?? DateTime.UtcNow) - DateTime.UtcNow).TotalSeconds),
                    (bot["scopes"] as JArray)?.Select(s => s.ToString()) ?? Array.Empty<string>(), bot.Value<string>("userId"), bot.Value<string>("login"), bot.Value<string>("clientId"));
                _logger.LogWarning("[Hub] bot token taken over from channel {Login}.", login);
            }
            string published = Path.Combine(_paths.SharedPath, TwitchTokenService.HubBotTokenFileName);
            _tokens.Attach(TwitchIdentity.Bot, c =>
            {
                try
                {
                    TwitchTokenService.WriteHubBotToken(published, c, _tokens.ExpiryOf(TwitchIdentity.Bot) ?? DateTime.UtcNow.AddHours(1));
                    _logger.LogInformation("[Hub] bot token published for the channels ({Login}).", c.Login);
                }
                catch (Exception ex) { _logger.LogError(ex, "[Hub] could not publish the bot token."); }
            });
            _logger.LogInformation("[Hub] {Count} channel(s) registered: {Logins}", _registry.All().Count, string.Join(", ", _registry.All().Select(c => c.Login + (c.Enabled ? "" : " (disabled)"))));
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
