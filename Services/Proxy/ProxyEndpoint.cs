using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SkillzBot.Services.Proxy
{
    public enum ProxyKind { Http, Socks, Vless, Hysteria2 }

    /// <summary>
    /// A parsed proxy share link: http(s)://, socks5://, vless://uuid@host:port?params#name,
    /// hysteria2://auth@host:port/?params#name (hy2:// is accepted too).
    /// </summary>
    public sealed class ProxyEndpoint
    {
        private static readonly Regex LinkPattern = new Regex(
            @"^(?<scheme>[a-zA-Z][a-zA-Z0-9+.-]*)://(?:(?<auth>[^@/?#]*)@)?(?<host>\[[^\]]+\]|[^/?#:]+)(?::(?<port>[0-9][0-9,\-]*))?(?<path>/[^?#]*)?(?:\?(?<query>[^#]*))?(?:#(?<name>.*))?$",
            RegexOptions.Compiled);

        public ProxyKind Kind { get; private set; }
        public string Scheme { get; private set; }
        public string Host { get; private set; }
        /// <summary>Port as written in the link; Hysteria allows ranges such as "20000-30000".</summary>
        public string PortSpec { get; private set; }
        public int Port { get; private set; }
        /// <summary>UUID for VLESS, password for Hysteria, user:pass for http/socks (may be empty).</summary>
        public string Auth { get; private set; }
        public string Name { get; private set; }
        public string Original { get; private set; }
        public IReadOnlyDictionary<string, string> Params => _params;
        private readonly Dictionary<string, string> _params = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string Param(string key, string fallback = null) => _params.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : fallback;
        public bool Flag(string key) => Param(key) is string v && (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));

        // VLESS stream settings
        public string Network => Param("type", "tcp").ToLowerInvariant();
        public string Security => Param("security", "none").ToLowerInvariant();
        public string Sni => Param("sni") ?? Param("host") ?? Host.Trim('[', ']');
        public string Flow => Param("flow");
        public string Fingerprint => Param("fp");
        public string PublicKey => Param("pbk");
        public string ShortId => Param("sid", "");
        public string SpiderX => Param("spx", "/");
        public string Path => Param("path", "/");
        public string WsHost => Param("host");
        public string ServiceName => Param("serviceName");
        public string XhttpMode => Param("mode");
        public bool Insecure => Flag("allowInsecure") || Flag("insecure");

        // Hysteria2 settings
        public string Obfs => Param("obfs");
        public string ObfsPassword => Param("obfs-password");
        public string PinSha256 => Param("pinSHA256");
        public string UpMbps => Param("upmbps");
        public string DownMbps => Param("downmbps");

        public bool IsTls => Security == "tls";
        public bool IsReality => Security == "reality";

        /// <summary>
        /// True when the link needs a real core (xray / hysteria): REALITY, XTLS flows,
        /// non-tcp/ws transports, port hopping, or Hysteria altogether.
        /// </summary>
        public bool RequiresCore =>
            Kind == ProxyKind.Hysteria2 ||
            (Kind == ProxyKind.Vless && (IsReality || !string.IsNullOrEmpty(Flow) || (Network != "tcp" && Network != "ws") || (Security != "tls" && Security != "none")));

        public static bool TryParse(string link, out ProxyEndpoint endpoint, out string error)
        {
            endpoint = null;
            error = null;
            if (string.IsNullOrWhiteSpace(link)) { error = "empty link"; return false; }

            var m = LinkPattern.Match(link.Trim());
            if (!m.Success) { error = "link does not look like scheme://[auth@]host[:port][?params][#name]"; return false; }

            var ep = new ProxyEndpoint
            {
                Original = link.Trim(),
                Scheme = m.Groups["scheme"].Value.ToLowerInvariant(),
                Host = m.Groups["host"].Value,
                PortSpec = m.Groups["port"].Success ? m.Groups["port"].Value : null,
                Auth = m.Groups["auth"].Success ? Uri.UnescapeDataString(m.Groups["auth"].Value) : "",
                Name = m.Groups["name"].Success ? Uri.UnescapeDataString(m.Groups["name"].Value) : "",
            };

            switch (ep.Scheme)
            {
                case "http": case "https": ep.Kind = ProxyKind.Http; break;
                case "socks": case "socks5": case "socks5h": case "socks4": case "socks4a": ep.Kind = ProxyKind.Socks; break;
                case "vless": ep.Kind = ProxyKind.Vless; break;
                case "hysteria2": case "hy2": ep.Kind = ProxyKind.Hysteria2; break;
                default: error = $"unsupported scheme '{ep.Scheme}' (supported: http, socks5, vless, hysteria2)"; return false;
            }

            if (m.Groups["query"].Success)
            {
                foreach (var pair in m.Groups["query"].Value.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = pair.IndexOf('=');
                    string key = Uri.UnescapeDataString(eq < 0 ? pair : pair.Substring(0, eq));
                    string value = eq < 0 ? "" : Uri.UnescapeDataString(pair.Substring(eq + 1));
                    ep._params[key] = value;
                }
            }

            if (ep.PortSpec == null)
            {
                ep.Port = ep.Kind switch { ProxyKind.Http => ep.Scheme == "https" ? 443 : 80, ProxyKind.Socks => 1080, _ => 443 };
                ep.PortSpec = ep.Port.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                // First number of a range/list is the primary port.
                var first = ep.PortSpec.Split(new[] { ',', '-' }, 2)[0];
                if (!int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port < 1 || port > 65535)
                {
                    error = $"invalid port '{ep.PortSpec}'"; return false;
                }
                ep.Port = port;
                if (ep.Kind != ProxyKind.Hysteria2 && ep.PortSpec != first)
                {
                    error = "port ranges are only supported for hysteria2"; return false;
                }
            }

            if (ep.Kind == ProxyKind.Vless && string.IsNullOrEmpty(ep.Auth)) { error = "vless link has no UUID"; return false; }
            if (ep.Kind == ProxyKind.Hysteria2 && string.IsNullOrEmpty(ep.Auth)) { error = "hysteria2 link has no auth password"; return false; }

            endpoint = ep;
            return true;
        }

        /// <summary>Hostname without IPv6 brackets.</summary>
        public string BareHost => Host.Trim('[', ']');

        public override string ToString() => $"{Scheme}://{Host}:{PortSpec}" + (string.IsNullOrEmpty(Name) ? "" : $" ({Name})");
    }
}
