using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SkillzBot.Singleton;
using SkillzBot.WRITERS;
using SkillzBot.API.YouTube;
using SkillzBot.API.Twitch;
using SkillzBot.Utils;
using SkillzBot.IllSkillzBot;
using SkillzBot.IllSTRINGS;
using SkillzBot.API.StreamElements;
using SkillzBot.MODELS;
using SkillzBot.QuartZ;
using SkillzBot.IllSkillzBot.IllCommandsNest;
using SkillzBot.Hosts;
using Microsoft.Extensions.Logging;
using SkillzBot.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace SkillzBot.TtvClient.TTVRewards
{
    internal class RewardsRedemption
    {
        private static bool CencelUvalIsWating = false;
        private static string CencelUvalUserName = "";
        private static readonly HashSet<Task> _runningTasks = new HashSet<Task>();
        private static readonly HashSet<string> mods = new HashSet<string>();
        private static readonly object _lock = new object();

        private readonly IDatabaseService _database;
        private readonly ITtvIRCClient _ircClient;
        private readonly ILogger<RewardsRedemption> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IllModeratorsInteractions _modInteractions;

        private IllCommands _illCommands => _serviceProvider.GetRequiredService<IllCommands>();

        public RewardsRedemption(IDatabaseService database, ITtvIRCClient ircClient, ILogger<RewardsRedemption> logger, IServiceProvider serviceProvider, IllModeratorsInteractions modInteractions)
        {
            _database = database;
            _ircClient = ircClient;
            _logger = logger;
            _serviceProvider = serviceProvider;
            _modInteractions = modInteractions;
        }

        public async Task UvalSabReward(string UserName, string message, string redemID, string rewardID)
        {
            var uName = StringUtil.GetUserNameFromInput(message);
            var qUser = await _database.GetUserAsync(UserName).ConfigureAwait(false);
            if (uName != IllSingleton.Config.RootUser)
            {
                if (uName != IllSingleton.Config.ChannelName)
                {
                    var user = await _database.GetUserAsync(uName).ConfigureAwait(false);
                    if (user.dbID == -404)
                    {
                        await _ircClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, UserName, uName)).ConfigureAwait(false);
                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
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
                                await TtvAPI.TimeOutUser(user, Convert.ToInt32(duration), STRINGS.TimeOutReason_TimeOutSub).ConfigureAwait(false);
                                await _ircClient.SendMessage(string.Format(STRINGS.TimeOutReward_chatMessage, UserName, uName, duration, user.UvalCon + 1)).ConfigureAwait(false);
                                if (UserName != IllSingleton.Config.RootUser)
                                    await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
                                else
                                    await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                            }
                            else
                            {
                                await _ircClient.SendMessage(string.Format(STRINGS.TimeOutReward_chatMessage, UserName, uName)).ConfigureAwait(false);
                                await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                            }
                        }
                        else
                            await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                    }
                }
                else
                {
                    await TtvAPI.TimeOutUser(qUser, 600, STRINGS.TimeOutReason_TimeOutBroadcaster).ConfigureAwait(false);
                    if (UserName != IllSingleton.Config.RootUser)
                        await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
                    else
                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                }
            }
            else
            {
                if (!Convert.ToBoolean(qUser.isMod))
                {
                    await TtvAPI.TimeOutUser(qUser, 600, STRINGS.TimeOutReason_TimeOutRootUser).ConfigureAwait(false);
                    if (UserName != IllSingleton.Config.RootUser)
                        await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
                    else
                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                }
                else
                    await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
            }
        }
        public async Task UvalVIPReward(string UserName, string message, string redemID, string rewardID)
        {
            try
            {
                var uName = StringUtil.GetUserNameFromInput(message);
                var qUser = await _database.GetUserAsync(UserName).ConfigureAwait(false);
                if (!IllSingleton.Config.RootUser.Equals(uName, StringComparison.OrdinalIgnoreCase))
                {
                    if (uName != IllSingleton.Config.ChannelName)
                    {
                        var user = await _database.GetUserAsync(uName).ConfigureAwait(false);

                        if (user.dbID == -404)
                        {
                            await _ircClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, UserName, uName)).ConfigureAwait(false);
                            await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                        }
                        else
                        {
                            if (!Convert.ToBoolean(user.isPartner) && !Convert.ToBoolean(user.isMod) && !mods.Contains(user.Name))
                            {
                                double duration = 600;
                                if (user.UvalTimer > DateTimeOffset.Now.ToUnixTimeSeconds())
                                    duration = user.UvalTimer - DateTimeOffset.Now.ToUnixTimeSeconds() + 600;
                                await TtvAPI.TimeOutUser(user, Convert.ToInt32(duration), STRINGS.TimeOutReason_TimeOutVIP).ConfigureAwait(false);
                                await _ircClient.SendMessage(string.Format(STRINGS.TimeOutReward_chatMessage, UserName, uName, duration, user.UvalCon + 1)).ConfigureAwait(false);
                                if (UserName != IllSingleton.Config.RootUser)
                                    await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
                                else
                                    await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                            }
                            else
                                await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        await TtvAPI.TimeOutUser(qUser, 600, STRINGS.TimeOutReason_TimeOutBroadcaster).ConfigureAwait(false);
                        if (UserName != IllSingleton.Config.RootUser)
                            await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
                        else
                            await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                    }
                }
                else
                {
                    if (!Convert.ToBoolean(qUser.isMod))
                    {
                        await TtvAPI.TimeOutUser(qUser, 600, STRINGS.TimeOutReason_TimeOutRootUser).ConfigureAwait(false);
                        if (UserName != IllSingleton.Config.RootUser)
                            await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
                        else
                            await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                    }
                    else
                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
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
                var qUser = await _database.GetUserAsync(UserName).ConfigureAwait(false);
                if (uName != IllSingleton.Config.ChannelName)
                {
                    var user = await _database.GetUserAsync(uName).ConfigureAwait(false);
                    if (user.dbID == -404)
                    {
                        await _ircClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, UserName, uName)).ConfigureAwait(false);
                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                    }
                    else
                    {
                        if (!Convert.ToBoolean(user.isPartner) && !(user.Name.Equals("bot_illskillz", StringComparison.OrdinalIgnoreCase)))
                        {
                            double duration = 600;
                            if (user.UvalTimer > DateTimeOffset.Now.ToUnixTimeSeconds())
                                duration = user.UvalTimer - DateTimeOffset.Now.ToUnixTimeSeconds() + 600;
                            await TtvAPI.TimeOutModerator(user, Convert.ToInt32(duration), STRINGS.TimeOutReason_TimeOutVIP).ConfigureAwait(false);
                            await _ircClient.SendMessage(string.Format(STRINGS.TimeOutReward_chatMessage, UserName, uName, duration, user.UvalCon + 1)).ConfigureAwait(false);
                            if (UserName != IllSingleton.Config.RootUser)
                                await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
                            else
                                await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                            await TimeOutModerator(user, duration, STRINGS.TimeOutReason_TimeOutVIP).ConfigureAwait(false);
                        }
                        else
                            await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                    }
                }
                else
                {
                    await TtvAPI.TimeOutUser(qUser, 600, STRINGS.TimeOutReason_TimeOutBroadcaster).ConfigureAwait(false);
                    if (UserName != IllSingleton.Config.RootUser)
                        await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
                    else
                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
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
                var qUser = await _database.GetUserAsync(UserName).ConfigureAwait(false);
                if (uName != IllSingleton.Config.RootUser)
                {
                    if (uName != IllSingleton.Config.ChannelName)
                    {
                        var user = await _database.GetUserAsync(uName).ConfigureAwait(false);
                        if (user.dbID == -404)
                        {
                            await _ircClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, UserName, uName)).ConfigureAwait(false);
                            await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
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
                                    await TtvAPI.TimeOutUser(user, Convert.ToInt32(duration), STRINGS.TimeOutReason_TimeOutUnsub).ConfigureAwait(false);
                                    await _ircClient.SendMessage(string.Format(STRINGS.TimeOutReward_chatMessage, UserName, uName, duration, user.UvalCon + 1)).ConfigureAwait(false);
                                    if (UserName != IllSingleton.Config.RootUser)
                                        await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
                                    else
                                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                                }
                                else
                                {
                                    await _ircClient.SendMessage(string.Format(STRINGS.Uval500_IsSub, UserName, uName));
                                    await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                                }
                            }
                            else
                                await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        await TtvAPI.TimeOutUser(qUser, 600, STRINGS.TimeOutReason_TimeOutBroadcaster).ConfigureAwait(false);
                        if (UserName != IllSingleton.Config.RootUser)
                            await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
                        else
                            await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                    }
                }
                else
                {
                    if (!Convert.ToBoolean(qUser.isMod))
                    {
                        await TtvAPI.TimeOutUser(qUser, 600, STRINGS.TimeOutReason_TimeOutRootUser).ConfigureAwait(false);
                        if (UserName != IllSingleton.Config.RootUser)
                            await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
                        else
                            await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                    }
                    else
                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UvalReward Error");
            }
        }
        public async Task<bool> ZakazTrekaReward(string UserName, string Link, string redemID, string rewardID)
        {
            var chatFilters = IllServiceProvider.GetService<IllChatFilters>();
            string yID = StringUtil.ExtractYouTubeVideoId(Link) ?? await YouTubeSearch.YouTubeSearchByKeyWordTask(Link).ConfigureAwait(false);
            if (yID != null)
            {
                if (chatFilters.CheckTreck(yID))
                {
                    await _ircClient.SendMessage(string.Format(STRINGS.Track500_bannedTrack, UserName)).ConfigureAwait(false);
                    if (rewardID == null) return false;
                    await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                    return false;
                }
                var response = await chatFilters.YouTubeFilter(yID).ConfigureAwait(false);
                if (response == null)
                {
                    if (rewardID != null)
                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                    await _ircClient.SendMessage(string.Format(STRINGS.Track_Esception, IllSingleton.Config.RootUser)).ConfigureAwait(false);
                    _logger.LogError("{Link} -> {yID}", Link, yID);
                    return false;
                }

                switch (response[0])
                {
                    case "ok":
                        if (chatFilters.CheckChannel(response[1]))
                        {
                            await _ircClient.SendMessage(string.Format(STRINGS.Track500_bannedTrack, UserName)).ConfigureAwait(false);
                            if (rewardID == null) return false;
                            await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                            return false;
                        }

                        var user = await _database.GetUserAsync(UserName).ConfigureAwait(false);
                        if (chatFilters.IsUserBlacklisted(user.TwitchID.ToString()))
                        {
                            await _ircClient.SendMessage(string.Format(STRINGS.Track500_bannedUser, UserName)).ConfigureAwait(false);
                            if (rewardID == null) return false;
                            await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                            return false;
                        }

                        if (await StreamElementsAPI.SendMediaAsync(yID).ConfigureAwait(false))
                        {
                            await _ircClient.SendMessage($"{UserName} Трек добавлен в очередь").ConfigureAwait(false);
                            await MediaqueueWriter.Write(user.TwitchID, yID).ConfigureAwait(false);
                            if (rewardID == null) return true;
                            if (UserName == IllSingleton.Config.RootUser)
                                await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                            else
                                await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
                            return true;
                        }
                        else
                        {
                            await _ircClient.SendMessage(string.Format(STRINGS.Track400_ERROR, UserName)).ConfigureAwait(false);
                            if (rewardID == null) return false;
                            await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                            return false;
                        }

                    case "age":
                        await _ircClient.SendMessage(string.Format(STRINGS.Track510_Age, UserName)).ConfigureAwait(false);
                        if (rewardID == null) return false;
                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                        return false;

                    case "duration":
                        await _ircClient.SendMessage(string.Format(STRINGS.Track510_Duration, UserName)).ConfigureAwait(false);
                        if (rewardID == null) return false;
                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                        return false;

                    case "view":
                        await _ircClient.SendMessage(string.Format(STRINGS.Track510_ViewCount, UserName)).ConfigureAwait(false);
                        if (rewardID == null) return false;
                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                        return false;

                    case "ZAP":
                        await MediaBlackListWriter.Write(yID).ConfigureAwait(false);
                        var user2 = await _database.GetUserAsync(UserName).ConfigureAwait(false);
                        if (user2.dbID == -404) return false;
                        await _illCommands.IllFilterTrigger(user2).ConfigureAwait(false);
                        await FlagWriter.FlagWriterTask($"{UserName} : {Link}").ConfigureAwait(false);
                        return false;

                    case "Embeddable":
                        await _ircClient.SendMessage(string.Format(STRINGS.Track510_Embedded, UserName)).ConfigureAwait(false);
                        if (rewardID == null) return false;
                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                        return false;
                }
            }
            else
            {
                await _ircClient.SendMessage(string.Format(STRINGS.Track404, UserName)).ConfigureAwait(false);
                if (rewardID == null) return false;
                await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                return false;
            }
            return false;
        }
        public async Task CenceleUvalReward(string UserName, string message, string redemID, string rewardID)
        {
            if (!CencelUvalIsWating)
            {
                await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                var uName = StringUtil.GetUserNameFromInput(message);
                var user = await _database.GetUserAsync(uName).ConfigureAwait(false);
                if (user.dbID == -404)
                {
                    await _ircClient.SendMessage(string.Format(STRINGS.CencelUval404, UserName, uName)).ConfigureAwait(false);
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
                        var CenceleCost = await IntUtil.CalculateCancelUvalCost(subscriptionType, uvalTime).ConfigureAwait(false);
                        await TtvAPI.UpdateReward(rewardID, string.Format(STRINGS.UpdateRewardTitleNew, uName), CenceleCost, "", true, false).ConfigureAwait(false);
                        await _ircClient.SendMessage(string.Format(STRINGS.CencelUval_ChatMessage, UserName, uName, uvalTime, CenceleCost)).ConfigureAwait(false);

                        _ = Task.Run(async () => {
                            await Task.Delay(60000);
                            if (CencelUvalIsWating)
                            {
                                await _ircClient.SendMessage(STRINGS.CencelUval_TimeOut).ConfigureAwait(false);
                                await TtvAPI.UpdateReward(rewardID, STRINGS.UpdateRewardTitleOrig, 10000, STRINGS.UpdateRewardPromptOrig, true, true).ConfigureAwait(false);
                                CencelUvalIsWating = false;
                            }
                        });
                    }
                    else
                    {
                        await _ircClient.SendMessage(string.Format(STRINGS.CencelUval_NotInTimeOut, UserName, uName)).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                CencelUvalIsWating = false;
                var user = await _database.GetUserAsync(CencelUvalUserName).ConfigureAwait(false);
                if (user.dbID == -404)
                {
                    await _ircClient.SendMessage(STRINGS.CencelUval404).ConfigureAwait(false);
                }
                else
                {
                    await TtvAPI.UnBanUser(user.TwitchID.ToString()).ConfigureAwait(false);
                }
                await TtvAPI.UpdateReward(rewardID, STRINGS.UpdateRewardTitleOrig, 10000, STRINGS.UpdateRewardPromptOrig, true, true).ConfigureAwait(false);
                CencelUvalUserName = "";
                if (!IllAccess.MeetsLevel(user, IllEnums.AccessLevel.Root))
                    await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
                else
                    await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
            }
        }
        public async Task Pi4kaReward(string UserName, string redemID, string rewardID)
        {
            if (UserName != IllSingleton.Config.RootUser)
                await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
            else
                await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
        }
        public async Task EmoteOnlyReward(string UserName, string redemID, string rewardID)
        {
            await TtvAPI.SetEmoteOnlyMode(true);
            if (UserName != IllSingleton.Config.RootUser)
                await TtvAPI.ApproveReward(rewardID, redemID);
            else
                await TtvAPI.CencelReward(rewardID, redemID);

            _ = Task.Run(async () => {
                await Task.Delay(180000);
                await TtvAPI.SetEmoteOnlyMode(false);
            });
        }
        public async Task ChatWithBot(string UserName, string message, string redemID, string rewardID)
        {

            await _ircClient.SendMessage($"@{UserName} GPT not implemented.").ConfigureAwait(false);
            await TtvAPI.CencelReward(rewardID, redemID);
        }
        public async Task TimeOutModerator(UserObject user, double duration, string reason)
        {
            await TtvAPI.TimeOutModerator(user, Convert.ToInt32(duration), reason).ConfigureAwait(false);
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
                    await backgroundTask.ConfigureAwait(false);
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
    }
}