using Microsoft.Extensions.Logging;
using SkillzBot.API.Twitch;
using SkillzBot.IllSTRINGS;
using SkillzBot.Interfaces;
using SkillzBot.IllConfiguration; 
using System;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.EventSub.Core.EventArgs.Channel;

namespace SkillzBot.IRC
{
    sealed class TtvIRCClientService : ITtvIRCClient
    {
        private readonly ILogger<TtvIRCClientService> _logger;
        private readonly IDatabaseService _databaseService;
        private readonly BotConfigModel _config;
        private readonly IGameStateService _gameState;
        private readonly IBotStateService _botState;
        private readonly IStreamElementsService _streamElementsService;

        private TwitchClient _client;
        private bool _isInitialized = false;
        private bool _isDisposed = false;
        private readonly object _lockObject = new object();
        private readonly SemaphoreSlim _connectionSemaphore = new SemaphoreSlim(1, 1);

        private const int MAX_RETRIES = 3;
        private const int BASE_DELAY_MS = 1000;
        private const int SMALL_DELAY_MS = 100;
        private const int CONNECTION_TIMEOUT_SECONDS = 15;
        private const int MESSAGE_MAX_LENGTH = 500;

        public event Func<OnMessageReceivedArgs, Task> OnMessageReceived;

        public TtvIRCClientService(
            ILogger<TtvIRCClientService> logger,
            IDatabaseService database,
            BotConfigModel config,
            IGameStateService gameState,
            IBotStateService botState,
            IStreamElementsService streamElementsService)
        {
            _logger = logger;
            _databaseService = database;
            _logger.LogDebug("TtvIRCClient logger initialized");
            _config = config;
            _gameState = gameState;
            _botState = botState;
            _streamElementsService = streamElementsService;
        }

        public async Task<bool> InitializeAsync()
        {
            if (_isDisposed) return false;
            if (_isInitialized) return true;

            await _connectionSemaphore.WaitAsync();
            try
            {
                if (_isInitialized) return true;
                _logger?.LogInformation("Initializing Twitch IRC client...");
                return await ConnectToTwitchAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Exception during Twitch IRC client initialization");
                return false;
            }
            finally
            {
                _connectionSemaphore.Release();
            }
        }

        private async Task<bool> ConnectToTwitchAsync()
        {
            if (string.IsNullOrWhiteSpace(_config?.BotTwitchName) ||
                string.IsNullOrWhiteSpace(_config?.BotTwitchAuth) ||
                string.IsNullOrWhiteSpace(_config?.ChannelName))
            {
                _logger?.LogError("Missing required Twitch configuration.");
                return false;
            }

            for (int attempt = 1; attempt <= MAX_RETRIES; attempt++)
            {
                try
                {
                    DisposeClient();

                    var credentials = new ConnectionCredentials(
                        _config.BotTwitchName,
                        _config.BotTwitchAuth);

                    _client = new TwitchClient();
                    RegisterEventHandlers();
                    _client.Initialize(credentials, _config.ChannelName);

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(CONNECTION_TIMEOUT_SECONDS));
                    await _client.ConnectAsync();

                    await Task.Delay(2000, CancellationToken.None);

                    if (_client.IsConnected)
                    {
                        lock (_lockObject) _isInitialized = true;
                        _logger?.LogInformation("Successfully connected to Twitch IRC.");
                        return true;
                    }
                }
                catch (TaskCanceledException)
                {
                    _logger?.LogWarning("Connection attempt {Attempt}/{Max} timed out.", attempt, MAX_RETRIES);
                }
                catch (Exception e)
                {
                    _logger?.LogError("Connection error attempt {Attempt}/{Max}: {Message}", attempt, MAX_RETRIES, e.Message);
                }

                if (attempt < MAX_RETRIES)
                {
                    int delayMs = BASE_DELAY_MS * attempt;
                    try
                    {
                        await Task.Delay(delayMs, CancellationToken.None);
                    }
                    catch { /* ignore cancellation */ }
                }
            }

            _logger?.LogError("Failed to connect to Twitch IRC after {Max} attempts.", MAX_RETRIES);
            return false;
        }

        public async Task<bool> ReconnectAsync()
        {
            await _connectionSemaphore.WaitAsync();
            try
            {
                if (_isDisposed) return false;
                return await ConnectToTwitchAsync();
            }
            finally
            {
                _connectionSemaphore.Release();
            }
        }

        private void RegisterEventHandlers()
        {
            if (_client == null) return;
            _client.OnMessageReceived += Client_OnMessageReceived;
            _client.OnUserTimedout += Client_OnUserTimedout;
            _client.OnDisconnected += Client_OnDisconnected;
            _client.OnConnected += Client_OnConnected;
        }

        private void DisposeClient()
        {
            if (_client != null)
            {
                try
                {
                    _client.OnMessageReceived -= Client_OnMessageReceived;
                    _client.OnUserTimedout -= Client_OnUserTimedout;
                    _client.OnDisconnected -= Client_OnDisconnected;
                    _client.OnConnected -= Client_OnConnected;

                    if (_client.IsConnected)
                    {
                        _ = _client.DisconnectAsync();
                    }
                }
                catch { }
                finally { _client = null; }
            }
        }

        public bool IsConnected => _client?.IsConnected ?? false;
        public bool IsInitialized => _isInitialized && !_isDisposed;

        private async Task Client_OnConnected(object sender, OnConnectedEventArgs e)
        {
            var channelName = _config.ChannelName;
            _logger?.LogInformation("Twitch IRC Socket Connected as {Username}. Joining channel: {Channel}", e.BotUsername, channelName);

            try
            {
                await _client.JoinChannelAsync(channelName);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to join channel {Channel}", channelName);
            }
        }
        private async Task Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            if (OnMessageReceived != null)
            {
                await OnMessageReceived.Invoke(e);
            }
        }
        /*
        private async Task Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            try
            {
                _logger?.LogDebug("Message received from {User}: {Message}", e.ChatMessage.Username, e.ChatMessage.Message);

                var user = await _illChatMessageHandler.MessageHandler(e);
                if (user != null)
                {
                    await _databaseService.UpdateUserAsync(user);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling message from {User}", e.ChatMessage?.Username);
            }
        }*/

        private async Task Client_OnUserTimedout(object sender, OnUserTimedoutArgs e)
        {
            try
            {
                _logger?.LogInformation("User {Username} timed out for {Duration} seconds", e.UserTimeout.Username, e.UserTimeout.TimeoutDuration);

                await UserTimedoutEventTask(e);

                if (e.UserTimeout.TimeoutDuration.TotalSeconds > 50000)
                {
                    await SendMessage("o7");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling timeout for user {Username}", e.UserTimeout?.Username);
            }
        }

        private async Task Client_OnDisconnected(object sender, OnDisconnectedArgs e)
        {
            _logger?.LogWarning("Twitch IRC disconnected. Waiting for Hosted Service to reconnect...");
            await Task.CompletedTask;
        }

        private async Task UserTimedoutEventTask(OnUserTimedoutArgs e)
        {
            if (e?.UserTimeout?.Username == null) return;

            try
            {
                var user = await _databaseService.GetUserAsync(e.UserTimeout.Username);
                if (user.dbID != -404)
                {
                    user.UvalTimer = e.UserTimeout.TimeoutDuration.TotalSeconds + DateTimeOffset.Now.ToUnixTimeSeconds();
                    user.UvalCon++;
                    await _databaseService.UpdateUserAsync(user);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing timeout for user {Username}", e.UserTimeout.Username);
            }
        }

        public async Task OnStreamDown()
        {
            try
            {
                _logger?.LogInformation("Processing stream down event");

                _botState.Current.BroadcasterIsOnline = false;
                _botState.Current.FirstQuizOfTheDay = true;

                // Simple check if user is online, avoiding deep logic here if possible
                string chatMessage = _gameState.Current.EarnedLP < 0 ? STRINGS.OnStreadDownLowLP : STRINGS.OnStreadDownHighLP;
                await SendMessage(chatMessage);

                // Discord notification remains
                //await _discordClient.SendEmbedMsg("Stream Ended", "", _gameState.Current.SummonerName, "", "", null, false, "");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing stream down event");
            }
        }

        public async Task OnStreamUp()
        {
            try
            {
                _logger?.LogInformation("Processing stream up event");
                _botState.Current.BroadcasterIsOnline = true;
                await SendMessage(string.Format(STRINGS.OnStreamUP, _config.ChannelName));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing stream up event");
            }
        }

        public async Task OnUnban(ChannelUnbanArgs e)
        {
            try
            {
                if (e?.Payload?.Event == null) return;
                _logger?.LogInformation("User {Username} unbanned by {Moderator}", e.Payload.Event.UserName, e.Payload.Event.ModeratorUserLogin);
                await SendMessage(string.Format(STRINGS.OnUnban, e.Payload.Event.ModeratorUserLogin, e.Payload.Event.UserName));
            }
            catch (Exception ex) { _logger?.LogError(ex, "Error processing unban event"); }
        }

        public async Task SendMessage(string messageToSend, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(messageToSend) || _botState.Current.IsSilent || !IsConnected) return;
            try
            {
                if (messageToSend.Length <= MESSAGE_MAX_LENGTH)
                    await SendSingleMessage(messageToSend, cancellationToken);
                else
                    await SendLongMessage(messageToSend, cancellationToken);
            }
            catch (Exception ex) { _logger?.LogError(ex, "SendMessage error"); }
        }

        private async Task SendSingleMessage(string message, CancellationToken cancellationToken)
        {
            try { await _streamElementsService.SendChatMessage(message, cancellationToken); }
            catch (Exception ex) { _logger?.LogError(ex, "SendSingleMessage error"); }
        }

        private async Task SendLongMessage(string message, CancellationToken cancellationToken)
        {
            int startIndex = 0;
            while (startIndex < message.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int length = Math.Min(MESSAGE_MAX_LENGTH, message.Length - startIndex);
                if (length == MESSAGE_MAX_LENGTH && message[startIndex + length - 1] != ' ')
                {
                    int lastSpace = message.LastIndexOf(' ', startIndex + length - 1, length);
                    if (lastSpace != -1) length = lastSpace - startIndex;
                }
                await SendSingleMessage(message.Substring(startIndex, length), cancellationToken);
                startIndex += length;
                if (startIndex < message.Length) await Task.Delay(SMALL_DELAY_MS, cancellationToken);
            }
        }

        public void Dispose()
        {
            lock (_lockObject)
            {
                if (_isDisposed) return;
                _isDisposed = true;
                _isInitialized = false;
            }
            DisposeClient();
            _connectionSemaphore?.Dispose();
        }
    }
}