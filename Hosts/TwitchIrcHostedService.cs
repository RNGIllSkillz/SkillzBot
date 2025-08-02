using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkillzBot.IRC;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace SkillzBot.Hosts
{
    // Add the IRC Hosted Service
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
            try
            {
                _logger.LogInformation("Starting Twitch IRC service...");

                // Initialize the IRC client
                bool initialized = await _ircClient.InitializeAsync().ConfigureAwait(false);
                if (!initialized)
                {
                    _logger.LogError("Failed to initialize Twitch IRC client");
                    return;
                }

                _logger.LogInformation("Twitch IRC service started successfully");

                // Monitor connection health
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(5000, stoppingToken); // Check every 5 seconds

                        // Health check - reconnect if disconnected
                        if (!_ircClient.IsConnected && _ircClient.IsInitialized)
                        {
                            _logger.LogWarning("IRC client disconnected, attempting reconnect...");
                            await _ircClient.ReconnectAsync().ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancellation is requested
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during IRC health check");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                _logger.LogInformation("Twitch IRC service cancellation requested");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error in Twitch IRC hosted service");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping Twitch IRC service...");

            try
            {
                // Give it a moment to clean up gracefully
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5));

                await base.StopAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("IRC service stop operation timed out");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping IRC service");
            }
            finally
            {
                _ircClient?.Dispose();
                _logger.LogInformation("Twitch IRC service stopped");
            }
        }
    }
}