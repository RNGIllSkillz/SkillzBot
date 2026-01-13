using Microsoft.Extensions.Logging;
using SkillzBot.IllSkillzBot;
using SkillzBot.IllSTRINGS;
using SkillzBot.Interfaces;
using SkillzBot.MODELS;
using SkillzBot.Services.Infrastructure;
using SkillzBot.Services.Writers;
using SkillzBot.IllConfiguration;
using SkillzBot.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SkillzBot.TtvClient.TTVRewards
{
    public class RewardsRedemption
    {
        private static bool CencelUvalIsWating = false;
        private static string CencelUvalUserName = "";
        private static readonly HashSet<Task> _runningTasks = new HashSet<Task>();
        private static readonly HashSet<string> mods = new HashSet<string>();
        private static readonly object _lock = new object();

        private readonly IDatabaseService _database;
        private readonly ITtvIRCClient _ircClient;
        private readonly ILogger<RewardsRedemption> _logger;
        private readonly IllModeratorsInteractions _modInteractions;
        private readonly ITwitchService _twitchService;
        private readonly BotConfigModel _config;
        private readonly IStreamElementsService _streamElementsService;
        private readonly MediaQueueService _mediaQueueService;
        private readonly IIllAccess _illAccess;
        private readonly BlacklistService _blacklistService;
        private readonly FlagWriterService _flagWriter;
        private readonly IYouTubeService _youTubeService;
        private readonly IllChatFilters _chatFilters;
        public RewardsRedemption(
            IDatabaseService database, 
            ITtvIRCClient ircClient, 
            ILogger<RewardsRedemption> logger, 
            IllModeratorsInteractions modInteractions, 
            ITwitchService twitchService,
            BotConfigModel config,
            IStreamElementsService streamElementsService,
            MediaQueueService mediaQueueService,
            IIllAccess illAccess,
            BlacklistService blacklistService,
            FlagWriterService flagWriter,
            IYouTubeService youTubeService,
            IllChatFilters chatFilters)
        {
            _database = database;
            _ircClient = ircClient;
            _logger = logger;
            _modInteractions = modInteractions;
            _twitchService = twitchService;
            _config = config;
            _streamElementsService = streamElementsService;
            _mediaQueueService = mediaQueueService;
            _illAccess = illAccess;
            _blacklistService = blacklistService;
            _flagWriter = flagWriter;
            _youTubeService = youTubeService;
            _chatFilters = chatFilters;
        }

        public async Task UvalSabReward(string UserName, string message, string redemID, string rewardID)
        {
            var uName = StringUtil.GetUserNameFromInput(message);
            var qUser = await _database.GetUserAsync(UserName);
            if (uName != _config.RootUser)
            {
                if (uName != _config.ChannelName)
                {
                    var user = await _database.GetUserAsync(uName);
                    if (user.dbID == -404)
                    {
                        await _ircClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, UserName, uName));
                        await _twitchService.CencelReward(rewardID, redemID);
                    }
                    else
                    {
                        if (!Convert.ToBoolean(user.isPartner) && !Convert.ToBoolean(user.isMod) && !mods.Contains(user.Name))
                        {
                            if (!Convert.ToBoolean(user.isVip))
                            {
                                double duration = 600;
                                if (user.UvalTimer > DateTimeOffset.Now.ToUnixTimeSeconds())
                                    duration = user.UvalTimer - DateTimeOffset.Now.ToUnixTimeSeconds() + 600;
                                await _twitchService.TimeOutUser(user, Convert.ToInt32(duration), STRINGS.TimeOutReason_TimeOutSub);
                                await _ircClient.SendMessage(string.Format(STRINGS.TimeOutReward_chatMessage, UserName, uName, duration, user.UvalCon + 1));
                                if (UserName != _config.RootUser)
                                    await _twitchService.ApproveReward(rewardID, redemID);
                                else
                                    await _twitchService.CencelReward(rewardID, redemID);
                            }
                            else
                            {
                                await _ircClient.SendMessage(string.Format(STRINGS.TimeOutReward_chatMessage, UserName, uName));
                                await _twitchService.CencelReward(rewardID, redemID);
                            }
                        }
                        else
                            await _twitchService.CencelReward(rewardID, redemID);
                    }
                }
                else
                {
                    await _twitchService.TimeOutUser(qUser, 600, STRINGS.TimeOutReason_TimeOutBroadcaster);
                    if (UserName != _config.RootUser)
                        await _twitchService.ApproveReward(rewardID, redemID);
                    else
                        await _twitchService.CencelReward(rewardID, redemID);
                }
            }
            else
            {
                if (!Convert.ToBoolean(qUser.isMod))
                {
                    await _twitchService.TimeOutUser(qUser, 600, STRINGS.TimeOutReason_TimeOutRootUser);
                    if (UserName != _config.RootUser)
                        await _twitchService.ApproveReward(rewardID, redemID);
                    else
                        await _twitchService.CencelReward(rewardID, redemID);
                }
                else
                    await _twitchService.CencelReward(rewardID, redemID);
            }
        }
        public async Task UvalVIPReward(string UserName, string message, string redemID, string rewardID)
        {
            try
            {
                var uName = StringUtil.GetUserNameFromInput(message);
                var qUser = await _database.GetUserAsync(UserName);
                if (!_config.RootUser.Equals(uName, StringComparison.OrdinalIgnoreCase))
                {
                    if (uName != _config.ChannelName)
                    {
                        var user = await _database.GetUserAsync(uName);

                        if (user.dbID == -404)
                        {
                            await _ircClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, UserName, uName));
                            await _twitchService.CencelReward(rewardID, redemID);
                        }
                        else
                        {
                            if (!Convert.ToBoolean(user.isPartner) && !Convert.ToBoolean(user.isMod) && !mods.Contains(user.Name))
                            {
                                double duration = 600;
                                if (user.UvalTimer > DateTimeOffset.Now.ToUnixTimeSeconds())
                                    duration = user.UvalTimer - DateTimeOffset.Now.ToUnixTimeSeconds() + 600;
                                await _twitchService.TimeOutUser(user, Convert.ToInt32(duration), STRINGS.TimeOutReason_TimeOutVIP);
                                await _ircClient.SendMessage(string.Format(STRINGS.TimeOutReward_chatMessage, UserName, uName, duration, user.UvalCon + 1));
                                if (UserName != _config.RootUser)
                                    await _twitchService.ApproveReward(rewardID, redemID);
                                else
                                    await _twitchService.CencelReward(rewardID, redemID);
                            }
                            else
                                await _twitchService.CencelReward(rewardID, redemID);
                        }
                    }
                    else
                    {
                        await _twitchService.TimeOutUser(qUser, 600, STRINGS.TimeOutReason_TimeOutBroadcaster);
                        if (UserName != _config.RootUser)
                            await _twitchService.ApproveReward(rewardID, redemID);
                        else
                            await _twitchService.CencelReward(rewardID, redemID);
                    }
                }
                else
                {
                    if (!Convert.ToBoolean(qUser.isMod))
                    {
                        await _twitchService.TimeOutUser(qUser, 600, STRINGS.TimeOutReason_TimeOutRootUser);
                        if (UserName != _config.RootUser)
                            await _twitchService.ApproveReward(rewardID, redemID);
                        else
                            await _twitchService.CencelReward(rewardID, redemID);
                    }
                    else
                        await _twitchService.CencelReward(rewardID, redemID);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UvalVIPReward Error");
            }
        }
        public async Task UvalModReward(string UserName, string message, string redemID, string rewardID)
        {
            try
            {
                var uName = StringUtil.GetUserNameFromInput(message);
                var qUser = await _database.GetUserAsync(UserName);
                if (uName != _config.ChannelName)
                {
                    var user = await _database.GetUserAsync(uName);
                    if (user.dbID == -404)
                    {
                        await _ircClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, UserName, uName));
                        await _twitchService.CencelReward(rewardID, redemID);
                    }
                    else
                    {
                        if (!Convert.ToBoolean(user.isPartner) && !(user.Name.Equals("bot_illskillz", StringComparison.OrdinalIgnoreCase)))
                        {
                            double duration = 600;
                            if (user.UvalTimer > DateTimeOffset.Now.ToUnixTimeSeconds())
                                duration = user.UvalTimer - DateTimeOffset.Now.ToUnixTimeSeconds() + 600;
                            await _twitchService.TimeOutModerator(user, Convert.ToInt32(duration), STRINGS.TimeOutReason_TimeOutVIP);
                            await _ircClient.SendMessage(string.Format(STRINGS.TimeOutReward_chatMessage, UserName, uName, duration, user.UvalCon + 1));
                            if (UserName != _config.RootUser)
                                await _twitchService.ApproveReward(rewardID, redemID);
                            else
                                await _twitchService.CencelReward(rewardID, redemID);
                            await TimeOutModerator(user, duration, STRINGS.TimeOutReason_TimeOutVIP);
                        }
                        else
                            await _twitchService.CencelReward(rewardID, redemID);
                    }
                }
                else
                {
                    await _twitchService.TimeOutUser(qUser, 600, STRINGS.TimeOutReason_TimeOutBroadcaster);
                    if (UserName != _config.RootUser)
                        await _twitchService.ApproveReward(rewardID, redemID);
                    else
                        await _twitchService.CencelReward(rewardID, redemID);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UvalModReward Error");
            }
        }
        public async Task UvalReward(string UserName, string message, string redemID, string rewardID)
        {
            try
            {
                var uName = StringUtil.GetUserNameFromInput(message);
                var qUser = await _database.GetUserAsync(UserName);
                if (uName != _config.RootUser)
                {
                    if (uName != _config.ChannelName)
                    {
                        var user = await _database.GetUserAsync(uName);
                        if (user.dbID == -404)
                        {
                            await _ircClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, UserName, uName));
                            await _twitchService.CencelReward(rewardID, redemID);
                        }
                        else
                        {
                            if (!Convert.ToBoolean(user.isPartner) && !Convert.ToBoolean(user.isMod) && !mods.Contains(user.Name))
                            {
                                if (!Convert.ToBoolean(user.isSub) & !Convert.ToBoolean(user.isVip))
                                {
                                    double duration = 600;
                                    if (user.UvalTimer > DateTimeOffset.Now.ToUnixTimeSeconds())
                                        duration = user.UvalTimer - DateTimeOffset.Now.ToUnixTimeSeconds() + 600;
                                    await _twitchService.TimeOutUser(user, Convert.ToInt32(duration), STRINGS.TimeOutReason_TimeOutUnsub);
                                    await _ircClient.SendMessage(string.Format(STRINGS.TimeOutReward_chatMessage, UserName, uName, duration, user.UvalCon + 1));
                                    if (UserName != _config.RootUser)
                                        await _twitchService.ApproveReward(rewardID, redemID);
                                    else
                                        await _twitchService.CencelReward(rewardID, redemID);
                                }
                                else
                                {
                                    await _ircClient.SendMessage(string.Format(STRINGS.Uval500_IsSub, UserName, uName));
                                    await _twitchService.CencelReward(rewardID, redemID);
                                }
                            }
                            else
                                await _twitchService.CencelReward(rewardID, redemID);
                        }
                    }
                    else
                    {
                        await _twitchService.TimeOutUser(qUser, 600, STRINGS.TimeOutReason_TimeOutBroadcaster);
                        if (UserName != _config.RootUser)
                            await _twitchService.ApproveReward(rewardID, redemID);
                        else
                            await _twitchService.CencelReward(rewardID, redemID);
                    }
                }
                else
                {
                    if (!Convert.ToBoolean(qUser.isMod))
                    {
                        await _twitchService.TimeOutUser(qUser, 600, STRINGS.TimeOutReason_TimeOutRootUser);
                        if (UserName != _config.RootUser)
                            await _twitchService.ApproveReward(rewardID, redemID);
                        else
                            await _twitchService.CencelReward(rewardID, redemID);
                    }
                    else
                        await _twitchService.CencelReward(rewardID, redemID);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UvalReward Error");
            }
        }
        public async Task<bool> ZakazTrekaReward(string UserName, string Link, string redemID, string rewardID)
        {
            string yID = StringUtil.ExtractYouTubeVideoId(Link) ?? await _youTubeService.SearchByKeywordAsync(Link);
            if (yID != null)
            {
                if (_chatFilters.CheckTreck(yID))
                {
                    await _ircClient.SendMessage(string.Format(STRINGS.Track500_bannedTrack, UserName));
                    if (rewardID == null) return false;
                    await _twitchService.CencelReward(rewardID, redemID);
                    return false;
                }
                var response = await _chatFilters.YouTubeFilter(yID);
                if (response == null)
                {
                    if (rewardID != null)
                        await _twitchService.CencelReward(rewardID, redemID);
                    await _ircClient.SendMessage(string.Format(STRINGS.Track_Esception, _config.RootUser));
                    _logger.LogError("{Link} -> {yID}", Link, yID);
                    return false;
                }

                switch (response[0])
                {
                    case "ok":
                        if (_chatFilters.CheckChannel(response[1]))
                        {
                            await _ircClient.SendMessage(string.Format(STRINGS.Track500_bannedTrack, UserName));
                            if (rewardID == null) return false;
                            await _twitchService.CencelReward(rewardID, redemID);
                            return false;
                        }

                        var user = await _database.GetUserAsync(UserName);
                        if (_chatFilters.IsUserBlacklisted(user.TwitchID.ToString()))
                        {
                            await _ircClient.SendMessage(string.Format(STRINGS.Track500_bannedUser, UserName));
                            if (rewardID == null) return false;
                            await _twitchService.CencelReward(rewardID, redemID);
                            return false;
                        }

                        if (await _streamElementsService.SendMediaAsync(yID).ConfigureAwait(false))
                        {
                            await _ircClient.SendMessage($"{UserName} Трек добавлен в очередь");
                            await _mediaQueueService.WriteAsync(user.TwitchID, yID);
                            if (rewardID == null) return true;
                            if (UserName == _config.RootUser)
                                await _twitchService.CencelReward(rewardID, redemID);
                            else
                                await _twitchService.ApproveReward(rewardID, redemID);
                            return true;
                        }
                        else
                        {
                            await _ircClient.SendMessage(string.Format(STRINGS.Track400_ERROR, UserName));
                            if (rewardID == null) return false;
                            await _twitchService.CencelReward(rewardID, redemID);
                            return false;
                        }

                    case "age":
                        await _ircClient.SendMessage(string.Format(STRINGS.Track510_Age, UserName));
                        if (rewardID == null) return false;
                        await _twitchService.CencelReward(rewardID, redemID);
                        return false;

                    case "duration":
                        await _ircClient.SendMessage(string.Format(STRINGS.Track510_Duration, UserName));
                        if (rewardID == null) return false;
                        await _twitchService.CencelReward(rewardID, redemID);
                        return false;

                    case "view":
                        await _ircClient.SendMessage(string.Format(STRINGS.Track510_ViewCount, UserName));
                        if (rewardID == null) return false;
                        await _twitchService.CencelReward(rewardID, redemID);
                        return false;

                    case "ZAP":
                        await _blacklistService.AddToMediaBlacklistAsync(yID);
                        var user2 = await _database.GetUserAsync(UserName);
                        if (user2.dbID == -404) return false;
                        await _modInteractions.IllFilterTrigger(user2);
                        await _flagWriter.WriteAsync($"{UserName} : {Link}");
                        return false;

                    case "Embeddable":
                        await _ircClient.SendMessage(string.Format(STRINGS.Track510_Embedded, UserName));
                        if (rewardID == null) return false;
                        await _twitchService.CencelReward(rewardID, redemID);
                        return false;
                }
            }
            else
            {
                await _ircClient.SendMessage(string.Format(STRINGS.Track404, UserName));
                if (rewardID == null) return false;
                await _twitchService.CencelReward(rewardID, redemID);
                return false;
            }
            return false;
        }
        public async Task CenceleUvalReward(string UserName, string message, string redemID, string rewardID)
        {
            if (!CencelUvalIsWating)
            {
                await _twitchService.CencelReward(rewardID, redemID);
                var uName = StringUtil.GetUserNameFromInput(message);
                var user = await _database.GetUserAsync(uName);
                if (user.dbID == -404)
                {
                    await _ircClient.SendMessage(string.Format(STRINGS.CencelUval404, UserName, uName));
                }
                else
                {
                    var uvalTime = user.UvalTimer - DateTimeOffset.Now.ToUnixTimeSeconds();
                    if (uvalTime > 5)
                    {
                        CencelUvalUserName = uName;
                        CencelUvalIsWating = true;
                        var subscriptionMap = new Dictionary<bool, string>
                        {
                            [Convert.ToBoolean(user.isSub)] = "IsSub",
                            [Convert.ToBoolean(user.isVip)] = "IsVip",
                            [mods.Contains(user.Name)] = "IsMod"
                        };
                        string subscriptionType = subscriptionMap.GetValueOrDefault(true, "IsUnsub");
                        var CenceleCost = await CalculateCancelUvalCost(_twitchService, subscriptionType, uvalTime);
                        await _twitchService.UpdateReward(rewardID, string.Format(STRINGS.UpdateRewardTitleNew, uName), CenceleCost, "", true, false);
                        await _ircClient.SendMessage(string.Format(STRINGS.CencelUval_ChatMessage, UserName, uName, uvalTime, CenceleCost));

                        _ = Task.Run(async () => {
                            await Task.Delay(60000);
                            if (CencelUvalIsWating)
                            {
                                await _ircClient.SendMessage(STRINGS.CencelUval_TimeOut);
                                await _twitchService.UpdateReward(rewardID, STRINGS.UpdateRewardTitleOrig, 10000, STRINGS.UpdateRewardPromptOrig, true, true);
                                CencelUvalIsWating = false;
                            }
                        });
                    }
                    else
                    {
                        await _ircClient.SendMessage(string.Format(STRINGS.CencelUval_NotInTimeOut, UserName, uName));
                    }
                }
            }
            else
            {
                CencelUvalIsWating = false;
                var user = await _database.GetUserAsync(CencelUvalUserName);
                if (user.dbID == -404)
                {
                    await _ircClient.SendMessage(STRINGS.CencelUval404);
                }
                else
                {
                    await _twitchService.UnBanUser(user.TwitchID.ToString());
                }
                await _twitchService.UpdateReward(rewardID, STRINGS.UpdateRewardTitleOrig, 10000, STRINGS.UpdateRewardPromptOrig, true, true);
                CencelUvalUserName = "";
                if (!_illAccess.MeetsLevel(user, IllEnums.AccessLevel.Root))
                    await _twitchService.ApproveReward(rewardID, redemID);
                else
                    await _twitchService.CencelReward(rewardID, redemID);
            }
        }
        public async Task Pi4kaReward(string UserName, string redemID, string rewardID)
        {
            if (UserName != _config.RootUser)
                await _twitchService.ApproveReward(rewardID, redemID);
            else
                await _twitchService.CencelReward(rewardID, redemID);
        }
        public async Task EmoteOnlyReward(string UserName, string redemID, string rewardID)
        {
            _logger.LogInformation("Activating Emote Only Mode by Reward redemption ({User})", UserName);
            await _twitchService.SetEmoteOnlyMode(true);
            if (UserName != _config.RootUser)
                await _twitchService.ApproveReward(rewardID, redemID);
            else
                await _twitchService.CencelReward(rewardID, redemID);

            _ = Task.Run(async () => {
                await Task.Delay(180000);
                _logger.LogInformation("Deactivating Emote Only Mode (Timer expired)");
                await _twitchService.SetEmoteOnlyMode(false);
            });
        }
        public async Task ChatWithBot(string UserName, string message, string redemID, string rewardID)
        {

            await _ircClient.SendMessage($"@{UserName} GPT not implemented.");
            await _twitchService.CencelReward(rewardID, redemID);
        }
        public async Task TimeOutModerator(UserObject user, double duration, string reason)
        {
            await _twitchService.TimeOutModerator(user, Convert.ToInt32(duration), reason);
            if (Convert.ToBoolean(user.isMod))
            {
                Task backgroundTask = _modInteractions.UserUntimeoutTrigger(user.Name);
                lock (_lock)
                {
                    if (_runningTasks.Contains(backgroundTask)) return;
                    _runningTasks.Add(backgroundTask);
                    mods.Add(user.Name);
                }
                try
                {
                    await backgroundTask;
                }
                finally
                {
                    lock (_lock)
                    {
                        _runningTasks.Remove(backgroundTask);
                        mods.Remove(user.Name);
                    }
                }
            }
            return;
        }
        private async Task<int> CalculateCancelUvalCost(ITwitchService twitchService, string subscriptionType, double remainingDuration)
        {
            const int SecondsInTenMinutes = 600;
            var rewardsMap = new Dictionary<string, string>
            {
                ["IsSub"] = _config.ChannelIds.UvalSabId,
                ["IsVip"] = _config.ChannelIds.UvalVipId,
                ["IsUnsub"] = _config.ChannelIds.UvalId,
                ["IsMod"] = _config.ChannelIds.UvalMod
            };

            if (rewardsMap.TryGetValue(subscriptionType, out string rewardId))
            {
                var reward = await twitchService.GetReward(rewardId);
                if (reward != null)
                {
                    var costPerSecond = reward.Cost / (double)SecondsInTenMinutes;
                    return (int)Math.Ceiling(costPerSecond * remainingDuration);
                }
            }
            return 10000000;
        }
    }
}