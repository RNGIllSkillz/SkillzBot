using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using urldetector.detection;

namespace SkillzBot.Utils
{
    /// <summary>
    /// Finds real links in chat messages. URL candidates come from the urldetector
    /// library; each candidate host is then confirmed through a cached, time-boxed
    /// DNS lookup so that "lol.ok" is not treated as a link.
    /// </summary>
    public sealed class LinkDetector
    {
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(2);
        private const int MaxCacheEntries = 5000;

        private readonly ILogger<LinkDetector> _logger;
        private readonly ConcurrentDictionary<string, (bool Resolves, DateTime CheckedAt)> _cache =
            new ConcurrentDictionary<string, (bool, DateTime)>(StringComparer.OrdinalIgnoreCase);

        public LinkDetector(ILogger<LinkDetector> logger)
        {
            _logger = logger;
        }

        /// <summary>Number of URL candidates in the message whose host actually resolves.</summary>
        public async Task<int> CountLinksAsync(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return 0;
            if (message.IndexOf('.') < 0 && message.IndexOf("://", StringComparison.Ordinal) < 0) return 0;

            List<urldetector.Url> candidates;
            try
            {
                candidates = new UrlDetector(message, UrlDetectorOptions.Default).Detect();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "UrlDetector failed on message");
                return 0;
            }
            if (candidates == null || candidates.Count == 0) return 0;

            int count = 0;
            var perMessage = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in candidates)
            {
                string host;
                try { host = candidate.GetHost(); }
                catch { continue; }
                if (string.IsNullOrEmpty(host)) continue;
                host = host.TrimEnd('.');

                if (!perMessage.TryGetValue(host, out bool resolves))
                {
                    resolves = await HostResolvesAsync(host).ConfigureAwait(false);
                    perMessage[host] = resolves;
                }
                if (resolves) count++;
            }
            return count;
        }

        private static bool HasPlausibleTld(string host)
        {
            int dot = host.LastIndexOf('.');
            if (dot < 1 || dot == host.Length - 1) return false;
            if (host.Length - dot - 1 < 2) return false;
            for (int i = dot + 1; i < host.Length; i++)
                if (!char.IsLetter(host[i])) return false;
            return true;
        }

        private async Task<bool> HostResolvesAsync(string host)
        {
            if (IPAddress.TryParse(host, out _)) return true;

            if (!HasPlausibleTld(host)) return false;

            var now = DateTime.UtcNow;
            if (_cache.TryGetValue(host, out var cached) && now - cached.CheckedAt < CacheTtl)
                return cached.Resolves;

            bool resolves;
            bool cacheResult = true;
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host).WaitAsync(DnsTimeout).ConfigureAwait(false);
                resolves = addresses.Length > 0;
            }
            catch (TimeoutException)
            {
                _logger.LogDebug("DNS lookup for {Host} timed out", host);
                resolves = false;
                cacheResult = false;
            }
            catch (SocketException)
            {
                resolves = false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "DNS lookup for {Host} failed", host);
                resolves = false;
                cacheResult = false;
            }

            if (cacheResult)
            {
                if (_cache.Count >= MaxCacheEntries) _cache.Clear();
                _cache[host] = (resolves, now);
            }
            return resolves;
        }
    }
}
