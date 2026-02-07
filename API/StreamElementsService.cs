using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SkillzBot.JSON.MediaHistory;
using SkillzBot.JSON.MediaQueue;
using SkillzBot.JSON.StreamElements;
using SkillzBot.IllConfiguration; 
using System;
using System.Collections.Generic;
using System.Threading.Channels;
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

        // Queue Components
        private readonly Channel<string> _messageQueue;
        private readonly CancellationTokenSource _shutdownCts;

        // Settings
        private const int MSG_RATE_LIMIT_MS = 50; // 1.1s delay to be safe


        public StreamElementsService(IHttpClientFactory httpClientFactory, BotConfigModel config, ILogger<StreamElementsService> logger)
        {
            _config = config;
            _logger = logger;

            _validToken = !string.IsNullOrEmpty(_config.StreamElementsApiToken);
            _httpClient = httpClientFactory.CreateClient("StreamElementsClient");

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

            _messageQueue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
            _shutdownCts = new CancellationTokenSource();

            // Start the Background Worker if token is valid
            if (_validToken)
            {
                _ = Task.Run(ProcessQueueAsync);
            }
        }
        private async Task<bool> ExecuteWithRetryAsync(Func<Task<bool>> action, string operationName)
        {
            int retries = 0;
            while (true)
            {
                try
                {
                    return await action();
                }
                catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
                {
                    retries++;
                    if (retries > 3)
                    {
                        _logger.LogError("StreamElements Timeout in {Operation} after 3 retries.", operationName);
                        return false;
                    }
                    _logger.LogWarning("StreamElements Request Timed Out. Retrying {Count}...", retries);
                    await Task.Delay(1000);
                }
                catch (HttpRequestException ex)
                {
                    retries++;
                    if (retries > 3)
                    {
                        _logger.LogError("StreamElements HTTP Error in {Operation}: {Message}", operationName, ex.Message);
                        return false;
                    }
                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in {Operation}", operationName);
                    return false;
                }
            }
        }
        public async Task<bool> SendMediaAsync(string youTubeVideoId, CancellationToken token = default)
        {
            if (!_validToken) return false;

            return await ExecuteWithRetryAsync(async () =>
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
            }, "SendMediaAsync");
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

        public Task SendChatMessage(string message, CancellationToken token = default)
        {
            if (!_validToken || string.IsNullOrWhiteSpace(message)) return Task.CompletedTask;
             _messageQueue.Writer.TryWrite(message);
            return Task.CompletedTask;
        }

        private async Task ProcessQueueAsync()
        {
            _logger.LogInformation("StreamElements Message Queue Started.");
            try
            {
                // Wait for messages to be available
                while (await _messageQueue.Reader.WaitToReadAsync(_shutdownCts.Token))
                {
                    while (_messageQueue.Reader.TryRead(out var msg))
                    {
                        try
                        {
                            await PerformApiPostAsync(msg);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to send queued message via StreamElements");
                        }
                        await Task.Delay(MSG_RATE_LIMIT_MS, _shutdownCts.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "StreamElements Message Loop Crashed");
            }
        }
        private async Task PerformApiPostAsync(string message)
        {
            var payload = new { message };
            var json = JsonConvert.SerializeObject(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.PostAsync($"bot/{_config.StreamElementsID}/say", content, _shutdownCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("StreamElements API Error: {StatusCode}", response.StatusCode);
            }
        }
    }
}