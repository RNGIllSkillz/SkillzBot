using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkillzBot.IllSkillzBot;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace SkillzBot.Services
{
    public class MatchMonitoringService : BackgroundService
    {
        private readonly IllPredictions _illPredictions;
        private readonly IBotStateService _botState;
        private readonly ILogger<MatchMonitoringService> _logger;

        // Define the interval here (5 seconds)
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(5);

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

            // PeriodicTimer handles the drift correction automatically
            using var timer = new PeriodicTimer(_interval);

            try
            {
                // This waits for the REMAINDER of the 5 seconds interval
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    try
                    {
                        var sw = Stopwatch.StartNew();

                        if (_botState.Current.AutoPred)
                        {
                            await _illPredictions.GetCurrentMatchTask();
                        }

                        sw.Stop();
                        _logger.LogDebug("GetCurrentMatchTask execution time: {ElapsedMs}ms", sw.ElapsedMilliseconds);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in Match Monitoring Loop");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown
            }

            _logger.LogInformation("Match Monitoring Service Stopped.");
        }
    }
}