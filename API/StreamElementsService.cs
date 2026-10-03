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

        // Outbound chat queue. Bounded so an outage cannot pile up thousands of stale replies,
        // and messages older than MaxMessageAge are dropped rather than sent out of context.
        private readonly record struct QueuedMessage(string Text, DateTimeOffset EnqueuedAt);
        private readonly Channel<QueuedMessage> _messageQueue;
        private readonly CancellationTokenSource _shutdownCts;

        private const int QueueCapacity = 100;
        private const int MSG_RATE_LIMIT_MS = 250;
        private static readonly TimeSpan MaxMessageAge = TimeSpan.FromSeconds(45);
        private long _lastDropLogTicks = 0;

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

            _messageQueue = Channel.CreateBounded<QueuedMessage>(new BoundedChannelOptions(QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
            _shutdownCts = new CancellationTokenSource();

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

        public Task<MediaHistoryJSON> GetHistory(CancellationToken token = default) =>
            GetJsonAsync<MediaHistoryJSON>($"songrequest/{_config.StreamElementsID}/history?limit=1&offset=0", "GetHistory", token);

        public Task<List<MediaQueueJson>> GetQueue(CancellationToken token = default) =>
            GetJsonAsync<List<MediaQueueJson>>($"songrequest/{_config.StreamElementsID}/queue", "GetQueue", token);

        public Task<StreamElementsJSON> GetCurrentSong(CancellationToken token = default) =>
            GetJsonAsync<StreamElementsJSON>($"songrequest/{_config.StreamElementsID}/playing", "GetCurrentSong", token);

        private async Task<T> GetJsonAsync<T>(string relativeUrl, string operationName, CancellationToken token) where T : class
        {
            if (!_validToken) return null;

            try
            {
                using var response = await _httpClient.GetAsync(relativeUrl, token);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("{Operation} failed: {StatusCode}", operationName, response.StatusCode);
                    return null;
                }

                var jsonResponse = await response.Content.ReadAsStringAsync(token);
                if (string.IsNullOrWhiteSpace(jsonResponse)) return null;
                return JsonConvert.DeserializeObject<T>(jsonResponse);
            }
            catch (TaskCanceledException) when (!token.IsCancellationRequested)
            {
                _logger.LogWarning("{Operation} timed out.", operationName);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Operation} Exception", operationName);
                return null;
            }
        }

        public Task SendChatMessage(string message, CancellationToken token = default)
        {
            if (!_validToken || string.IsNullOrWhiteSpace(message)) return Task.CompletedTask;
            _messageQueue.Writer.TryWrite(new QueuedMessage(message, DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }

        private async Task ProcessQueueAsync()
        {
            _logger.LogInformation("StreamElements Message Queue Started.");
            try
            {
                while (await _messageQueue.Reader.WaitToReadAsync(_shutdownCts.Token))
                {
                    while (_messageQueue.Reader.TryRead(out var msg))
                    {
                        if (DateTimeOffset.UtcNow - msg.EnqueuedAt > MaxMessageAge)
                        {
                            LogDropped(msg);
                            continue;
                        }

                        await SendWithRetryAsync(msg.Text);
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

        private void LogDropped(QueuedMessage msg)
        {
            long now = DateTime.UtcNow.Ticks;
            if (now - Interlocked.Read(ref _lastDropLogTicks) > TimeSpan.FromSeconds(30).Ticks)
            {
                Interlocked.Exchange(ref _lastDropLogTicks, now);
                _logger.LogWarning("Dropping stale chat message queued {Age:F0}s ago: {Text}", (DateTimeOffset.UtcNow - msg.EnqueuedAt).TotalSeconds, msg.Text);
            }
        }

        private async Task SendWithRetryAsync(string message)
        {
            const int attempts = 2;
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    await PerformApiPostAsync(message);
                    return;
                }
                catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (attempt < attempts && (ex is HttpRequestException || ex is TaskCanceledException))
                {
                    // Typically a pooled connection the server already closed, or a slow response. One retry is enough.
                    _logger.LogWarning("StreamElements send failed ({Type}); retrying once.", ex.GetType().Name);
                    await Task.Delay(1000, _shutdownCts.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send queued message via StreamElements");
                    return;
                }
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
