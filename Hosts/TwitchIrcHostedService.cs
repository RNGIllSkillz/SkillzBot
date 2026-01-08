using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkillzBot.Interfaces;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace SkillzBot.Hosts
{
    public class TwitchIrcHostedService : BackgroundService
    {
        private readonly ITtvIRCClient _ircClient;
        private readonly ILogger<TwitchIrcHostedService> _logger;

        public TwitchIrcHostedService(ITtvIRCClient ircClient, ILogger<TwitchIrcHostedService> logger)
        {
            _ircClient = ircClient;
            _logger = logger;
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
                        bool success = await _ircClient.InitializeAsync().ConfigureAwait(false);
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
                    // Check every 10 seconds
                    await Task.Delay(10000, stoppingToken);

                    if (!_ircClient.IsConnected && _ircClient.IsInitialized)
                    {
                        _logger.LogWarning("IRC client disconnected, attempting reconnect...");
                        await _ircClient.ReconnectAsync().ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during IRC health check");
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
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
    }
}