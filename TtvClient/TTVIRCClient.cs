using System;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Events;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Channel;
using SkillzBot.IllSkillzBot;
using SkillzBot.IllSTRINGS;
using SkillzBot.API.StreamElements;
using SkillzBot.Discord;
using SkillzBot.API.Twitch;
using SkillzBot.IllSkillzBot.IllCommandsNest;
using SkillzBot.Hosts;
using SkillzBot.Singleton;

namespace SkillzBot.IRC
{
    sealed class TtvIRCClient : IDisposable
    {
        #region Private Fields
        private static ILogger<TtvIRCClient>? _logger;

        private static TwitchClient? _client;
        private static bool _isInitialized = false;
        private static bool _isDisposed = false;
        private static readonly object _lockObject = new object();
        private static readonly SemaphoreSlim _connectionSemaphore = new SemaphoreSlim(1, 1);
        

        // Connection configuration
        private const int MAX_RETRIES = 3;
        private const int BASE_DELAY_MS = 1000;
        private const int CONNECTION_TIMEOUT_SECONDS = 10;
        private const int MESSAGE_MAX_LENGTH = 500;
        #endregion

        #region Initialization
        public static void Initialize(IServiceProvider serviceProvider)
        {
            _logger = serviceProvider.GetRequiredService<ILogger<TtvIRCClient>>();
            _logger.LogDebug("TtvIRCClient logger initialized");
        }

        public static async Task<bool> InitializeAsync()
        {
            if (_isDisposed)
            {
                _logger?.LogWarning("Attempted to initialize disposed TtvIRCClient");
                return false;
            }

            if (_isInitialized)
            {
                _logger?.LogDebug("TtvIRCClient already initialized");
                return true;
            }

            await _connectionSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_isInitialized) return true; // Double-check after acquiring semaphore

                _logger?.LogInformation("Initializing Twitch IRC client...");
                bool success = await ConnectToTwitchAsync().ConfigureAwait(false);

                if (success)
                {
                    _logger?.LogInformation("Twitch IRC client initialized successfully");
                }
                else
                {
                    _logger?.LogError("Failed to initialize Twitch IRC client");
                }

                return success;
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
        #endregion

        #region Connection Management
        private static async Task<bool> ConnectToTwitchAsync()
        {
            if (string.IsNullOrWhiteSpace(IllSingleton.Config?.BotTwitchName) ||
                string.IsNullOrWhiteSpace(IllSingleton.Config?.BotTwitchAuth) ||
                string.IsNullOrWhiteSpace(IllSingleton.Config?.ChannelName))
            {
                _logger?.LogError("Missing required Twitch configuration (BotTwitchName, BotTwitchAuth, or ChannelName)");
                return false;
            }

            for (int attempt = 1; attempt <= MAX_RETRIES; attempt++)
            {
                try
                {
                    _logger?.LogDebug("Connection attempt {Attempt}/{MaxRetries}", attempt, MAX_RETRIES);

                    // Dispose existing client if any
                    DisposeClient();

                    var credentials = new ConnectionCredentials(
                        IllSingleton.Config.BotTwitchName,
                        IllSingleton.Config.BotTwitchAuth);

                    _client = new TwitchClient();
                    RegisterEventHandlers();

                    _client.Initialize(credentials, IllSingleton.Config.ChannelName);

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(CONNECTION_TIMEOUT_SECONDS));
                    await Task.Run(() => _client.Connect(), cts.Token).ConfigureAwait(false);

                    // Wait for connection to stabilize
                    await Task.Delay(1000, CancellationToken.None).ConfigureAwait(false);

                    // Verify connection
                    if (_client.IsConnected)
                    {
                        lock (_lockObject)
                        {
                            _isInitialized = true;
                        }
                        _logger?.LogInformation("Successfully connected to Twitch IRC on attempt {Attempt}", attempt);
                        return true;
                    }
                    else
                    {
                        _logger?.LogWarning("Client reports not connected after connection attempt {Attempt}", attempt);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger?.LogWarning("Connection timeout on attempt {Attempt}/{MaxRetries}", attempt, MAX_RETRIES);
                }
                catch (Exception e)
                {
                    _logger?.LogError(e, "Connection error on attempt {Attempt}/{MaxRetries}: {ErrorMessage}",
                        attempt, MAX_RETRIES, e.Message);
                }

                // Wait before retry (exponential backoff)
                if (attempt < MAX_RETRIES)
                {
                    int delayMs = BASE_DELAY_MS * attempt;
                    _logger?.LogInformation("Retrying connection in {DelayMs}ms (attempt {NextAttempt}/{MaxRetries})",
                        delayMs, attempt + 1, MAX_RETRIES);
                    await Task.Delay(delayMs).ConfigureAwait(false);
                }
            }

            _logger?.LogError("Failed to connect to Twitch IRC after {MaxRetries} attempts", MAX_RETRIES);
            return false;
        }

        public static async Task<bool> ReconnectAsync()
        {
            if (_isDisposed)
            {
                _logger?.LogWarning("Attempted to reconnect disposed TtvIRCClient");
                return false;
            }

            if (_client?.IsConnected == true)
            {
                _logger?.LogDebug("Already connected, skipping reconnect");
                return true;
            }

            _logger?.LogInformation("Attempting to reconnect to Twitch IRC...");

            await _connectionSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                lock (_lockObject)
                {
                    _isInitialized = false;
                }

                return await ConnectToTwitchAsync().ConfigureAwait(false);
            }
            finally
            {
                _connectionSemaphore.Release();
            }
        }

        private static void RegisterEventHandlers()
        {
            if (_client == null) return;

            _client.OnMessageReceived += Client_OnMessageReceived;
            _client.OnUserTimedout += Client_OnUserTimedout;
            _client.OnDisconnected += Client_OnDisconnected;
            _client.OnConnected += Client_OnConnected;

            _logger?.LogDebug("Event handlers registered");
        }

        private static void DisposeClient()
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
                        _client.Disconnect();
                    }
                    _client = null;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error disposing previous client");
                }
            }
        }
        #endregion

        #region Properties
        public static bool IsConnected => _client?.IsConnected ?? false;
        public static bool IsInitialized => _isInitialized && !_isDisposed;
        #endregion

        #region Event Handlers
        private static async void Client_OnConnected(object sender, OnConnectedArgs e)
        {
            _logger?.LogInformation("Twitch IRC client connected to channel: {Channel}", e.AutoJoinChannel);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        private static async void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            try
            {
                _logger?.LogDebug("Message received from {User}: {Message}",
                    e.ChatMessage.Username, e.ChatMessage.Message);

                var user = await IllChatMessageHandler.MessageHandler(e).ConfigureAwait(false);
                if (user != null)
                {
                    await IllServiceProvider.Database.UpdateUserAsync(user).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling message from {User}", e.ChatMessage?.Username);
            }
        }

        private static async void Client_OnUserTimedout(object sender, OnUserTimedoutArgs e)
        {
            try
            {
                _logger?.LogInformation("User {Username} timed out for {Duration} seconds",
                    e.UserTimeout.Username, e.UserTimeout.TimeoutDuration);

                await UserTimedoutEventTask(e).ConfigureAwait(false);

                if (e.UserTimeout.TimeoutDuration > 50000)
                {
                    SendMessage("o7");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling timeout for user {Username}", e.UserTimeout?.Username);
            }
        }

        private static void Client_OnDisconnected(object sender, OnDisconnectedEventArgs e)
        {
            _logger?.LogWarning("Twitch IRC disconnected. Attempting to reconnect...");

            lock (_lockObject)
            {
                _isInitialized = false;
            }

            // Start reconnection task without awaiting to avoid blocking
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000).ConfigureAwait(false); // Brief delay before reconnecting
                    await ReconnectAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error during automatic reconnection");
                }
            });
        }
        #endregion

        #region Event Processing
        private static async Task UserTimedoutEventTask(OnUserTimedoutArgs e)
        {
            if (e?.UserTimeout?.Username == null)
            {
                _logger?.LogWarning("UserTimedoutEventTask called with null timeout data");
                return;
            }

            try
            {
                var user = await IllServiceProvider.Database.GetUserAsync(e.UserTimeout.Username).ConfigureAwait(false);
                if (user.dbID == -404)
                {
                    _logger?.LogWarning("User {Username} not found in database during timeout event",
                        e.UserTimeout.Username);
                }
                else
                {
                    user.UvalTimer = e.UserTimeout.TimeoutDuration + DateTimeOffset.Now.ToUnixTimeSeconds();
                    user.UvalCon++;
                    await IllServiceProvider.Database.UpdateUserAsync(user).ConfigureAwait(false);

                    _logger?.LogDebug("Updated timeout info for user {Username}", e.UserTimeout.Username);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing timeout for user {Username}", e.UserTimeout.Username);
            }
        }
        #endregion

        #region Stream Events
        public static async Task OnStreamDown()
        {
            try
            {
                _logger?.LogInformation("Processing stream down event");

                IllSingleton.State.BroadcasterIsOnline = false;
                IllGames.ClearQuizzActiveUsers();
                IllSingleton.State.FirstQuizOfTheDay = true;

                var lastStats = await IllCommands.GetLpAsync().ConfigureAwait(false);
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

                SendMessage(chatMessage);
                await DiscordClient.SendEmbedMsg(discordTitle, "", IllSingleton.Game.SummonerName,
                    lastStats.RANK, lastStats.LPoints, null, false, msg).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing stream down event");
            }
        }

        public static async Task OnStreamUp()
        {
            try
            {
                _logger?.LogInformation("Processing stream up event");

                IllSingleton.State.BroadcasterIsOnline = true;
                SendMessage(string.Format(STRINGS.OnStreamUP, IllSingleton.Config.ChannelName));

                var info = await TtvAPI.GetStreamInfo().ConfigureAwait(false);
                var lp = await IllCommands.GetLpAsync().ConfigureAwait(false);

                if (info != null)
                {
                    await DiscordClient.SendEmbedMsg(info.Title, info.ThumbnailUrl,
                        IllSingleton.Game.SummonerName, lp.RANK, lp.LPoints).ConfigureAwait(false);
                }
                else
                {
                    var cInfo = await TtvAPI.GetChannelInformationAsync().ConfigureAwait(false);
                    if (cInfo != null)
                    {
                        await DiscordClient.SendEmbedMsg(cInfo.Title, null,
                            IllSingleton.Game.SummonerName, lp.RANK, lp.LPoints).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing stream up event");
            }
        }

        public static void OnUnban(ChannelUnbanArgs e)
        {
            try
            {
                if (e?.Notification?.Payload?.Event == null)
                {
                    _logger?.LogWarning("OnUnban called with null event data");
                    return;
                }

                _logger?.LogInformation("User {Username} unbanned by {Moderator}",
                    e.Notification.Payload.Event.UserName, e.Notification.Payload.Event.ModeratorUserLogin);

                SendMessage(string.Format(STRINGS.OnUnban,
                    e.Notification.Payload.Event.ModeratorUserLogin,
                    e.Notification.Payload.Event.UserName));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing unban event");
            }
        }
        #endregion

        #region Message Sending
        public static void SendMessage(string messageToSend)
        {
            if (string.IsNullOrWhiteSpace(messageToSend))
            {
                _logger?.LogDebug("Attempted to send null or empty message");
                return;
            }

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
                    SendSingleMessage(messageToSend);
                }
                else
                {
                    SendLongMessage(messageToSend);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error sending message: {Message}", messageToSend);
            }
        }

        private static void SendSingleMessage(string message)
        {
            try
            {
                _logger?.LogDebug("Sending message: {Message}", message);
                StreamElementsAPI.SendChatMessage(message).GetAwaiter().GetResult();
                // Alternative: _client.SendMessage(IllSingleton.Config.ChannelName, message);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to send single message");                
            }
        }

        private static void SendLongMessage(string messageToSend)
        {
            _logger?.LogDebug("Splitting long message into chunks");

            int startIndex = 0;
            int messageNumber = 1;

            while (startIndex < messageToSend.Length)
            {
                int length = Math.Min(MESSAGE_MAX_LENGTH, messageToSend.Length - startIndex);

                // Try to break at word boundary
                if (length == MESSAGE_MAX_LENGTH && messageToSend[startIndex + length - 1] != ' ')
                {
                    int lastSpaceIndex = messageToSend.LastIndexOf(' ', startIndex + length - 1, length);
                    if (lastSpaceIndex != -1)
                    {
                        length = lastSpaceIndex - startIndex;
                    }
                    else
                    {
                        // No word boundary found, send error message instead
                        _logger?.LogWarning("Cannot split message at word boundary, sending error message");
                        SendSingleMessage(STRINGS.SendMessageERROR);
                        return;
                    }
                }

                string messagePart = messageToSend.Substring(startIndex, length);

                try
                {
                    _logger?.LogDebug("Sending message chunk {ChunkNumber}: {MessagePart}", messageNumber, messagePart);
                    StreamElementsAPI.SendChatMessage(messagePart).GetAwaiter().GetResult();
                    messageNumber++;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to send message chunk {ChunkNumber}", messageNumber);                    
                }

                startIndex += length;

                // Small delay between chunks to avoid rate limiting
                if (startIndex < messageToSend.Length)
                {
                    Task.Delay(100).GetAwaiter().GetResult();
                }
            }
        }
        #endregion

        #region Disposal
        public static void Dispose()
        {
            lock (_lockObject)
            {
                if (_isDisposed) return;
                _isDisposed = true;
                _isInitialized = false;
            }

            _logger?.LogInformation("Disposing TtvIRCClient");

            try
            {
                DisposeClient();
                _connectionSemaphore?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during TtvIRCClient disposal");
            }

            _logger?.LogInformation("TtvIRCClient disposed");
        }

        void IDisposable.Dispose()
        {
            Dispose();
        }
        #endregion
    }
}