using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkillzBot.Services.Twitch;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Hub
{
    public sealed record ProcessStatus(string State, int? Pid, DateTime? StartedUtc, int Restarts, int? LastExitCode, DateTime? LastExitUtc);

    /// <summary>
    /// Runs one process per enabled channel (this same executable with ENV_CHANNEL_NAME set), restarts it when it
    /// exits (a panel restart is just an exit) with a backoff that resets after ten minutes of uptime, and stops
    /// everything on shutdown with SIGTERM first.
    /// </summary>
    public sealed class ChannelSupervisor : BackgroundService
    {
        private sealed class Managed
        {
            public ChannelEntry Entry;
            public Process Process;
            public DateTime? StartedUtc;
            public int Restarts;
            public int? LastExitCode;
            public DateTime? LastExitUtc;
            public DateTime NextStartUtc = DateTime.MinValue;
            public int Backoff;
            public bool StopRequested;
        }

        private static readonly TimeSpan Tick = TimeSpan.FromSeconds(3);
        private readonly ChannelRegistry _registry;
        private readonly HubConfig _hub;
        private readonly string _secret;
        private readonly string _workDir;
        private readonly ILogger<ChannelSupervisor> _logger;
        private readonly ConcurrentDictionary<string, Managed> _running = new ConcurrentDictionary<string, Managed>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Longer than a channel's own shutdown timeout (15s), so a clean exit always wins over the SIGKILL.</summary>
        private static readonly TimeSpan Grace = TimeSpan.FromSeconds(25);
        private volatile bool _shuttingDown;

        public ChannelSupervisor(ChannelRegistry registry, HubConfig hub, HubSecret secret, ILogger<ChannelSupervisor> logger)
        {
            _registry = registry; _hub = hub; _secret = secret.Value; _logger = logger;
            _workDir = AppContext.BaseDirectory;
        }

        public ProcessStatus StatusOf(string login)
        {
            if (!_running.TryGetValue(login, out var m)) return new ProcessStatus("stopped", null, null, 0, null, null);
            bool alive = m.Process != null && !m.Process.HasExited;
            string state = alive ? "running" : m.StopRequested ? "stopped" : m.NextStartUtc > DateTime.UtcNow ? "restarting" : "starting";
            return new ProcessStatus(state, alive ? m.Process.Id : null, alive ? m.StartedUtc : null, m.Restarts, m.LastExitCode, m.LastExitUtc);
        }

        /// <summary>Starts a channel now (also used right after provisioning).</summary>
        public void Ensure(string login) => _ = Reconcile();

        public async Task RestartAsync(string login)
        {
            await _reconcileLock.WaitAsync();
            try
            {
                if (_running.TryGetValue(login, out var m) && m.Process != null && !m.Process.HasExited)
                {
                    _logger.LogWarning("[Hub] restarting channel {Login} (pid {Pid}).", login, m.Process.Id);
                    await TerminateAsync(m.Process, Grace);
                    m.LastExitCode = SafeExitCode(m.Process); m.LastExitUtc = DateTime.UtcNow;
                    m.Process.Dispose(); m.Process = null;
                    m.NextStartUtc = DateTime.UtcNow; m.Backoff = 0; // a requested restart is not a crash
                }
            }
            finally { _reconcileLock.Release(); }
            await Reconcile();
        }

        /// <summary>Stops a channel that is no longer wanted (disabled or removed from the registry first, so the tick does not bring it back).</summary>
        public async Task StopAsync(string login)
        {
            await _reconcileLock.WaitAsync();
            try { await StopCore(login); }
            finally { _reconcileLock.Release(); }
        }

        private async Task StopCore(string login)
        {
            if (_running.TryRemove(login, out var m))
            {
                m.StopRequested = true;
                if (m.Process != null && !m.Process.HasExited) await TerminateAsync(m.Process, Grace);
                _logger.LogWarning("[Hub] channel {Login} stopped.", login);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[Hub] supervisor started; executable {Exe}, work dir {Dir}.", Environment.ProcessPath, _workDir);
            using var timer = new PeriodicTimer(Tick);
            try
            {
                await Reconcile();
                while (await timer.WaitForNextTickAsync(stoppingToken)) await Reconcile();
            }
            catch (OperationCanceledException) { }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _shuttingDown = true;
            await base.StopAsync(cancellationToken); // the tick loop ends first, so nothing gets restarted below
            var live = _running.Values.Where(m => m.Process != null && !m.Process.HasExited).ToList();
            _logger.LogInformation("[Hub] stopping {Count} channel process(es)...", live.Count);
            await Task.WhenAll(live.Select(m => TerminateAsync(m.Process, Grace)));
        }

        private readonly SemaphoreSlim _reconcileLock = new SemaphoreSlim(1, 1);

        private async Task Reconcile()
        {
            if (_shuttingDown || !await _reconcileLock.WaitAsync(0)) return;
            try
            {
                var wanted = _registry.All().Where(c => c.Enabled).ToDictionary(c => c.Login, StringComparer.OrdinalIgnoreCase);
                foreach (var login in _running.Keys.Where(k => !wanted.ContainsKey(k)).ToList()) await StopCore(login);
                foreach (var entry in wanted.Values)
                {
                    var m = _running.GetOrAdd(entry.Login, _ => new Managed { Entry = entry });
                    m.Entry = entry;
                    if (m.Process != null && !m.Process.HasExited) continue;
                    if (m.Process != null)
                    {
                        // It exited since the last tick: record it and schedule the restart with backoff.
                        m.LastExitCode = SafeExitCode(m.Process); m.LastExitUtc = DateTime.UtcNow;
                        bool healthyRun = m.StartedUtc.HasValue && DateTime.UtcNow - m.StartedUtc.Value > TimeSpan.FromMinutes(10);
                        bool planned = m.LastExitCode == 0; // the panel's own "restart" ends the process cleanly
                        m.Backoff = healthyRun || planned ? 0 : Math.Min(m.Backoff + 1, 5);
                        double delay = m.Backoff == 0 ? 3 : Math.Min(60, 3 * Math.Pow(2, m.Backoff));
                        m.NextStartUtc = DateTime.UtcNow.AddSeconds(delay);
                        m.Restarts++;
                        _logger.LogWarning("[Hub] channel {Login} exited with {Code} after {Uptime}; restarting in {Delay}s.", entry.Login, m.LastExitCode, HealthAge(m.StartedUtc), delay);
                        m.Process.Dispose(); m.Process = null;
                    }
                    if (DateTime.UtcNow < m.NextStartUtc) continue;
                    Start(m);
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "[Hub] supervisor pass failed."); }
            finally { _reconcileLock.Release(); }
        }

        private void Start(Managed m)
        {
            var psi = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath,
                WorkingDirectory = _workDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            string entry = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrEmpty(entry) && Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "").Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                psi.ArgumentList.Add(entry); // the hub runs as "dotnet SkillzBot.dll" (development); the published single file needs no argument
            psi.Environment["ENV_CHANNEL_NAME"] = m.Entry.Login;
            psi.Environment[HubSignature.EnvRole] = "channel";
            psi.Environment[HubSignature.EnvManaged] = "1";
            psi.Environment[HubSignature.EnvSecret] = _secret;
            psi.Environment[HubSignature.EnvApiPort] = m.Entry.ApiPort.ToString();
            psi.Environment[Hosts.ManagedProcessGuard.EnvHubPid] = Environment.ProcessId.ToString();
            try
            {
                var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                string tag = m.Entry.Login;
                p.OutputDataReceived += (s, e) => { if (e.Data != null) _logger.LogDebug("[{Channel}] {Line}", tag, e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) _logger.LogWarning("[{Channel} stderr] {Line}", tag, e.Data); };
                p.Start();
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                m.Process = p; m.StartedUtc = DateTime.UtcNow; m.StopRequested = false;
                _logger.LogInformation("[Hub] channel {Login} started (pid {Pid}, port {Port}).", m.Entry.Login, p.Id, m.Entry.ApiPort);
            }
            catch (Exception ex)
            {
                m.NextStartUtc = DateTime.UtcNow.AddSeconds(30);
                _logger.LogError(ex, "[Hub] could not start channel {Login}; retrying in 30s.", m.Entry.Login);
            }
        }

        [DllImport("libc", SetLastError = true, EntryPoint = "kill")]
        private static extern int SysKill(int pid, int sig);

        /// <summary>SIGTERM so the channel shuts down cleanly (flushes state, closes websockets), SIGKILL after the grace period.</summary>
        private async Task TerminateAsync(Process p, TimeSpan grace)
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) SysKill(p.Id, 15);
                else p.CloseMainWindow();
                using var cts = new CancellationTokenSource(grace);
                try { await p.WaitForExitAsync(cts.Token); return; }
                catch (OperationCanceledException) { }
                _logger.LogWarning("[Hub] pid {Pid} did not exit within {Grace}s; killing.", p.Id, grace.TotalSeconds);
                p.Kill(true);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "[Hub] terminate failed for pid {Pid}.", p.Id); }
        }

        private static int? SafeExitCode(Process p) { try { return p.ExitCode; } catch { return null; } }
        private static string HealthAge(DateTime? since) => since.HasValue ? Services.HealthState.FormatAge(DateTime.UtcNow - since.Value) : "?";
    }

    /// <summary>The per-install secret that signs identities between the hub and its channel processes.</summary>
    public sealed class HubSecret
    {
        public string Value { get; }
        public HubSecret(string path)
        {
            if (System.IO.File.Exists(path)) Value = System.IO.File.ReadAllText(path).Trim();
            if (string.IsNullOrEmpty(Value))
            {
                Value = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                System.IO.File.WriteAllText(path, Value);
                try { if (!OperatingSystem.IsWindows()) System.IO.File.SetUnixFileMode(path, System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite); } catch { }
            }
        }
    }
}
