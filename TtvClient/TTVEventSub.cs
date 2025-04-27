using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using TwitchLib.EventSub.Websockets.Core.EventArgs;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Channel;
using SkillzBot.Singleton;
using TwitchLib.EventSub.Websockets;
using System.Collections.Generic;
using SkillzBot.WRITERS;
using SkillzBot.TtvClient.TTVRewards;
using SkillzBot.IRC;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Stream;
using SkillzBot.IllSTRINGS;
using SkillzBot.MYSQL;

namespace SkillzBot.EventSub
{
    public class TTVEventSub: IHostedService
    {
        private readonly EventSubWebsocketClient _eventSubWebsocketClient;
        private readonly TwitchAPI _twitchApi = new TwitchAPI();
        private readonly IllSingleton singleton = IllSingleton.GetInstance();
        private readonly string zakazTreka;
        private readonly string pi4ka;
        private readonly string uval;
        private readonly string uvalSab;
        private readonly string uvalVIP;
        private readonly string emoteMode;
        private readonly string cenceleUval;
        private readonly string uvalMod;
        private readonly string ChatWithBot;
        private readonly string BrodcasterId;
        private int tryes = 0;
        private readonly Dictionary<string, string> SubscriptionsTypes;

        public TTVEventSub(EventSubWebsocketClient eventSubWebsocketClient)
        {
            _eventSubWebsocketClient = eventSubWebsocketClient ?? throw new ArgumentNullException(nameof(eventSubWebsocketClient));
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

            _twitchApi.Settings.ClientId = singleton.TApiClientId;
            _twitchApi.Settings.AccessToken = singleton.TApiAccessToken;
            BrodcasterId = singleton.BrodcasterId;
            pi4ka = singleton.Pi4KaId;
            uval = singleton.UvalId;
            uvalSab = singleton.UvalSabId;
            uvalVIP = singleton.UvalVipId;
            emoteMode = singleton.EmoteModeId;
            cenceleUval = singleton.CenceleUval;
            uvalMod = singleton.uvalMod;
            ChatWithBot = singleton.ChatWithBot;
            zakazTreka = singleton.ZakazTrekaId; 
            SubscriptionsTypes = new Dictionary<string, string> //name, version
            { 
                { "channel.bits.use", "1" }, 
                { "channel.channel_points_custom_reward_redemption.add", "1" },
                { "channel.moderate", "1"},
                { "channel.prediction.begin", "1"}
            };
        }        
        #region EventSub stuff
        private async Task OnErrorOccurred(object sender, ErrorOccuredArgs e)
        {
            Log.WriteLog(null, $"Websocket error: {e.Message}, Session ID: {_eventSubWebsocketClient.SessionId}");
            await Task.CompletedTask.ConfigureAwait(false);
        }

        private async Task OnChannelFollow(object sender, ChannelFollowArgs e)
        {            
            //var eventData = e.Notification.Payload.Event;
            //Log.WriteLog(null, $"{eventData.UserName} followed {eventData.BroadcasterUserName} at {eventData.FollowedAt}");
            await Task.CompletedTask.ConfigureAwait(false);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Log.WriteLog(null, "Starting Twitch EventSub service...");
            await _eventSubWebsocketClient.ConnectAsync().ConfigureAwait(false);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Log.WriteLog(null, "Stopping Twitch EventSub service...");
            
            await _eventSubWebsocketClient.DisconnectAsync().ConfigureAwait(false);
        }

        private async Task OnWebsocketConnected(object sender, WebsocketConnectedArgs e)
        {
            Log.WriteLog(null, $"Websocket connected. Session ID: {_eventSubWebsocketClient.SessionId}, Reconnect: {e.IsRequestedReconnect}");
            if (!e.IsRequestedReconnect)
            {
                await Subscribe().ConfigureAwait(false);
            }
        }

        private async Task OnWebsocketDisconnected(object sender, EventArgs e)
        {
            Log.WriteLog(null, $"Websocket disconnected. Session ID: {_eventSubWebsocketClient.SessionId}");

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
                        Log.WriteLog(null, "Websocket reconnected successfully.");
                        await Subscribe().ConfigureAwait(false);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteLog(ex, $"Websocket reconnect attempt {retryCount + 1}/{maxRetries} failed.");
                }

                retryCount++;
                int delay = baseDelayMs * (int)Math.Pow(2, retryCount) + jitter.Next(0, 100); // Exponential backoff with jitter
                Log.WriteLog(null, $"Waiting {delay}ms before retry {retryCount + 1}/{maxRetries}...");
                await Task.Delay(delay).ConfigureAwait(false);
            }
            Log.WriteLog(null, "Failed to reconnect after maximum retries. Please restart the service.");
        }

        private async Task OnWebsocketReconnected(object sender, EventArgs e)
        {
            Log.WriteLog(null, $"Websocket reconnected. Session ID: {_eventSubWebsocketClient.SessionId}");
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
                var condition = new Dictionary<string, string> { { "broadcaster_user_id", BrodcasterId }, { "moderator_user_id", BrodcasterId } };
                var subscription = await _twitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                    type: _type,
                    version: _version,
                    condition: condition,
                    method: EventSubTransportMethod.Websocket,
                    websocketSessionId: _eventSubWebsocketClient.SessionId).ConfigureAwait(false);
                Log.WriteLog(null, $"Subscribed to {_type}. Subscription ID: {subscription.Subscriptions[0].Id}");
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, $"Failed to subscribe to {_type} event.");
            }
        }
        #endregion

        #region Events
        private async Task OnStreamUp(object sender, StreamOnlineArgs e)
        {
            await TtvIRCClient.OnStreamUp().ConfigureAwait(false);
        }
        private async Task OnStreamDown(object sender, StreamOfflineArgs e)
        {
            await TtvIRCClient.OnStreamDown().ConfigureAwait(false);
        }
        private async Task OnChannelBan(object sender, ChannelBanArgs e)
        {
            if (e.Notification.Payload.Event.IsPermanent)
            {
                //BAN                
                TtvIRCClient.SendMessage($"{e.Notification.Payload.Event.UserName} o7");
            }
            else
            {
                //TIMEOUT
            }
            await Task.CompletedTask.ConfigureAwait(false);
        }        
        private async Task OnPrediction(object sender, ChannelPredictionBeginArgs e)
        {
            if (!singleton.isActiveSub) return;
            TtvIRCClient.SendMessage(string.Format(STRINGS.PredictionStarted, e.Notification.Payload.Event.Title));
            await Task.CompletedTask.ConfigureAwait(false);
        }
        private async Task OnUnban(object sender, ChannelUnbanArgs e)
        {
            if (!singleton.isActiveSub) return;
            TtvIRCClient.OnUnban(e);
            await Task.CompletedTask.ConfigureAwait(false);
        }
        private async Task OnChannelPointsCustomRewardRedemptionAdd(object sender, ChannelPointsCustomRewardRedemptionArgs e)
        {
            if (!singleton.isActiveSub) return;
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
            if (!singleton.isActiveSub) return;
            try
            {
                if (rewardID == zakazTreka)
                {
                    await RewardsRedemption.ZakazTrekaReward(userName, message, redemID, rewardID).ConfigureAwait(false);
                }
                if (rewardID == pi4ka)
                {
                    await RewardsRedemption.Pi4kaReward(userName, redemID, rewardID).ConfigureAwait(false);
                }
                if (rewardID == uval)
                {
                    await RewardsRedemption.UvalReward(userName, message, redemID, rewardID).ConfigureAwait(false);
                }
                if (rewardID == uvalSab)
                {
                    await RewardsRedemption.UvalSabReward(userName, message, redemID, rewardID).ConfigureAwait(false);
                }
                if (rewardID == uvalVIP)
                {
                    await RewardsRedemption.UvalVIPReward(userName, message, redemID, rewardID).ConfigureAwait(false);
                }
                if (rewardID == emoteMode)
                {
                    await RewardsRedemption.EmoteOnlyReward(userName, redemID, rewardID).ConfigureAwait(false);
                }
                if (rewardID == cenceleUval)
                {
                    await RewardsRedemption.CenceleUvalReward(userName, message, redemID, rewardID).ConfigureAwait(false);
                }
                if (rewardID == uvalMod)
                {
                    await RewardsRedemption.UvalModReward(userName, message, redemID, rewardID).ConfigureAwait(false);
                }
                if (rewardID == ChatWithBot)
                {
                    await RewardsRedemption.ChatWithBot(userName, message, redemID, rewardID).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                if (tryes <= 3)
                {
                    if (e.Message.Contains("500"))
                    {
                        Log.WriteLog(e, $"rewardProcess() tryes: {tryes}");
                        await Task.Delay(2000).ConfigureAwait(false);
                        tryes++;
                        await RewardProcess(rewardID, userName, message, redemID).ConfigureAwait(false);
                    }
                }
                tryes = 0;
                Log.WriteLog(e, "rewardProcess()");
            }
        }
    }
}