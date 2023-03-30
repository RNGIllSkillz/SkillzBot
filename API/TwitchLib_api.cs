using System;
using TwitchLib.Api;
using SkillzBot.Readers;
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
using TwitchLib.Api.Helix.Models.ChannelPoints;
using TwitchLib.Api.Helix.Models.Channels.GetChannelVIPs;
using SkillzBot.MODELS;
using TwitchLib.Api.Helix.Models.Chat.ChatSettings;
using SkillzBot.Singleton;
using Newtonsoft.Json;
using SkillzBot.JSON.nChatters;
using System.IO;
using System.Net;
using System.Security.Policy;
using TwitchLib.Api.Helix.Models.Predictions.GetPredictions;
using TwitchLib.Api.Helix.Models.Chat.GetChatters;

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
            await API.Helix.Predictions.CreatePredictionAsync(request).ConfigureAwait(false);
            await GetCurrentPred().ConfigureAwait(false);

        }
        public static async ValueTask Start_10_Prediction(List<string> Champs, string Title, int windowSec)
        {
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
            if (ValidToken)
            {
                await API.Helix.Predictions.CreatePredictionAsync(request).ConfigureAwait(false);
                await GetCurrentPred().ConfigureAwait(false);
            }
        }
        public static async ValueTask Start_5_Prediction(List<string> Champs, string Title, int windowSec)
        {
            if (Champs == null || Champs.Count != 5)
            {
                throw new ArgumentException("Champs list must have exactly 5 items.");
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

            if (ValidToken)
            {
                await API.Helix.Predictions.CreatePredictionAsync(request).ConfigureAwait(false);
                await GetCurrentPred().ConfigureAwait(false);
            }
        }
        public static async Task<string> End_Multy_Prediction(string Champ)
        {
            var Predictions = await API.Helix.Predictions.GetPredictionsAsync(BrodcasterID).ConfigureAwait(false);
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
                if (OutcomeID != "")
                {
                    if (ValidToken)
                    {
                        await API.Helix.Predictions.EndPredictionAsync(BrodcasterID, PredID, predictionStatus, OutcomeID).ConfigureAwait(false);
                        return "OK";
                    }
                }
                else
                    return "Twitch API error. Ставка рассчитана не будет.";
            }
            else
            {
                Log.WriteLog(null, "(Task EndPrediction) currentPredID != PredID");                
            }
            return "OK";
        }
        private static async Task GetCurrentPred()
        {
            var Predictions = await API.Helix.Predictions.GetPredictionsAsync(BrodcasterID).ConfigureAwait(false);
            PredID = Predictions.Data.First().Id;
            winID = Predictions.Data.First().Outcomes.First().Id;
            looseID = Predictions.Data.First().Outcomes.Last().Id;
        }
        public static async Task<GetPredictionsResponse> GetCurrentPredPublic()
        {
            return await API.Helix.Predictions.GetPredictionsAsync(BrodcasterID).ConfigureAwait(false);
        }
        public static async Task End_WinLoose_Prediction(bool win)
        {
            TwitchLib.Api.Helix.Models.Predictions.GetPredictions.GetPredictionsResponse Predictions = new TwitchLib.Api.Helix.Models.Predictions.GetPredictions.GetPredictionsResponse();
            if (ValidToken)
                Predictions = await API.Helix.Predictions.GetPredictionsAsync(BrodcasterID).ConfigureAwait(false);
            if (Predictions != null)
            {
                string currentPredID = Predictions.Data.First().Id;
                var predictionStatus = TwitchLib.Api.Core.Enums.PredictionEndStatus.RESOLVED;
                if (currentPredID == PredID)
                {
                    if (win)
                        await API.Helix.Predictions.EndPredictionAsync(BrodcasterID, PredID, predictionStatus, winID).ConfigureAwait(false);
                    else
                        await API.Helix.Predictions.EndPredictionAsync(BrodcasterID, PredID, predictionStatus, looseID).ConfigureAwait(false);
                }
                else
                {
                    Log.WriteLog(null, "(Task EndPrediction) currentPredID != PredID");
                }
            }
        }
        public static async Task CencelePrediction()
        {
            TwitchLib.Api.Helix.Models.Predictions.GetPredictions.GetPredictionsResponse Predictions = new TwitchLib.Api.Helix.Models.Predictions.GetPredictions.GetPredictionsResponse();
            if (ValidToken)
                Predictions = await API.Helix.Predictions.GetPredictionsAsync(BrodcasterID).ConfigureAwait(false);
            if (Predictions != null)
            {
                string currentPredID = Predictions.Data.First().Id;
                var predictionStatus = TwitchLib.Api.Core.Enums.PredictionEndStatus.CANCELED;
                if (currentPredID == PredID)
                {
                    await API.Helix.Predictions.EndPredictionAsync(BrodcasterID, PredID, predictionStatus, null).ConfigureAwait(false);
                }
                else
                {
                    Log.WriteLog(null, "(Task EndPrediction) currentPredID != PredID");
                }
            }
        }
        public static async Task<GetCustomRewardsResponse> getAllRewards()
        {
            GetCustomRewardsResponse AllRewards = new GetCustomRewardsResponse();
            if (ValidToken)
                AllRewards = await API.Helix.ChannelPoints.GetCustomRewardAsync(BrodcasterID).ConfigureAwait(false);
            if (AllRewards != null)
            {
                foreach (var reward in AllRewards.Data)
                {
                    Log.WriteLog(null, $"{reward.Id} - {reward.Title} - {reward.IsEnabled}");
                }
                return AllRewards;
            }
            return null;
        }
        public static async Task<List<string>> getReward(string id)
        {
            if (ValidToken)
            {
                var rewards = await API.Helix.ChannelPoints.GetCustomRewardAsync(BrodcasterID).ConfigureAwait(false);
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
            else
            {
                return new List<string>
                {
                    "500"
                };
            }
        }
        public static async Task<List<string>> getReward(string title, string fl)
        {
            if (ValidToken)
            {
                var rewards = await API.Helix.ChannelPoints.GetCustomRewardAsync(BrodcasterID).ConfigureAwait(false);
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
                responce.Add("Error 404");
                return responce;
            }
            else
            {
                List<string> responce = new List<string>();
                responce.Add("Error 500");
                return responce;
            }
        }
        public static async Task updateReward(string rewardID, string title, int cost, string prompt,bool enable, bool isUserInputRequired)
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
        public static async Task deleteReward(string rewardID)
        {
            if (ValidToken)
                await API.Helix.ChannelPoints.DeleteCustomRewardAsync(BrodcasterID, rewardID).ConfigureAwait(false);
        }
        public static async Task<string> createReward(string title, int cost, string promt, bool enabled, bool userinput)
        {
            if (ValidToken)
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
            return null;
        }
        public static async Task CencelReward(string rewardID, string RedemID)
        {
            if (ValidToken)
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
            if (ValidToken)
                await API.Helix.ChannelPoints.UpdateRedemptionStatusAsync(BrodcasterID, rewardID, new List<string> { RedemID }, new UpdateCustomRewardRedemptionStatusRequest
                {
                    Status = TwitchLib.Api.Core.Enums.CustomRewardRedemptionStatus.FULFILLED
                }).ConfigureAwait(false);
        }
        public static async Task<CreatedClipResponse> CreateClip()
        {
            if (ValidToken)
            {
                return await API.Helix.Clips.CreateClipAsync(BrodcasterID).ConfigureAwait(false);                 
            }
            return null;
        }
        public static async Task<bool> GetStreamStatus()
        {
            try
            {
                List<string> bID = new List<string>();
                bID.Add(BrodcasterID);
                var streams = await API.Helix.Streams.GetStreamsAsync(null, 1, null, null, bID, null, null);
                if (streams != null && streams.Streams.Any())
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetStreamStatus()");
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
            var redemption = await API.Helix.ChannelPoints.GetCustomRewardRedemptionAsync(BrodcasterID, rewardID).ConfigureAwait(false);
            foreach (var rew in redemption.Data)
            {
                if (rew.UserId == userID)
                    return rew.Id;
            }
            return null;
        }
        private static async Task TimeOutUserAsync(string UserID, int Duration, string Reason)
        {
            await API.Helix.Moderation.BanUserAsync(BrodcasterID, BrodcasterID, new BanUserRequest
            {
                UserId = UserID,
                Duration = Duration,
                Reason = Reason
            }).ConfigureAwait(false);
        }
        public static async Task BanUser(string UserID, string Reason)
        {
            await API.Helix.Moderation.BanUserAsync(BrodcasterID, BrodcasterID, new BanUserRequest
            {
                UserId = UserID,
                Reason = Reason
            }).ConfigureAwait(false);            
        }
        public static async Task UnBanUser(string UserID)
        {
            await API.Helix.Moderation.UnbanUserAsync(BrodcasterID, BrodcasterID, UserID).ConfigureAwait(false);
        }
        public static async Task Announce(string Message)
        {
            await API.Helix.Chat.SendChatAnnouncementAsync(BrodcasterID, BrodcasterID, Message).ConfigureAwait(false);
        }
        public static async Task DeleteMessage(string MessageID)
        {
            await API.Helix.Moderation.DeleteChatMessagesAsync(BrodcasterID,BrodcasterID,MessageID).ConfigureAwait(false);
        }
        public static async Task DeleteAllMessages()
        {
            await API.Helix.Moderation.DeleteChatMessagesAsync(BrodcasterID, BrodcasterID).ConfigureAwait(false);
        }
        public static async Task AddChannelModerator(string UserID)
        {
            await API.Helix.Moderation.AddChannelModeratorAsync(BrodcasterID,UserID).ConfigureAwait(false);
        }
        public static async Task DeleteChannelModerator(string UserID)
        {
            await API.Helix.Moderation.DeleteChannelModeratorAsync(BrodcasterID, UserID).ConfigureAwait(false);
        }
        public static async Task<GetChannelVIPsResponse> GetVIPs()
        {
            return await API.Helix.Channels.GetVIPsAsync(BrodcasterID,null,100).ConfigureAwait(false);            
        }
        public static async Task AddChannelVIP(string UserID)
        {
            await API.Helix.Channels.AddChannelVIPAsync(BrodcasterID, UserID).ConfigureAwait(false);
        }
        public static async Task DeleteChannelVIP(string UserID)
        {
            await API.Helix.Channels.RemoveChannelVIPAsync(BrodcasterID, UserID).ConfigureAwait(false);
        }
        public static async Task SetEmoteOnlyMode(bool IsEmoteOnly)
        {
            await API.Helix.Chat.UpdateChatSettingsAsync(BrodcasterID, BrodcasterID, new ChatSettings
            {
                EmoteMode = IsEmoteOnly
            }).ConfigureAwait(false);
        }
        public static async Task<UserObject> TimeOutUser(UserObject user, int Duration, string Reasone)
        {
            if (user.isMod == 1) return user;
            await TimeOutUserAsync(user.TwitchID.ToString(), Duration, Reasone).ConfigureAwait(false);
            user.UvalCon++;            
            return user;
        }
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
        }
        public static async Task<GetChattersResponse> GetChattersAsync()
        {
            return await API.Helix.Chat.GetChattersAsync(BrodcasterID, BrodcasterID);
        }
    }
}