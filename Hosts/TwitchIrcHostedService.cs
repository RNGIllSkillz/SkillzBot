using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using System;
using SkillzBot.Interfaces;
using SkillzBot.IllSkillzBot;

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

        public TwitchIrcHostedService(
            ITtvIRCClient ircClient,
            IllChatMessageHandler messageHandler,
            ILogger<TwitchIrcHostedService> logger)
        {
            _ircClient = ircClient;
            _messageHandler = messageHandler;
            _logger = logger;
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
                    bool libDisconnected = !_ircClient.IsConnected;
                    bool isZombie = _ircClient.IsConnected && timeSinceLastActivity > ZombieThreshold;

                    if (!libDisconnected && !isZombie) continue;

                    if (isZombie)
                        _logger.LogWarning("IRC Zombie Connection detected! No traffic for {Minutes:F1} minutes. Forcing Reconnect...", timeSinceLastActivity.TotalMinutes);
                    else
                        _logger.LogWarning("Twitch IRC Disconnected detected by Monitor. Attempting Reconnect...");

                    await TryConnectAsync(isZombie);
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
