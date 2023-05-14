using SkillzBot.WRITERS;
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
using System.IO;
using SkillzBot.JSON.StreamElements;
using SkillzBot.Singleton;
using SkillzBot.IRC;
using TwitchLib.PubSub.Models.Responses;
using TwitchLib.PubSub.Models.Responses.Messages.AutomodCaughtMessage;
using SkillzBot.Utils;

namespace SkillzBot.API.StreamElements
{
    internal class StreamElementsAPI
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly IllSingleton singleton = IllSingleton.GetInstance();
        private static readonly bool ValidToken = false;
        static StreamElementsAPI()
        {
            Console.Write("Initializing StreamElements API... ");
            if (singleton.StreamElementsApiToken == null)
            {
                Console.WriteLine();
                Console.WriteLine("No valid StreamElements API token. StreamElements API functionality is offline");
                return;
            }
            ValidToken = true;
            httpClient.BaseAddress = new Uri("https://api.streamelements.com/kappa/v2/");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", singleton.StreamElementsApiToken);
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
                using var request = new HttpRequestMessage(HttpMethod.Post, $"songrequest/{singleton.StreamElementsID}/queue")
                {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };
                using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                if (ex is WebException webEx)
                {
                    if (webEx.Response is HttpWebResponse httpResp)
                    {
                        TtvIRCClient.SendMessage($"StreamElements API Error: {Convert.ToString(httpResp.StatusCode)}");
                        Log.WriteLog(null, $"SendMediaAsync() {httpResp.StatusCode}");
                    }
                }
                else
                    Log.WriteLog(ex, $"SendMediaAsync({youTubeVideoId})");
                return false;
            }
        }
        public static async Task<MediaHistoryJSON> GetHistory(CancellationToken cancellationToken = default)
        {
            if (!ValidToken) return null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"songrequest/{singleton.StreamElementsID}/history?limit=1&offset=0");
                using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return JsonConvert.DeserializeObject<MediaHistoryJSON>(jsonResponse);
                }
                else
                {
                    TtvIRCClient.SendMessage($"StreamElements API Error: {Convert.ToString(response.StatusCode)}");
                    Log.WriteLog(null, $"getFirstInHistory() {Convert.ToString(response.StatusCode)}");
                }
                return null;
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetHistory()");
                return null;
            }
        }
        public static async Task<List<MediaQueueJson>> GetQueue(CancellationToken cancellationToken = default)
        {
            if (!ValidToken) return null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"songrequest/{singleton.StreamElementsID}/queue");
                using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return JsonConvert.DeserializeObject<List<MediaQueueJson>>(jsonResponse);  
                }
                else
                {
                    TtvIRCClient.SendMessage($"StreamElements API Error: {Convert.ToString(response.StatusCode)}");
                    Log.WriteLog(null, $"getTrackQueue() {Convert.ToString(response.StatusCode)}");
                }
                return null;
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetQueue()");
                return null;
            }
        }
        public static async Task<StreamElementsJSON> GetCurrentSong(CancellationToken cancellationToken = default)
        {
            if (!ValidToken) return null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"songrequest/{singleton.StreamElementsID}/playing");
                using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return JsonConvert.DeserializeObject<StreamElementsJSON>(jsonResponse);
                }
                else
                {
                    TtvIRCClient.SendMessage($"StreamElements API Error: {Convert.ToString(response.StatusCode)}");
                    Log.WriteLog(null, $"GetCurrentSong() {Convert.ToString(response.StatusCode)}");
                }
                return null;
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetCurrentSong()");
                return null;
            }            
        }
    }
}
