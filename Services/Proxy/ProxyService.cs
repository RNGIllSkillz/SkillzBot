using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkillzBot.IllConfiguration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Services.Proxy
{
    public enum ProxyMode { Disabled, Direct, NativeVless, Sidecar }

    /// <summary>
    /// Owns the outbound proxy for selected services (YouTube by default). Depending on the
    /// configured link it either hands HttpClient a plain http/socks proxy, tunnels through a
    /// native VLESS connection, or runs an xray/hysteria sidecar and proxies through its SOCKS port.
    /// </summary>
    public sealed class ProxyService : IHostedService, IDisposable
    {
        private readonly BotConfigModel _config;
        private readonly IPathProvider _paths;
        private readonly ILogger<ProxyService> _logger;
        private readonly HashSet<string> _applyTo;

        private ProxyEndpoint _endpoint;
        private VlessConnector _vless;
        private SidecarProcess _sidecar;
        private volatile Uri _proxyUri;
        private volatile bool _ready;

        public ProxyMode Mode { get; private set; } = ProxyMode.Disabled;
        public ProxyEndpoint Endpoint => _endpoint;
        public bool IsReady => Mode == ProxyMode.Disabled || _ready;

        public ProxyService(BotConfigModel config, IPathProvider paths, ILogger<ProxyService> logger)
        {
            _config = config;
            _paths = paths;
            _logger = logger;
            _applyTo = new HashSet<string>(config.ProxyApplyTo ?? new[] { "youtube" }, StringComparer.OrdinalIgnoreCase);
            Configure();
        }

        /// <summary>Decides the mode from config only; nothing is started here.</summary>
        private void Configure()
        {
            if (string.IsNullOrWhiteSpace(_config.ProxyUrl))
            {
                Mode = ProxyMode.Disabled;
                return;
            }

            if (!ProxyEndpoint.TryParse(_config.ProxyUrl, out _endpoint, out var error))
            {
                _logger.LogError("ProxyUrl is invalid ({Error}); proxy disabled.", error);
                Mode = ProxyMode.Disabled;
                return;
            }

            switch (_endpoint.Kind)
            {
                case ProxyKind.Http:
                case ProxyKind.Socks:
                    _proxyUri = new Uri(_endpoint.Original.Split('#')[0]);
                    Mode = ProxyMode.Direct;
                    _ready = true;
                    break;

                case ProxyKind.Vless:
                case ProxyKind.Hysteria2:
                    var core = LocateCore(_endpoint.Kind);
                    if (core != null)
                    {
                        Mode = ProxyMode.Sidecar;
                        _sidecar = new SidecarProcess(_logger, _endpoint, core, Path.Combine(_paths.DataPath, "proxy"));
                        _proxyUri = new Uri($"socks5://127.0.0.1:{_sidecar.SocksPort}");
                    }
                    else if (!_endpoint.RequiresCore)
                    {
                        Mode = ProxyMode.NativeVless;
                        _vless = new VlessConnector(_endpoint, _logger);
                        _ready = true;
                    }
                    else
                    {
                        string need = _endpoint.Kind == ProxyKind.Hysteria2 ? "hysteria" : "xray";
                        _logger.LogError("ProxyUrl {Link} needs the {Core} binary (REALITY, XTLS flow, non tcp/ws transport or Hysteria are not supported natively). Set ProxyCorePath or put '{Core}' on PATH. Proxy disabled.", _endpoint, need, need);
                        Mode = ProxyMode.Disabled;
                    }
                    break;
            }
        }

        private string LocateCore(ProxyKind kind)
        {
            string wanted = kind == ProxyKind.Hysteria2 ? "hysteria" : "xray";

            if (!string.IsNullOrWhiteSpace(_config.ProxyCorePath))
            {
                var path = _config.ProxyCorePath;
                if (Directory.Exists(path)) path = Path.Combine(path, wanted);
                if (File.Exists(path)) return path;
                if (File.Exists(path + ".exe")) return path + ".exe";
                _logger.LogWarning("ProxyCorePath '{Path}' does not contain a '{Core}' binary.", _config.ProxyCorePath, wanted);
            }

            var candidates = new List<string>
            {
                Path.Combine(AppContext.BaseDirectory, wanted),
                Path.Combine(_paths.SharedPath, wanted),
                Path.Combine(_paths.DataPath, "proxy", wanted),
            };
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                candidates.Add(Path.Combine(dir, wanted));

            foreach (var c in candidates)
            {
                if (File.Exists(c)) return c;
                if (File.Exists(c + ".exe")) return c + ".exe";
            }
            return null;
        }

        public bool AppliesTo(string purpose) =>
            Mode != ProxyMode.Disabled && (_applyTo.Contains("all") || _applyTo.Contains(purpose));

        /// <summary>
        /// Primary handler for a named HTTP client. Routed through the proxy only when the
        /// purpose is listed in ProxyApplyTo; otherwise a plain pooled handler.
        /// </summary>
        public SocketsHttpHandler CreateHandler(string purpose, Action<SocketsHttpHandler> configure = null)
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            };
            configure?.Invoke(handler);

            if (!AppliesTo(purpose)) return handler;

            switch (Mode)
            {
                case ProxyMode.Direct:
                case ProxyMode.Sidecar:
                    handler.UseProxy = true;
                    handler.Proxy = new DynamicWebProxy(this);
                    break;
                case ProxyMode.NativeVless:
                    handler.UseProxy = false;
                    handler.ConnectCallback = (ctx, ct) => _vless.ConnectAsync(ctx.DnsEndPoint.Host, ctx.DnsEndPoint.Port, ct);
                    break;
            }
            return handler;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            switch (Mode)
            {
                case ProxyMode.Disabled:
                    _logger.LogInformation("Outbound proxy: disabled.");
                    return;
                case ProxyMode.Direct:
                    _logger.LogInformation("Outbound proxy: {Proxy} for [{Targets}].", _proxyUri, string.Join(",", _applyTo));
                    return;
                case ProxyMode.NativeVless:
                    _logger.LogInformation("Outbound proxy: native VLESS via {Endpoint} ({Network}/{Security}) for [{Targets}].", _endpoint, _endpoint.Network, _endpoint.Security, string.Join(",", _applyTo));
                    return;
                case ProxyMode.Sidecar:
                    try
                    {
                        _ready = await _sidecar.StartAsync(cancellationToken);
                        if (_ready)
                            _logger.LogInformation("Outbound proxy: {Core} sidecar via {Endpoint}, SOCKS5 {Proxy}, for [{Targets}].", _sidecar.DisplayName, _endpoint, _proxyUri, string.Join(",", _applyTo));
                        else
                            _logger.LogError("Proxy sidecar {Core} did not open its SOCKS port in time; requests will fail until it does.", _sidecar.DisplayName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to start proxy sidecar.");
                    }
                    return;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _sidecar?.Stop();
            return Task.CompletedTask;
        }

        /// <summary>One-line status for the health heartbeat and !service.</summary>
        public string Describe()
        {
            string targets = $"[{string.Join(",", _applyTo)}]";
            return Mode switch
            {
                ProxyMode.Disabled => "disabled",
                ProxyMode.Direct => $"{_proxyUri} {targets}",
                ProxyMode.NativeVless => $"vless-native {_endpoint.BareHost}:{_endpoint.Port} {targets}",
                ProxyMode.Sidecar => $"{_sidecar.DisplayName} {(_sidecar.IsRunning ? "up" : "DOWN")} socks5:{_sidecar.SocksPort} restarts={_sidecar.Restarts} {targets}",
                _ => "?",
            };
        }

        public void Dispose()
        {
            _sidecar?.Dispose();
        }

        /// <summary>Resolves the proxy URI per request so a restarted sidecar is picked up.</summary>
        private sealed class DynamicWebProxy : IWebProxy
        {
            private readonly ProxyService _owner;
            public DynamicWebProxy(ProxyService owner) { _owner = owner; }
            public ICredentials Credentials { get; set; }
            public Uri GetProxy(Uri destination) => _owner._proxyUri ?? destination;
            public bool IsBypassed(Uri host) => _owner._proxyUri == null;
        }
    }
}
