using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkillzBot.Api;
using SkillzBot.Services.Twitch;
using System;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Hub
{
    /// <summary>
    /// /c/{login}/api/... to the channel process on loopback. The hub session becomes a short-lived signed identity
    /// header; the channel decides the role. Responses stream through (the chat SSE feed depends on it).
    /// </summary>
    public sealed class HubProxy
    {
        public const string HttpClientName = "HubProxy";
        private static readonly string[] HopByHop = { "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization", "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Host", "Cookie", "Set-Cookie", "Content-Length" };

        private readonly ChannelRegistry _registry;
        private readonly IHttpClientFactory _http;
        private readonly string _secret;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly ILogger<HubProxy> _logger;

        public HubProxy(ChannelRegistry registry, IHttpClientFactory http, HubSecret secret, IHostApplicationLifetime lifetime, ILogger<HubProxy> logger)
        {
            _registry = registry; _http = http; _secret = secret.Value; _lifetime = lifetime; _logger = logger;
        }

        /// <summary>A signed identity for the hub itself: internal calls into a channel process.</summary>
        public string HubToken() => HubSignature.Sign(_secret, new HubIdentity("hub", "0", true, DateTime.UtcNow.AddMinutes(2)));

        public string UserToken(ClaimsPrincipal user) =>
            user?.Identity?.IsAuthenticated == true
                ? HubSignature.Sign(_secret, new HubIdentity(user.Identity.Name, user.FindFirstValue(ClaimTypes.NameIdentifier), false, DateTime.UtcNow.AddMinutes(2)))
                : null;

        public async Task Forward(HttpContext ctx)
        {
            string login = ctx.Request.RouteValues["login"]?.ToString();
            string rest = ctx.Request.RouteValues["rest"]?.ToString() ?? "";
            var channel = login == null ? null : _registry.Get(login);
            if (channel == null) { ctx.Response.StatusCode = 404; await ctx.Response.WriteAsJsonAsync(new { error = "unknown channel" }); return; }

            var target = new Uri($"http://127.0.0.1:{channel.ApiPort}/api/{rest}{ctx.Request.QueryString}");
            using var upstream = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), target);
            foreach (var h in ctx.Request.Headers)
            {
                if (HopByHop.Contains(h.Key, StringComparer.OrdinalIgnoreCase) || h.Key.Equals(HubSignature.Header, StringComparison.OrdinalIgnoreCase)) continue;
                upstream.Headers.TryAddWithoutValidation(h.Key, h.Value.ToArray());
            }
            string identity = UserToken(ctx.User);
            if (identity != null) upstream.Headers.TryAddWithoutValidation(HubSignature.Header, identity);
            if (ctx.Request.ContentLength > 0 || ctx.Request.Headers.ContainsKey("Transfer-Encoding"))
            {
                upstream.Content = new StreamContent(ctx.Request.Body);
                if (ctx.Request.ContentType != null) upstream.Content.Headers.TryAddWithoutValidation("Content-Type", ctx.Request.ContentType);
            }

            // a long-lived stream (chat SSE) ends when the browser leaves or when the hub shuts down, never later
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted, _lifetime.ApplicationStopping);
            var token = cts.Token;
            HttpResponseMessage response;
            try
            {
                response = await _http.CreateClient(HttpClientName).SendAsync(upstream, HttpCompletionOption.ResponseHeadersRead, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("[Hub] channel {Login} unreachable on port {Port}: {Message}", channel.Login, channel.ApiPort, ex.Message);
                ctx.Response.StatusCode = 502;
                await ctx.Response.WriteAsJsonAsync(new { error = "channel process is not reachable (starting or stopped)" });
                return;
            }
            using (response)
            {
                ctx.Response.StatusCode = (int)response.StatusCode;
                foreach (var h in response.Headers.Concat(response.Content.Headers))
                {
                    if (HopByHop.Contains(h.Key, StringComparer.OrdinalIgnoreCase)) continue;
                    ctx.Response.Headers[h.Key] = h.Value.ToArray();
                }
                ctx.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
                try
                {
                    await using var body = await response.Content.ReadAsStreamAsync(token);
                    var buffer = new byte[16 * 1024];
                    int n;
                    while ((n = await body.ReadAsync(buffer, token)) > 0)
                    {
                        await ctx.Response.Body.WriteAsync(buffer.AsMemory(0, n), token);
                        await ctx.Response.Body.FlushAsync(token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) when (token.IsCancellationRequested) { _logger.LogDebug(ex, "proxy stream ended"); }
            }
        }
    }
}
