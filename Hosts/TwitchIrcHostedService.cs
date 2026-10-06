using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using System;
using SkillzBot.Interfaces;
using SkillzBot.IllSkillzBot;
using SkillzBot.Services;

namespace SkillzBot.Hosts
{
    public class TwitchIrcHostedService : BackgroundService
    {
        private readonly ITtvIRCClient _ircClient;
        private readonly IllChatMessageHandler _messageHandler;
        private readonly ILogger<TwitchIrcHostedService> _logger;

        private CancellationTokenSource _loopCts;
        private Task _loopTask;

        private static readonly TimeSpan MonitorInterval = TimeSpan.FromSeconds(15);
        // Twitch sends an IRC PING roughly every five minutes, so eight silent minutes means the socket is dead.
        private static readonly TimeSpan ZombieThreshold = TimeSpan.FromMinutes(8);
        // A socket that still answers PINGs can have lost the channel on Twitch's side ("ghost"): no PRIVMSG ever
        // arrives again while everything looks connected. A live chat that says nothing for this long is treated
        // as a ghost and reconnected; the wait doubles after each reconnect that brought no messages (cap x8).
        private static readonly TimeSpan SilentLiveThreshold = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan SilentOfflineThreshold = TimeSpan.FromHours(3);
        private readonly IBotStateService _botState;
        private int _ghostStrikes;

        public TwitchIrcHostedService(
            ITtvIRCClient ircClient,
            IllChatMessageHandler messageHandler,
            IBotStateService botState,
            ILogger<TwitchIrcHostedService> logger)
        {
            _ircClient = ircClient;
            _messageHandler = messageHandler;
            _botState = botState;
            _logger = logger;
        }

        /// <summary>The reconnect decision, kept pure so it can be tested: "zombie" (socket silent), "ghost" (socket alive, chat silent), "down" or null.</summary>
        internal static string Evaluate(bool connected, TimeSpan sinceTraffic, TimeSpan sinceMessage, bool online, int ghostStrikes)
        {
            if (!connected) return "down";
            if (sinceTraffic > ZombieThreshold) return "zombie";
            var threshold = (online ? SilentLiveThreshold : SilentOfflineThreshold) * Math.Pow(2, Math.Min(ghostStrikes, 3));
            return sinceMessage > threshold ? "ghost" : null;
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _ircClient.OnMessageReceived += _messageHandler.HandleMessage;
            _loopCts = new CancellationTokenSource();
            _loopTask = Task.Run(() => RunProcessingLoopAsync(_loopCts.Token));
            return base.StartAsync(cancellationToken);
        }

        /// <summary>
        /// Keeps the chat processing loop alive for the lifetime of the service; if it ever
        /// dies from an unexpected exception it is restarted instead of silently stopping chat handling.
        /// </summary>
        private async Task RunProcessingLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await _messageHandler.StartProcessingLoop(token);
                    return;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(ex, "Chat processing loop crashed. Restarting in 1s.");
                    try { await Task.Delay(1000, token); }
                    catch (OperationCanceledException) { return; }
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _ircClient.OnMessageReceived -= _messageHandler.HandleMessage;
            _logger.LogInformation("Stopping Twitch IRC service...");
            _loopCts?.Cancel();
            try
            {
                _ircClient?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing IRC client during stop");
            }
            await base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting Twitch IRC Monitor Loop...");

            await TryConnectAsync();

            using var timer = new PeriodicTimer(MonitorInterval);

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    var timeSinceLastActivity = DateTimeOffset.UtcNow - _ircClient.LastActivity;
                    var timeSinceLastMessage = DateTimeOffset.UtcNow - _ircClient.LastChatMessage;
                    bool online = _botState.Current.BroadcasterIsOnline;
                    if (timeSinceLastMessage < SilentLiveThreshold) _ghostStrikes = 0; // chat is flowing again

                    string verdict = Evaluate(_ircClient.IsConnected, timeSinceLastActivity, timeSinceLastMessage, online, _ghostStrikes);
                    if (verdict == null) continue;

                    switch (verdict)
                    {
                        case "zombie":
                            _logger.LogWarning("IRC Zombie Connection detected! No traffic for {Minutes:F1} minutes. Forcing Reconnect...", timeSinceLastActivity.TotalMinutes);
                            break;
                        case "ghost":
                            _ghostStrikes++;
                            _logger.LogWarning("IRC ghost connection suspected: socket alive (traffic {Traffic} ago, in channel: {InChannel}) but no chat message for {Silence} while the stream is {State}. Reconnecting (strike {Strike}).",
                                HealthState.FormatAge(timeSinceLastActivity), _ircClient.InChannel, HealthState.FormatAge(timeSinceLastMessage), online ? "LIVE" : "offline", _ghostStrikes);
                            break;
                        default:
                            _logger.LogWarning("Twitch IRC Disconnected detected by Monitor. Attempting Reconnect...");
                            break;
                    }

                    await TryConnectAsync(verdict != "down");
                }
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown
            }
        }

        private async Task TryConnectAsync(bool isZombie = false)
        {
            try
            {
                bool success = isZombie
                    ? await _ircClient.ReconnectAsync()
                    : await _ircClient.InitializeAsync();

                if (!success)
                {
                    _logger.LogWarning("IRC Connection attempt failed. Will retry in next tick.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error during IRC connection attempt.");
            }
        }
    }
}
