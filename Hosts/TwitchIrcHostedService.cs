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
            _logger.LogInformation("Starting Twitch IRC Hosted Service Loop...");

            // Initial Connection Loop
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!_ircClient.IsConnected)
                    {
                        bool success = await _ircClient.InitializeAsync();
                        if (success)
                        {
                            _logger.LogInformation("Twitch IRC connected successfully.");
                            break; // Exit initial loop, move to maintenance loop
                        }
                        else
                        {
                            _logger.LogWarning("Twitch IRC connection failed. Retrying in 5s...");
                        }
                    }
                    else
                    {
                        break; // Already connected
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during initial IRC connection.");
                }

                await Task.Delay(5000, stoppingToken);
            }

            // Maintenance Loop
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(10000, stoppingToken);
                    if (_ircClient.IsInitialized && !_ircClient.IsConnected)
                    {
                        _logger.LogWarning("Monitor detected disconnect. Forcing reconnect...");
                        await _ircClient.ReconnectAsync();
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during IRC health check");
                }
            }
        }

    }
}