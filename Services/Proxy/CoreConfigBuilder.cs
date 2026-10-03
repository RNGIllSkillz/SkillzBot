using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace SkillzBot.Services.Proxy
{
    /// <summary>
    /// Generates client configs for the sidecar cores from a parsed share link. Each config
    /// exposes a SOCKS5 inbound on 127.0.0.1 that the bot's HTTP clients use.
    /// </summary>
    public static class CoreConfigBuilder
    {
        public static string BuildXray(ProxyEndpoint ep, int socksPort)
        {
            if (ep.Kind != ProxyKind.Vless) throw new ArgumentException("xray config builder supports vless links", nameof(ep));

            var user = new JObject { ["id"] = ep.Auth, ["encryption"] = "none" };
            if (!string.IsNullOrEmpty(ep.Flow)) user["flow"] = ep.Flow;

            var stream = new JObject
            {
                ["network"] = ep.Network == "splithttp" ? "xhttp" : ep.Network,
                ["security"] = ep.Security,
            };

            if (ep.IsTls)
            {
                var tls = new JObject { ["serverName"] = ep.Sni, ["allowInsecure"] = ep.Insecure };
                if (!string.IsNullOrEmpty(ep.Fingerprint)) tls["fingerprint"] = ep.Fingerprint;
                var alpn = ep.Param("alpn");
                if (!string.IsNullOrEmpty(alpn)) tls["alpn"] = new JArray(alpn.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                stream["tlsSettings"] = tls;
            }
            else if (ep.IsReality)
            {
                stream["realitySettings"] = new JObject
                {
                    ["serverName"] = ep.Sni,
                    ["fingerprint"] = ep.Fingerprint ?? "chrome",
                    ["publicKey"] = ep.PublicKey ?? "",
                    ["shortId"] = ep.ShortId,
                    ["spiderX"] = ep.SpiderX,
                };
            }

            switch (ep.Network)
            {
                case "ws":
                    {
                        var ws = new JObject { ["path"] = ep.Path };
                        if (!string.IsNullOrEmpty(ep.WsHost)) ws["host"] = ep.WsHost;
                        stream["wsSettings"] = ws;
                        break;
                    }
                case "grpc":
                    stream["grpcSettings"] = new JObject { ["serviceName"] = ep.ServiceName ?? "" };
                    break;
                case "xhttp":
                case "splithttp":
                    {
                        var x = new JObject { ["path"] = ep.Path };
                        if (!string.IsNullOrEmpty(ep.WsHost)) x["host"] = ep.WsHost;
                        if (!string.IsNullOrEmpty(ep.XhttpMode)) x["mode"] = ep.XhttpMode;
                        stream["xhttpSettings"] = x;
                        break;
                    }
                case "httpupgrade":
                    {
                        var hu = new JObject { ["path"] = ep.Path };
                        if (!string.IsNullOrEmpty(ep.WsHost)) hu["host"] = ep.WsHost;
                        stream["httpupgradeSettings"] = hu;
                        break;
                    }
                case "tcp":
                    if (string.Equals(ep.Param("headerType"), "http", StringComparison.OrdinalIgnoreCase))
                    {
                        var tcp = new JObject { ["header"] = new JObject { ["type"] = "http" } };
                        if (!string.IsNullOrEmpty(ep.WsHost))
                            tcp["header"]["request"] = new JObject { ["headers"] = new JObject { ["Host"] = new JArray(ep.WsHost) } };
                        stream["tcpSettings"] = tcp;
                    }
                    break;
            }

            var config = new JObject
            {
                ["log"] = new JObject { ["loglevel"] = "warning" },
                ["inbounds"] = new JArray
                {
                    new JObject
                    {
                        ["tag"] = "socks-in",
                        ["listen"] = "127.0.0.1",
                        ["port"] = socksPort,
                        ["protocol"] = "socks",
                        ["settings"] = new JObject { ["auth"] = "noauth", ["udp"] = false },
                    }
                },
                ["outbounds"] = new JArray
                {
                    new JObject
                    {
                        ["tag"] = "proxy",
                        ["protocol"] = "vless",
                        ["settings"] = new JObject
                        {
                            ["vnext"] = new JArray
                            {
                                new JObject
                                {
                                    ["address"] = ep.BareHost,
                                    ["port"] = ep.Port,
                                    ["users"] = new JArray { user },
                                }
                            }
                        },
                        ["streamSettings"] = stream,
                    },
                    new JObject { ["tag"] = "direct", ["protocol"] = "freedom" },
                },
            };
            return config.ToString(Formatting.Indented);
        }

        public static string BuildHysteria(ProxyEndpoint ep, int socksPort)
        {
            if (ep.Kind != ProxyKind.Hysteria2) throw new ArgumentException("hysteria config builder supports hysteria2 links", nameof(ep));

            var config = new JObject
            {
                ["server"] = $"{ep.Host}:{ep.PortSpec}",
                ["auth"] = ep.Auth,
                ["tls"] = new JObject { ["sni"] = ep.Sni, ["insecure"] = ep.Insecure },
                ["socks5"] = new JObject { ["listen"] = $"127.0.0.1:{socksPort}" },
                ["lazy"] = true,
            };
            if (!string.IsNullOrEmpty(ep.PinSha256)) config["tls"]["pinSHA256"] = ep.PinSha256;
            if (string.Equals(ep.Obfs, "salamander", StringComparison.OrdinalIgnoreCase))
            {
                config["obfs"] = new JObject
                {
                    ["type"] = "salamander",
                    ["salamander"] = new JObject { ["password"] = ep.ObfsPassword ?? "" },
                };
            }
            if (!string.IsNullOrEmpty(ep.UpMbps) || !string.IsNullOrEmpty(ep.DownMbps))
            {
                var bw = new JObject();
                if (!string.IsNullOrEmpty(ep.UpMbps)) bw["up"] = $"{ep.UpMbps} mbps";
                if (!string.IsNullOrEmpty(ep.DownMbps)) bw["down"] = $"{ep.DownMbps} mbps";
                config["bandwidth"] = bw;
            }
            return config.ToString(Formatting.Indented);
        }
    }
}
