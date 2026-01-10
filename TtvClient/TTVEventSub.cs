using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkillzBot.IllSTRINGS;
using SkillzBot.Interfaces;
using SkillzBot.IllConfiguration; 
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
    internal class TTVEventSub : IHostedService
    {
        private readonly ILogger<TTVEventSub> _logger;
        private readonly IDatabaseService _databaseService;
        private readonly ITtvIRCClient _ircClient;
        private readonly EventSubWebsocketClient _eventSubWebsocketClient;
        private readonly RewardsRedemption _rewardsRedemption;
        private readonly TwitchAPI _twitchApi = new TwitchAPI();
        private readonly BotConfigModel _config;
        private readonly IBotStateService _botState;
        private int tryes = 0;
        private readonly Dictionary<string, string> SubscriptionsTypes;

        public TTVEventSub(
            EventSubWebsocketClient eventSubWebsocketClient, 
            IDatabaseService databaseService, 
            ITtvIRCClient ircClient,
            RewardsRedemption rewardsRedemption,
            ILogger<TTVEventSub> logger,
            BotConfigModel config,
            IBotStateService botState)
        {
            _ircClient = ircClient;
            _eventSubWebsocketClient = eventSubWebsocketClient ?? throw new ArgumentNullException(nameof(eventSubWebsocketClient));
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
            _rewardsRedemption = rewardsRedemption;
            _logger = logger;
            _config = config;
            _botState = botState;

            _logger.LogDebug("TTVEventSub initialized");

            _eventSubWebsocketClient.WebsocketConnected += OnWebsocketConnected;
            _eventSubWebsocketClient.WebsocketDisconnected += OnWebsocketDisconnected;
            _eventSubWebsocketClient.WebsocketReconnected += OnWebsocketReconnected;
            _eventSubWebsocketClient.ErrorOccurred += OnErrorOccurred;
            _eventSubWebsocketClient.ChannelFollow += OnChannelFollow;

            _eventSubWebsocketClient.ChannelPointsCustomRewardRedemptionAdd += OnChannelPointsCustomRewardRedemptionAdd;
            _eventSubWebsocketClient.StreamOnline += OnStreamUp;
            _eventSubWebsocketClient.StreamOffline += OnStreamDown;
            _eventSubWebsocketClient.ChannelPredictionBegin += OnPrediction;
            _eventSubWebsocketClient.ChannelUnban += OnUnban;
            _eventSubWebsocketClient.ChannelBan += OnChannelBan;

            _twitchApi.Settings.ClientId = _config.TApiClientId;
            _twitchApi.Settings.AccessToken = _config.TApiAccessToken;

            SubscriptionsTypes = new Dictionary<string, string>
            {
                { "channel.bits.use", "1" },
                { "channel.channel_points_custom_reward_redemption.add", "1" },
                { "channel.unban", "1"},
                { "channel.ban", "1"},
                { "channel.prediction.begin", "1"}
            };
        }

        #region EventSub stuff
        private async Task OnErrorOccurred(object sender, ErrorOccuredArgs e)
        {
            _logger.LogError("Websocket error: {Message} , Session ID: {SessionId}", e.Message, _eventSubWebsocketClient.SessionId);
            await Task.CompletedTask;
        }

        private async Task OnChannelFollow(object sender, ChannelFollowArgs e)
        {
            await Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting Twitch EventSub service...");
            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation("EventSub connecting...");
                    await _eventSubWebsocketClient.ConnectAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to connect EventSub Websocket during startup.");
                }
            }, cancellationToken);

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
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
        }

        private async Task OnWebsocketConnected(object sender, WebsocketConnectedArgs e)
        {
            _logger.LogInformation("Websocket connected. Session ID: {SessionId}, Reconnect: {IsRequestedReconnect}", _eventSubWebsocketClient.SessionId, e.IsRequestedReconnect);
            if (!e.IsRequestedReconnect)
            {
                await Subscribe();
            }
        }

        private async Task OnWebsocketDisconnected(object sender, EventArgs e)
        {
            _logger.LogWarning(null, "Websocket disconnected. Session ID: {SessionId}", _eventSubWebsocketClient.SessionId);

            int retryCount = 0;
            int maxRetries = 5;
            int baseDelayMs = 1000;

            while (retryCount < maxRetries)
            {
                try
                {
                    if (await _eventSubWebsocketClient.ReconnectAsync().ConfigureAwait(false))
                    {
                        _logger.LogInformation("Websocket reconnected successfully.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Websocket reconnect attempt {retryCount}/{maxRetries} failed.", retryCount + 1, maxRetries);
                }

                retryCount++;
                await Task.Delay(baseDelayMs * (int)Math.Pow(2, retryCount));
            }
            _logger.LogCritical("Failed to reconnect after maximum retries. Please restart the service.");
        }

        private async Task OnWebsocketReconnected(object sender, EventArgs e)
        {
            _logger.LogInformation("Websocket reconnected. Session ID: {SessionId}", _eventSubWebsocketClient.SessionId);
            await Task.CompletedTask;
        }
        private async Task Subscribe()
        {
            foreach (var type in SubscriptionsTypes)
            {
                await SubscribeToChannelEvents(type.Key, type.Value);
            }
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
                    { "broadcaster_user_id", _config.BroadcasterId },
                    { "moderator_user_id", _config.BroadcasterId }
                };

                var subscription = await _twitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                    type: _type,
                    version: _version,
                    condition: condition,
                    method: EventSubTransportMethod.Websocket,
                    websocketSessionId: _eventSubWebsocketClient.SessionId);

                _logger.LogInformation("Subscribed to {_type}. Subscription ID: {Id}", _type, subscription.Subscriptions[0].Id);
            }
            catch (BadRequestException ex)
            {
                // 400 Bad Request: Usually means SessionId is dead or invalid
                _logger.LogError("Failed to subscribe to {_type}: Bad Request (Invalid Session?): {Message}", _type, ex.Message);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("409") || ex.Message.Contains("Conflict"))
            {
                // 409 Conflict: Subscription already exists. This is fine, effectively a success.
                _logger.LogWarning("Subscription for {_type} already exists (Conflict 409). Skipping.", _type);
            }
            catch (Exception ex)
            {
                // Check inner exception for 409 Conflict just in case it's wrapped
                if (ex.Message.Contains("Conflict") || (ex.InnerException != null && ex.InnerException.Message.Contains("Conflict")))
                {
                    _logger.LogWarning("Subscription for {_type} already exists (Conflict). Skipping.", _type);
                }
                else
                {
                    _logger.LogError(ex, "Failed to subscribe to {_type} event.", _type);
                }
            }
        }
        #endregion

        #region Events
        private async Task OnStreamUp(object sender, StreamOnlineArgs e)
        {
            await _ircClient.OnStreamUp();
        }
        private async Task OnStreamDown(object sender, StreamOfflineArgs e)
        {
            await _ircClient.OnStreamDown();
        }
        private async Task OnChannelBan(object sender, ChannelBanArgs e)
        {
            // FIX: Updated property access. Payload is now direct.
            if (e.Payload.Event.IsPermanent)
            {
                await _ircClient.SendMessage($"o7");
            }
            await Task.CompletedTask;
        }
        private async Task OnPrediction(object sender, ChannelPredictionBeginArgs e)
        {
            if (!_botState.Current.IsSubActive) return;
            await _ircClient.SendMessage(string.Format(STRINGS.PredictionStarted, e.Payload.Event.Title));
        }
        private async Task OnUnban(object sender, ChannelUnbanArgs e)
        {
            if (!_botState.Current.IsSubActive) return;

            try
            {
                var user = await _databaseService.GetUserAsync(e.Payload.Event.UserLogin);
                if (user.dbID == -404)
                {
                    _logger.LogCritical("UserTimedoutEventTask id = -1 username:{UserLogin}", e.Payload.Event.UserLogin);
                }
                else
                {
                    user.UvalTimer = 0;
                    await _databaseService.UpdateUserAsync(user);
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Database operation failed");
            }
        }
        private async Task OnChannelPointsCustomRewardRedemptionAdd(object sender, ChannelPointsCustomRewardRedemptionArgs e)
        {
            if (!_botState.Current.IsSubActive) return;
            await RewardProcess
            (
                e.Payload.Event.Reward.Id,
                e.Payload.Event.UserLogin,
                e.Payload.Event.UserInput,
                e.Payload.Event.Id
            );
        }

        #endregion

        private async Task RewardProcess(string rewardID, string userName, string message, string redemID)
        {
            if (!_botState.Current.IsSubActive) return;
            try
            {
                if (rewardID == _config.ChannelIds.ZakazTrekaId)
                {
                    await _rewardsRedemption.ZakazTrekaReward(userName, message, redemID, rewardID);
                }
                else if (rewardID == _config.ChannelIds.Pi4KaId)
                {
                    await _rewardsRedemption.Pi4kaReward(userName, redemID, rewardID);
                }
                else if (rewardID == _config.ChannelIds.UvalId)
                {
                    await _rewardsRedemption.UvalReward(userName, message, redemID, rewardID);
                }
                else if (rewardID == _config.ChannelIds.UvalSabId)
                {
                    await _rewardsRedemption.UvalSabReward(userName, message, redemID, rewardID);
                }
                else if (rewardID == _config.ChannelIds.UvalVipId)
                {
                    await _rewardsRedemption.UvalVIPReward(userName, message, redemID, rewardID);
                }
                else if (rewardID == _config.ChannelIds.EmoteModeId)
                {
                    await _rewardsRedemption.EmoteOnlyReward(userName, redemID, rewardID);
                }
                else if (rewardID == _config.ChannelIds.CenceleUval)
                {
                    await _rewardsRedemption.CenceleUvalReward(userName, message, redemID, rewardID);
                }
                else if (rewardID == _config.ChannelIds.UvalMod)
                {
                    await _rewardsRedemption.UvalModReward(userName, message, redemID, rewardID);
                }

            }
            catch (Exception e)
            {
                if (tryes <= 3)
                {
                    if (e.Message.Contains("500"))
                    {
                        _logger.LogError(e, "Cant perform ttv reward operation! Num of tryes: {tryes}", tryes);
                        await Task.Delay(2000);
                        tryes++;
                        await RewardProcess(rewardID, userName, message, redemID);
                        return;
                    }
                }
                tryes = 0;
                _logger.LogWarning(e, "Cant perform ttv reward operation. Retrying...");
            }
        }
    }
}