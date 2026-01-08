using Microsoft.Extensions.Logging;
using SkillzBot.MODELS;
using SkillzBot.Singleton;
using SkillzBot.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.ChannelPoints;
using TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward;
using TwitchLib.Api.Helix.Models.ChannelPoints.GetCustomReward;
using TwitchLib.Api.Helix.Models.ChannelPoints.GetCustomRewardRedemption;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomReward;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomRewardRedemptionStatus;
using TwitchLib.Api.Helix.Models.Channels.GetChannelInformation;
using TwitchLib.Api.Helix.Models.Channels.GetChannelVIPs;
using TwitchLib.Api.Helix.Models.Chat.ChatSettings;
using TwitchLib.Api.Helix.Models.Chat.GetChatters;
using TwitchLib.Api.Helix.Models.Clips.CreateClip;
using TwitchLib.Api.Helix.Models.Moderation.BanUser;
using TwitchLib.Api.Helix.Models.Moderation.GetModerators;
using TwitchLib.Api.Helix.Models.Predictions.CreatePrediction;
using TwitchLib.Api.Helix.Models.Predictions.GetPredictions;
using TwitchLib.Api.Helix.Models.Streams.GetStreams;

namespace SkillzBot.API.Twitch
{
    public sealed class TtvAPI
    {
        private static TwitchAPI API;
        private static string PredID;
        private static string winID;
        private static string looseID;
        private static bool ValidToken = false;
        private static string BrodcasterID;
        private static ILogger<TtvAPI> _logger;

        public static void Initialize(ILogger<TtvAPI> logger)
        {
            _logger = logger;
            try
            {
                BrodcasterID = IllSingleton.Config.BroadcasterId;
                Console.Write("Initializing Ttv API... ");

                API = new TwitchAPI();
                API.Settings.ClientId = IllSingleton.Config.TApiClientId;
                API.Settings.AccessToken = IllSingleton.Config.TApiAccessToken;

                if (!StringUtil.IsValidApiToken(API.Settings.ClientId) || !StringUtil.IsValidApiToken(API.Settings.AccessToken))
                {
                    Console.WriteLine("ERROR: Invalid Tokens.");
                    _logger?.LogError("No valid TTV API access token or client ID. TTV API functionality is offline");
                    ValidToken = false;
                    return;
                }
                ValidToken = true;
                Console.WriteLine("OK.");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to initialize TtvAPI");
                ValidToken = false;
            }
        }

        private static bool IsReady()
        {
            if (!ValidToken || API == null)
            {
                _logger?.LogWarning("TTV API was called but it is not ready (invalid token or not initialized).");
                return false;
            }
            return true;
        }

        public static async ValueTask Start_2_Prediction(string Title, string blue, string red, int windowSec)
        {
            if (!IsReady()) return;
            var request = new CreatePredictionRequest
            {
                Title = Title,
                Outcomes = new[]
                {
                    new Outcome { Title = blue },
                    new Outcome { Title = red }
                },
                PredictionWindowSeconds = windowSec,
                BroadcasterId = BrodcasterID
            };
            try
            {
                await API.Helix.Predictions.CreatePredictionAsync(request).ConfigureAwait(false);
                await GetCurrentPred().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Start_2_Prediction");
            }
        }

        public static async ValueTask Start_10_Prediction(List<string> Champs, string Title, int windowSec)
        {
            if (!IsReady()) return;
            if (Champs == null || Champs.Count != 10)
            {
                _logger?.LogError("Champs list must have exactly 10 items.");
                return;
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
                request.Outcomes[i] = new Outcome { Title = Champs[i] };
            }
            try
            {
                await API.Helix.Predictions.CreatePredictionAsync(request).ConfigureAwait(false);
                await GetCurrentPred().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Start_10_Prediction");
            }
        }

        public static async ValueTask Start_5_Prediction(List<string> Champs, string Title, int windowSec)
        {
            if (!IsReady()) return;
            if (Champs == null || Champs.Count != 5)
            {
                _logger?.LogError("Champs list must have exactly 5 items.");
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
                await GetCurrentPred().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Start_5_Prediction");
            }
        }

        public static async Task<string> End_Multy_Prediction(string Champ)
        {
            if (!IsReady()) return "Invalid AccessToken";
            try
            {
                var Predictions = await API.Helix.Predictions.GetPredictionsAsync(BrodcasterID).ConfigureAwait(false);
                if (Predictions.Data.Length == 0) return "ERR";

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
                    await API.Helix.Predictions.EndPredictionAsync(BrodcasterID, PredID, predictionStatus, OutcomeID).ConfigureAwait(false);
                    return "OK";
                }
                else
                {
                    _logger?.LogError("(Task EndPrediction) currentPredID != PredID");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "End_Multy_Prediction");
                return "ERR";
            }
            return "OK";
        }

        private static async Task GetCurrentPred()
        {
            if (!IsReady()) return;
            try
            {
                var Predictions = await API.Helix.Predictions.GetPredictionsAsync(BrodcasterID).ConfigureAwait(false);
                if (Predictions.Data.Length > 0)
                {
                    PredID = Predictions.Data.First().Id;
                    winID = Predictions.Data.First().Outcomes.First().Id;
                    looseID = Predictions.Data.First().Outcomes.Last().Id;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetCurrentPred");
            }
        }

        public static async Task<GetPredictionsResponse> GetCurrentPredPublic()
        {
            if (!IsReady()) return null;
            try
            {
                return await API.Helix.Predictions.GetPredictionsAsync(BrodcasterID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetCurrentPredPublic");
                return null;
            }
        }

        public static async Task End_WinLoose_Prediction(bool win, int tryes)
        {
            if (!IsReady()) return;
            if (tryes > 10)
            {
                _logger.LogError("End_WinLoose_Prediction failed after {Tryes} retries.", tryes);
                return;
            }

            try
            {
                var Predictions = await API.Helix.Predictions.GetPredictionsAsync(BrodcasterID).ConfigureAwait(false);
                if (Predictions.Data.Length == 0) return;

                string currentPredID = Predictions.Data.First().Id;
                if (currentPredID == PredID)
                {
                    var status = TwitchLib.Api.Core.Enums.PredictionEndStatus.RESOLVED;
                    await API.Helix.Predictions.EndPredictionAsync(BrodcasterID, PredID, status, win ? winID : looseID).ConfigureAwait(false);
                }
                else
                {
                    _logger?.LogError("(Task EndPrediction) currentPredID != PredID");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "End_WinLoose_Prediction()");
                await Task.Delay(1000);
                await End_WinLoose_Prediction(win, tryes + 1).ConfigureAwait(false);
            }
        }

        public static async Task CencelePrediction()
        {
            if (!IsReady()) return;
            try
            {
                var Predictions = await API.Helix.Predictions.GetPredictionsAsync(BrodcasterID).ConfigureAwait(false);
                if (Predictions.Data.Length == 0) return;

                string currentPredID = Predictions.Data.First().Id;
                if (currentPredID == PredID)
                {
                    await API.Helix.Predictions.EndPredictionAsync(BrodcasterID, PredID, TwitchLib.Api.Core.Enums.PredictionEndStatus.CANCELED, null).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "CencelePrediction");
            }
        }

        public static async Task<GetCustomRewardsResponse> GetAllRewards()
        {
            if (!IsReady()) return null;
            try
            {
                var AllRewards = await API.Helix.ChannelPoints.GetCustomRewardAsync(BrodcasterID).ConfigureAwait(false);
                return AllRewards;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "getAllRewards");
                return null;
            }
        }

        public static async Task<CustomReward> GetReward(string id)
        {
            if (!IsReady()) return null;
            try
            {
                var rewards = await API.Helix.ChannelPoints.GetCustomRewardAsync(BrodcasterID, new List<string> { id }).ConfigureAwait(false);
                return rewards.Data.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetReward");
                return null;
            }
        }

        public static async Task<CustomReward> GetReward(string title, string OverloadParam)
        {
            if (!IsReady()) return null;
            try
            {
                var rewards = await API.Helix.ChannelPoints.GetCustomRewardAsync(BrodcasterID).ConfigureAwait(false);
                return rewards.Data.FirstOrDefault(r => r.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetReward");
                return null;
            }
        }

        public static async Task UpdateReward(string rewardID, string title, int cost, string prompt, bool enable, bool isUserInputRequired)
        {
            if (!IsReady()) return;
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
                _logger?.LogError(ex, "updateReward");
            }
        }

        public static async Task DeleteReward(string rewardID)
        {
            if (!IsReady()) return;
            try
            {
                await API.Helix.ChannelPoints.DeleteCustomRewardAsync(BrodcasterID, rewardID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "DeleteReward");
            }
        }

        public static async Task<string> CreateReward(string title, int cost, string promt, bool enabled, bool userinput)
        {
            if (!IsReady()) return null;
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
                _logger?.LogError(ex, "CreateReward");
                return null;
            }
        }

        public static async Task CencelReward(string rewardID, string RedemID)
        {
            if (!IsReady()) return;
            try
            {
                await API.Helix.ChannelPoints.UpdateRedemptionStatusAsync(BrodcasterID, rewardID, new List<string> { RedemID }, new UpdateCustomRewardRedemptionStatusRequest
                {
                    Status = TwitchLib.Api.Core.Enums.CustomRewardRedemptionStatus.CANCELED
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "CencelReward");
            }
        }

        public static async Task ApproveReward(string rewardID, string RedemID)
        {
            if (!IsReady()) return;
            try
            {
                await API.Helix.ChannelPoints.UpdateRedemptionStatusAsync(BrodcasterID, rewardID, new List<string> { RedemID }, new UpdateCustomRewardRedemptionStatusRequest
                {
                    Status = TwitchLib.Api.Core.Enums.CustomRewardRedemptionStatus.FULFILLED
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ApproveReward");
            }
        }

        public static async Task<CreatedClipResponse> CreateClip()
        {
            if (!IsReady()) return null;
            try
            {
                return await API.Helix.Clips.CreateClipAsync(BrodcasterID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "CreateClip");
                return null;
            }
        }

        public static async Task<bool> GetStreamStatus()
        {
            if (!IsReady()) return false;
            try
            {
                var streams = await API.Helix.Streams.GetStreamsAsync(null, 1, null, null, new List<string> { BrodcasterID }, null, null);
                return streams != null && streams.Streams.Any();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetStreamStatus()");
                return false;
            }
        }

        public static async Task<string> GetCustomReward(string rewardID, string userID)
        {
            if (!IsReady()) return null;
            try
            {
                var redemption = await API.Helix.ChannelPoints.GetCustomRewardRedemptionAsync(BrodcasterID, rewardID).ConfigureAwait(false);
                return redemption.Data.FirstOrDefault(r => r.UserId == userID)?.Id;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetCustomReward");
                return null;
            }
        }

        private static async Task TimeOutUserAsync(string UserID, int Duration, string Reason)
        {
            if (!IsReady()) return;
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
                _logger?.LogError(ex, "TimeOutUserAsync");
            }
        }

        public static async Task BanUser(string UserID, string Reason)
        {
            if (!IsReady()) return;
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
                _logger?.LogError(ex, "BanUser");
            }
        }

        public static async Task UnBanUser(string UserID)
        {
            if (!IsReady()) return;
            try
            {
                await API.Helix.Moderation.UnbanUserAsync(BrodcasterID, BrodcasterID, UserID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "UnBanUser");
            }
        }

        public static async Task<bool> Announce(string Message)
        {
            if (!IsReady()) return false;
            try
            {
                await API.Helix.Chat.SendChatAnnouncementAsync(BrodcasterID, BrodcasterID, Message).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Announce");
                return false;
            }
        }

        public static async Task DeleteMessage(string MessageID)
        {
            if (!IsReady()) return;
            try
            {
                await API.Helix.Moderation.DeleteChatMessagesAsync(BrodcasterID, BrodcasterID, MessageID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "DeleteMessage");
            }
        }

        public static async Task DeleteAllMessages()
        {
            if (!IsReady()) return;
            try
            {
                await API.Helix.Moderation.DeleteChatMessagesAsync(BrodcasterID, BrodcasterID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "DeleteAllMessages");
            }
        }

        public static async Task<bool> AddChannelModerator(string UserID)
        {
            if (!IsReady()) return true;
            try
            {
                await API.Helix.Moderation.AddChannelModeratorAsync(BrodcasterID, UserID).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "AddChannelModerator");
                return false;
            }
        }

        public static async Task DisableRewardAsync(string rewardID)
        {
            if (!IsReady()) return;
            var reward = await GetReward(rewardID).ConfigureAwait(false);
            if (reward != null)
            {
                await UpdateReward(reward.Id, reward.Title, reward.Cost, reward.Prompt, false, reward.IsUserInputRequired).ConfigureAwait(false);
            }
            else
                _logger?.LogError("DisableRewardAsync -> null. Id: {RewardID}", rewardID);
        }

        public static async Task EnableRewardAsync(string rewardID)
        {
            if (!IsReady()) return;
            var reward = await GetReward(rewardID).ConfigureAwait(false);
            if (reward != null)
            {
                await UpdateReward(reward.Id, reward.Title, reward.Cost, reward.Prompt, true, reward.IsUserInputRequired).ConfigureAwait(false);
            }
            else
                _logger?.LogError("EnableRewardAsync -> null. Id: {RewardID}", rewardID);
        }

        public static async Task DeleteChannelModerator(string UserID)
        {
            if (!IsReady()) return;
            try
            {
                await API.Helix.Moderation.DeleteChannelModeratorAsync(BrodcasterID, UserID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "DeleteChannelModerator");
            }
        }

        public static async Task<GetChannelVIPsResponse> GetVIPs()
        {
            if (!IsReady()) return null;
            try
            {
                return await API.Helix.Channels.GetVIPsAsync(BrodcasterID, null, 100).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetVIPs");
                return null;
            }
        }

        public static async Task AddChannelVIP(string UserID)
        {
            if (!IsReady()) return;
            try
            {
                await API.Helix.Channels.AddChannelVIPAsync(BrodcasterID, UserID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "AddChannelVIP");
            }
        }

        public static async Task DeleteChannelVIP(string UserID)
        {
            if (!IsReady()) return;
            try
            {
                await API.Helix.Channels.RemoveChannelVIPAsync(BrodcasterID, UserID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "DeleteChannelVIP");
            }
        }

        public static async Task SetEmoteOnlyMode(bool IsEmoteOnly)
        {
            if (!IsReady()) return;
            try
            {
                await API.Helix.Chat.UpdateChatSettingsAsync(BrodcasterID, BrodcasterID, new ChatSettings
                {
                    EmoteMode = IsEmoteOnly
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SetEmoteOnlyMode");
            }
        }

        public static async Task TimeOutUser(UserObject user, int Duration, string Reasone)
        {
            if (!IsReady()) return;
            if (user.isMod == 1) return;
            try
            {
                await TimeOutUserAsync(user.TwitchID.ToString(), Duration, Reasone).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "TimeOutUser");
            }
        }

        public static async Task TimeOutModerator(UserObject user, int Duration, string Reasone)
        {
            if (!IsReady()) return;
            try
            {
                await TimeOutUserAsync(user.TwitchID.ToString(), Duration, Reasone).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "TimeOutModerator");
            }
        }

        public static async Task SendWhisper(string toUserID, string message, bool newRec = true)
        {
            if (!IsReady()) return;
            try
            {
                await API.Helix.Whispers.SendWhisperAsync(BrodcasterID, toUserID, message, newRec).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SendWhisper");
            }
        }

        public static async Task<Moderator[]> GetAllMods()
        {
            if (!IsReady()) return null;
            try
            {
                var Responce = await API.Helix.Moderation.GetModeratorsAsync(BrodcasterID, null, 100).ConfigureAwait(false);
                return Responce.Data;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetAllMods");
                return null;
            }
        }

        public static async Task<Stream> GetStreamInfo()
        {
            if (!IsReady()) return null;
            try
            {
                var responce = await API.Helix.Streams.GetStreamsAsync(null, 1, null, null, new List<string> { BrodcasterID }).ConfigureAwait(false);
                return responce.Streams?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetStreamInfo");
                return null;
            }
        }

        public static async Task<ChannelInformation> GetChannelInformationAsync()
        {
            if (!IsReady()) return null;
            try
            {
                var responce = await API.Helix.Channels.GetChannelInformationAsync(BrodcasterID).ConfigureAwait(false);
                return responce.Data?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetChannelInformationAsync");
                return null;
            }
        }

        public static async Task<GetChattersResponse> GetChattersAsync()
        {
            if (!IsReady()) return null;
            try
            {
                return await API.Helix.Chat.GetChattersAsync(BrodcasterID, BrodcasterID);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetChattersAsync");
                return null;
            }
        }

        public static async Task<bool> CheckClipExistence(string clipID)
        {
            if (!IsReady()) return false;
            try
            {
                var clips = await API.Helix.Clips.GetClipsAsync(new List<string> { clipID }).ConfigureAwait(false);
                if (clips.Clips.Length == 0 || clips.Clips[0].BroadcasterId != BrodcasterID)
                    return false;
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "CheckClipExistence");
                return false;
            }
        }

        public static async Task<string> GetUsetIDByName(string UserLogin)
        {
            if (!IsReady()) return null;
            try
            {
                List<string> list = new List<string> { UserLogin };
                var Responce = await API.Helix.Users.GetUsersAsync(null, list).ConfigureAwait(false);
                return Responce.Users?.FirstOrDefault()?.Id;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetUsetIDByName");
                return null;
            }
        }
    }
}