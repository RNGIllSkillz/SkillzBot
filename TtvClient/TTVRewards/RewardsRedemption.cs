using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using SkillzBot.WRITERS;
using SkillzBot.API.YouTube;
using SkillzBot.MYSQL;
using SkillzBot.API.Twitch;
using SkillzBot.IRC;
using SkillzBot.Utils;
using SkillzBot.Singleton;
using SkillzBot.IllSkillzBot;
using SkillzBot.IllSTRINGS;
using SkillzBot.API.StreamElements;
using SkillzBot.MODELS;
using SkillzBot.QuartZ;
using SkillzBot.IllSkillzBot.IllCommandsNest;

namespace SkillzBot.TtvClient.TTVRewards
{
    internal class RewardsRedemption
    {
        private static bool CencelUvalIsWating = false;        
        private static string CencelUvalUserName = "";
        private static readonly HashSet<Task> _runningTasks = new HashSet<Task>();
        private static readonly HashSet<string> mods = new HashSet<string>();
        private static readonly object _lock = new object();

        public static async Task UvalSabReward(string UserName, string message, string redemID, string rewardID)
        {            
            var singleton = IllSingleton.GetInstance();
            var uName = StringUtil.GetUserNameFromInput(message);
            var qUser = await MySQL.GetUser(UserName).ConfigureAwait(false);
            if (uName != singleton.rootUser)
            {
                if (uName != singleton.ChannelName)
                {
                    var user = await MySQL.GetUser(uName).ConfigureAwait(false);
                    if (user.dbID == -404)
                    {
                        TtvIRCClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, UserName, uName));
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
                                TtvIRCClient.SendMessage(string.Format(STRINGS.TimeOutReward_chatMessage, UserName, uName, duration, user.UvalCon + 1));
                                if (UserName != singleton.rootUser)
                                    await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
                                else
                                    await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                            }
                            else
                            {
                                TtvIRCClient.SendMessage(string.Format(STRINGS.TimeOutReward_chatMessage, UserName, uName));
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
                    if (UserName != singleton.rootUser)
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
                    if (UserName != singleton.rootUser)
                        await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
                    else
                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                }
                else
                    await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
            }
        }
        public static async Task UvalVIPReward(string UserName, string message, string redemID, string rewardID)
        {
            try
            {
                var uName = StringUtil.GetUserNameFromInput(message);
                var qUser = await MySQL.GetUser(UserName).ConfigureAwait(false);
                var singleton = IllSingleton.GetInstance();
                if (!singleton.rootUser.Equals(uName, StringComparison.OrdinalIgnoreCase))
                {
                    if (uName != singleton.ChannelName)
                    {
                        var user = await MySQL.GetUser(uName).ConfigureAwait(false);

                        if (user.dbID == -404)
                        {
                            TtvIRCClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, UserName, uName));
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
                                TtvIRCClient.SendMessage(string.Format(STRINGS.TimeOutReward_chatMessage, UserName, uName, duration, user.UvalCon + 1));
                                if (UserName != singleton.rootUser)
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
                        if (UserName != singleton.rootUser)
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
                        if (UserName != singleton.rootUser)
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
                Log.WriteLog(ex, "");
            }
        }
        public static async Task UvalModReward(string UserName, string message, string redemID, string rewardID)
        {
            try
            {
                var uName = StringUtil.GetUserNameFromInput(message);
                var qUser = await MySQL.GetUser(UserName).ConfigureAwait(false);
                var singleton = IllSingleton.GetInstance();
                if (uName != singleton.ChannelName)
                {
                    var user = await MySQL.GetUser(uName).ConfigureAwait(false);
                    if (user.dbID == -404)
                    {
                        TtvIRCClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, UserName, uName));
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
                            TtvIRCClient.SendMessage(string.Format(STRINGS.TimeOutReward_chatMessage, UserName, uName, duration, user.UvalCon + 1));
                            if (UserName != singleton.rootUser)
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
                    if (UserName != singleton.rootUser)
                        await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
                    else
                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "");
            }
        }
        public static async Task UvalReward(string UserName, string message, string redemID, string rewardID)
        {
            try
            {
                var singleton = IllSingleton.GetInstance();
                var uName = StringUtil.GetUserNameFromInput(message);
                var qUser = await MySQL.GetUser(UserName).ConfigureAwait(false);
                if (uName != singleton.rootUser)
                {
                    if (uName != singleton.ChannelName)
                    {
                        var user = await MySQL.GetUser(uName).ConfigureAwait(false);
                        if (user.dbID == -404)
                        {
                            TtvIRCClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, UserName, uName));
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
                                    TtvIRCClient.SendMessage(string.Format(STRINGS.TimeOutReward_chatMessage, UserName, uName, duration, user.UvalCon + 1));
                                    if (UserName != singleton.rootUser)
                                        await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
                                    else
                                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                                }
                                else
                                {
                                    TtvIRCClient.SendMessage(string.Format(STRINGS.Uval500_IsSub, UserName, uName));
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
                        if (UserName != singleton.rootUser)
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
                        if (UserName != singleton.rootUser)
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
                Log.WriteLog(ex, "");
            }
        }
        public static async Task<bool> ZakazTrekaReward(string UserName, string Link, string redemID, string rewardID)
        {
            var singleton = IllSingleton.GetInstance();
            string yID = StringUtil.ExtractYouTubeVideoId(Link) ?? await YouTubeSearch.YouTubeSearchByKeyWordTask(Link).ConfigureAwait(false);            
            if (yID != null)
            {
                if (IllChatFilters.CheckTreck(yID))
                {
                    TtvIRCClient.SendMessage(string.Format(STRINGS.Track500_bannedTrack, UserName));
                    if (rewardID == null) return false;
                    await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                    return false;
                }
                var response = await IllChatFilters.YouTubeFilter(yID).ConfigureAwait(false);
                if (response == null)
                {
                    if (rewardID != null)
                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                    TtvIRCClient.SendMessage(string.Format(STRINGS.Track_Esception, singleton.rootUser));
                    Log.WriteLog(null, $"{Link} -> {yID}");
                    return false;
                }

                switch (response[0])
                {
                    case "ok":
                        if (IllChatFilters.CheckChannel(response[1]))
                        {
                            TtvIRCClient.SendMessage(string.Format(STRINGS.Track500_bannedTrack, UserName));
                            if (rewardID == null) return false;
                            await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                            return false;
                        }

                        var user = await MySQL.GetUser(UserName).ConfigureAwait(false);
                        if (IllChatFilters.IsUserBlacklisted(user.TwitchID.ToString()))
                        {
                            TtvIRCClient.SendMessage(string.Format(STRINGS.Track500_bannedUser, UserName));
                            if (rewardID == null) return false;
                            await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                            return false;
                        }

                        if (await StreamElementsAPI.SendMediaAsync(yID).ConfigureAwait(false))
                        {
                            //TtvIRCClient.SendMessage(string.Format(STRINGS.Track200_Success, UserName, response[2])); //dont show added track.
                            TtvIRCClient.SendMessage($"{UserName} Трек добавлен в очередь");
                            MediaqueueWriter.Write(user.TwitchID, yID);
                            if (rewardID == null) return true;
                            if (UserName == singleton.rootUser)
                                await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                            else
                                await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
                            return true;
                        }
                        else
                        {
                            TtvIRCClient.SendMessage(string.Format(STRINGS.Track400_ERROR, UserName));
                            if (rewardID == null) return false;
                            await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                            return false;
                        }

                    case "age":
                        TtvIRCClient.SendMessage(string.Format(STRINGS.Track510_Age, UserName));
                        if (rewardID == null) return false;
                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                        return false;

                    case "duration":
                        TtvIRCClient.SendMessage(string.Format(STRINGS.Track510_Duration, UserName));
                        if (rewardID == null) return false;
                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                        return false;

                    case "view":
                        TtvIRCClient.SendMessage(string.Format(STRINGS.Track510_ViewCount, UserName));
                        if (rewardID == null) return false;
                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                        return false;

                    case "ZAP":
                        MediaBlackListWriter.Write(yID);
                        var user2 = await MySQL.GetUser(UserName).ConfigureAwait(false);
                        if (user2.dbID == -404) return false;
                        await IllCommands.IllFilterTrigger(user2).ConfigureAwait(false);                        
                        FlagWriter.FlagWriterTask($"{UserName} : {Link}");
                        return false;

                    case "Embeddable":
                        TtvIRCClient.SendMessage(string.Format(STRINGS.Track510_Embedded, UserName));
                        if (rewardID == null) return false;
                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                        return false;
                }
            }
            else
            {
                TtvIRCClient.SendMessage(string.Format(STRINGS.Track404, UserName));
                if (rewardID == null) return false;
                await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                return false;
            }
            return false;
        }
        public static async Task CenceleUvalReward(string UserName, string message, string redemID, string rewardID)
        {
            if (!CencelUvalIsWating)
            {
                await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);               
                var uName = StringUtil.GetUserNameFromInput(message);
                var user = await MySQL.GetUser(uName).ConfigureAwait(false);
                if (user.dbID == -404)
                {
                    TtvIRCClient.SendMessage(string.Format(STRINGS.CencelUval404, UserName, uName));
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
                        TtvIRCClient.SendMessage(string.Format(STRINGS.CencelUval_ChatMessage, UserName, uName, uvalTime, CenceleCost));
                        double startTime = DateTimeOffset.Now.ToUnixTimeSeconds();
                        while (CencelUvalIsWating)
                        {
                            if ((DateTimeOffset.Now.ToUnixTimeSeconds() - startTime) >= 60)
                            {
                                TtvIRCClient.SendMessage(STRINGS.CencelUval_TimeOut);
                                await TtvAPI.UpdateReward(rewardID, STRINGS.UpdateRewardTitleOrig, 10000, STRINGS.UpdateRewardPromptOrig, true, true).ConfigureAwait(false);
                                CencelUvalIsWating = false;
                            }
                            await Task.Delay(500);
                        }
                    }
                    else
                    {
                        TtvIRCClient.SendMessage(string.Format(STRINGS.CencelUval_NotInTimeOut, UserName, uName));
                    }
                }
            }
            else
            {
                //отменить увал
                CencelUvalIsWating = false;
                var user = await MySQL.GetUser(CencelUvalUserName).ConfigureAwait(false);
                if (user.dbID == -404)
                {
                    TtvIRCClient.SendMessage(STRINGS.CencelUval404);
                }
                else
                {
                    await TtvAPI.UnBanUser(user.TwitchID.ToString()).ConfigureAwait(false);
                }
                await TtvAPI.UpdateReward(rewardID, STRINGS.UpdateRewardTitleOrig, 10000, STRINGS.UpdateRewardPromptOrig, true, true).ConfigureAwait(false);
                CencelUvalUserName = "";
                if (UserName != IllSingleton.GetInstance().rootUser)
                    await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
                else
                    await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
            }
        }
        public static async Task Pi4kaReward(string UserName, string redemID, string rewardID)
        {
            if (UserName != IllSingleton.GetInstance().rootUser)
                await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
            else
                await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
        }
        public static async Task EmoteOnlyReward(string UserName, string redemID, string rewardID)
        {
            await TtvAPI.SetEmoteOnlyMode(true);
            long emoteModeTimer = DateTimeOffset.Now.ToUnixTimeSeconds();
            if (UserName != IllSingleton.GetInstance().rootUser)
                await TtvAPI.ApproveReward(rewardID, redemID);
            else
                await TtvAPI.CencelReward(rewardID, redemID);
            while (true)
            {
                if ((DateTimeOffset.Now.ToUnixTimeSeconds() - emoteModeTimer)  >= 180)
                {
                    await TtvAPI.SetEmoteOnlyMode(false);
                    break;
                }
                await Task.Delay(1000);
            }
        }
        public static async Task EnglishWisReward(string UserName, string redemID, string rewardID)
        {
            var singleton = IllSingleton.GetInstance();
            var reward = await TtvAPI.GetReward(rewardID).ConfigureAwait(false);
            if (reward == null)
            {
                await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                TtvIRCClient.SendMessage("Error");
                return;
            }
            if (UserName != singleton.rootUser)
                await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);                
            else
                await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
            await TtvAPI.UpdateReward(reward.Id, reward.Title, reward.Cost, reward.Prompt, false, reward.IsUserInputRequired).ConfigureAwait(false);
            TtvIRCClient.SendMessage($"@{singleton.ChannelName}, СЛОВО! @{singleton.ChannelName}, СЛОВО! @{singleton.ChannelName}, СЛОВО! @{singleton.ChannelName}, СЛОВО! @{singleton.ChannelName}, СЛОВО! @{singleton.ChannelName}, СЛОВО! @{singleton.ChannelName}, СЛОВО! ");
            singleton.WisCD = DateTimeOffset.Now.ToUnixTimeSeconds();
            singleton.wisEnabled = false;
        }
        public static async Task ChatWithBot(string UserName, string message, string redemID, string rewardID)
        {
            var responce = await IllCommands.GetGPTResponce(message, UserName).ConfigureAwait(false);
            if (responce == "900")
            {
                TtvIRCClient.SendMessage($"@{UserName} Возможно в сгенерированном тексте присутствуют запретные слова. Текст не будет отображен. Баллы не вернутся.");
                await TtvAPI.ApproveReward(rewardID, redemID);
                return;
            }
            if (responce == "404")
            {
                TtvIRCClient.SendMessage($"@{UserName} Возникла ошибка. Баллы вернутся, можешь попробовать еще раз.");
                await TtvAPI.CencelReward(rewardID, redemID);
                return;
            }
            TtvIRCClient.SendMessage($"@{UserName} {responce}");
            if (UserName != IllSingleton.GetInstance().rootUser)
                await TtvAPI.ApproveReward(rewardID, redemID);
            else
                await TtvAPI.CencelReward(rewardID, redemID);
        }
        public static async Task TimeOutModerator(UserObject user, double duration, string reason)
        {
            await TtvAPI.TimeOutModerator(user, Convert.ToInt32(duration), reason).ConfigureAwait(false);
            if (Convert.ToBoolean(user.isMod))
            {
                Task backgroundTask = BackGroundTasks.UserUntimeoutTrigger(user.Name);
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
