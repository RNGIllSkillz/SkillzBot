using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SkillzBot.Services.Twitch
{
    public sealed record TwitchTokenGrant(string AccessToken, string RefreshToken, int ExpiresIn, IReadOnlyList<string> Scopes, string UserId, string Login);

    /// <summary>The two id.twitch.tv calls both the hub and a channel need: exchange a code, validate a token.</summary>
    public static class TwitchOAuthClient
    {
        public static string AuthorizeUrl(string clientId, string redirectUri, string scopes, string state, bool forceVerify) =>
            "https://id.twitch.tv/oauth2/authorize?response_type=code&client_id=" + Uri.EscapeDataString(clientId) +
            "&redirect_uri=" + Uri.EscapeDataString(redirectUri) + "&scope=" + Uri.EscapeDataString(scopes ?? "") +
            "&state=" + Uri.EscapeDataString(state) + (forceVerify ? "&force_verify=true" : "");

        /// <summary>Exchanges the code and validates the result; returns null (with a reason) when Twitch refuses.</summary>
        public static async Task<(TwitchTokenGrant Grant, string Error)> ExchangeAsync(HttpClient client, string clientId, string clientSecret, string code, string redirectUri)
        {
            using var tokenResponse = await client.PostAsync("https://id.twitch.tv/oauth2/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId, ["client_secret"] = clientSecret, ["code"] = code, ["grant_type"] = "authorization_code", ["redirect_uri"] = redirectUri,
            }));
            string body = await tokenResponse.Content.ReadAsStringAsync();
            if (!tokenResponse.IsSuccessStatusCode) return (null, $"token exchange failed: {(int)tokenResponse.StatusCode} {body}");
            using var tokenJson = JsonDocument.Parse(body);
            var token = tokenJson.RootElement;
            string accessToken = token.GetProperty("access_token").GetString();
            string refreshToken = token.TryGetProperty("refresh_token", out var rt) && rt.ValueKind == JsonValueKind.String ? rt.GetString() : null;
            int expiresIn = token.TryGetProperty("expires_in", out var ei) && ei.TryGetInt32(out int e) ? e : 14400;

            using var validate = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
            validate.Headers.TryAddWithoutValidation("Authorization", "OAuth " + accessToken);
            using var validateResponse = await client.SendAsync(validate);
            if (!validateResponse.IsSuccessStatusCode) return (null, "validate failed");
            using var who = JsonDocument.Parse(await validateResponse.Content.ReadAsStringAsync());
            string login = who.RootElement.GetProperty("login").GetString()?.ToLowerInvariant();
            string userId = who.RootElement.GetProperty("user_id").GetString();
            var scopes = who.RootElement.TryGetProperty("scopes", out var sc) && sc.ValueKind == JsonValueKind.Array
                ? sc.EnumerateArray().Select(s => s.GetString()).Where(s => s != null).ToList() : new List<string>();
            return (new TwitchTokenGrant(accessToken, refreshToken, expiresIn, scopes, userId, login), null);
        }

        public static async Task RevokeQuietlyAsync(HttpClient client, string clientId, string accessToken)
        {
            try { await client.PostAsync("https://id.twitch.tv/oauth2/revoke", new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = clientId, ["token"] = accessToken })); } catch { }
        }
    }
}
