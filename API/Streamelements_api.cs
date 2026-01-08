using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using SkillzBot.JSON.MediaHistory;
using SkillzBot.JSON.MediaQueue;
using Newtonsoft.Json;
using System.Net;
using System.Threading;
using SkillzBot.JSON.StreamElements;
using SkillzBot.IRC;
using Microsoft.Extensions.Logging;
using SkillzBot.Hosts;
using SkillzBot.Singleton;
using SkillzBot.Interfaces;

namespace SkillzBot.API.StreamElements
{
    internal class StreamElementsAPI
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly bool ValidToken = false;
        private static readonly ILogger<StreamElementsAPI> _logger = IllServiceProvider.GetLogger<StreamElementsAPI>();
        private static readonly ITtvIRCClient _ircClient = IllServiceProvider.GetService<ITtvIRCClient>();
        static StreamElementsAPI()
        {
            Console.Write("Initializing StreamElements API... ");
            if (IllSingleton.Config.StreamElementsApiToken == null)
            {
                Console.WriteLine();
                Console.WriteLine("No valid StreamElements API token. StreamElements API functionality is offline");
                return;
            }
            ValidToken = true;
            httpClient.BaseAddress = new Uri("https://api.streamelements.com/kappa/v2/");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IllSingleton.Config.StreamElementsApiToken);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            Console.WriteLine("OK.");
        }
        public static async Task<bool> SendMediaAsync(string youTubeVideoId, CancellationToken cancellationToken = default)
        {
            if (!ValidToken) return true;
            try
            {
                var payload = new { video = youTubeVideoId };
                var jsonPayload = JsonConvert.SerializeObject(payload);
                using var request = new HttpRequestMessage(HttpMethod.Post, $"songrequest/{IllSingleton.Config.StreamElementsID}/queue")
                {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };
                using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                if (ex is WebException webEx)
                {
                    if (webEx.Response is HttpWebResponse httpResp)
                    {
                        await _ircClient.SendMessage($"StreamElements API Error: {Convert.ToString(httpResp.StatusCode)}");
                        _logger.LogError(null, $"SendMediaAsync() {httpResp.StatusCode}");
                    }
                }
                else
                    _logger.LogError(ex, $"SendMediaAsync({youTubeVideoId})");
                return false;
            }
        }
        public static async Task<MediaHistoryJSON> GetHistory(CancellationToken cancellationToken = default)
        {
            if (!ValidToken) return null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"songrequest/{IllSingleton.Config.StreamElementsID}/history?limit=1&offset=0");
                using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    return JsonConvert.DeserializeObject<MediaHistoryJSON>(jsonResponse);
                }
                else
                {
                    await _ircClient.SendMessage($"StreamElements API Error: {Convert.ToString(response.StatusCode)}");
                    _logger.LogError("getFirstInHistory() {StatusCode}", Convert.ToString(response.StatusCode));
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetHistory()");
                return null;
            }
        }
        public static async Task<List<MediaQueueJson>> GetQueue(CancellationToken cancellationToken = default)
        {
            if (!ValidToken) return null;
            using var request = new HttpRequestMessage(HttpMethod.Get, $"songrequest/{IllSingleton.Config.StreamElementsID}/queue");
            try
            {
                using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    return JsonConvert.DeserializeObject<List<MediaQueueJson>>(jsonResponse);
                }
                else
                {
                    await _ircClient.SendMessage($"StreamElements API Error: {Convert.ToString(response.StatusCode)}");
                    _logger.LogError("getTrackQueue() {StatusCode}", Convert.ToString(response.StatusCode));
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetQueue()");
                return null;
            }
        }
        public static async Task<StreamElementsJSON> GetCurrentSong(CancellationToken cancellationToken = default)
        {
            if (!ValidToken) return null;
            using var request = new HttpRequestMessage(HttpMethod.Get, $"songrequest/{IllSingleton.Config.StreamElementsID}/playing");
            try
            {
                using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    return JsonConvert.DeserializeObject<StreamElementsJSON>(jsonResponse);
                }
                else
                {
                    await _ircClient.SendMessage($"StreamElements API Error: {Convert.ToString(response.StatusCode)}");
                    _logger.LogError("GetCurrentSong() {StatusCode}", Convert.ToString(response.StatusCode));
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetCurrentSong()");
                return null;
            }
        }
        public static async Task SendChatMessage(string message, CancellationToken cancellationToken = default)
        {
            if (!ValidToken) return;
            var jsonPayload = JsonConvert.SerializeObject(new { message });
            using var request = new HttpRequestMessage(HttpMethod.Post, $"bot/{IllSingleton.Config.StreamElementsID}/say")
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };
            try
            {
                using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    _logger.LogError("SendChatMessage() {StatusCode}", response.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SendChatMessage({message})");
            }
        }
    }
}