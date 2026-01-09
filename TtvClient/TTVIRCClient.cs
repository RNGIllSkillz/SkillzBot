using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkillzBot.API.StreamElements;
using SkillzBot.API.Twitch;
using SkillzBot.Discord;
using SkillzBot.Hosts;
using SkillzBot.IllSkillzBot;
using SkillzBot.IllSkillzBot.IllCommandsNest;
using SkillzBot.IllSTRINGS;
using SkillzBot.Interfaces;
using SkillzBot.Singleton;
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
        private static readonly ILogger<TtvIRCClientService> _logger = IllServiceProvider.GetLogger<TtvIRCClientService>();
        private IDatabaseService _databaseService;
        private readonly IServiceProvider _serviceProvider;
        private IllChatMessageHandler _messageHandler;
        private readonly ITwitchService _twitchService;

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

        public TtvIRCClientService(
            IDatabaseService database,
            IServiceProvider serviceProvider,
            ITwitchService twitchService)
        {
            _databaseService = database;
            _serviceProvider = serviceProvider;
            _logger.LogDebug("TtvIRCClient logger initialized");
            _twitchService = twitchService;
        }

        public async Task<bool> InitializeAsync()
        {
            if (_isDisposed) return false;
            if (_isInitialized) return true;

            await _connectionSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_isInitialized) return true;
                _logger?.LogInformation("Initializing Twitch IRC client...");
                return await ConnectToTwitchAsync().ConfigureAwait(false);
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
            if (string.IsNullOrWhiteSpace(IllSingleton.Config?.BotTwitchName) ||
                string.IsNullOrWhiteSpace(IllSingleton.Config?.BotTwitchAuth) ||
                string.IsNullOrWhiteSpace(IllSingleton.Config?.ChannelName))
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
                        IllSingleton.Config.BotTwitchName,
                        IllSingleton.Config.BotTwitchAuth);

                    _client = new TwitchClient();
                    RegisterEventHandlers();
                    _client.Initialize(credentials, IllSingleton.Config.ChannelName);

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(CONNECTION_TIMEOUT_SECONDS));
                    await _client.ConnectAsync();                    

                    await Task.Delay(2000, CancellationToken.None).ConfigureAwait(false);

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
                        await Task.Delay(delayMs, CancellationToken.None).ConfigureAwait(false);
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
                return await ConnectToTwitchAsync().ConfigureAwait(false);
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

                    // Disconnect is also likely async now, but in Dispose context we fire and forget
                    if (_client.IsConnected)
                    {
                        // Fire and forget disconnect to avoid blocking dispose
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
            var channelName = IllSingleton.Config.ChannelName;
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
            try
            {
                if (_messageHandler == null)
                {
                    _messageHandler = _serviceProvider.GetRequiredService<IllChatMessageHandler>();
                }

                _logger?.LogDebug("Message received from {User}: {Message}", e.ChatMessage.Username, e.ChatMessage.Message);

                var user = await _messageHandler.MessageHandler(e).ConfigureAwait(false);
                if (user != null)
                {
                    await _databaseService.UpdateUserAsync(user).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling message from {User}", e.ChatMessage?.Username);
            }
        }

        private async Task Client_OnUserTimedout(object sender, OnUserTimedoutArgs e)
        {
            try
            {
                _logger?.LogInformation("User {Username} timed out for {Duration} seconds", e.UserTimeout.Username, e.UserTimeout.TimeoutDuration);

                await UserTimedoutEventTask(e).ConfigureAwait(false);

                if (e.UserTimeout.TimeoutDuration.TotalSeconds > 50000)
                {
                    await SendMessage("o7").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling timeout for user {Username}", e.UserTimeout?.Username);
            }
        }

        private async Task Client_OnDisconnected(object sender, OnDisconnectedArgs e)
        {
            _logger?.LogWarning("Twitch IRC disconnected. Attempting to reconnect...");

            lock (_lockObject)
            {
                _isInitialized = false;
            }

            // Fire and forget reconnection logic
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(BASE_DELAY_MS).ConfigureAwait(false);
                    if (!_isDisposed)
                    {
                        await ReconnectAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error during automatic reconnection");
                }
            });
            await Task.CompletedTask;
        }

        private async Task UserTimedoutEventTask(OnUserTimedoutArgs e)
        {
            if (e?.UserTimeout?.Username == null) return;

            try
            {
                var user = await _databaseService.GetUserAsync(e.UserTimeout.Username).ConfigureAwait(false);
                if (user.dbID != -404)
                {
                    user.UvalTimer = e.UserTimeout.TimeoutDuration.TotalSeconds + DateTimeOffset.Now.ToUnixTimeSeconds();
                    user.UvalCon++;
                    await _databaseService.UpdateUserAsync(user).ConfigureAwait(false);
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

                IllSingleton.State.BroadcasterIsOnline = false;
                IllGames.ClearQuizzActiveUsers();
                IllSingleton.State.FirstQuizOfTheDay = true;

                var illCommands = _serviceProvider.GetRequiredService<IllCommands>();
                var lastStats = await illCommands.GetLpAsync();

                string msg = $"Cыграно {IllSingleton.Game.NumGames} игр, из них побед {IllSingleton.Game.NumWins} / поражений {IllSingleton.Game.NumLosses}. Заработано {IllSingleton.Game.EarnedLP} LP";

                string chatMessage;
                string discordTitle;

                if (IllSingleton.Game.EarnedLP < 0)
                {
                    chatMessage = STRINGS.OnStreadDownLowLP;
                    discordTitle = "С позором!";
                }
                else if (IllSingleton.Game.EarnedLP > 0)
                {
                    chatMessage = STRINGS.OnStreadDownHighLP;
                    discordTitle = "Героем!";
                }
                else
                {
                    chatMessage = "Стример офнул PoroSad";
                    discordTitle = "";
                }

                await SendMessage(chatMessage);
                await DiscordClient.SendEmbedMsg(discordTitle, "", IllSingleton.Game.SummonerName,
                    lastStats.RANK, lastStats.LPoints, null, false, msg);
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

                IllSingleton.State.BroadcasterIsOnline = true;
                await SendMessage(string.Format(STRINGS.OnStreamUP, IllSingleton.Config.ChannelName));

                var info = await _twitchService.GetStreamInfo();

                var illCommands = _serviceProvider.GetRequiredService<IllCommands>();
                var lp = await illCommands.GetLpAsync();

                if (info != null)
                {
                    await DiscordClient.SendEmbedMsg(info.Title, info.ThumbnailUrl,
                        IllSingleton.Game.SummonerName, lp.RANK, lp.LPoints);
                }
                else
                {
                    var cInfo = await _twitchService.GetChannelInformationAsync();
                    if (cInfo != null)
                    {
                        await DiscordClient.SendEmbedMsg(cInfo.Title, null,
                            IllSingleton.Game.SummonerName, lp.RANK, lp.LPoints);
                    }
                }
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
                await SendMessage(string.Format(STRINGS.OnUnban, e.Payload.Event.ModeratorUserLogin, e.Payload.Event.UserName)).ConfigureAwait(false);
            }
            catch (Exception ex) { _logger?.LogError(ex, "Error processing unban event"); }
        }
        public async Task SendBotMessage(string messageToSend, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(messageToSend)) return;

            if (IllSingleton.State.IsSilent)
            {
                _logger?.LogDebug("Bot is in silent mode, message not sent: {Message}", messageToSend);
                return;
            }

            if (!IsConnected)
            {
                _logger?.LogWarning("Cannot send message - not connected to Twitch IRC");
                return;
            }

            try
            {
                if (messageToSend.Length <= MESSAGE_MAX_LENGTH)
                {
                    await _client.SendMessageAsync(IllSingleton.Config.ChannelName, messageToSend).ConfigureAwait(false);
                }
                else
                {
                    await _client.SendMessageAsync(IllSingleton.Config.ChannelName, messageToSend.Substring(0, MESSAGE_MAX_LENGTH)).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error sending message: {Message}", messageToSend);
            }
        }
        public async Task SendMessage(string messageToSend, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(messageToSend) || IllSingleton.State.IsSilent || !IsConnected) return;
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
            try { await StreamElementsAPI.SendChatMessage(message, cancellationToken); }
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