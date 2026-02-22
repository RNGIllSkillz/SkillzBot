using Microsoft.Extensions.Logging;
using SkillzBot.IllConfiguration;
using SkillzBot.IllSTRINGS;
using SkillzBot.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Events; 
using TwitchLib.EventSub.Core.EventArgs.Channel;
using TwitchLib.PubSub.Events;
using OnConnectedEventArgs = TwitchLib.Client.Events.OnConnectedEventArgs;
using OnConnectionErrorArgs = TwitchLib.Client.Events.OnConnectionErrorArgs;
using OnMessageReceivedArgs = TwitchLib.Client.Events.OnMessageReceivedArgs;
using OnUserTimedoutArgs = TwitchLib.Client.Events.OnUserTimedoutArgs;

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

        private DateTimeOffset _lastActivity = DateTimeOffset.UtcNow;
        public DateTimeOffset LastActivity => _lastActivity;

        private TwitchClient _client;
        private bool _isInitialized = false;
        private bool _isDisposed = false;

        // Semaphore to ensure only one connection attempt happens at a time
        private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);

        private const int MAX_RETRIES = 3;
        private const int MESSAGE_MAX_LENGTH = 500;
        private const int SMALL_DELAY_MS = 100;

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
            _config = config;
            _gameState = gameState;
            _botState = botState;
            _streamElementsService = streamElementsService;
        }

        public bool IsConnected => _client?.IsConnected ?? false;
        public bool IsInitialized => _isInitialized && !_isDisposed;

        public async Task<bool> InitializeAsync()
        {
            if (_isDisposed) return false;
            if (IsConnected)
            {
                _isInitialized = true;
                return true;
            }

            return await ConnectToTwitchAsync();
        }

        public async Task<bool> ReconnectAsync()
        {
            _logger.LogWarning("Forcing Reconnect sequence...");
            _lastActivity = DateTimeOffset.UtcNow;
            if (_isDisposed) return false;
            return await ConnectToTwitchAsync();
        }

        private async Task<bool> ConnectToTwitchAsync()
        {
            if (!await _connectionLock.WaitAsync(5000))
            {
                _logger.LogWarning("Reconnect skipped: Previous connection attempt is still locked.");
                return false;
            }
            try
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
                        // Dispose old client (async)
                        await DisposeClientInstanceAsync();

                        var credentials = new ConnectionCredentials(_config.BotTwitchName, _config.BotTwitchAuth);
                        _client = new TwitchClient();
                        RegisterEventHandlers();
                        _client.Initialize(credentials, _config.ChannelName);

                        // Use TaskCompletionSource to await the ACTUAL OnConnected event
                        var connectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                        // Use AsyncEventHandler and return Task.CompletedTask
                        AsyncEventHandler<OnConnectedEventArgs> onConnectedHandler = (s, e) =>
                        {
                            connectedTcs.TrySetResult(true);
                            return Task.CompletedTask;
                        };

                        AsyncEventHandler<OnConnectionErrorArgs> onErrorHandler = (s, e) =>
                        {
                            _logger?.LogError("Twitch Connection Error: {Message}", e.Error.Message);
                            connectedTcs.TrySetResult(false);
                            return Task.CompletedTask;
                        };

                        _client.OnConnected += onConnectedHandler;
                        _client.OnConnectionError += onErrorHandler;

                        try
                        {
                            _logger?.LogInformation("Connecting to Twitch IRC (Attempt {Attempt})...", attempt);

                            // ConnectAsync returns boolean in newer TwitchLib versions
                            if (!await _client.ConnectAsync())
                            {
                                _logger?.LogWarning("ConnectAsync returned false immediately.");
                                continue;
                            }

                            // Wait up to 15 seconds for the OnConnected event
                            var timeoutTask = Task.Delay(15000);
                            var completedTask = await Task.WhenAny(connectedTcs.Task, timeoutTask);

                            if (completedTask == timeoutTask)
                            {
                                _logger?.LogError("Twitch IRC connection timed out waiting for OnConnected.");
                                continue;
                            }

                            bool success = await connectedTcs.Task;
                            if (success)
                            {
                                for (int i = 0; i < 5; i++)
                                {
                                    if (_client.IsConnected)
                                    {
                                        _isInitialized = true;
                                        _lastActivity = DateTimeOffset.UtcNow; // Success! Reset timer.
                                        _logger?.LogInformation("Twitch IRC Connected Successfully.");
                                        return true;
                                    }
                                    await Task.Delay(500);
                                }                                
                            }
                        }
                        finally
                        {
                            if (_client != null)
                            {
                                _client.OnConnected -= onConnectedHandler;
                                _client.OnConnectionError -= onErrorHandler;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        _logger?.LogError(e, "Connection attempt {Attempt}/{Max} crashed.", attempt, MAX_RETRIES);
                    }

                    if (attempt < MAX_RETRIES)
                    {
                        await Task.Delay(1000 * (int)Math.Pow(2, attempt - 1));
                    }
                }

                _logger?.LogError("Failed to connect to Twitch IRC after {Max} attempts.", MAX_RETRIES);
                return false;
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private void RegisterEventHandlers()
        {
            if (_client == null) return;
            _client.OnMessageReceived += Client_OnMessageReceived;
            _client.OnUserTimedout += Client_OnUserTimedout;
            _client.OnDisconnected += Client_OnDisconnected;
        }

        private async Task DisposeClientInstanceAsync()
        {
            if (_client != null)
            {
                try
                {
                    _client.OnMessageReceived -= Client_OnMessageReceived;
                    _client.OnUserTimedout -= Client_OnUserTimedout;
                    _client.OnDisconnected -= Client_OnDisconnected;

                    if (_client.IsConnected)
                    {
                        await _client.DisconnectAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning("Error disposing old TwitchClient: {Message}", ex.Message);
                }
                finally { _client = null; }
            }
        }
        
        private async Task Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            _lastActivity = DateTimeOffset.UtcNow;
            if (OnMessageReceived != null)
            {
                await OnMessageReceived.Invoke(e);
            }
        }
        private void Client_OnLog(object sender, OnLogArgs e)
        {
            // Update the timestamp whenever data flows
            _lastActivity = DateTimeOffset.UtcNow;
        }
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
        private async Task Client_OnDisconnected(object sender, OnDisconnectedArgs e)
        {
            // Just log. The HostedService monitors IsConnected and will trigger the Reconnect.
            _logger?.LogWarning("Twitch IRC Client raised OnDisconnected event.");
            await Task.CompletedTask;
        }
        public void Dispose()
        {
            _isDisposed = true;
            _isInitialized = false;
            _connectionLock.Wait();
            try
            {
                // Fire and forget disposal since we are in void Dispose
                _ = DisposeClientInstanceAsync();
            }
            finally
            {
                _connectionLock.Release();
                _connectionLock.Dispose();
            }
        }
    }
}