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

        public TwitchIrcHostedService(
            ITtvIRCClient ircClient, 
            IllChatMessageHandler messageHandler, 
            ILogger<TwitchIrcHostedService> logger)
        {
            _ircClient = ircClient;
            _messageHandler = messageHandler;
            _logger = logger;
        }
        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _ircClient.OnMessageReceived += _messageHandler.HandleMessage;
            _ = Task.Run(() => _messageHandler.StartProcessingLoop(cancellationToken), cancellationToken);
            await base.StartAsync(cancellationToken);
        }
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            // Unsubscribe to allow GC
            _ircClient.OnMessageReceived -= _messageHandler.HandleMessage;
            _logger.LogInformation("Stopping Twitch IRC service...");
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

            // Initial Connection
            await TryConnectAsync();

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15)); // Check every 15 seconds

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (stoppingToken.IsCancellationRequested) break;

                var timeSinceLastActivity = DateTimeOffset.UtcNow - _ircClient.LastActivity;
                bool libDisconnected = !_ircClient.IsConnected;
                bool isZombie = _ircClient.IsConnected && timeSinceLastActivity.TotalMinutes > 6;

                if (libDisconnected || isZombie)
                {
                    if (isZombie)
                    {
                        _logger.LogWarning("IRC Zombie Connection detected! No activity for {Time} minutes. Forcing Reconnect...", timeSinceLastActivity.TotalMinutes);
                    }
                    else
                    {
                        _logger.LogWarning("Twitch IRC Disconnected detected by Monitor. Attempting Reconnect...");
                    }

                    // ReconnectAsync calls Dispose internally in your implementation, breaking the zombie socket
                    await TryConnectAsync(isZombie);
                }
            }
        }
        private async Task TryConnectAsync(bool isZombie = false)
        {
            try
            {                
                // InitializeAsync handles connection logic safely internally
                bool success = false;
                if (isZombie)
                    success = await _ircClient.ReconnectAsync();
                else
                    success = await _ircClient.InitializeAsync();

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