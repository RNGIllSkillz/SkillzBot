using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkillzBot.IllSkillzBot;
using SkillzBot.Interfaces;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Services
{
    /// <summary>
    /// Writes one "Health" line every few minutes so a log excerpt shows at a glance whether
    /// IRC, EventSub, the database and the chat loop were alive around an incident.
    /// </summary>
    public sealed class HealthReporter : BackgroundService
    {
        private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

        private readonly ITtvIRCClient _irc;
        private readonly IllChatMessageHandler _chat;
        private readonly HealthState _health;
        private readonly IBotStateService _botState;
        private readonly ILogger<HealthReporter> _logger;

        public HealthReporter(
            ITtvIRCClient irc,
            IllChatMessageHandler chat,
            HealthState health,
            IBotStateService botState,
            ILogger<HealthReporter> logger)
        {
            _irc = irc;
            _chat = chat;
            _health = health;
            _botState = botState;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(InitialDelay, stoppingToken);
                using var timer = new PeriodicTimer(Interval);
                do
                {
                    try { _logger.LogInformation("{Health}", BuildLine()); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Health line failed"); }
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown
            }
        }

        public string BuildLine()
        {
            using var process = Process.GetCurrentProcess();
            var uptime = DateTime.Now - process.StartTime;
            double ramMb = GC.GetTotalMemory(false) / 1024.0 / 1024.0;
            var (pending, processed, buffered) = _chat.GetStats();
            var s = _botState.Current;

            string irc = _irc.IsConnected ? "connected" : "DOWN";
            string eventSub = _health.EventSubConnected ? "connected" : "DOWN";
            string db = _health.DbCircuitOpen ? "CIRCUIT-OPEN" : "ok";

            return $"Health | up={HealthState.FormatAge(uptime)} ram={ramMb:F0}MB threads={process.Threads.Count}" +
                   $" | irc={irc} traffic={HealthState.FormatAge(DateTimeOffset.UtcNow - _irc.LastActivity)}" +
                   $" | eventsub={eventSub} since={HealthState.FormatAge(_health.EventSubSinceUtc)} lastEvent={HealthState.FormatAge(_health.EventSubLastEventUtc)} reconnects={_health.EventSubReconnects}" +
                   $" | chat pending={pending} processed={processed} buffered={buffered}" +
                   $" | db={db} failures={_health.DbFailures}" +
                   $" | silent={s.IsSilent} sub={s.IsSubActive} filter={s.ChatFilterLvl} autopred={s.AutoPred} inMatch={s.InMatch} online={s.BroadcasterIsOnline}";
        }
    }
}
