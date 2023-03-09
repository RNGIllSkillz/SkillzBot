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

namespace SkillzBot.TtvClient.TTVRewards
{
    internal class RewardsRedemption
    {
        private static bool CencelUvalIsWating = false;        
        private static string CencelUvalUserName = "";

        public static async Task UvalSabReward(string UserName, string message, string redemID, string rewardID)
        {
            try
            {
                var uName = StringUtil.GetUserNameFromInput(message);
                var qUser = await MySQL.GetUser(UserName).ConfigureAwait(false);
                if (uName != IllSingleton.GetInstance().rootUser)
                {
                    if (uName != IllSingleton.GetInstance().ChannelName)
                    {
                        var user = await MySQL.GetUser(uName).ConfigureAwait(false);
                        if (user.dbID == -404)
                        {
                            TtvIRCClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, UserName, uName));
                            //await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                        }
                        else
                        {
                            if (!Convert.ToBoolean(user.isPartner) && !Convert.ToBoolean(user.isMod))
                            {
                                if (!Convert.ToBoolean(user.isVip))
                                {
                                    double duration = 600;
                                    if (user.UvalTimer > DateTimeOffset.Now.ToUnixTimeSeconds())
                                        duration = user.UvalTimer - DateTimeOffset.Now.ToUnixTimeSeconds() + 600;
                                    await TtvAPI.TimeOutUser(user, Convert.ToInt32(duration), STRINGS.TimeOutReason_TimeOutSub).ConfigureAwait(false);
                                    TtvIRCClient.SendMessage(string.Format(STRINGS.TimeOutReward_chatMessage, UserName, uName, duration, user.UvalCon + 1));
                                    if (UserName != IllSingleton.GetInstance().rootUser)
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
                        if (UserName != IllSingleton.GetInstance().rootUser)
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
                        if (UserName != IllSingleton.GetInstance().rootUser)
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
        public static async Task UvalVIPReward(string UserName, string message, string redemID, string rewardID)
        {
            try
            {
                var uName = StringUtil.GetUserNameFromInput(message);
                var qUser = await MySQL.GetUser(UserName).ConfigureAwait(false);
                if (uName != IllSingleton.GetInstance().rootUser.ToLower())
                {
                    if (uName != IllSingleton.GetInstance().ChannelName)
                    {
                        var user = await MySQL.GetUser(uName).ConfigureAwait(false);

                        if (user.dbID == -404)
                        {
                            TtvIRCClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, UserName, uName));
                            //await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                        }
                        else
                        {
                            if (!Convert.ToBoolean(user.isPartner) && !Convert.ToBoolean(user.isMod))
                            {
                                double duration = 600;
                                if (user.UvalTimer > DateTimeOffset.Now.ToUnixTimeSeconds())
                                    duration = user.UvalTimer - DateTimeOffset.Now.ToUnixTimeSeconds() + 600;
                                await TtvAPI.TimeOutUser(user, Convert.ToInt32(duration), STRINGS.TimeOutReason_TimeOutVIP).ConfigureAwait(false);
                                TtvIRCClient.SendMessage(string.Format(STRINGS.TimeOutReward_chatMessage, UserName, uName, duration, user.UvalCon + 1));
                                if (UserName != IllSingleton.GetInstance().rootUser)
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
                        if (UserName != IllSingleton.GetInstance().rootUser)
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
                        if (UserName != IllSingleton.GetInstance().rootUser)
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
        public static async Task UvalReward(string UserName, string message, string redemID, string rewardID)
        {
            try
            {
                var uName = StringUtil.GetUserNameFromInput(message);
                var qUser = await MySQL.GetUser(UserName).ConfigureAwait(false);
                if (uName != IllSingleton.GetInstance().rootUser)
                {
                    if (uName != IllSingleton.GetInstance().ChannelName)
                    {
                        var user = await MySQL.GetUser(uName).ConfigureAwait(false);
                        if (user.dbID == -404)
                        {
                            TtvIRCClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, UserName, uName));
                            //await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                        }
                        else
                        {
                            if (!Convert.ToBoolean(user.isPartner) && !Convert.ToBoolean(user.isMod))
                            {
                                if (!Convert.ToBoolean(user.isSub) & !Convert.ToBoolean(user.isVip))
                                {
                                    double duration = 600;
                                    if (user.UvalTimer > DateTimeOffset.Now.ToUnixTimeSeconds())
                                        duration = user.UvalTimer - DateTimeOffset.Now.ToUnixTimeSeconds() + 600;
                                    await TtvAPI.TimeOutUser(user, Convert.ToInt32(duration), STRINGS.TimeOutReason_TimeOutUnsub).ConfigureAwait(false);
                                    TtvIRCClient.SendMessage(string.Format(STRINGS.TimeOutReward_chatMessage, UserName, uName, duration, user.UvalCon + 1));
                                    if (UserName != IllSingleton.GetInstance().rootUser)
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
                        if (UserName != IllSingleton.GetInstance().rootUser)
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
                        if (UserName != IllSingleton.GetInstance().rootUser)
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
            string yID = StringUtil.ExtractYouTubeVideoId(Link) ?? await YouTubeSearch.YouTubeSearchByKeyWordTask(Link).ConfigureAwait(false);
            List<string> response;
            if (yID != null)
            {
                if (IllChatFilters.CheckTreck(yID))
                {
                    TtvIRCClient.SendMessage(string.Format(STRINGS.Track500_bannedTrack, UserName));
                    if (rewardID != null)
                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                    return false;
                }
                try
                {
                    response = await IllChatFilters.YouTubeFilter(yID).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (rewardID != null)
                        await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                    TtvIRCClient.SendMessage(string.Format(STRINGS.Track_Esception, IllSingleton.GetInstance().rootUser));
                    Log.WriteLog(ex, $"{Link} -> {yID}");
                    return false;
                }
                switch (response[0])
                {
                    case "ok":
                        if (IllChatFilters.CheckChannel(response[1]))
                        {
                            TtvIRCClient.SendMessage(string.Format(STRINGS.Track500_bannedTrack, UserName));
                            if (rewardID != null)
                                await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                            return false;
                        }

                        var user = await MySQL.GetUser(UserName).ConfigureAwait(false);                        
                        if (IllChatFilters.IsUserBlacklisted(user.TwitchID.ToString()))
                        {
                            TtvIRCClient.SendMessage(string.Format(STRINGS.Track500_bannedUser, UserName));
                            if (rewardID != null)
                                await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                            return false;
                        }
                        
                        if (await StreamElementsAPI.SendMediaAsync(yID).ConfigureAwait(false))
                        {
                            TtvIRCClient.SendMessage(string.Format(STRINGS.Track200_Success, UserName, response[2]));
                            MediaqueueWriter.Write(user.TwitchID, yID);
                            if (rewardID != null)
                                if (UserName == IllSingleton.GetInstance().rootUser)
                                    await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                                else
                                    await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
                            return true;
                        }
                        else
                        {
                            TtvIRCClient.SendMessage(string.Format(STRINGS.Track400_ERROR, UserName));
                            if (rewardID != null)
                                await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                            return false;
                        }                                             

                    case "age":
                        TtvIRCClient.SendMessage(string.Format(STRINGS.Track510_Age, UserName));
                        if (rewardID != null)
                            await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                        return false;

                    case "duration":
                        TtvIRCClient.SendMessage(string.Format(STRINGS.Track510_Duration, UserName));
                        if (rewardID != null)
                            await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                        return false;

                    case "view":
                        TtvIRCClient.SendMessage(string.Format(STRINGS.Track510_ViewCount, UserName));
                        if (rewardID != null)
                            await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                        return false;

                    case "ZAP":
                        try
                        {
                            MediaBlackListWriter.Write(yID);
                            var user2 = await MySQL.GetUser(UserName).ConfigureAwait(false);
                            if (user2.dbID == -404)
                            {
                                Log.WriteLog(null, $"ID {UserName} == -1 zakazTrekaMethod");
                            }
                            else
                            {
                                await IllCommands.IllBanUser(user2).ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.WriteLog(ex, "");
                        }
                        try
                        {
                            FlagWriter.FlagWriterTask($"{DateTime.Now} {UserName} : {Link}");
                        }
                        catch (Exception e)
                        {
                            Log.WriteLog(e, "zakazTrekaMethod()");
                        }
                        return false;

                    case "Embeddable":
                        TtvIRCClient.SendMessage(string.Format(STRINGS.Track510_Embedded, UserName));
                        if (rewardID != null)
                            await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                        return false;
                }
            }
            else
            {
                TtvIRCClient.SendMessage(string.Format(STRINGS.Track404, UserName));
                if (rewardID != null)
                    await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                return false;
            }
            return false;
        }
        public static async Task CenceleUvalReward(string UserName, string message, string redemID, string rewardID)
        {
            if (!CencelUvalIsWating)
            {                
                try
                {
                    await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
                    double uvalTime = 0;
                    var uName = StringUtil.GetUserNameFromInput(message);
                    var user = await MySQL.GetUser(uName).ConfigureAwait(false);
                    if (user.dbID == -404)
                    {
                        TtvIRCClient.SendMessage(string.Format(STRINGS.CencelUval404, UserName,uName));
                    }
                    else
                    {
                        uvalTime = user.UvalTimer - DateTimeOffset.Now.ToUnixTimeSeconds();
                        if (uvalTime > 5)
                        {
                            CencelUvalUserName = uName;
                            CencelUvalIsWating = true;
                            await TtvAPI.updateReward(rewardID, string.Format(STRINGS.UpdateRewardTitleNew, uName), (Convert.ToInt32(uvalTime) * 33), "", true, false).ConfigureAwait(false);
                            TtvIRCClient.SendMessage(string.Format(STRINGS.CencelUval_ChatMessage, UserName, uName, uvalTime, uvalTime * 33));
                            double startTime = DateTimeOffset.Now.ToUnixTimeSeconds();
                            while (CencelUvalIsWating)
                            {
                                if ((DateTimeOffset.Now.ToUnixTimeSeconds() - startTime) >= 60)
                                {
                                    TtvIRCClient.SendMessage(STRINGS.CencelUval_TimeOut);
                                    await TtvAPI.updateReward(rewardID, STRINGS.UpdateRewardTitleOrig, 10000, STRINGS.UpdateRewardPromptOrig, true, true).ConfigureAwait(false);
                                    CencelUvalIsWating = false;                                    
                                }                                
                                await Task.Delay(250);
                            }   
                        }
                        else
                        {
                            TtvIRCClient.SendMessage(string.Format(STRINGS.CencelUval_NotInTimeOut, UserName, uName));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteLog(ex, "cenceleUval");
                }
            }
            else
            {
                //отменить увал
                CencelUvalIsWating = false;
                var user = await MySQL.GetUser(CencelUvalUserName.ToLower()).ConfigureAwait(false);
                if (user.dbID == -404)
                {
                    TtvIRCClient.SendMessage(STRINGS.CencelUval404);
                }
                else
                {
                    await TtvAPI.UnBanUser(user.TwitchID.ToString()).ConfigureAwait(false);
                }
                await TtvAPI.updateReward(rewardID, STRINGS.UpdateRewardTitleOrig, 10000, STRINGS.UpdateRewardPromptOrig, true, true).ConfigureAwait(false);
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
            var reward = await TtvAPI.getReward(rewardID).ConfigureAwait(false);
            if (UserName != IllSingleton.GetInstance().rootUser)
                await TtvAPI.ApproveReward(rewardID, redemID).ConfigureAwait(false);
            else
                await TtvAPI.CencelReward(rewardID, redemID).ConfigureAwait(false);
            await TtvAPI.updateReward(reward[0], reward[1], Convert.ToInt32(reward[2]), reward[3], false, Convert.ToBoolean(reward[4])).ConfigureAwait(false);
            TtvIRCClient.SendMessage($"@{IllSingleton.GetInstance().ChannelName}, СЛОВО! @{IllSingleton.GetInstance().ChannelName}, СЛОВО! @{IllSingleton.GetInstance().ChannelName}, СЛОВО! @{IllSingleton.GetInstance().ChannelName}, СЛОВО! @{IllSingleton.GetInstance().ChannelName}, СЛОВО! @{IllSingleton.GetInstance().ChannelName}, СЛОВО! @{IllSingleton.GetInstance().ChannelName}, СЛОВО! ");
            IllSingleton.GetInstance().WisCD = DateTimeOffset.Now.ToUnixTimeSeconds();
            IllSingleton.GetInstance().wisEnabled = false;
        } 
    }
}
