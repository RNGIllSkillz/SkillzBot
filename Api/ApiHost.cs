using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SkillzBot.IllConfiguration;
using SkillzBot.IllSkillzBot;
using SkillzBot.Interfaces;
using SkillzBot.Services;
using SkillzBot.Services.Infrastructure;
using SkillzBot.Services.Twitch;
using SkillzBot.Services.Vip;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Api
{
    /// <summary>HTTP API for the web panel. Hosted by Kestrel inside the bot process when ApiPort &gt; 0.</summary>
    public static class ApiHost
    {
        public const string CsrfHeader = "X-Requested-With";
        public const string CsrfValue = "SkillzBot";
        private static readonly DateTime ProcessStartedUtc = DateTime.UtcNow;

        /// <summary>Reads ApiPort from the channel config before the host is built (default 8080, 0 disables the API).</summary>
        public static int ReadConfiguredPort()
        {
            try
            {
                var paths = new Services.Infrastructure.PathProvider();
                string path = File.Exists(paths.ConfigPath) ? paths.ConfigPath : paths.LegacyConfigPath;
                if (!File.Exists(path)) return 8080;
                var root = JObject.Parse(File.ReadAllText(path));
                return root.TryGetValue("ApiPort", StringComparison.OrdinalIgnoreCase, out var v) && int.TryParse(v.ToString(), out int port) ? port : 8080;
            }
            catch { return 8080; }
        }

        public static void ConfigureWebServices(IServiceCollection services)
        {
            services.AddRouting();
            services.AddHttpClient(TwitchAuth.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15));
            services.AddSingleton<TwitchAuth>();
            services.AddDataProtection()
                .SetApplicationName("SkillzBot")
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(new Services.Infrastructure.PathProvider().DataPath, "keys")));
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
            {
                o.Cookie.Name = "skillzbot.session";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromDays(30);
                o.SlidingExpiration = true;
                // An API never redirects to a login page: the UI handles 401/403 itself.
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            });
            services.AddAuthorization(o =>
            {
                o.AddPolicy("editor", p => p.RequireAuthenticatedUser());
                o.AddPolicy("admin", p => p.RequireRole(TwitchAuth.RoleAdmin, TwitchAuth.RoleRoot));
                o.AddPolicy("root", p => p.RequireRole(TwitchAuth.RoleRoot));
            });
        }

        public static void Configure(IApplicationBuilder app)
        {
            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                // Defaults trust loopback only, which is where nginx runs.
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            });
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            // Mutating calls must carry a custom header: a plain cross-site form post cannot add one.
            app.Use(async (ctx, next) =>
            {
                var path = ctx.Request.Path;
                bool mutating = !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method);
                if (mutating && path.StartsWithSegments("/api") && !path.StartsWithSegments("/api/auth") && ctx.Request.Headers[CsrfHeader] != CsrfValue)
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await ctx.Response.WriteAsJsonAsync(new { error = $"missing {CsrfHeader}: {CsrfValue} header" });
                    return;
                }
                await next();
            });
            app.UseEndpoints(MapEndpoints);
        }

        private static T S<T>(HttpContext ctx) => ctx.RequestServices.GetRequiredService<T>();
        private static string Who(HttpContext ctx) => ctx.User?.Identity?.Name ?? "anonymous";
        private static bool IsRoot(HttpContext ctx) => ctx.User?.IsInRole(TwitchAuth.RoleRoot) == true;
        private static int Q(HttpContext ctx, string name, int fallback, int min, int max) =>
            int.TryParse(ctx.Request.Query[name], out int v) ? Math.Clamp(v, min, max) : fallback;
        private static DateTime? QDate(HttpContext ctx, string name) =>
            DateTime.TryParse(ctx.Request.Query[name], null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;
        private static async Task<Dictionary<string, JsonElement>> Body(HttpContext ctx) =>
            await ctx.Request.ReadFromJsonAsync<Dictionary<string, JsonElement>>() ?? new Dictionary<string, JsonElement>();
        private static string Str(Dictionary<string, JsonElement> body, string key) =>
            body.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static void MapEndpoints(IEndpointRouteBuilder e)
        {
            // ---- auth ----
            e.MapGet("/api/auth/login", ctx => S<TwitchAuth>(ctx).Login(ctx));
            e.MapGet("/api/auth/callback", ctx => S<TwitchAuth>(ctx).Callback(ctx));
            e.MapPost("/api/auth/logout", ctx => S<TwitchAuth>(ctx).Logout(ctx));
            e.MapGet("/api/auth/me", ctx => ctx.User?.Identity?.IsAuthenticated == true
                ? Results.Json(TwitchAuth.Describe(ctx.User)).ExecuteAsync(ctx)
                : Results.Json(new { error = "unauthenticated", loginConfigured = S<TwitchAuth>(ctx).Configured }, statusCode: 401).ExecuteAsync(ctx));

            // ---- status, state, config ----
            e.MapGet("/api/status", ctx => Results.Json(S<HealthReporter>(ctx).BuildSnapshot()).ExecuteAsync(ctx)).RequireAuthorization("editor");
            e.MapGet("/api/state", ctx => Results.Json(S<IBotStateService>(ctx).Current).ExecuteAsync(ctx)).RequireAuthorization("editor");
            e.MapMethods("/api/state", new[] { "PATCH" }, async ctx =>
            {
                try
                {
                    var applied = await S<BotSettingsService>(ctx).PatchStateAsync(await Body(ctx), Who(ctx));
                    await Results.Json(new { applied, state = S<IBotStateService>(ctx).Current }).ExecuteAsync(ctx);
                }
                catch (ArgumentException ex) { await Results.Json(new { error = ex.Message }, statusCode: 400).ExecuteAsync(ctx); }
            }).RequireAuthorization("editor");
            e.MapGet("/api/gamestate", ctx => Results.Json(S<IGameStateService>(ctx).Current).ExecuteAsync(ctx)).RequireAuthorization("editor");
            // The full channel config is root-only; everyone else gets just the bot-setting keys (EditorConfigKeys).
            e.MapGet("/api/config", async ctx => await Results.Json(await S<BotSettingsService>(ctx).GetConfigAsync(IsRoot(ctx))).ExecuteAsync(ctx)).RequireAuthorization("editor");
            e.MapMethods("/api/config", new[] { "PATCH" }, async ctx =>
            {
                try
                {
                    bool restart = await S<BotSettingsService>(ctx).PatchConfigAsync(await Body(ctx), IsRoot(ctx), Who(ctx));
                    await Results.Json(new { restartRequired = restart }).ExecuteAsync(ctx);
                }
                catch (ArgumentException ex) { await Results.Json(new { error = ex.Message }, statusCode: IsRoot(ctx) ? 400 : 403).ExecuteAsync(ctx); }
            }).RequireAuthorization("editor");
            e.MapGet("/api/system/info", ctx =>
            {
                var cfg = S<BotConfigModel>(ctx); var paths = S<IPathProvider>(ctx);
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
                return Results.Json(new SystemInfoDto(version, System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, ProcessStartedUtc, paths.DataPath, cfg.ChannelName, cfg.ApiPort, cfg.ApiPublicUrl)).ExecuteAsync(ctx);
            }).RequireAuthorization("editor");
            e.MapPost("/api/system/restart", async ctx =>
            {
                S<BotSettingsService>(ctx).RequestRestart(Who(ctx));
                await Results.Json(new { restarting = true }, statusCode: 202).ExecuteAsync(ctx);
            }).RequireAuthorization("editor");
            e.MapGet("/api/logs", async ctx =>
            {
                string file = ctx.Request.Query["file"] == "errors" ? "errors" : "bot";
                await Results.Json(new { file, lines = await S<BotSettingsService>(ctx).TailLogAsync(file, Q(ctx, "lines", 300, 10, 5000)) }).ExecuteAsync(ctx);
            }).RequireAuthorization("editor");

            // ---- live chat ----
            e.MapGet("/api/chat/recent", ctx => Results.Json(S<ChatFeed>(ctx).Recent(Q(ctx, "limit", 200, 1, 500))).ExecuteAsync(ctx)).RequireAuthorization("editor");
            e.MapGet("/api/chat/stream", StreamAsync).RequireAuthorization("editor");
            e.MapPost("/api/chat/send", async ctx =>
            {
                string text = Str(await Body(ctx), "text")?.Trim();
                if (string.IsNullOrEmpty(text) || text.Length > 500) { await Results.Json(new { error = "text must be 1-500 characters" }, statusCode: 400).ExecuteAsync(ctx); return; }
                S<ILogger<ChatFeed>>(ctx).LogInformation("[API] {By} sent chat message: {Text}", Who(ctx), text);
                await S<ITtvIRCClient>(ctx).SendMessage(text);
                await Results.Json(new { sent = true }).ExecuteAsync(ctx);
            }).RequireAuthorization("editor");

            // ---- data ----
            e.MapGet("/api/users", async ctx => await Results.Json(await S<IAdminRepository>(ctx).SearchUsersAsync(
                ctx.Request.Query["q"], ctx.Request.Query["sort"], ctx.Request.Query["dir"] != "asc", Q(ctx, "page", 1, 1, 100000), Q(ctx, "pageSize", 50, 1, 200))).ExecuteAsync(ctx)).RequireAuthorization("editor");
            e.MapGet("/api/users/{login}", async ctx =>
            {
                string login = ctx.Request.RouteValues["login"]?.ToString();
                var user = await S<IDatabaseService>(ctx).GetUserAsync(login);
                if (user == null || user.dbID == -404) { await Results.Json(new { error = "not found" }, statusCode: 404).ExecuteAsync(ctx); return; }
                var messages = await S<IAdminRepository>(ctx).SearchMessagesAsync(login, null, null, null, 1, 50);
                await Results.Json(new { user, messages }).ExecuteAsync(ctx);
            }).RequireAuthorization("editor");
            e.MapGet("/api/messages", async ctx => await Results.Json(await S<IAdminRepository>(ctx).SearchMessagesAsync(
                ctx.Request.Query["user"], ctx.Request.Query["q"], QDate(ctx, "from"), QDate(ctx, "to"), Q(ctx, "page", 1, 1, 100000), Q(ctx, "pageSize", 50, 1, 200))).ExecuteAsync(ctx)).RequireAuthorization("editor");
            e.MapGet("/api/stats/activity", async ctx =>
            {
                int days = Q(ctx, "days", 7, 1, 365);
                int bucket = Q(ctx, "bucket", days <= 2 ? 900 : days <= 14 ? 3600 : 86400, 60, 86400 * 7);
                await Results.Json(await S<IAdminRepository>(ctx).GetActivityAsync(DateTime.UtcNow.AddDays(-days), bucket)).ExecuteAsync(ctx);
            }).RequireAuthorization("editor");
            e.MapGet("/api/stats/engagement", async ctx => await Results.Json(await S<IEngagementRepository>(ctx).GetEngagementSummaryAsync(Q(ctx, "days", 30, 1, 365))).ExecuteAsync(ctx)).RequireAuthorization("editor");
            e.MapGet("/api/stats/predictions", async ctx => await Results.Json(await S<IAdminRepository>(ctx).GetPredictionsAsync(Q(ctx, "days", 30, 1, 365), Q(ctx, "limit", 100, 1, 500))).ExecuteAsync(ctx)).RequireAuthorization("editor");
            e.MapGet("/api/stats/polls", async ctx => await Results.Json(await S<IAdminRepository>(ctx).GetPollsAsync(Q(ctx, "days", 30, 1, 365), Q(ctx, "limit", 100, 1, 500))).ExecuteAsync(ctx)).RequireAuthorization("editor");
            e.MapGet("/api/stats/db", async ctx => await Results.Json(await S<IDatabaseService>(ctx).GetStatsAsync()).ExecuteAsync(ctx)).RequireAuthorization("editor");

            // ---- predictions / actions ----
            e.MapGet("/api/predictions/status", ctx =>
            {
                var s = S<IBotStateService>(ctx).Current;
                return Results.Json(new { summary = S<IllPredictions>(ctx).DescribePollState(), active = s.ActivePrediction, poll = s.ActivePoll, nextKind = s.NextPredictionKey, kinds = IllPredictions.ListKinds() }).ExecuteAsync(ctx);
            }).RequireAuthorization("editor");
            e.MapPost("/api/actions/poll", async ctx => await Results.Json(new { result = await S<IllPredictions>(ctx).StartPollAsync() }).ExecuteAsync(ctx)).RequireAuthorization("editor");
            e.MapPost("/api/actions/quiz", async ctx => { await S<IllGames>(ctx).Quizz(true); await Results.Json(new { started = true }).ExecuteAsync(ctx); }).RequireAuthorization("editor");
            e.MapPost("/api/actions/prediction/cancel", async ctx =>
            {
                S<ILogger<ChatFeed>>(ctx).LogWarning("[API] {By} canceled the current prediction.", Who(ctx));
                await S<ITwitchService>(ctx).CencelePrediction();
                await Results.Json(new { canceled = true }).ExecuteAsync(ctx);
            }).RequireAuthorization("editor");
            e.MapPost("/api/actions/filters/reload", ctx => { S<BotSettingsService>(ctx).ReloadFilters(); return Results.Json(new { reloaded = true }).ExecuteAsync(ctx); }).RequireAuthorization("editor");

            // ---- subscription: root and the broadcaster see it, only root changes it ----
            e.MapGet("/api/subscription", async ctx => await Results.Json(await S<SubscriptionService>(ctx).GetStatusAsync()).ExecuteAsync(ctx)).RequireAuthorization("admin");
            e.MapPut("/api/subscription", async ctx =>
            {
                var body = await Body(ctx);
                var subs = S<SubscriptionService>(ctx);
                if (body.TryGetValue("addMonths", out var m))
                {
                    if (m.ValueKind != JsonValueKind.Number || !m.TryGetInt32(out int months) || months < 1 || months > 24)
                    {
                        await Results.Json(new { error = "addMonths must be a whole number from 1 to 24" }, statusCode: 400).ExecuteAsync(ctx); return;
                    }
                    await subs.ExtendAsync(months, Who(ctx));
                }
                else if (body.TryGetValue("due", out var d))
                {
                    if (d.ValueKind == JsonValueKind.Null || (d.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(d.GetString())))
                        await subs.SetDueAsync(null, Who(ctx));
                    else if (d.ValueKind == JsonValueKind.String && SubscriptionService.TryParseDue(d.GetString(), out var due))
                        await subs.SetDueAsync(due, Who(ctx));
                    else
                    {
                        await Results.Json(new { error = "due must be YYYY-MM-DD, an ISO 8601 date-time, or null" }, statusCode: 400).ExecuteAsync(ctx); return;
                    }
                }
                else
                {
                    await Results.Json(new { error = "body needs due or addMonths" }, statusCode: 400).ExecuteAsync(ctx); return;
                }
                await Results.Json(await subs.GetStatusAsync()).ExecuteAsync(ctx);
            }).RequireAuthorization("root");

            // ---- twitch tokens: visible to root and the broadcaster; the bot grant and removals are root-only ----
            e.MapGet("/api/twitch/tokens", ctx => Results.Json(S<TwitchTokenService>(ctx).Describe()).ExecuteAsync(ctx)).RequireAuthorization("admin");
            e.MapGet("/api/twitch/authorize", ctx =>
            {
                string identity = ctx.Request.Query["identity"].ToString();
                if (identity == "broadcaster") return S<TwitchAuth>(ctx).Authorize(ctx, TwitchIdentity.Broadcaster);
                if (identity == "bot" && IsRoot(ctx)) return S<TwitchAuth>(ctx).Authorize(ctx, TwitchIdentity.Bot);
                return Results.Json(new { error = identity == "bot" ? "only root may authorize the bot account" : "identity must be broadcaster or bot" },
                    statusCode: identity == "bot" ? 403 : 400).ExecuteAsync(ctx);
            }).RequireAuthorization("admin");
            e.MapDelete("/api/twitch/tokens/{identity}", async ctx =>
            {
                if (!Enum.TryParse<TwitchIdentity>(ctx.Request.RouteValues["identity"]?.ToString(), true, out var identity))
                {
                    await Results.Json(new { error = "identity must be broadcaster or bot" }, statusCode: 400).ExecuteAsync(ctx); return;
                }
                await S<TwitchTokenService>(ctx).RemoveAsync(identity, Who(ctx));
                await Results.Json(S<TwitchTokenService>(ctx).Describe()).ExecuteAsync(ctx);
            }).RequireAuthorization("root");

            // ---- filters ----
            // The word lists (dic, whitelist) exist only for root; other roles neither see nor save them.
            e.MapGet("/api/filters", ctx => Results.Json(S<BotSettingsService>(ctx).GetFilters(IsRoot(ctx))).ExecuteAsync(ctx)).RequireAuthorization("editor");
            e.MapPut("/api/filters/{name}", async ctx =>
            {
                string name = ctx.Request.RouteValues["name"]?.ToString();
                if (!IsRoot(ctx) && BotSettingsService.IsRootOnlyFilter(name))
                {
                    await Results.Json(new { error = "this list is root-only" }, statusCode: 403).ExecuteAsync(ctx); return;
                }
                var body = await ctx.Request.ReadFromJsonAsync<FilterSaveRequest>();
                bool ok = body?.Lines != null && await S<BotSettingsService>(ctx).SaveFilterAsync(name, body.Lines, Who(ctx));
                await (ok ? Results.Json(new { saved = true }) : Results.Json(new { error = "unknown list or missing lines" }, statusCode: 400)).ExecuteAsync(ctx);
            }).RequireAuthorization("editor");

            // ---- quiz ----
            e.MapGet("/api/quiz", async ctx => await Results.Json(await S<IAdminRepository>(ctx).GetQuizzesAsync()).ExecuteAsync(ctx)).RequireAuthorization("editor");
            e.MapPost("/api/quiz", async ctx =>
            {
                var q = await ctx.Request.ReadFromJsonAsync<QuizRequest>();
                if (q == null || string.IsNullOrWhiteSpace(q.Question) || string.IsNullOrWhiteSpace(q.Answer)) { await Results.Json(new { error = "question and answer are required" }, statusCode: 400).ExecuteAsync(ctx); return; }
                int id = await S<IAdminRepository>(ctx).AddQuizAsync(q.Question.Trim(), q.Answer.Trim(), Math.Max(0, q.Prize));
                await Results.Json(new { id }).ExecuteAsync(ctx);
            }).RequireAuthorization("editor");
            e.MapPut("/api/quiz/{id:int}", async ctx =>
            {
                var q = await ctx.Request.ReadFromJsonAsync<QuizRequest>();
                int id = int.Parse(ctx.Request.RouteValues["id"].ToString());
                bool ok = q != null && await S<IAdminRepository>(ctx).UpdateQuizAsync(id, q.Question?.Trim(), q.Answer?.Trim(), Math.Max(0, q.Prize));
                await (ok ? Results.Json(new { updated = true }) : Results.Json(new { error = "not found" }, statusCode: 404)).ExecuteAsync(ctx);
            }).RequireAuthorization("editor");
            e.MapDelete("/api/quiz/{id:int}", async ctx =>
            {
                bool ok = await S<IAdminRepository>(ctx).DeleteQuizAsync(int.Parse(ctx.Request.RouteValues["id"].ToString()));
                await (ok ? Results.Json(new { deleted = true }) : Results.Json(new { error = "not found" }, statusCode: 404)).ExecuteAsync(ctx);
            }).RequireAuthorization("editor");

            // ---- VIPs ----
            e.MapGet("/api/vips", async ctx =>
            {
                var vips = S<VipRegistryService>(ctx);
                var list = VipRegistryService.Oldest(await vips.SnapshotAsync()).ToList();
                await Results.Json(new { limit = vips.Limit, autoRotate = vips.AutoRotate, lastSyncUtc = S<IBotStateService>(ctx).Current.VipLastSyncUtc, vips = list }).ExecuteAsync(ctx);
            }).RequireAuthorization("editor");
            e.MapPost("/api/vips/sync", async ctx => await Results.Json(await S<VipRegistryService>(ctx).SyncAsync("api") ?? (object)new { error = "sync failed" }).ExecuteAsync(ctx)).RequireAuthorization("editor");
            e.MapMethods("/api/vips/{login}", new[] { "PATCH" }, async ctx =>
            {
                string login = ctx.Request.RouteValues["login"]?.ToString();
                var body = await Body(ctx); var vips = S<VipRegistryService>(ctx); var results = new List<string>();
                if (body.TryGetValue("pinned", out var p) && (p.ValueKind == JsonValueKind.True || p.ValueKind == JsonValueKind.False)) results.Add(await vips.SetPinnedAsync(login, p.ValueKind == JsonValueKind.True));
                if (body.TryGetValue("since", out var s) && s.ValueKind == JsonValueKind.String) results.Add(await vips.SetSinceAsync(login, s.GetString()));
                S<ILogger<ChatFeed>>(ctx).LogInformation("[API] {By} edited VIP {Login}: {Result}", Who(ctx), login, string.Join(" | ", results));
                await Results.Json(new { results }).ExecuteAsync(ctx);
            }).RequireAuthorization("editor");

            // ---- editors (admin) ----
            e.MapGet("/api/editors", async ctx => await Results.Json(await S<IAdminRepository>(ctx).GetEditorsAsync()).ExecuteAsync(ctx)).RequireAuthorization("admin");
            e.MapPost("/api/editors", async ctx =>
            {
                string login = Str(await Body(ctx), "login")?.Trim().TrimStart('@').ToLowerInvariant();
                if (string.IsNullOrEmpty(login)) { await Results.Json(new { error = "login required" }, statusCode: 400).ExecuteAsync(ctx); return; }
                long id = await ResolveTwitchIdAsync(ctx, login);
                if (id == 0) { await Results.Json(new { error = "twitch user not found" }, statusCode: 404).ExecuteAsync(ctx); return; }
                await S<IAdminRepository>(ctx).AddEditorAsync(id, login, Who(ctx));
                S<ILogger<ChatFeed>>(ctx).LogInformation("[API] {By} added editor {Login}", Who(ctx), login);
                await Results.Json(new { twitchId = id, login }).ExecuteAsync(ctx);
            }).RequireAuthorization("admin");
            e.MapDelete("/api/editors/{id:long}", async ctx =>
            {
                bool ok = await S<IAdminRepository>(ctx).RemoveEditorAsync(long.Parse(ctx.Request.RouteValues["id"].ToString()));
                S<ILogger<ChatFeed>>(ctx).LogInformation("[API] {By} removed editor {Id}", Who(ctx), ctx.Request.RouteValues["id"]);
                await (ok ? Results.Json(new { removed = true }) : Results.Json(new { error = "not found" }, statusCode: 404)).ExecuteAsync(ctx);
            }).RequireAuthorization("admin");
        }

        /// <summary>Twitch id for a login: the bot's user table first, Helix as a fallback.</summary>
        public static async Task<long> ResolveTwitchIdAsync(HttpContext ctx, string login)
        {
            try
            {
                var user = await S<IDatabaseService>(ctx).GetUserAsync(login);
                if (user != null && user.dbID != -404 && user.TwitchID > 0) return user.TwitchID;
            }
            catch { }
            var fromTwitch = await S<ITwitchService>(ctx).GetUsetIDByName(login);
            return long.TryParse(fromTwitch, out long id) ? id : 0;
        }

        /// <summary>Server-Sent Events: chat messages as they arrive plus a health snapshot every 5 seconds.</summary>
        private static async Task StreamAsync(HttpContext ctx)
        {
            var feed = S<ChatFeed>(ctx);
            var health = S<HealthReporter>(ctx);
            ctx.Response.Headers["Content-Type"] = "text/event-stream";
            ctx.Response.Headers["Cache-Control"] = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no"; // nginx: do not buffer the stream
            var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var writeLock = new SemaphoreSlim(1, 1);
            var token = ctx.RequestAborted;

            async Task WriteEvent(string type, object data)
            {
                await writeLock.WaitAsync(token);
                try
                {
                    await ctx.Response.WriteAsync($"event: {type}\ndata: {JsonSerializer.Serialize(data, jsonOptions)}\n\n", token);
                    await ctx.Response.Body.FlushAsync(token);
                }
                finally { writeLock.Release(); }
            }

            var reader = feed.Subscribe(out var id);
            try
            {
                await WriteEvent("health", health.BuildSnapshot());
                var healthLoop = Task.Run(async () =>
                {
                    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
                    while (await timer.WaitForNextTickAsync(token)) await WriteEvent("health", health.BuildSnapshot());
                }, token);
                await foreach (var ev in reader.ReadAllAsync(token)) await WriteEvent(ev.Type, ev.Data);
                await healthLoop;
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            finally { feed.Unsubscribe(id); }
        }

        public sealed class FilterSaveRequest { public List<string> Lines { get; set; } }
        public sealed class QuizRequest { public string Question { get; set; } public string Answer { get; set; } public int Prize { get; set; } }
    }
}
