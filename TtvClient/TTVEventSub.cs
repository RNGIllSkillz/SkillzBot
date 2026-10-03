using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkillzBot.IllConfiguration;
using SkillzBot.IllSTRINGS;
using SkillzBot.Interfaces;
using SkillzBot.TtvClient.TTVRewards;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Core.Exceptions;
using TwitchLib.EventSub.Core.EventArgs.Channel;
using TwitchLib.EventSub.Core.EventArgs.Stream;
using TwitchLib.EventSub.Websockets;
using TwitchLib.EventSub.Websockets.Core.EventArgs;

namespace SkillzBot.EventSub
{
    /// <summary>
    /// Owns the EventSub websocket. A watchdog loop reconnects for as long as the process
    /// lives, with capped exponential backoff, so a dropped socket never requires a restart.
    /// </summary>
    internal class TTVEventSub : BackgroundService
    {
        private readonly ILogger<TTVEventSub> _logger;
        private readonly IDatabaseService _databaseService;
        private readonly ITtvIRCClient _ircClient;
        private readonly EventSubWebsocketClient _eventSubWebsocketClient;
        private readonly RewardsRedemption _rewardsRedemption;
        private readonly TwitchAPI _twitchApi = new TwitchAPI();
        private readonly BotConfigModel _config;
        private readonly IBotStateService _botState;
        private readonly ITwitchService _twitchService;

        private readonly Dictionary<string, string> SubscriptionsTypes;
        private List<string> _lockedRewards = new List<string>();

        private volatile bool _isConnected = false;
        private bool _hasConnectedBefore = false;
        private long _disconnectedSinceTicks = DateTime.UtcNow.Ticks;
        private int _consecutiveFailures = 0;
        private DateTime _nextAttemptUtc = DateTime.MinValue;
        private readonly SemaphoreSlim _connectLock = new SemaphoreSlim(1, 1);

        private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(10);
        // Gives the library's own session-migration a chance before the watchdog steps in.
        private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

        public TTVEventSub(
            EventSubWebsocketClient eventSubWebsocketClient,
            IDatabaseService databaseService,
            ITtvIRCClient ircClient,
            RewardsRedemption rewardsRedemption,
            ITwitchService twitchService,
            ILogger<TTVEventSub> logger,
            BotConfigModel config,
            IBotStateService botState)
        {
            _ircClient = ircClient;
            _eventSubWebsocketClient = eventSubWebsocketClient ?? throw new ArgumentNullException(nameof(eventSubWebsocketClient));
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
            _rewardsRedemption = rewardsRedemption;
            _twitchService = twitchService;
            _logger = logger;
            _config = config;
            _botState = botState;

            _eventSubWebsocketClient.WebsocketConnected += OnWebsocketConnected;
            _eventSubWebsocketClient.WebsocketDisconnected += OnWebsocketDisconnected;
            _eventSubWebsocketClient.WebsocketReconnected += OnWebsocketReconnected;
            _eventSubWebsocketClient.ErrorOccurred += OnErrorOccurred;

            _eventSubWebsocketClient.ChannelPointsCustomRewardRedemptionAdd += OnChannelPointsCustomRewardRedemptionAdd;
            _eventSubWebsocketClient.StreamOnline += OnStreamUp;
            _eventSubWebsocketClient.StreamOffline += OnStreamDown;
            _eventSubWebsocketClient.ChannelPredictionBegin += OnPrediction;
            _eventSubWebsocketClient.ChannelUnban += OnUnban;
            _eventSubWebsocketClient.ChannelBan += OnChannelBan;
            _eventSubWebsocketClient.ChannelChatSettingsUpdate += OnChannelChatSettingsUpdate;

            _twitchApi.Settings.ClientId = _config.TApiClientId;
            _twitchApi.Settings.AccessToken = _config.TApiAccessToken;

            SubscriptionsTypes = new Dictionary<string, string>
            {
                { "channel.bits.use", "1" },
                { "channel.channel_points_custom_reward_redemption.add", "1" },
                { "channel.unban", "1"},
                { "channel.ban", "1"},
                { "channel.prediction.begin", "1"},
                { "channel.chat_settings.update", "1"}
            };
        }

        #region Lifecycle & watchdog

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting Twitch EventSub service...");
            await TryConnectAsync();

            using var timer = new PeriodicTimer(WatchdogInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    if (_isConnected) continue;

                    var now = DateTime.UtcNow;
                    var disconnectedSince = new DateTime(Interlocked.Read(ref _disconnectedSinceTicks), DateTimeKind.Utc);
                    if (now - disconnectedSince < GracePeriod) continue;
                    if (now < _nextAttemptUtc) continue;

                    await TryConnectAsync();
                }
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping Twitch EventSub service...");
            try
            {
                await _eventSubWebsocketClient.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting EventSub");
            }
            MarkDisconnected();
            await base.StopAsync(cancellationToken);
        }

        private void MarkDisconnected()
        {
            if (_isConnected)
                Interlocked.Exchange(ref _disconnectedSinceTicks, DateTime.UtcNow.Ticks);
            _isConnected = false;
        }

        private async Task TryConnectAsync()
        {
            if (!await _connectLock.WaitAsync(0)) return;
            try
            {
                int attempt = _consecutiveFailures + 1;
                _logger.LogInformation("EventSub connecting (attempt {Attempt})...", attempt);

                bool ok = false;
                try
                {
                    if (_hasConnectedBefore)
                    {
                        ok = await _eventSubWebsocketClient.ReconnectAsync();
                        if (!ok)
                        {
                            _logger.LogWarning("EventSub ReconnectAsync returned false; trying a fresh ConnectAsync.");
                            ok = await _eventSubWebsocketClient.ConnectAsync();
                        }
                    }
                    else
                    {
                        ok = await _eventSubWebsocketClient.ConnectAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "EventSub connection attempt {Attempt} threw.", attempt);
                }

                if (!ok)
                {
                    _consecutiveFailures++;
                    var backoff = TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, 5 * Math.Pow(2, Math.Min(_consecutiveFailures - 1, 6))));
                    _nextAttemptUtc = DateTime.UtcNow + backoff;
                    _logger.LogWarning("EventSub connection failed. Next attempt in {Seconds:F0}s.", backoff.TotalSeconds);
                }
            }
            finally
            {
                _connectLock.Release();
            }
        }

        #endregion

        #region Websocket events

        private Task OnErrorOccurred(object sender, ErrorOccuredArgs e)
        {
            var errorMessage = e.Message ?? e.Exception?.Message ?? "Unknown Error";
            _logger.LogError("Websocket error: {Message} , Session ID: {SessionId}", errorMessage, _eventSubWebsocketClient.SessionId);
            return Task.CompletedTask;
        }

        private async Task OnWebsocketConnected(object sender, WebsocketConnectedArgs e)
        {
            _isConnected = true;
            _hasConnectedBefore = true;
            _consecutiveFailures = 0;
            _logger.LogInformation("Websocket connected. Session ID: {SessionId}, Reconnect: {IsRequestedReconnect}", _eventSubWebsocketClient.SessionId, e.IsRequestedReconnect);

            // Twitch carries subscriptions over on a requested reconnect, but after a manual
            // reconnect the session is new. Subscribing is idempotent (409 = already exists),
            // so always subscribe.
            await Guard(Subscribe, nameof(Subscribe));
        }

        private Task OnWebsocketReconnected(object sender, EventArgs e)
        {
            _isConnected = true;
            _consecutiveFailures = 0;
            _logger.LogInformation("Websocket reconnected. Session ID: {SessionId}", _eventSubWebsocketClient.SessionId);
            return Task.CompletedTask;
        }

        private Task OnWebsocketDisconnected(object sender, EventArgs e)
        {
            MarkDisconnected();
            _logger.LogWarning("Websocket disconnected. Session ID: {SessionId}. Watchdog will reconnect.", _eventSubWebsocketClient.SessionId);
            return Task.CompletedTask;
        }

        private async Task Subscribe()
        {
            if (string.IsNullOrEmpty(_eventSubWebsocketClient.SessionId))
            {
                _logger.LogWarning("Skipping Subscribe: SessionId is null.");
                return;
            }

            foreach (var type in SubscriptionsTypes)
            {
                bool success = await SubscribeToChannelEventsWithRetry(type.Key, type.Value);

                if (!success)
                {
                    _logger.LogError("Critical: Failed to subscribe to {Type} after retries. Disconnecting to reset state.", type.Key);
                    try { await _eventSubWebsocketClient.DisconnectAsync(); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Disconnect after failed subscribe threw."); }
                    MarkDisconnected();
                    _consecutiveFailures++;
                    _nextAttemptUtc = DateTime.UtcNow + TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, 5 * Math.Pow(2, Math.Min(_consecutiveFailures - 1, 6))));
                    return;
                }
            }
        }

        private async Task<bool> SubscribeToChannelEventsWithRetry(string _type, string _version)
        {
            int attempts = 0;
            while (attempts < 3)
            {
                try
                {
                    await SubscribeToChannelEvents(_type, _version);
                    return true;
                }
                catch (HttpRequestException ex)
                {
                    attempts++;
                    _logger.LogWarning("Subscription HTTP failed ({Attempt}/3). Error: {Message}", attempts, ex.Message);
                    await Task.Delay(1000 * attempts);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error during subscription.");
                    return false;
                }
            }
            return false;
        }

        private async Task SubscribeToChannelEvents(string _type, string _version)
        {
            if (string.IsNullOrEmpty(_eventSubWebsocketClient.SessionId))
            {
                _logger.LogError("Cannot subscribe to {_type}: SessionId is null.", _type);
                return;
            }

            try
            {
                var condition = new Dictionary<string, string>
                {
                    { "broadcaster_user_id", _config.BroadcasterId }
                };

                if (_type == "channel.chat_settings.update")
                {
                    condition["user_id"] = _config.BroadcasterId;
                }

                var subscription = await _twitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                    type: _type,
                    version: _version,
                    condition: condition,
                    method: EventSubTransportMethod.Websocket,
                    websocketSessionId: _eventSubWebsocketClient.SessionId);

                if (subscription.Subscriptions.Length > 0)
                    _logger.LogInformation("Subscribed to {_type}. Subscription ID: {Id}", _type, subscription.Subscriptions[0].Id);
            }
            catch (BadRequestException ex)
            {
                _logger.LogError("Failed to subscribe to {_type}: Bad Request: {Message}", _type, ex.Message);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("409") || ex.Message.Contains("Conflict"))
            {
                _logger.LogDebug("Subscription for {_type} already exists (Conflict 409).", _type);
            }
            catch (Exception ex)
            {
                if (ex.InnerException is System.Net.Sockets.SocketException || ex is HttpRequestException)
                {
                    throw; // Re-throw for retry
                }

                if (ex.Message.Contains("Conflict") || (ex.InnerException?.Message.Contains("Conflict") ?? false))
                {
                    _logger.LogDebug("Subscription for {_type} already exists (Conflict).", _type);
                }
                else
                {
                    _logger.LogError(ex, "Failed to subscribe to {_type} event.", _type);
                    throw; // Re-throw to trigger retry
                }
            }
        }

        #endregion

        #region Channel events

        /// <summary>
        /// Exceptions thrown from an event handler would propagate into the websocket read
        /// loop and could silently kill it; every handler runs through this guard.
        /// </summary>
        private async Task Guard(Func<Task> handler, string name)
        {
            try
            {
                await handler();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EventSub handler {Handler} failed", name);
            }
        }

        private Task OnChannelChatSettingsUpdate(object sender, ChannelChatSettingsUpdateArgs e) => Guard(async () =>
        {
            bool isEmoteMode = e.Payload.Event.EmoteMode;
            _logger.LogInformation("Chat Settings Update: EmoteOnly is now {Status}", isEmoteMode);

            if (isEmoteMode)
            {
                _lockedRewards = await _twitchService.DisableAllRewardsSafeAsync(_config.ChannelIds.EmoteModeId);
                _logger.LogInformation("Lockdown active. {Count} rewards disabled.", _lockedRewards.Count);
            }
            else if (_lockedRewards != null && _lockedRewards.Count > 0)
            {
                await _twitchService.RestoreRewardsAsync(_lockedRewards);
                _logger.LogInformation("Lockdown lifted. {Count} rewards restored.", _lockedRewards.Count);
                _lockedRewards.Clear();
            }
        }, nameof(OnChannelChatSettingsUpdate));

        private Task OnStreamUp(object sender, StreamOnlineArgs e) => Guard(() => _ircClient.OnStreamUp(), nameof(OnStreamUp));

        private Task OnStreamDown(object sender, StreamOfflineArgs e) => Guard(() => _ircClient.OnStreamDown(), nameof(OnStreamDown));

        private Task OnChannelBan(object sender, ChannelBanArgs e) => Guard(async () =>
        {
            if (e.Payload.Event.IsPermanent)
                await _ircClient.SendMessage("o7");
        }, nameof(OnChannelBan));

        private Task OnPrediction(object sender, ChannelPredictionBeginArgs e) => Guard(async () =>
        {
            if (!_botState.Current.IsSubActive) return;
            string message = $"PopNemo {string.Format(STRINGS.PredictionStarted, e.Payload.Event.Title)} PopNemo";
            for (int i = 0; i < 3; i++)
            {
                await _ircClient.SendMessage(message);
                await Task.Delay(100);
            }
        }, nameof(OnPrediction));

        private Task OnUnban(object sender, ChannelUnbanArgs e) => Guard(async () =>
        {
            if (!_botState.Current.IsSubActive) return;

            var user = await _databaseService.GetUserAsync(e.Payload.Event.UserLogin);
            if (user.dbID == -404)
            {
                _logger.LogWarning("Unbanned user {UserLogin} is not in the database.", e.Payload.Event.UserLogin);
                return;
            }
            user.UvalTimer = 0;
            await _databaseService.UpdateUserAsync(user);
        }, nameof(OnUnban));

        private Task OnChannelPointsCustomRewardRedemptionAdd(object sender, ChannelPointsCustomRewardRedemptionArgs e) => Guard(() =>
        {
            if (!_botState.Current.IsSubActive) return Task.CompletedTask;
            return RewardProcess(
                e.Payload.Event.Reward.Id,
                e.Payload.Event.UserLogin,
                e.Payload.Event.UserInput,
                e.Payload.Event.Id);
        }, nameof(OnChannelPointsCustomRewardRedemptionAdd));

        #endregion

        private async Task RewardProcess(string rewardID, string userName, string message, string redemID)
        {
            const int maxAttempts = 3;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await DispatchReward(rewardID, userName, message, redemID);
                    return;
                }
                catch (Exception e) when (attempt < maxAttempts && e.Message.Contains("500"))
                {
                    _logger.LogError(e, "Twitch returned 500 while processing reward {RewardId}. Attempt {Attempt}/{Max}.", rewardID, attempt, maxAttempts);
                    await Task.Delay(2000);
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Cant perform ttv reward operation for reward {RewardId} by {User}.", rewardID, userName);
                    return;
                }
            }
        }

        private Task DispatchReward(string rewardID, string userName, string message, string redemID)
        {
            var ids = _config.ChannelIds;
            if (rewardID == ids.ZakazTrekaId) return _rewardsRedemption.ZakazTrekaReward(userName, message, redemID, rewardID);
            if (rewardID == ids.Pi4KaId) return _rewardsRedemption.Pi4kaReward(userName, redemID, rewardID);
            if (rewardID == ids.UvalId) return _rewardsRedemption.UvalReward(userName, message, redemID, rewardID);
            if (rewardID == ids.UvalSabId) return _rewardsRedemption.UvalSabReward(userName, message, redemID, rewardID);
            if (rewardID == ids.UvalVipId) return _rewardsRedemption.UvalVIPReward(userName, message, redemID, rewardID);
            if (rewardID == ids.EmoteModeId) return _rewardsRedemption.EmoteOnlyReward(userName, redemID, rewardID);
            if (rewardID == ids.CenceleUval) return _rewardsRedemption.CenceleUvalReward(userName, message, redemID, rewardID);
            if (rewardID == ids.UvalMod) return _rewardsRedemption.UvalModReward(userName, message, redemID, rewardID);
            return Task.CompletedTask;
        }
    }
}
