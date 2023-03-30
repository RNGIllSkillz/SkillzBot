using System;
using IllPubSub;
using IllPubSub.Events;
using System.Threading.Tasks;
using SkillzBot.WRITERS;
using SkillzBot.IRC;
using SkillzBot.TtvClient.TTVRewards;
using SkillzBot.API.Twitch;
using IllPubSub.Enums;
using SkillzBot.MYSQL;
using SkillzBot.Singleton;
using SkillzBot.IllSTRINGS;
using IllSkillzBot;

namespace SkillzBot.PubSub
{
    class PubSubClient : IDisposable
    {
        private static TwitchPubSub client;
        readonly string accToken;
        readonly string zakazTreka = IllSingleton.GetInstance().ZakazTrekaId;
        readonly string pi4ka;
        readonly string uval;
        readonly string uvalSab;
        readonly string uvalVIP;
        readonly string emoteMode;
        readonly string cenceleUval;
        readonly string englishWis;
        readonly string ChatWithBot;
        readonly string BrodcasterId;
        private int tryes = 0;
        private bool disposed = false;
        bool lockPubSub = false;

        public PubSubClient()
        {
            var singleton = IllSingleton.GetInstance();
            BrodcasterId = singleton.BrodcasterId;
            pi4ka = singleton.Pi4KaId;
            uval = singleton.UvalId;
            uvalSab = singleton.UvalSabId;
            uvalVIP = singleton.UvalVipId;
            emoteMode = singleton.EmoteModeId;
            cenceleUval = singleton.CenceleUval;
            englishWis = singleton.EnglishWis;
            ChatWithBot = singleton.ChatWithBot;
            accToken = singleton.TApiAccessToken;

            client = new TwitchPubSub();
            client.OnPubSubServiceClosed += OnPubSubServiceClosed;
            client.OnListenResponse += OnListenResponse;
            client.OnPubSubServiceConnected += OnPubSubServiceConnected;
            client.OnPubSubServiceError += OnPubSubServiceError;
            ListenToBits(BrodcasterId);
            ListenToChatModeratorActions(BrodcasterId, BrodcasterId);
            ListenToFollows(BrodcasterId);
            ListenToPredictions(BrodcasterId);
            ListenToRaid(BrodcasterId);
            ListenToRewards(BrodcasterId);
            ListenToSubscriptions(BrodcasterId);
            ListenToVideoPlayback(BrodcasterId);
            client.Connect();
        }        

        #region Video Playback Events

        private void ListenToVideoPlayback(string channelId)
        {
            client.OnStreamUp += PubSub_OnStreamUp;
            client.OnStreamDown += PubSub_OnStreamDown;
            client.OnViewCount += PubSub_OnViewCount;
            client.ListenToVideoPlayback(channelId);
        }

        private void PubSub_OnViewCount(object sender, OnViewCountArgs e)
        {
            //_bot.OnViewCount(e.Viewers);
        }

        private void PubSub_OnStreamDown(object sender, OnStreamDownArgs e)
        {
            TtvIRCClient.OnStreamDown();
        }

        private void PubSub_OnStreamUp(object sender, OnStreamUpArgs e)
        {
            TtvIRCClient.OnStreamUp();
        }

        #endregion

        #region Subscription Events

        private void ListenToSubscriptions(string channelId)
        {
            client.OnChannelSubscription += PubSub_OnChannelSubscription;
            client.ListenToSubscriptions(channelId);
        }

        private async void PubSub_OnChannelSubscription(object sender, OnChannelSubscriptionArgs e)
        {
            var gifted = e.Subscription.IsGift ?? false;
            if (gifted)
            {
                TtvIRCClient.SendMessage(string.Format(STRINGS.GiftedSubMessage, e.Subscription.DisplayName, e.Subscription.RecipientName));
                await MySQL.AddPoints(600, int.Parse(e.Subscription.UserId)).ConfigureAwait(false);
            }
            else
            {
                var cumulativeMonths = e.Subscription.CumulativeMonths ?? 0;
                if (cumulativeMonths != 0)
                {
                    TtvIRCClient.SendMessage(string.Format(STRINGS.SubMessage, e.Subscription.DisplayName, cumulativeMonths, 450));
                    await MySQL.AddPoints(450, int.Parse(e.Subscription.UserId)).ConfigureAwait(false);
                }
                else
                {
                    TtvIRCClient.SendMessage(string.Format(STRINGS.SubMessage, e.Subscription.DisplayName, 0, 550));
                    await MySQL.AddPoints(550, int.Parse(e.Subscription.UserId)).ConfigureAwait(false);
                }
            }
        }

        #endregion

        #region Reward Events

        private void ListenToRewards(string channelId)
        {
            client.OnChannelPointsRewardRedeemed += PubSub_OnChannelPointsRewardRedeemed;
            client.ListenToChannelPoints(channelId);
        }
        private async void PubSub_OnChannelPointsRewardRedeemed(object sender, OnChannelPointsRewardRedeemedArgs e)
        {
            if (lockPubSub) return;
            await RewardProcess
            (
                e.RewardRedeemed.Redemption.Reward.Id,
                e.RewardRedeemed.Redemption.User.Login,
                e.RewardRedeemed.Redemption.UserInput,
                e.RewardRedeemed.Redemption.Id
            ).ConfigureAwait(false);
        }

        #endregion

        #region Follow Events

        private void ListenToFollows(string channelId)
        {
            client.OnFollow += PubSub_OnFollow;
            client.ListenToFollows(channelId);
        }

        private void PubSub_OnFollow(object sender, OnFollowArgs e)
        {
            //_logger.Information($"{e.Username} is now following");
        }

        #endregion

        #region Prediction Events

        private void ListenToPredictions(string channelId)
        {
            client.OnPrediction += PubSub_OnPrediction;
            client.ListenToPredictions(channelId);
        }

        private void PubSub_OnPrediction(object sender, OnPredictionArgs e)
        {
            if (e.Type == PredictionType.EventCreated)
            {
                TtvAPI.Announce(string.Format(STRINGS.PredictionStarted, e.Title)).GetAwaiter().GetResult();
            }
        }

        #endregion

        #region Outgoing Raid Events

        private void ListenToRaid(string channelId)
        {
            client.OnRaidUpdateV2 += PubSub_OnRaidUpdateV2;
            client.OnRaidGo += PubSub_OnRaidGo;
            client.ListenToRaid(channelId);
        }

        private void PubSub_OnRaidGo(object sender, OnRaidGoArgs e)
        {
            //_logger.Information($"Execute raid for {e.TargetDisplayName}");\
            //_bot.OnRaidUpdate(e);
        }

        private void PubSub_OnRaidUpdateV2(object sender, OnRaidUpdateV2Args e)
        {
            //_bot.OnRaidUpdate(e);
        }

        #endregion

        #region Moderator Events

        private void ListenToChatModeratorActions(string myTwitchId, string channelId)
        {
            client.OnTimeout += PubSub_OnTimeout;
            client.OnBan += PubSub_OnBan;
            client.OnMessageDeleted += PubSub_OnMessageDeleted;
            client.OnUnban += PubSub_OnUnban;
            client.OnUntimeout += PubSub_OnUntimeout;
            client.OnHost += PubSub_OnHost;
            client.OnSubscribersOnly += PubSub_OnSubscribersOnly;
            client.OnSubscribersOnlyOff += PubSub_OnSubscribersOnlyOff;
            client.OnClear += PubSub_OnClear;
            client.OnEmoteOnly += PubSub_OnEmoteOnly;
            client.OnEmoteOnlyOff += PubSub_OnEmoteOnlyOff;
            client.OnR9kBeta += PubSub_OnR9kBeta;
            client.OnR9kBetaOff += PubSub_OnR9kBetaOff;
            client.ListenToChatModeratorActions(myTwitchId, channelId);
        }
        private void PubSub_OnR9kBetaOff(object sender, OnR9kBetaOffArgs e)
        {
            //_logger.Information($"{e.Moderator} disabled R9K mode");
        }
        private void PubSub_OnR9kBeta(object sender, OnR9kBetaArgs e)
        {
            //_logger.Information($"{e.Moderator} enabled R9K mode");
        }
        private void PubSub_OnEmoteOnlyOff(object sender, OnEmoteOnlyOffArgs e)
        {
            //_logger.Information($"{e.Moderator} disabled emote only mode");
        }
        private void PubSub_OnEmoteOnly(object sender, OnEmoteOnlyArgs e)
        {
            //_logger.Information($"{e.Moderator} enabled emote only mode");
        }
        private void PubSub_OnClear(object sender, OnClearArgs e)
        {
            //_logger.Information($"{e.Moderator} cleared the chat");
        }
        private void PubSub_OnSubscribersOnlyOff(object sender, OnSubscribersOnlyOffArgs e)
        {
            //_logger.Information($"{e.Moderator} disabled subscriber only mode");
        }
        private void PubSub_OnSubscribersOnly(object sender, OnSubscribersOnlyArgs e)
        {
            //_logger.Information($"{e.Moderator} enabled subscriber only mode");
        }
        private void PubSub_OnHost(object sender, OnHostArgs e)
        {
            //_logger.Information($"{e.Moderator} started host to {e.HostedChannel}");
        }
        private async void PubSub_OnUntimeout(object sender, OnUntimeoutArgs e)
        {
            try
            {
                var user = await MySQL.GetUser(e.UntimeoutedUser.ToLower()).ConfigureAwait(false);
                if (user.dbID == -404)
                {
                    Log.WriteLog(null, $"UserTimedoutEventTask id = -1 username:{e.UntimeoutedUser}");
                }
                else
                {
                    user.UvalTimer = 0;
                    await MySQL.UpdateUser(user).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "null");
            }
        }
        private void PubSub_OnUnban(object sender, OnUnbanArgs e)
        {
            TtvIRCClient.OnUnban(e);
        }
        private void PubSub_OnMessageDeleted(object sender, OnMessageDeletedArgs e)
        {
            //_logger.Information($"{e.DeletedBy} deleted the message \"{e.Message}\" from {e.TargetUser}");
        }
        private void PubSub_OnBan(object sender, OnBanArgs e)
        {
            //_logger.Information($"{e.BannedBy} banned {e.BannedUser} ({e.BanReason})");
        }
        private void PubSub_OnTimeout(object sender, OnTimeoutArgs e)
        {
            //_logger.Information($"{e.TimedoutBy} timed out {e.TimedoutUser} ({e.TimeoutReason}) for {e.TimeoutDuration.Seconds} seconds");
        }

        #endregion

        #region Bits Events

        private void ListenToBits(string channelId)
        {
            client.OnBitsReceived += PubSub_OnBitsReceived;
            client.ListenToBitsEventsV2(channelId);
        }
        private void PubSub_OnBitsReceived(object sender, OnBitsReceivedArgs e)
        {
            TtvIRCClient.SendMessage(string.Format(STRINGS.PredictionStarted, e.Username, e.TotalBitsUsed));
        }

        #endregion

        #region Pubsub events

        private void OnPubSubServiceError(object sender, OnPubSubServiceErrorArgs e)
        {
            if (lockPubSub) return;
            lockPubSub = true;
            Log.WriteLog(e.Exception, "PubSub server Error!"); 
            IllSkillzBotMain.PubSubReconnect();
        }
        private void OnPubSubServiceClosed(object sender, EventArgs e)
        {
            Log.WriteLog(null, $"PubSub connection closed!");
        }
        private void OnPubSubServiceConnected(object sender, EventArgs e)
        {
            if (lockPubSub) return;
            Console.WriteLine("PubSub Connected");
            client.SendTopics(accToken);
        }
        private void OnListenResponse(object sender, OnListenResponseArgs e)
        {
            if (lockPubSub) return;
            if (!e.Successful)
                    throw new Exception($"Failed to listen! Response: {e.Topic}");
                else Console.WriteLine(e.Topic);            
        }
        #endregion
        private async Task RewardProcess(string rewardID, string userName, string message, string redemID)
        {
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
                if (rewardID == englishWis)
                {
                    await RewardsRedemption.EnglishWisReward(userName, redemID, rewardID).ConfigureAwait(false);
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

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                client.OnFollow -= PubSub_OnFollow;
                client.OnPubSubServiceClosed -= OnPubSubServiceClosed;
                client.OnListenResponse -= OnListenResponse;
                client.OnPubSubServiceConnected -= OnPubSubServiceConnected;
                client.OnPubSubServiceError -= OnPubSubServiceError;
                client.OnStreamUp -= PubSub_OnStreamUp;
                client.OnStreamDown -= PubSub_OnStreamDown;
                client.OnViewCount -= PubSub_OnViewCount;
                client.OnChannelSubscription -= PubSub_OnChannelSubscription;
                client.OnChannelPointsRewardRedeemed -= PubSub_OnChannelPointsRewardRedeemed;
                client.OnFollow -= PubSub_OnFollow;
                client.OnPrediction -= PubSub_OnPrediction;
                client.OnRaidUpdateV2 -= PubSub_OnRaidUpdateV2;
                client.OnRaidGo -= PubSub_OnRaidGo;
                client.OnTimeout -= PubSub_OnTimeout;
                client.OnBan -= PubSub_OnBan;
                client.OnMessageDeleted -= PubSub_OnMessageDeleted;
                client.OnUnban -= PubSub_OnUnban;
                client.OnUntimeout -= PubSub_OnUntimeout;
                client.OnHost -= PubSub_OnHost;
                client.OnSubscribersOnly -= PubSub_OnSubscribersOnly;
                client.OnSubscribersOnlyOff -= PubSub_OnSubscribersOnlyOff;
                client.OnClear -= PubSub_OnClear;
                client.OnEmoteOnly -= PubSub_OnEmoteOnly;
                client.OnEmoteOnlyOff -= PubSub_OnEmoteOnlyOff;
                client.OnR9kBeta -= PubSub_OnR9kBeta;
                client.OnR9kBetaOff -= PubSub_OnR9kBetaOff;
                client.OnBitsReceived -= PubSub_OnBitsReceived;
                client.Dispose();
                client = null;
            }
            // Free any unmanaged objects here.
            //
            disposed = true;
        }

        ~PubSubClient()
        {
            Dispose(false);
        }
    }
}