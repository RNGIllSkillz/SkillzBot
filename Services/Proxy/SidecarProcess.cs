using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Services.Proxy
{
    /// <summary>
    /// Runs and supervises an xray or hysteria client binary with a generated config.
    /// Restarts it with backoff if it exits, and kills it when the bot stops.
    /// </summary>
    public sealed class SidecarProcess : IDisposable
    {
        private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan MaxRestartBackoff = TimeSpan.FromSeconds(60);

        private readonly ILogger _logger;
        private readonly string _exePath;
        private readonly string _configPath;
        private readonly string _arguments;
        private readonly string _displayName;

        private Process _process;
        private int _restarts;
        private volatile bool _stopping;
        private readonly SemaphoreSlim _startLock = new SemaphoreSlim(1, 1);

        public int SocksPort { get; }
        public int Restarts => _restarts;
        public bool IsRunning => _process != null && !_process.HasExited;
        public string DisplayName => _displayName;

        public SidecarProcess(ILogger logger, ProxyEndpoint endpoint, string exePath, string workDir)
        {
            _logger = logger;
            _exePath = exePath;
            SocksPort = GetFreePort();
            Directory.CreateDirectory(workDir);

            if (endpoint.Kind == ProxyKind.Hysteria2)
            {
                _displayName = "hysteria";
                _configPath = Path.Combine(workDir, "hysteria-client.json");
                File.WriteAllText(_configPath, CoreConfigBuilder.BuildHysteria(endpoint, SocksPort));
                _arguments = $"client -c \"{_configPath}\"";
            }
            else
            {
                _displayName = "xray";
                _configPath = Path.Combine(workDir, "xray-client.json");
                File.WriteAllText(_configPath, CoreConfigBuilder.BuildXray(endpoint, SocksPort));
                _arguments = $"run -c \"{_configPath}\"";
            }
        }

        /// <summary>Starts the core and waits until its SOCKS port accepts connections.</summary>
        public async Task<bool> StartAsync(CancellationToken ct)
        {
            await _startLock.WaitAsync(ct);
            try
            {
                if (IsRunning) return true;
                LaunchProcess();
            }
            finally
            {
                _startLock.Release();
            }
            return await WaitForPortAsync(ct);
        }

        private void LaunchProcess()
        {
            var psi = new ProcessStartInfo
            {
                FileName = _exePath,
                Arguments = _arguments,
                WorkingDirectory = Path.GetDirectoryName(_configPath),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => LogCoreLine(e.Data);
            process.ErrorDataReceived += (_, e) => LogCoreLine(e.Data);
            process.Exited += OnProcessExited;

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
            _logger.LogInformation("Proxy sidecar {Core} started (pid {Pid}), SOCKS5 on 127.0.0.1:{Port}.", _displayName, process.Id, SocksPort);
        }

        private void LogCoreLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            var level = line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
                ? LogLevel.Warning
                : LogLevel.Debug;
            _logger.Log(level, "[{Core}] {Line}", _displayName, line.Trim());
        }

        private void OnProcessExited(object sender, EventArgs e)
        {
            if (_stopping) return;
            int code = -1;
            try { code = _process?.ExitCode ?? -1; } catch { }
            int restarts = Interlocked.Increment(ref _restarts);
            var backoff = TimeSpan.FromSeconds(Math.Min(MaxRestartBackoff.TotalSeconds, 2 * Math.Pow(2, Math.Min(restarts - 1, 5))));
            _logger.LogWarning("Proxy sidecar {Core} exited with code {Code}; restarting in {Seconds}s (restart #{N}).", _displayName, code, backoff.TotalSeconds, restarts);

            _ = Task.Run(async () =>
            {
                await Task.Delay(backoff);
                if (_stopping) return;
                try
                {
                    await _startLock.WaitAsync();
                    try
                    {
                        if (!IsRunning) LaunchProcess();
                    }
                    finally { _startLock.Release(); }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restart proxy sidecar {Core}.", _displayName);
                }
            });
        }

        private async Task<bool> WaitForPortAsync(CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + ReadyTimeout;
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                if (!IsRunning) return false;
                try
                {
                    using var probe = new TcpClient();
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(1000);
                    await probe.ConnectAsync(IPAddress.Loopback, SocksPort, timeout.Token);
                    return true;
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    await Task.Delay(250, ct);
                }
            }
            return false;
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void Stop()
        {
            _stopping = true;
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(3000);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping proxy sidecar {Core}.", _displayName);
            }
        }

        public void Dispose()
        {
            Stop();
            _process?.Dispose();
            _startLock.Dispose();
        }
    }
}
