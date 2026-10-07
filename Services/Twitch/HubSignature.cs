using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SkillzBot.Services.Twitch
{
    /// <summary>Identity the hub asserts for a proxied request, or "hub" itself for internal calls.</summary>
    public sealed record HubIdentity(string Login, string UserId, bool IsHub, DateTime ExpiresUtc);

    /// <summary>
    /// HMAC-signed identity tokens exchanged between the hub and channel processes over loopback:
    /// base64url(json) + "." + base64url(HMAC-SHA256(json)). Short-lived, no secrets inside.
    /// </summary>
    public static class HubSignature
    {
        public const string Header = "X-SkillzBot-Auth";
        public const string EnvSecret = "ENV_SKILLZBOT_HUB_SECRET";
        public const string EnvManaged = "ENV_SKILLZBOT_MANAGED";
        public const string EnvApiPort = "ENV_API_PORT";
        public const string EnvRole = "ENV_SKILLZBOT_ROLE";

        public static bool IsManagedProcess => Environment.GetEnvironmentVariable(EnvManaged) == "1";
        public static bool IsHubProcess => string.Equals(Environment.GetEnvironmentVariable(EnvRole), "hub", StringComparison.OrdinalIgnoreCase);

        public static string Sign(string secret, HubIdentity identity)
        {
            string json = JsonSerializer.Serialize(new { login = identity.Login, id = identity.UserId, hub = identity.IsHub, exp = new DateTimeOffset(identity.ExpiresUtc).ToUnixTimeSeconds() });
            string payload = B64(Encoding.UTF8.GetBytes(json));
            return payload + "." + B64(Mac(secret, payload));
        }

        /// <summary>Null when the token is malformed, forged or expired.</summary>
        public static HubIdentity Verify(string secret, string token)
        {
            if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(token)) return null;
            int dot = token.IndexOf('.');
            if (dot <= 0) return null;
            string payload = token.Substring(0, dot), mac = token.Substring(dot + 1);
            byte[] expected = Mac(secret, payload), given;
            try { given = UnB64(mac); } catch { return null; }
            if (given.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(given, expected)) return null;
            try
            {
                using var doc = JsonDocument.Parse(UnB64(payload));
                var r = doc.RootElement;
                var exp = DateTimeOffset.FromUnixTimeSeconds(r.GetProperty("exp").GetInt64()).UtcDateTime;
                if (exp < DateTime.UtcNow) return null;
                return new HubIdentity(r.GetProperty("login").GetString(), r.GetProperty("id").GetString(), r.TryGetProperty("hub", out var h) && h.GetBoolean(), exp);
            }
            catch { return null; }
        }

        private static byte[] Mac(string secret, string payload)
        {
            using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            return h.ComputeHash(Encoding.UTF8.GetBytes(payload));
        }
        private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static byte[] UnB64(string s)
        {
            string t = s.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(t.PadRight(t.Length + (4 - t.Length % 4) % 4, '='));
        }
    }
}
