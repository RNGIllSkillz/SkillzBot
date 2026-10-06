using SkillzBot.TtvClient;
using SkillzBot.Services.Chat;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkillzBot.IllConfiguration;
using SkillzBot.IllSTRINGS;
using SkillzBot.Interfaces;
using SkillzBot.Services;
using SkillzBot.Services.Twitch;
using SkillzBot.TtvClient.TTVRewards;
using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly TwitchTokenService _tokens;
        private readonly ChatIngress _chatIngress;
        private readonly BotConfigModel _config;
        private readonly IBotStateService _botState;
        private readonly ITwitchService _twitchService;
        private readonly HealthState _health;

        private readonly Dictionary<string, string> SubscriptionsTypes;
        private readonly Services.Vip.VipRegistryService _vips;
        private readonly IEngagementRepository _engagement;
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
            IBotStateService botState,
            HealthState health,
            Services.Vip.VipRegistryService vips,
            IEngagementRepository engagement,
            TwitchTokenService tokens,
            ChatIngress chatIngress)
        {
            _ircClient = ircClient;
            _health = health;
            _vips = vips;
            _engagement = engagement;
            _tokens = tokens;
            _chatIngress = chatIngress;
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
            _eventSubWebsocketClient.ChannelPredictionEnd += OnPredictionEnd;
            _eventSubWebsocketClient.ChannelPollEnd += OnPollEnd;
            _eventSubWebsocketClient.ChannelVipAdd += OnVipAdd;
            _eventSubWebsocketClient.ChannelVipRemove += OnVipRemove;
            _eventSubWebsocketClient.ChannelChatMessage += OnChannelChatMessage;
            _eventSubWebsocketClient.Revocation += OnRevocation;
            _chatIngress.EventSubRecovery = ForceReconnectAsync;

            // Subscriptions are created under the broadcaster token; a refreshed token lands here without a restart.
            _tokens.Attach(TwitchIdentity.Broadcaster, c =>
            {
                // A different application or source (panel grant replacing the config token, or the reverse) means the
                // subscriptions of the current session belong to the old client: open a fresh session. A refresh of the
                // same token changes nothing here.
                // A new grant with the same application (ObtainedUtc moves) also counts: it may carry new scopes or revive a revoked authorization.
                bool changed = _lastClientId != null && (_lastClientId != c.ClientId || _lastSource != c.Source || _lastObtainedUtc != c.ObtainedUtc);
                _lastClientId = c.ClientId; _lastSource = c.Source; _lastObtainedUtc = c.ObtainedUtc;
                _twitchApi.Settings.ClientId = c.ClientId; _twitchApi.Settings.AccessToken = c.AccessToken;
                if (changed) _ = ForceReconnectAsync($"broadcaster credential changed ({c.Source}, client {c.ClientId})");
            });

            SubscriptionsTypes = new Dictionary<string, string>
            {
                { "channel.bits.use", "1" },
                { "channel.channel_points_custom_reward_redemption.add", "1" },
                { "channel.unban", "1"},
                { "channel.ban", "1"},
                { "channel.prediction.begin", "1"},
                { "channel.chat_settings.update", "1"},
                { "stream.online", "1"},
                { "stream.offline", "1"},
                { "channel.prediction.end", "1"},
                { "channel.poll.end", "1"},
                { "channel.vip.add", "1"},
                { "channel.vip.remove", "1"}
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
                    if (_isConnected)
                    {
                        if (!_chatIngress.EventSubChatActive && DateTime.UtcNow >= _chatRetryDueUtc)
                            await Guard(SubscribeToChatAsync, "chat resubscribe");
                        continue;
                    }

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
            _health.ClearEventSubSubscriptions();
            if (_chatIngress.EventSubWanted) _chatIngress.SetEventSubChat(false, "websocket disconnected");
            if (_isConnected)
                Interlocked.Exchange(ref _disconnectedSinceTicks, DateTime.UtcNow.Ticks);
            _isConnected = false;
            _health.SetEventSubConnected(false, _eventSubWebsocketClient.SessionId);
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
                    _chatIngress.SetEventSubChat(false, "EventSub (re)connect failed");
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
            _health.SetEventSubConnected(true, _eventSubWebsocketClient.SessionId);
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
            _health.SetEventSubConnected(true, _eventSubWebsocketClient.SessionId);
            _logger.LogInformation("Websocket reconnected. Session ID: {SessionId}", _eventSubWebsocketClient.SessionId);
            return Task.CompletedTask;
        }

        private Task OnWebsocketDisconnected(object sender, EventArgs e)
        {
            MarkDisconnected();
            _logger.LogWarning("Websocket disconnected. Session ID: {SessionId}. Watchdog will reconnect.", _eventSubWebsocketClient.SessionId);
            return Task.CompletedTask;
        }

        private enum SubscribeResult { Subscribed, Rejected, Failed }

        private DateTime _chatRetryDueUtc = DateTime.MinValue;
        private string _lastClientId, _lastSource;
        private DateTime? _lastObtainedUtc;
        private readonly HashSet<string> _seenBanNotifications = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> _seenBanOrder = new Queue<string>();

        private async Task Subscribe()
        {
            if (string.IsNullOrEmpty(_eventSubWebsocketClient.SessionId))
            {
                _logger.LogWarning("Skipping Subscribe: SessionId is null.");
                return;
            }

            int subscribed = 0;
            foreach (var type in SubscriptionsTypes)
            {
                var result = await SubscribeToChannelEventsWithRetry(type.Key, type.Value);
                _health.SetEventSubSubscription(type.Key, result == SubscribeResult.Subscribed);
                if (result == SubscribeResult.Subscribed) { subscribed++; continue; }
                if (result == SubscribeResult.Rejected)
                {
                    // A 400 is a permanent answer for this token (scope or affiliate status); keep the rest alive.
                    _logger.LogError("Subscription {Type} rejected by Twitch; continuing without it.", type.Key);
                    continue;
                }
                    _logger.LogError("Critical: Failed to subscribe to {Type} after retries. Disconnecting to reset state.", type.Key);
                    try { await _eventSubWebsocketClient.DisconnectAsync(); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Disconnect after failed subscribe threw."); }
                    MarkDisconnected();
                    _consecutiveFailures++;
                    _nextAttemptUtc = DateTime.UtcNow + TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, 5 * Math.Pow(2, Math.Min(_consecutiveFailures - 1, 6))));
                    return;
            }
            _logger.LogInformation("EventSub ready: {Count}/{Total} subscriptions active on session {SessionId}.", subscribed, SubscriptionsTypes.Count, _eventSubWebsocketClient.SessionId);
            await SubscribeToChatAsync();
        }

        /// <summary>
        /// channel.chat.message under the broadcaster token (user:read:chat). Optional: a refusal leaves the other
        /// subscriptions alone and hands chat to IRC through the ingress. Transient failures are retried by the watchdog;
        /// a missing scope waits for the credential to change (the streamer authorizing on the panel forces a new session).
        /// </summary>
        private async Task SubscribeToChatAsync()
        {
            if (!_chatIngress.EventSubWanted)
            {
                _chatIngress.SetEventSubChat(false, "ChatTransport=irc");
                _chatRetryDueUtc = DateTime.MaxValue;
                return;
            }
            if (!_isConnected || string.IsNullOrEmpty(_eventSubWebsocketClient.SessionId)) return;
            var cred = _tokens.Current(TwitchIdentity.Broadcaster);
            if (cred == null)
            {
                _chatIngress.SetEventSubChat(false, "no broadcaster token");
                _chatRetryDueUtc = DateTime.MaxValue;
                return;
            }
            // The scopes of a config token are only known after a successful validate call; when unknown, let Twitch decide.
            if (cred.Scopes.Count > 0 && !cred.Scopes.Contains("user:read:chat", StringComparer.OrdinalIgnoreCase))
            {
                _chatIngress.SetEventSubChat(false, "broadcaster token has no user:read:chat; authorize the streamer on the panel's Twitch page");
                _chatRetryDueUtc = DateTime.MaxValue;
                return;
            }
            try
            {
                var result = await SubscribeToChannelEvents("channel.chat.message", "1");
                _health.SetEventSubSubscription("channel.chat.message", result == SubscribeResult.Subscribed);
                if (result == SubscribeResult.Subscribed)
                {
                    _chatIngress.SetEventSubChat(true, "subscribed on session " + _eventSubWebsocketClient.SessionId);
                    _chatRetryDueUtc = DateTime.MaxValue;
                }
                else
                {
                    _chatIngress.SetEventSubChat(false, "subscription rejected by Twitch (missing user:read:chat or wrong application); authorize the streamer on the panel's Twitch page");
                    _chatRetryDueUtc = DateTime.UtcNow.AddMinutes(10);
                }
            }
            catch (Exception ex)
            {
                _chatIngress.SetEventSubChat(false, "subscribe failed: " + ex.Message);
                _chatRetryDueUtc = DateTime.UtcNow.AddSeconds(60);
            }
        }

        private Task OnChannelChatMessage(object sender, ChannelChatMessageArgs e)
        {
            _health.MarkEventSubEvent();
            return Guard(() => _chatIngress.PublishAsync(ChatMessageMapper.FromEventSub(e.Payload.Event)), nameof(OnChannelChatMessage));
        }

        private Task OnRevocation(object sender, RevocationArgs e)
        {
            var sub = e.Payload?.Subscription;
            string type = sub?.Type ?? "?", status = sub?.Status ?? "?";
            _logger.LogWarning("EventSub revoked subscription {Type}: {Status}.", type, status);
            _health.SetEventSubSubscription(type, false);
            if (type == "channel.chat.message")
            {
                _chatIngress.SetEventSubChat(false, $"revoked by Twitch ({status})");
                _chatRetryDueUtc = status.Contains("authorization", StringComparison.OrdinalIgnoreCase) ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(60);
            }
            return Task.CompletedTask;
        }

        /// <summary>Drops the websocket so the watchdog opens a fresh session and re-subscribes everything, chat included.</summary>
        private async Task ForceReconnectAsync(string reason)
        {
            if (!_hasConnectedBefore || !_isConnected) return; // a reconnect already in flight will subscribe with the current token
            _logger.LogWarning("EventSub: forcing a fresh session ({Reason}).", reason);
            try { await _eventSubWebsocketClient.DisconnectAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Disconnect for the forced reconnect threw."); }
            MarkDisconnected();
            Interlocked.Exchange(ref _disconnectedSinceTicks, DateTime.MinValue.Ticks); // no grace: reconnect on the next tick
            _nextAttemptUtc = DateTime.UtcNow;
            _chatRetryDueUtc = DateTime.MinValue;
        }

        /// <summary>True the first time a ban/timeout key is seen; EventSub may redeliver a notification.</summary>
        private bool FirstDelivery(string notificationId)
        {
            if (string.IsNullOrEmpty(notificationId)) return true;
            lock (_seenBanNotifications)
            {
                if (!_seenBanNotifications.Add(notificationId)) return false;
                _seenBanOrder.Enqueue(notificationId);
                while (_seenBanOrder.Count > 256) _seenBanNotifications.Remove(_seenBanOrder.Dequeue());
                return true;
            }
        }

        private async Task<SubscribeResult> SubscribeToChannelEventsWithRetry(string _type, string _version)
        {
            int attempts = 0;
            while (attempts < 3)
            {
                try
                {
                    return await SubscribeToChannelEvents(_type, _version);
                }
                catch (Exception ex) when (ex is BadScopeException || ex is TokenExpiredException)
                {
                    attempts++;
                    bool refreshed = await _tokens.HandleUnauthorizedAsync(TwitchIdentity.Broadcaster);
                    _logger.LogWarning("Subscription to {Type} got 401 ({Attempt}/3); token {Result}. {Message}", _type, attempts, refreshed ? "refreshed" : "not refreshed", ex.Message);
                    if (!refreshed) await Task.Delay(1000 * attempts);
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
                    return SubscribeResult.Failed;
                }
            }
            return SubscribeResult.Failed;
        }

        /// <summary>Subscribed on success or 409 (already there); Rejected on 400; transient errors are thrown for the retry wrapper.</summary>
        private async Task<SubscribeResult> SubscribeToChannelEvents(string _type, string _version)
        {
            if (string.IsNullOrEmpty(_eventSubWebsocketClient.SessionId))
            {
                _logger.LogError("Cannot subscribe to {_type}: SessionId is null.", _type);
                return SubscribeResult.Failed;
            }

            try
            {
                var condition = new Dictionary<string, string>
                {
                    { "broadcaster_user_id", _config.BroadcasterId }
                };

                if (_type == "channel.chat_settings.update" || _type == "channel.chat.message")
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
                return SubscribeResult.Subscribed;
            }
            catch (BadRequestException ex)
            {
                _logger.LogError("Failed to subscribe to {_type}: Bad Request: {Message}", _type, ex.Message);
                return SubscribeResult.Rejected;
            }
            catch (BadTokenException ex)
            {
                // 403: the token lacks the scope for this subscription. Permanent for this token; keep the session.
                _logger.LogError("Failed to subscribe to {_type}: Forbidden (missing scope): {Message}", _type, ex.Message);
                return SubscribeResult.Rejected;
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("409") || ex.Message.Contains("Conflict"))
            {
                _logger.LogDebug("Subscription for {_type} already exists (Conflict 409).", _type);
                return SubscribeResult.Subscribed;
            }
            catch (Exception ex)
            {
                if (ex.InnerException is System.Net.Sockets.SocketException || ex is HttpRequestException || ex is BadScopeException || ex is TokenExpiredException)
                {
                    throw; // Re-throw for retry
                }

                if (ex.Message.Contains("Conflict") || (ex.InnerException?.Message.Contains("Conflict") ?? false))
                {
                    _logger.LogDebug("Subscription for {_type} already exists (Conflict).", _type);
                    return SubscribeResult.Subscribed;
                }

                _logger.LogError(ex, "Failed to subscribe to {_type} event.", _type);
                throw; // Re-throw to trigger retry
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

        /// <summary>Guard for channel events; also records that EventSub is delivering.</summary>
        private Task OnEvent(Func<Task> handler, string name)
        {
            _health.MarkEventSubEvent();
            _logger.LogDebug("EventSub event: {Handler}", name);
            return Guard(handler, name);
        }

        private Task OnChannelChatSettingsUpdate(object sender, ChannelChatSettingsUpdateArgs e) => OnEvent(async () =>
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

        private Task OnStreamUp(object sender, StreamOnlineArgs e) => OnEvent(() => _ircClient.OnStreamUp(), nameof(OnStreamUp));

        private Task OnStreamDown(object sender, StreamOfflineArgs e) => OnEvent(() => _ircClient.OnStreamDown(), nameof(OnStreamDown));

        private Task OnChannelBan(object sender, ChannelBanArgs e) => OnEvent(async () =>
        {
            var ev = e.Payload.Event;
            // The library does not expose the notification id, so a redelivery is recognized by user + banned_at.
            if (!FirstDelivery($"{ev.UserId}|{ev.BannedAt.UtcTicks}")) return;
            // Timeout bookkeeping used by the uval rewards and the re-mod logic; this replaces the IRC CLEARCHAT path
            // and runs whatever transport carries chat.
            if (!ev.IsPermanent && ev.EndsAt.HasValue && long.TryParse(ev.UserId, out long uid))
            {
                try
                {
                    var user = await _databaseService.GetUserAsync(uid);
                    if (user != null && user.dbID != -404)
                    {
                        user.UvalTimer = ev.EndsAt.Value.ToUnixTimeSeconds();
                        user.UvalCon++;
                        await _databaseService.UpdateUserAsync(user);
                    }
                }
                catch (Exception ex) { _logger.LogError(ex, "Timeout bookkeeping failed for {Login}", ev.UserLogin); }
                _logger.LogInformation("User {Login} timed out until {EndsAt:HH:mm:ss} by {Mod}", ev.UserLogin, ev.EndsAt.Value.ToLocalTime(), ev.ModeratorUserLogin);
            }
            bool longOne = !ev.IsPermanent && ev.EndsAt.HasValue && (ev.EndsAt.Value - ev.BannedAt).TotalSeconds > 50000;
            if (ev.IsPermanent || longOne)
                await _ircClient.SendMessage("o7");
        }, nameof(OnChannelBan));

        private Task OnPrediction(object sender, ChannelPredictionBeginArgs e) => OnEvent(async () =>
        {
            if (!_botState.Current.IsSubActive) return;
            string message = $"PopNemo {string.Format(STRINGS.PredictionStarted, e.Payload.Event.Title)} PopNemo";
            for (int i = 0; i < 3; i++)
            {
                await _ircClient.SendMessage(message);
                await Task.Delay(100);
            }
        }, nameof(OnPrediction));

        private Task OnPredictionEnd(object sender, ChannelPredictionEndArgs e) => OnEvent(async () =>
        {
            var ev = e.Payload.Event;
            var record = new MODELS.PredictionResultRecord
            {
                PredictionId = ev.Id, Title = ev.Title, Status = ev.Status, WinningOutcomeId = ev.WinningOutcomeId,
                StartedUtc = ev.StartedAt.UtcDateTime, EndedUtc = ev.EndedAt.UtcDateTime,
            };
            foreach (var o in ev.Outcomes ?? Array.Empty<TwitchLib.EventSub.Core.Models.Predictions.PredictionOutcomes>())
            {
                var outcome = new MODELS.PredictionOutcomeRecord
                {
                    OutcomeId = o.Id, Title = o.Title, Color = o.Color, Users = o.Users ?? 0, ChannelPoints = o.ChannelPoints ?? 0,
                    IsWinner = o.Id == ev.WinningOutcomeId,
                };
                foreach (var p in o.TopPredictors ?? Array.Empty<TwitchLib.EventSub.Core.Models.Predictions.Predictor>())
                    if (long.TryParse(p.UserId, out long uid))
                        outcome.TopPredictors.Add(new MODELS.PredictorRecord { TwitchId = uid, Login = p.UserLogin, PointsUsed = p.ChannelPointsUsed, PointsWon = p.ChannelPointsWon });
                record.Outcomes.Add(outcome);
            }
            await _engagement.PredictionEndedAsync(record);
            _logger.LogInformation("Prediction {Id} ended ({Status}): {Users} users, {Points} points over {Outcomes} outcomes.",
                ev.Id, ev.Status, record.Outcomes.Sum(o => o.Users), record.Outcomes.Sum(o => o.ChannelPoints), record.Outcomes.Count);
        }, nameof(OnPredictionEnd));

        private Task OnPollEnd(object sender, ChannelPollEndArgs e) => OnEvent(async () =>
        {
            var ev = e.Payload.Event;
            var record = new MODELS.PollResultRecord
            {
                PollId = ev.Id, Title = ev.Title, Status = ev.Status, StartedUtc = ev.StartedAt.UtcDateTime, EndedUtc = ev.EndedAt.UtcDateTime,
                Choices = (ev.Choices ?? Array.Empty<TwitchLib.EventSub.Core.Models.Polls.PollChoice>())
                    .Select(c => new MODELS.PollChoiceRecord { Title = c.Title, Votes = c.Votes ?? 0, ChannelPointsVotes = c.ChannelPointsVotes ?? 0 }).ToList(),
            };
            await _engagement.PollEndedAsync(record);
            _logger.LogInformation("Poll {Id} ended ({Status}): {Votes} votes.", ev.Id, ev.Status, record.Choices.Sum(c => c.Votes));
        }, nameof(OnPollEnd));

        private Task OnVipAdd(object sender, ChannelVipArgs e) => OnEvent(async () =>
        {
            var ev = e.Payload.Event;
            if (long.TryParse(ev.UserId, out long id)) await _vips.RecordGrantAsync(id, ev.UserLogin, ev.UserName, "twitch");
        }, nameof(OnVipAdd));

        private Task OnVipRemove(object sender, ChannelVipArgs e) => OnEvent(async () =>
        {
            var ev = e.Payload.Event;
            if (long.TryParse(ev.UserId, out long id)) await _vips.RecordRevokeAsync(id, "twitch");
        }, nameof(OnVipRemove));

        private Task OnUnban(object sender, ChannelUnbanArgs e) => OnEvent(async () =>
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

        private Task OnChannelPointsCustomRewardRedemptionAdd(object sender, ChannelPointsCustomRewardRedemptionArgs e) => OnEvent(() =>
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
