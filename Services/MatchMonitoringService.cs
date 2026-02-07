using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkillzBot.IllSkillzBot;
using SkillzBot.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Services
{
    public class MatchMonitoringService : BackgroundService
    {
        private readonly IllPredictions _illPredictions;
        private readonly IBotStateService _botState;
        private readonly ILogger<MatchMonitoringService> _logger;

        // Check for a new game every 60 seconds if not in a match
        private const int CHECK_INTERVAL_MS = 60000;

        public MatchMonitoringService(
            IllPredictions illPredictions,
            IBotStateService botState,
            ILogger<MatchMonitoringService> logger)
        {
            _illPredictions = illPredictions;
            _botState = botState;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Match Monitoring Service Started.");

            // Initial startup delay to let other services (API, DB) warm up
            await Task.Delay(5000, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Only check if Auto-Predictions are enabled and we aren't already tracking a match.
                    // GetCurrentMatchTask handles the internal "InMatch" logic loop, 
                    // so if a match starts, this call will "block" here until the match ends.
                    if (_botState.Current.AutoPred)
                    {
                        await _illPredictions.GetCurrentMatchTask();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Match Monitoring Loop");
                }
                // Wait before checking again (or checking after a match finished)
                try
                {
                    await Task.Delay(CHECK_INTERVAL_MS, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break; // Stop requested
                }
            }
            _logger.LogInformation("Match Monitoring Service Stopped.");
        }
    }
}