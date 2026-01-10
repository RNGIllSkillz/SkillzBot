using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SkillzBot.Interfaces;
using SkillzBot.JSON.MediaHistory;
using SkillzBot.JSON.MediaQueue;
using SkillzBot.JSON.StreamElements;
using SkillzBot.IllConfiguration; 
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.API.StreamElements
{
    public class StreamElementsService : IStreamElementsService
    {
        private readonly HttpClient _httpClient;
        private readonly BotConfigModel _config;
        private readonly ILogger<StreamElementsService> _logger;
        private readonly bool _validToken;

        public StreamElementsService(HttpClient httpClient, BotConfigModel config, ILogger<StreamElementsService> logger)
        {
            _httpClient = httpClient;
            _config = config;
            _logger = logger;

            _validToken = !string.IsNullOrEmpty(_config.StreamElementsApiToken);

            if (_validToken)
            {
                _httpClient.BaseAddress = new Uri("https://api.streamelements.com/kappa/v2/");
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.StreamElementsApiToken);
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            }
            else
            {
                _logger.LogWarning("StreamElements API Token is missing. Service disabled.");
            }
        }

        public async Task<bool> SendMediaAsync(string youTubeVideoId, CancellationToken token = default)
        {
            if (!_validToken) return false;

            try
            {
                var payload = new { video = youTubeVideoId };
                var json = JsonConvert.SerializeObject(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await _httpClient.PostAsync($"songrequest/{_config.StreamElementsID}/queue", content, token);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("SendMediaAsync failed: {StatusCode}", response.StatusCode);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SendMediaAsync Exception");
                return false;
            }
        }

        public async Task<MediaHistoryJSON> GetHistory(CancellationToken token = default)
        {
            if (!_validToken) return null;

            try
            {
                using var response = await _httpClient.GetAsync($"songrequest/{_config.StreamElementsID}/history?limit=1&offset=0", token);

                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync(token);
                    return JsonConvert.DeserializeObject<MediaHistoryJSON>(jsonResponse);
                }
                else
                {
                    _logger.LogError("GetHistory failed: {StatusCode}", response.StatusCode);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetHistory Exception");
                return null;
            }
        }

        public async Task<List<MediaQueueJson>> GetQueue(CancellationToken token = default)
        {
            if (!_validToken) return null;

            try
            {
                using var response = await _httpClient.GetAsync($"songrequest/{_config.StreamElementsID}/queue", token);

                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync(token);
                    return JsonConvert.DeserializeObject<List<MediaQueueJson>>(jsonResponse);
                }
                else
                {
                    _logger.LogError("GetQueue failed: {StatusCode}", response.StatusCode);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetQueue Exception");
                return null;
            }
        }

        public async Task<StreamElementsJSON> GetCurrentSong(CancellationToken token = default)
        {
            if (!_validToken) return null;

            try
            {
                using var response = await _httpClient.GetAsync($"songrequest/{_config.StreamElementsID}/playing", token);

                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync(token);
                    return JsonConvert.DeserializeObject<StreamElementsJSON>(jsonResponse);
                }
                else
                {
                    _logger.LogError("GetCurrentSong failed: {StatusCode}", response.StatusCode);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetCurrentSong Exception");
                return null;
            }
        }

        public async Task SendChatMessage(string message, CancellationToken token = default)
        {
            if (!_validToken || string.IsNullOrWhiteSpace(message)) return;

            try
            {
                var payload = new { message };
                var json = JsonConvert.SerializeObject(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await _httpClient.PostAsync($"bot/{_config.StreamElementsID}/say", content, token);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("SendChatMessage failed: {StatusCode}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SendChatMessage Exception");
            }
        }
    }
}