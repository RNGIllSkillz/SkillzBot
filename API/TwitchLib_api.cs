using System;
using TwitchLib.Api;
using System.Linq;
using System.Threading.Tasks;
using SkillzBot.WRITERS;
using System.Collections.Generic;
using TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomRewardRedemptionStatus;
using TwitchLib.Api.Helix.Models.Predictions.CreatePrediction;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomReward;
using TwitchLib.Api.Core.Models.Undocumented.Chatters;
using TwitchLib.Api.Helix.Models.Clips.CreateClip;
using TwitchLib.Api.Helix.Models.ChannelPoints.GetCustomReward;
using TwitchLib.Api.Helix.Models.Moderation.BanUser;
using TwitchLib.Api.Helix.Models.Channels.GetChannelVIPs;
using SkillzBot.MODELS;
using TwitchLib.Api.Helix.Models.Chat.ChatSettings;
using SkillzBot.Singleton;
using Newtonsoft.Json;
using SkillzBot.JSON.nChatters;
using System.Net;
using TwitchLib.Api.Helix.Models.Predictions.GetPredictions;
using TwitchLib.Api.Helix.Models.Chat.GetChatters;
using TwitchLib.Api.Helix.Models.Streams.GetStreams;
using TwitchLib.Api.Helix.Models.ChannelPoints.GetCustomRewardRedemption;

namespace SkillzBot.API.Twitch
{
    public sealed class TtvAPI
    {
        private readonly static TwitchAPI API;
        static string PredID;
        static string winID;
        static string looseID;
        static readonly bool ValidToken = false;
        static readonly string BrodcasterID = IllSingleton.GetInstance().BrodcasterId;

        static TtvAPI()
        {
            API = new TwitchAPI();
            API.Settings.ClientId = IllSingleton.GetInstance().TApiClientId;             
            API.Settings.AccessToken = IllSingleton.GetInstance().TApiAccessToken;
            if (API.Settings.ClientId != "ClientId для доступа к API Twitch" && API.Settings.AccessToken != "Token для доступа к API Twitch")
                ValidToken = true;
            else
                Console.WriteLine("No valid TTV API access token. TTV API functionality is offline");
        }

        public static async ValueTask Start_2_Prediction(string Title, string blue, string red, int windowSec)
        {
            if (!ValidToken) return;
            var request = new CreatePredictionRequest
            {
                Title = Title,
                Outcomes = new[]
                {
                    new Outcome
                    {
                        Title = blue
                    },
                    new Outcome
                    {
                        Title = red
                    }
                },
                PredictionWindowSeconds = windowSec,
                BroadcasterId = BrodcasterID
            };
            try
            {
                await API.Helix.Predictions.CreatePredictionAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "Start_2_Prediction");
                return;
            }
            await GetCurrentPred().ConfigureAwait(false);

        }
        public static async ValueTask Start_10_Prediction(List<string> Champs, string Title, int windowSec)
        {
            if (!ValidToken) return;
            if (Champs == null || Champs.Count != 10)
            {
                throw new ArgumentException("Champs list must have exactly 10 items.");
            }
            var request = new CreatePredictionRequest
            {
                Title = Title,
                Outcomes = new Outcome[10],
                PredictionWindowSeconds = windowSec,
                BroadcasterId = BrodcasterID
            };
            for (int i = 0; i < 10; i++)
            {
                request.Outcomes[i] = new Outcome
                {
                    Title = Champs[i]
                };
            }
            try
            {
                await API.Helix.Predictions.CreatePredictionAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "Start_10_Prediction");
                return;
            }
            await GetCurrentPred().ConfigureAwait(false);
        }
        public static async ValueTask Start_5_Prediction(List<string> Champs, string Title, int windowSec)
        {
            if (!ValidToken) return;
            if (Champs == null || Champs.Count != 5)
            {
                Log.WriteLog(null, "Champs list must have exactly 5 items.");
                return;
            }

            var request = new CreatePredictionRequest
            {
                Title = Title,
                Outcomes = new Outcome[5],
                PredictionWindowSeconds = windowSec,
                BroadcasterId = BrodcasterID
            };

            for (int i = 0; i < 5; i++)
            {
                request.Outcomes[i] = new Outcome { Title = Champs[i] };
            }
            try
            {
                await API.Helix.Predictions.CreatePredictionAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "Start_10_Prediction");
                return;
            }
            await GetCurrentPred().ConfigureAwait(false);            
        }
        public static async Task<string> End_Multy_Prediction(string Champ)
        {
            if (!ValidToken) return "ERR";
            GetPredictionsResponse Predictions;
            try
            {
                Predictions = await API.Helix.Predictions.GetPredictionsAsync(BrodcasterID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "End_Multy_Prediction");
                return "ERR";
            }
            string currentPredID = Predictions.Data.First().Id;
            var predictionStatus = TwitchLib.Api.Core.Enums.PredictionEndStatus.RESOLVED;
            string OutcomeID = "";

            var Outcomes = Predictions.Data.First().Outcomes.ToArray();
            foreach (var Outcom in Outcomes)
            {
                if (Outcom.Title == Champ)
                {
                    OutcomeID = Outcom.Id;
                }
            }
            if (currentPredID == PredID)
            {
                try
                {
                    await API.Helix.Predictions.EndPredictionAsync(BrodcasterID, PredID, predictionStatus, OutcomeID).ConfigureAwait(false);
                    return "OK";
                }
                catch (Exception ex)
                {
                    Log.WriteLog(ex, "End_Multy_Prediction");
                    return "ERR";
                }
            }
            else
            {
                Log.WriteLog(null, "(Task EndPrediction) currentPredID != PredID");
            }
            return "OK";
        }
        private static async Task GetCurrentPred()
        {
            GetPredictionsResponse Predictions;
            try
            {
                Predictions = await API.Helix.Predictions.GetPredictionsAsync(BrodcasterID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetCurrentPred");
                return;
            }
            PredID = Predictions.Data.First().Id;
            winID = Predictions.Data.First().Outcomes.First().Id;
            looseID = Predictions.Data.First().Outcomes.Last().Id;
        }
        public static async Task<GetPredictionsResponse> GetCurrentPredPublic()
        {
            try
            {
                return await API.Helix.Predictions.GetPredictionsAsync(BrodcasterID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetCurrentPredPublic");
                return null;
            }
        }
        public static async Task End_WinLoose_Prediction(bool win)
        {
            GetPredictionsResponse Predictions;
            if (!ValidToken) return;
            try
            {
                Predictions = await API.Helix.Predictions.GetPredictionsAsync(BrodcasterID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "End_WinLoose_Prediction()");
                return;
            }
            if (Predictions == null) return;
            string currentPredID = Predictions.Data.First().Id;
            if (currentPredID == PredID)
            {
                try
                {
                    if (win)
                        await API.Helix.Predictions.EndPredictionAsync(BrodcasterID, PredID, TwitchLib.Api.Core.Enums.PredictionEndStatus.RESOLVED, winID).ConfigureAwait(false);
                    else
                        await API.Helix.Predictions.EndPredictionAsync(BrodcasterID, PredID, TwitchLib.Api.Core.Enums.PredictionEndStatus.RESOLVED, looseID).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.WriteLog(ex, "End_WinLoose_Prediction()");
                }
            }
            else
            {
                Log.WriteLog(null, "(Task EndPrediction) currentPredID != PredID");
            }
        }
        public static async Task CencelePrediction()
        {            
            if (!ValidToken) return;
            GetPredictionsResponse Predictions;
            try
            {
                Predictions = await API.Helix.Predictions.GetPredictionsAsync(BrodcasterID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "CencelePrediction");
                return;
            }
            string currentPredID = Predictions.Data.First().Id;
            if (currentPredID == PredID)
            {
                try
                {
                    await API.Helix.Predictions.EndPredictionAsync(BrodcasterID, PredID, TwitchLib.Api.Core.Enums.PredictionEndStatus.CANCELED, null).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.WriteLog(ex, "Start_10_Prediction");
                    return;
                }
            }
            else
            {
                Log.WriteLog(null, "(Task EndPrediction) currentPredID != PredID");
            }
        }
        public static async Task<GetCustomRewardsResponse> GetAllRewards()
        {            
            if (!ValidToken) return null;
            GetCustomRewardsResponse AllRewards;
            try
            {
                AllRewards = await API.Helix.ChannelPoints.GetCustomRewardAsync(BrodcasterID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "getAllRewards");
                return null;
            }
            foreach (var reward in AllRewards.Data)
            {
                Log.WriteLog(null, $"{reward.Id} - {reward.Title} - {reward.IsEnabled}");
            }
            return AllRewards;
        }
        public static async Task<List<string>> GetReward(string id)
        {
            if (!ValidToken)
                return new List<string>
                {
                    "500"
                };
            GetCustomRewardsResponse rewards;
            try
            {
                rewards = await API.Helix.ChannelPoints.GetCustomRewardAsync(BrodcasterID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetReward");
                return new List<string>
                {
                    "500"
                };
            }
            List<string> responce = new List<string>();
            foreach (var reward in rewards.Data)
            {
                if (reward.Id == id)
                {
                    responce.Add(reward.Id);
                    responce.Add(reward.Title);
                    responce.Add(reward.Cost.ToString());
                    responce.Add(reward.Prompt);
                    responce.Add(reward.IsEnabled.ToString());
                    responce.Add(reward.IsUserInputRequired.ToString());
                    return responce;
                }
            }
            responce.Add("404");
            return responce;
        }
        public static async Task<List<string>> GetReward(string title, string OverloadParam)
        {
            if (!ValidToken)
                return new List<string>
                {
                    "500"
                };

            GetCustomRewardsResponse rewards;
            try
            {
                rewards = await API.Helix.ChannelPoints.GetCustomRewardAsync(BrodcasterID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetReward");
                return new List<string>
                {
                    "500"
                };
            }
            List<string> responce = new List<string>();
            foreach (var reward in rewards.Data)
            {
                if (reward.Title == title)
                {
                    responce.Add(reward.Id);
                    responce.Add(reward.Title);
                    responce.Add(reward.Cost.ToString());
                    responce.Add(reward.Prompt);
                    responce.Add(reward.IsEnabled.ToString());
                    responce.Add(reward.IsUserInputRequired.ToString());
                    return responce;
                }
            }
            responce.Add("404");
            return responce;

        }
        public static async Task UpdateReward(string rewardID, string title, int cost, string prompt,bool enable, bool isUserInputRequired)
        {
            try
            {
                await API.Helix.ChannelPoints.UpdateCustomRewardAsync(BrodcasterID, rewardID, new UpdateCustomRewardRequest
                {
                    Title = title,
                    Cost = cost,
                    Prompt = prompt,
                    IsEnabled = enable,
                    IsUserInputRequired = isUserInputRequired,
                    ShouldRedemptionsSkipRequestQueue = false
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "updateReward");
            }
        }
        public static async Task DeleteReward(string rewardID)
        {
            if (!ValidToken) return;
            try
            {
                await API.Helix.ChannelPoints.DeleteCustomRewardAsync(BrodcasterID, rewardID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "DeleteReward");
                return;
            }
        }
        public static async Task<string> CreateReward(string title, int cost, string promt, bool enabled, bool userinput)
        {
            if (!ValidToken) return null;
            try
            {
                var createResp = await API.Helix.ChannelPoints.CreateCustomRewardsAsync(BrodcasterID, new CreateCustomRewardsRequest
                {
                    Title = title,
                    Cost = cost,
                    Prompt = promt,
                    IsEnabled = enabled,
                    IsUserInputRequired = userinput,
                    ShouldRedemptionsSkipRequestQueue = false
                }).ConfigureAwait(false);
                return createResp.Data[0].Id;
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "DeleteReward");
                return null;
            }            
        }
        public static async Task CencelReward(string rewardID, string RedemID)
        {
            if (!ValidToken) return;
            try
            {
                await API.Helix.ChannelPoints.UpdateRedemptionStatusAsync(BrodcasterID, rewardID, new List<string> { RedemID }, new UpdateCustomRewardRedemptionStatusRequest
                {
                    Status = TwitchLib.Api.Core.Enums.CustomRewardRedemptionStatus.CANCELED
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "CencelReward");
            }
        }
        public static async Task ApproveReward(string rewardID, string RedemID)
        {
            if (!ValidToken) return;
            try
            {
                await API.Helix.ChannelPoints.UpdateRedemptionStatusAsync(BrodcasterID, rewardID, new List<string> { RedemID }, new UpdateCustomRewardRedemptionStatusRequest
                {
                    Status = TwitchLib.Api.Core.Enums.CustomRewardRedemptionStatus.FULFILLED
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "ApproveReward");
            }
        }
        public static async Task<CreatedClipResponse> CreateClip()
        {
            if (!ValidToken) return null;
            try
            {
                return await API.Helix.Clips.CreateClipAsync(BrodcasterID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "CreateClip");
                return null;
            }
        }
        public static async Task<bool> GetStreamStatus()
        {
            List<string> bID = new List<string>
                {
                    BrodcasterID
                };
            GetStreamsResponse streams;
            try
            {
                streams = await API.Helix.Streams.GetStreamsAsync(null, 1, null, null, bID, null, null);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetStreamStatus()");
                return false;
            }
            if (streams != null && streams.Streams.Any())
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        public static async Task GetCustomRewardTest(string rewardID)
        {
            var redemption = await API.Helix.ChannelPoints.GetCustomRewardRedemptionAsync(BrodcasterID, rewardID).ConfigureAwait(false);
            foreach (var rew in redemption.Data)
            {
                Console.WriteLine(rew.Id);                    
            }           
        }
        public static async Task<string> GetCustomReward(string rewardID,string userID)
        {
            GetCustomRewardRedemptionResponse redemption;
            try
            {
                redemption = await API.Helix.ChannelPoints.GetCustomRewardRedemptionAsync(BrodcasterID, rewardID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetStreamStatus()");
                return null;
            }
            for (int i = 0; i < redemption.Data.Length; i++)            
                if (redemption.Data[i].UserId == userID)
                    return redemption.Data[i].Id;            
            return null;
        }
        private static async Task TimeOutUserAsync(string UserID, int Duration, string Reason)
        {
            try
            {
                await API.Helix.Moderation.BanUserAsync(BrodcasterID, BrodcasterID, new BanUserRequest
                {
                    UserId = UserID,
                    Duration = Duration,
                    Reason = Reason
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "TimeOutUserAsync");
            }
        }
        public static async Task BanUser(string UserID, string Reason)
        {
            try
            {
                await API.Helix.Moderation.BanUserAsync(BrodcasterID, BrodcasterID, new BanUserRequest
                {
                    UserId = UserID,
                    Reason = Reason
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "BanUser");
            }
        }
        public static async Task UnBanUser(string UserID)
        {
            try
            {
                await API.Helix.Moderation.UnbanUserAsync(BrodcasterID, BrodcasterID, UserID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "UnBanUser");
            }
        }
        public static async Task Announce(string Message)
        {
            try
            {
                await API.Helix.Chat.SendChatAnnouncementAsync(BrodcasterID, BrodcasterID, Message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "Announce");
            }
        }
        public static async Task DeleteMessage(string MessageID)
        {
            try
            {
                await API.Helix.Moderation.DeleteChatMessagesAsync(BrodcasterID,BrodcasterID,MessageID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "DeleteMessage");
            }
        }
        public static async Task DeleteAllMessages()
        {
            try
            {
                await API.Helix.Moderation.DeleteChatMessagesAsync(BrodcasterID, BrodcasterID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "DeleteAllMessages");
            }
        }
        public static async Task AddChannelModerator(string UserID)
        {
            try
            {
                await API.Helix.Moderation.AddChannelModeratorAsync(BrodcasterID,UserID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "AddChannelModerator");
            }
        }
        public static async Task DeleteChannelModerator(string UserID)
        {
            try
            {
                await API.Helix.Moderation.DeleteChannelModeratorAsync(BrodcasterID, UserID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "AddChannelModerator");
            }
        }
        public static async Task<GetChannelVIPsResponse> GetVIPs()
        {
            try
            {
                return await API.Helix.Channels.GetVIPsAsync(BrodcasterID,null,100).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "AddChannelModerator");
                return null;
            }
        }
        public static async Task AddChannelVIP(string UserID)
        {
            try
            {
                await API.Helix.Channels.AddChannelVIPAsync(BrodcasterID, UserID).ConfigureAwait(false);
            }
            catch (Exception ex) 
            { 
                Log.WriteLog(ex, "AddChannelVIP"); 
            }
        }
        public static async Task DeleteChannelVIP(string UserID)
        {
            try
            {
                await API.Helix.Channels.RemoveChannelVIPAsync(BrodcasterID, UserID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "DeleteChannelVIP");
            }
        }
        public static async Task SetEmoteOnlyMode(bool IsEmoteOnly)
        {
            try
            {
                await API.Helix.Chat.UpdateChatSettingsAsync(BrodcasterID, BrodcasterID, new ChatSettings
                {
                    EmoteMode = IsEmoteOnly
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "SetEmoteOnlyMode");
            }
        }
        public static async Task<UserObject> TimeOutUser(UserObject user, int Duration, string Reasone)
        {
            if (user.isMod == 1) return user;
            try
            {
                await TimeOutUserAsync(user.TwitchID.ToString(), Duration, Reasone).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "TimeOutUser");
                return user;
            }
            user.UvalCon++;            
            return user;
        }
        /*
        public static async Task<SChatters> GetChattersAsyncDepricated()
        {
            try
            {
                String url = $"https://tmi.twitch.tv/group/user/{IllSingleton.GetInstance().ChannelName}/chatters";
                HttpWebRequest HttpWebRequest = (HttpWebRequest)WebRequest.Create(url);
                HttpWebRequest.UserAgent = "<Linux>:<IllSkillz_bot>:<v1.5>";
                using HttpWebResponse HttpWebResponse = (HttpWebResponse)HttpWebRequest.GetResponse();
                Stream streamResponse = HttpWebResponse.GetResponseStream();
                using StreamReader streamRead = new StreamReader(streamResponse);
                Char[] readBuff = new Char[256];
                string JSONResponse = "";
                int count = await streamRead.ReadAsync(readBuff, 0, 256).ConfigureAwait(false);
                while (count > 0)
                {
                    String outputData = new String(readBuff, 0, count);
                    JSONResponse += outputData;
                    count = await streamRead.ReadAsync(readBuff, 0, 256).ConfigureAwait(false);
                }
                return JsonConvert.DeserializeObject<SChatters>(JSONResponse);
            }
            catch (Exception e)
            {
                Log.WriteLog(e, "getChateers()");
                return null;
            }
        }*/
        public static async Task<GetChattersResponse> GetChattersAsync()
        {
            try
            {
                return await API.Helix.Chat.GetChattersAsync(BrodcasterID, BrodcasterID);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetChattersAsync");
                return null;
            }
        }
    }
}