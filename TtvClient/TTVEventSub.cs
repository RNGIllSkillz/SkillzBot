using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using TwitchLib.EventSub.Websockets.Core.EventArgs;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Channel;
using TwitchLib.EventSub.Websockets;
using System.Collections.Generic;
using SkillzBot.TtvClient.TTVRewards;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Stream;
using SkillzBot.IllSTRINGS;
using Microsoft.Extensions.Logging;
using SkillzBot.Interfaces;
using SkillzBot.Singleton;

namespace SkillzBot.EventSub
{
    public class TTVEventSub: IHostedService
    {
        private static ILogger<TTVEventSub>? _logger;
        private readonly IDatabaseService _databaseService;
        private readonly ITtvIRCClient _ircClient;
        private readonly EventSubWebsocketClient _eventSubWebsocketClient;
        private readonly TwitchAPI _twitchApi = new TwitchAPI();
        private int tryes = 0;
        private readonly Dictionary<string, string> SubscriptionsTypes;

        public TTVEventSub(EventSubWebsocketClient eventSubWebsocketClient, ILogger<TTVEventSub> logger, IDatabaseService databaseService, ITtvIRCClient ircClient)
        {
            _logger = logger;
            _ircClient = ircClient;
            _eventSubWebsocketClient = eventSubWebsocketClient ?? throw new ArgumentNullException(nameof(eventSubWebsocketClient));
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
            _logger.LogDebug("TTVEventSub initialized with injected dependencies");

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

            _twitchApi.Settings.ClientId = IllSingleton.Config.TApiClientId;
            _twitchApi.Settings.AccessToken = IllSingleton.Config.TApiAccessToken;
            
            SubscriptionsTypes = new Dictionary<string, string> //name, version
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
            await Task.CompletedTask.ConfigureAwait(false);
        }

        private async Task OnChannelFollow(object sender, ChannelFollowArgs e)
        {            
            await Task.CompletedTask.ConfigureAwait(false);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting Twitch EventSub service...");
            await _eventSubWebsocketClient.ConnectAsync().ConfigureAwait(false);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping Twitch EventSub service...");
            
            await _eventSubWebsocketClient.DisconnectAsync().ConfigureAwait(false);
        }

        private async Task OnWebsocketConnected(object sender, WebsocketConnectedArgs e)
        {
            _logger.LogInformation("Websocket connected. Session ID: {SessionId}, Reconnect: {IsRequestedReconnect}", _eventSubWebsocketClient.SessionId, e.IsRequestedReconnect);
            if (!e.IsRequestedReconnect)
            {
                await Subscribe().ConfigureAwait(false);
            }
        }

        private async Task OnWebsocketDisconnected(object sender, EventArgs e)
        {
            _logger.LogWarning(null, "Websocket disconnected. Session ID: {SessionId}", _eventSubWebsocketClient.SessionId);

            int retryCount = 0;
            int maxRetries = 5;
            int baseDelayMs = 1000;
            Random jitter = new Random();

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
                    _logger.LogError(ex, "Websocket reconnect attempt {retryCount}/{maxRetries} failed.", retryCount+1, maxRetries);
                }

                retryCount++;
                int delay = baseDelayMs * (int)Math.Pow(2, retryCount) + jitter.Next(0, 100); // Exponential backoff with jitter
                _logger.LogDebug("Waiting {delay}ms before retry {retryCount}/{maxRetries}...", delay, retryCount + 1, maxRetries);
                await Task.Delay(delay).ConfigureAwait(false);
            }
            _logger.LogCritical("Failed to reconnect after maximum retries. Please restart the service.");
        }

        private async Task OnWebsocketReconnected(object sender, EventArgs e)
        {
            _logger.LogInformation("Websocket reconnected. Session ID: {SessionId}", _eventSubWebsocketClient.SessionId);
            await Task.CompletedTask.ConfigureAwait(false);
        }
        private async Task Subscribe()
        {
            foreach (var type in SubscriptionsTypes)
            {
                await SubscribeToChannelEvents(type.Key, type.Value).ConfigureAwait(false);
            }
        }
        private async Task SubscribeToChannelEvents(string _type, string _version)
        {
            try
            {
                var condition = new Dictionary<string, string> { { "broadcaster_user_id", IllSingleton.Config.BroadcasterId }, { "moderator_user_id", IllSingleton.Config.BroadcasterId } };
                var subscription = await _twitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                    type: _type,
                    version: _version,
                    condition: condition,
                    method: EventSubTransportMethod.Websocket,
                    websocketSessionId: _eventSubWebsocketClient.SessionId).ConfigureAwait(false);
                _logger.LogInformation("Subscribed to {_type}. Subscription ID: {Id}", _type, subscription.Subscriptions[0].Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to subscribe to {_type} event.", _type);
            }
        }
        #endregion

        #region Events
        private async Task OnStreamUp(object sender, StreamOnlineArgs e)
        {
            await _ircClient.OnStreamUp().ConfigureAwait(false);
        }
        private async Task OnStreamDown(object sender, StreamOfflineArgs e)
        {
            await _ircClient.OnStreamDown().ConfigureAwait(false);
        }
        private async Task OnChannelBan(object sender, ChannelBanArgs e)
        {
            if (e.Notification.Payload.Event.IsPermanent)
            {
                //BAN
                _ircClient.SendMessage($"o7");
            }
            else
            {
                //TIMEOUT
                //Implemented via IRC Client_OnUserTimedout
            }
            await Task.CompletedTask.ConfigureAwait(false);
        }        
        private async Task OnPrediction(object sender, ChannelPredictionBeginArgs e)
        {
            if (!IllSingleton.State.isSubActive) return;
            _ircClient.SendMessage(string.Format(STRINGS.PredictionStarted, e.Notification.Payload.Event.Title));
            await Task.CompletedTask.ConfigureAwait(false);
        }
        private async Task OnUnban(object sender, ChannelUnbanArgs e)
        {
            if (!IllSingleton.State.isSubActive) return;
            //TtvIRCClient.OnUnban(e);
            try
            {
                var user = await _databaseService.GetUserAsync(e.Notification.Payload.Event.UserLogin).ConfigureAwait(false);
                if (user.dbID == -404)
                {
                    _logger.LogCritical("UserTimedoutEventTask id = -1 username:{UserLogin}", e.Notification.Payload.Event.UserLogin);
                }
                else
                {
                    user.UvalTimer = 0;
                    await _databaseService.UpdateUserAsync(user).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Database operation failed");
            }
        }
        private async Task OnChannelPointsCustomRewardRedemptionAdd(object sender, ChannelPointsCustomRewardRedemptionArgs e)
        {
            if (!IllSingleton.State.isSubActive) return;
            await RewardProcess
            (
                e.Notification.Payload.Event.Reward.Id,
                e.Notification.Payload.Event.UserLogin,
                e.Notification.Payload.Event.UserInput,
                e.Notification.Payload.Event.Id
            ).ConfigureAwait(false);
        }
        
        #endregion


        private async Task RewardProcess(string rewardID, string userName, string message, string redemID)
        {
            if (!IllSingleton.State.isSubActive) return;
            try
            {
                if (rewardID == IllSingleton.Config.ChannelIds.ZakazTrekaId)
                {
                    await RewardsRedemption.ZakazTrekaReward(userName, message, redemID, rewardID).ConfigureAwait(false);
                }
                if (rewardID == IllSingleton.Config.ChannelIds.Pi4KaId)
                {
                    await RewardsRedemption.Pi4kaReward(userName, redemID, rewardID).ConfigureAwait(false);
                }
                if (rewardID == IllSingleton.Config.ChannelIds.UvalId)
                {
                    await RewardsRedemption.UvalReward(userName, message, redemID, rewardID).ConfigureAwait(false);
                }
                if (rewardID == IllSingleton.Config.ChannelIds.UvalSabId)
                {
                    await RewardsRedemption.UvalSabReward(userName, message, redemID, rewardID).ConfigureAwait(false);
                }
                if (rewardID == IllSingleton.Config.ChannelIds.UvalVipId)
                {
                    await RewardsRedemption.UvalVIPReward(userName, message, redemID, rewardID).ConfigureAwait(false);
                }
                if (rewardID == IllSingleton.Config.ChannelIds.EmoteModeId)
                {
                    await RewardsRedemption.EmoteOnlyReward(userName, redemID, rewardID).ConfigureAwait(false);
                }
                if (rewardID == IllSingleton.Config.ChannelIds.CenceleUval)
                {
                    await RewardsRedemption.CenceleUvalReward(userName, message, redemID, rewardID).ConfigureAwait(false);
                }
                if (rewardID == IllSingleton.Config.ChannelIds.UvalMod)
                {
                    await RewardsRedemption.UvalModReward(userName, message, redemID, rewardID).ConfigureAwait(false);
                }
                //if (rewardID == ChatWithBot)
                //{
                //    await RewardsRedemption.ChatWithBot(userName, message, redemID, rewardID).ConfigureAwait(false);
                //}
            }
            catch (Exception e)
            {
                if (tryes <= 3)
                {
                    if (e.Message.Contains("500"))
                    {
                        _logger.LogError(e, "Cant perform ttv reward operation! Num of tryes: {tryes}", tryes);
                        await Task.Delay(2000).ConfigureAwait(false);
                        tryes++;
                        await RewardProcess(rewardID, userName, message, redemID).ConfigureAwait(false);
                    }
                }
                tryes = 0;
                _logger.LogWarning(e, "Cant perform ttv reward operation. Retrying...");
            }
        }
    }
}