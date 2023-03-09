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

namespace SkillzBot.API.StreamElements
{
    internal class StreamElementsAPI
    {
        private static readonly HttpClient httpClient = new HttpClient();
        static StreamElementsAPI()
        {
            httpClient.BaseAddress = new Uri("https://api.streamelements.com/kappa/v2/");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IllSingleton.GetInstance().StreamElementsApiToken);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
        public static async Task<bool> SendMediaAsync(string youTubeVideoId, CancellationToken cancellationToken = default)
        {
            try
            {
                var payload = new { video = youTubeVideoId };
                var jsonPayload = JsonConvert.SerializeObject(payload);
                using var request = new HttpRequestMessage(HttpMethod.Post, "songrequest/5de7b07e268e83750da21881/queue")
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
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "songrequest/5de7b07e268e83750da21881/history?limit=1&offset=0");
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
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "songrequest/5de7b07e268e83750da21881/queue");
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
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "songrequest/5de7b07e268e83750da21881/playing");
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
            /*
            try
            {
                String url = "https://api.streamelements.com/kappa/v2/songrequest/5de7b07e268e83750da21881/playing";
                HttpWebRequest HttpWebRequest = (HttpWebRequest)WebRequest.Create(url);
                HttpWebRequest.UserAgent = "<Linux>:<IllSkillz_bot>:<v1.5>";
                using HttpWebResponse HttpWebResponse = (HttpWebResponse)HttpWebRequest.GetResponse();
                Stream streamResponse = HttpWebResponse.GetResponseStream();
                using StreamReader streamRead = new StreamReader(streamResponse);
                Char[] readBuff = new Char[256];
                string JSONResponse = "";
                int count = await streamRead.ReadAsync(readBuff, 0, 256).ConfigureAwait(false);
                while (count > 0)
                {
                    String outputData = new String(readBuff, 0, count);
                    JSONResponse += outputData;
                    count = await streamRead.ReadAsync(readBuff, 0, 256).ConfigureAwait(false);
                }
                return JsonConvert.DeserializeObject<StreamElementsJSON>(JSONResponse);
            }
            catch (Exception e)
            {
                Log.WriteLog(e, "getTreck()");
                return null;
            }*/
        }
    }
}
