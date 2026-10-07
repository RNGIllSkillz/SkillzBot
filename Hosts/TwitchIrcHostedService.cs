using SkillzBot.Services.Chat;
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
        private readonly ChatIngress _ingress;
        private int _ghostStrikes;

        public TwitchIrcHostedService(
            ITtvIRCClient ircClient,
            IllChatMessageHandler messageHandler,
            IBotStateService botState,
            ChatIngress ingress,
            ILogger<TwitchIrcHostedService> logger)
        {
            _ingress = ingress;
            _ircClient = ircClient;
            _messageHandler = messageHandler;
            _botState = botState;
            _logger = logger;
        }

        /// <summary>
        /// The reconnect decision, kept pure so it can be tested: "park" (EventSub carries chat), "down" (connect),
        /// "zombie" (socket silent), "ghost" (socket alive, chat silent) or null (nothing to do).
        /// </summary>
        internal static string Evaluate(bool connected, TimeSpan sinceTraffic, TimeSpan sinceMessage, bool online, int ghostStrikes, IrcPolicy policy = IrcPolicy.Required)
        {
            if (policy == IrcPolicy.Unwanted) return connected ? "park" : null;
            if (!connected) return policy == IrcPolicy.Required ? "down" : null;
            if (sinceTraffic > ZombieThreshold) return "zombie";
            var threshold = (online ? SilentLiveThreshold : SilentOfflineThreshold) * Math.Pow(2, Math.Min(ghostStrikes, 3));
            return sinceMessage > threshold ? "ghost" : null;
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _ingress.MessageReceived += _messageHandler.HandleMessage;
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
            _ingress.MessageReceived -= _messageHandler.HandleMessage;
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

            if (_ingress.Decide(DateTime.UtcNow) == IrcPolicy.Required) await TryConnectAsync();
            else _logger.LogInformation("IRC not started: ChatTransport={Mode}.", _ingress.Mode);

            using var timer = new PeriodicTimer(MonitorInterval);

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    var timeSinceLastActivity = DateTimeOffset.UtcNow - _ircClient.LastActivity;
                    var timeSinceLastMessage = DateTimeOffset.UtcNow - _ircClient.LastChatMessage;
                    bool online = _botState.Current.BroadcasterIsOnline;
                    if (timeSinceLastMessage < SilentLiveThreshold) _ghostStrikes = 0; // chat is flowing again

                    await WatchEventSubChatAsync(online);

                    string verdict = Evaluate(_ircClient.IsConnected, timeSinceLastActivity, timeSinceLastMessage, online, _ghostStrikes, _ingress.Decide(DateTime.UtcNow));
                    if (verdict == null) continue;
                    if (verdict == "park")
                    {
                        _ghostStrikes = 0;
                        await _ircClient.ParkAsync();
                        continue;
                    }

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

        private int _eventSubStrikes;

        /// <summary>
        /// EventSub has keepalives, but a session can also just stop delivering chat. The same silence rule as for IRC
        /// applies: no message for 10 minutes on a live stream (doubling per strike, cap x8) asks the EventSub service
        /// for a fresh session, which re-subscribes chat.
        /// </summary>
        private async Task WatchEventSubChatAsync(bool online)
        {
            if (!_ingress.EventSubChatActive) return; // a session being rebuilt keeps its strike count
            var since = _ingress.EventSubChatSinceUtc ?? DateTime.MinValue;
            var last = _ingress.LastMessageUtc;
            var silence = DateTime.UtcNow - (last > since ? last : since); // a fresh session gets a full threshold
            if (silence < SilentLiveThreshold || !online) { _eventSubStrikes = 0; return; }
            var threshold = SilentLiveThreshold * Math.Pow(2, Math.Min(_eventSubStrikes, 3));
            if (silence <= threshold) return;
            var recovery = _ingress.EventSubRecovery;
            if (recovery == null) return;
            _eventSubStrikes++;
            _logger.LogWarning("EventSub chat silent for {Silence} on a live stream; asking for a fresh EventSub session (strike {Strike}).", HealthState.FormatAge(silence), _eventSubStrikes);
            try { await recovery($"no chat message for {HealthState.FormatAge(silence)} while live"); }
            catch (Exception ex) { _logger.LogError(ex, "EventSub recovery request failed."); }
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
