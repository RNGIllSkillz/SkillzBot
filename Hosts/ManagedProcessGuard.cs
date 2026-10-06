using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Hosts
{
    /// <summary>
    /// A channel process run by the hub ends itself when the hub is gone (killed, crashed, or stopped without the
    /// chance to stop its children), so no orphan keeps the port, the chat identity and the EventSub session.
    /// </summary>
    public sealed class ManagedProcessGuard : BackgroundService
    {
        public const string EnvHubPid = "ENV_SKILLZBOT_HUB_PID";
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

        [DllImport("libc")] private static extern int getppid();

        private readonly IHostApplicationLifetime _lifetime;
        private readonly ILogger<ManagedProcessGuard> _logger;

        public ManagedProcessGuard(IHostApplicationLifetime lifetime, ILogger<ManagedProcessGuard> logger) { _lifetime = lifetime; _logger = logger; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (OperatingSystem.IsWindows() || !int.TryParse(Environment.GetEnvironmentVariable(EnvHubPid), out int hubPid) || hubPid <= 0) return;
            using var timer = new PeriodicTimer(Interval);
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    int parent;
                    try { parent = getppid(); }
                    catch (Exception ex) { _logger.LogWarning("[Hub] cannot read the parent pid ({Message}); orphan guard off.", ex.Message); return; }
                    if (parent == hubPid) continue;
                    _logger.LogWarning("[Hub] the hub process {HubPid} is gone (parent is now {Parent}); shutting down.", hubPid, parent);
                    _lifetime.StopApplication();
                    return;
                }
            }
            catch (OperationCanceledException) { }
        }
    }
}
