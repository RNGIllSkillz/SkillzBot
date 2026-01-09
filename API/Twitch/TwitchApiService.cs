using Microsoft.Extensions.Logging;
using SkillzBot.MODELS;
using SkillzBot.Singleton;
using SkillzBot.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Helix.Models.ChannelPoints;
using TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomReward;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomRewardRedemptionStatus;
using TwitchLib.Api.Helix.Models.Chat.ChatSettings;
using TwitchLib.Api.Helix.Models.Moderation.BanUser;
using TwitchLib.Api.Helix.Models.Predictions.CreatePrediction;

namespace SkillzBot.API.Twitch
{
    public class TwitchApiService : ITwitchService
    {
        private readonly TwitchAPI _api;
        private readonly ILogger<TwitchApiService> _logger;

        // Internal State matching original static fields
        private string _predID;
        private string _winID;
        private string _looseID;
        private readonly string _broadcasterID;
        private readonly bool _isValidToken;

        public TwitchApiService(ILogger<TwitchApiService> logger)
        {
            _logger = logger;
            _broadcasterID = IllSingleton.Config.BroadcasterId;

            _logger.LogInformation("Initializing Twitch API Service...");

            _api = new TwitchAPI();
            _api.Settings.ClientId = IllSingleton.Config.TApiClientId;
            _api.Settings.AccessToken = IllSingleton.Config.TApiAccessToken;

            if (!StringUtil.IsValidApiToken(_api.Settings.ClientId) || !StringUtil.IsValidApiToken(_api.Settings.AccessToken))
            {
                _logger.LogError("ERROR: Invalid Tokens. Twitch API functionality is offline.");
                _isValidToken = false;
            }
            else
            {
                _isValidToken = true;
                _logger.LogInformation("Twitch API Service Initialized. OK.");
            }
        }

        public bool IsReady()
        {
            if (!_isValidToken || _api == null)
            {
                _logger.LogWarning("Twitch API call attempted but service is not ready.");
                return false;
            }
            return true;
        }

        #region Predictions

        // Matches GetCurrentPred()
        private async Task GetCurrentPred()
        {
            if (!IsReady()) return;
            try
            {
                var predictions = await _api.Helix.Predictions.GetPredictionsAsync(_broadcasterID).ConfigureAwait(false);
                if (predictions.Data.Length > 0)
                {
                    var current = predictions.Data.First();
                    _predID = current.Id;
                    _winID = current.Outcomes.First().Id;
                    _looseID = current.Outcomes.Last().Id;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetCurrentPred");
            }
        }

        public async ValueTask Start_2_Prediction(string title, string blue, string red, int windowSec)
        {
            if (!IsReady()) return;
            var request = new CreatePredictionRequest
            {
                Title = title,
                Outcomes = new[]
                {
                    new Outcome { Title = blue },
                    new Outcome { Title = red }
                },
                PredictionWindowSeconds = windowSec,
                BroadcasterId = _broadcasterID
            };
            try
            {
                await _api.Helix.Predictions.CreatePredictionAsync(request).ConfigureAwait(false);
                await GetCurrentPred().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Start_2_Prediction");
            }
        }

        public async ValueTask Start_10_Prediction(List<string> champs, string title, int windowSec)
        {
            if (!IsReady()) return;
            if (champs == null || champs.Count != 10)
            {
                _logger.LogError("Champs list must have exactly 10 items.");
                return;
            }
            var request = new CreatePredictionRequest
            {
                Title = title,
                Outcomes = new Outcome[10],
                PredictionWindowSeconds = windowSec,
                BroadcasterId = _broadcasterID
            };
            for (int i = 0; i < 10; i++)
            {
                request.Outcomes[i] = new Outcome { Title = champs[i] };
            }
            try
            {
                await _api.Helix.Predictions.CreatePredictionAsync(request).ConfigureAwait(false);
                await GetCurrentPred().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Start_10_Prediction");
            }
        }

        public async ValueTask Start_5_Prediction(List<string> champs, string title, int windowSec)
        {
            if (!IsReady()) return;
            if (champs == null || champs.Count != 5)
            {
                _logger.LogError("Champs list must have exactly 5 items.");
                return;
            }

            var request = new CreatePredictionRequest
            {
                Title = title,
                Outcomes = new Outcome[5],
                PredictionWindowSeconds = windowSec,
                BroadcasterId = _broadcasterID
            };

            for (int i = 0; i < 5; i++)
            {
                request.Outcomes[i] = new Outcome { Title = champs[i] };
            }
            try
            {
                await _api.Helix.Predictions.CreatePredictionAsync(request).ConfigureAwait(false);
                await GetCurrentPred().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Start_5_Prediction");
            }
        }

        public async Task<string> End_Multy_Prediction(string champ)
        {
            if (!IsReady()) return "Invalid AccessToken";
            try
            {
                var predictions = await _api.Helix.Predictions.GetPredictionsAsync(_broadcasterID).ConfigureAwait(false);
                if (predictions.Data.Length == 0) return "ERR";

                string currentPredID = predictions.Data.First().Id;
                var predictionStatus = PredictionEndStatus.RESOLVED;
                string outcomeID = "";

                var outcomes = predictions.Data.First().Outcomes;
                foreach (var outcome in outcomes)
                {
                    if (outcome.Title == champ)
                    {
                        outcomeID = outcome.Id;
                    }
                }

                if (currentPredID == _predID)
                {
                    await _api.Helix.Predictions.EndPredictionAsync(_broadcasterID, _predID, predictionStatus, outcomeID).ConfigureAwait(false);
                    return "OK";
                }
                else
                {
                    _logger.LogError("(Task EndPrediction) currentPredID != PredID");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "End_Multy_Prediction");
                return "ERR";
            }
            return "OK";
        }

        public async Task End_WinLoose_Prediction(bool win, int tryes = 0)
        {
            if (!IsReady()) return;
            if (tryes > 10)
            {
                _logger.LogError("End_WinLoose_Prediction failed after {Tryes} retries.", tryes);
                return;
            }

            try
            {
                var predictions = await _api.Helix.Predictions.GetPredictionsAsync(_broadcasterID).ConfigureAwait(false);
                if (predictions.Data.Length == 0) return;

                string currentPredID = predictions.Data.First().Id;
                if (currentPredID == _predID)
                {
                    var status = PredictionEndStatus.RESOLVED;
                    await _api.Helix.Predictions.EndPredictionAsync(_broadcasterID, _predID, status, win ? _winID : _looseID).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogError("(Task EndPrediction) currentPredID != PredID");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "End_WinLoose_Prediction()");
                await Task.Delay(1000);
                await End_WinLoose_Prediction(win, tryes + 1).ConfigureAwait(false);
            }
        }

        public async Task CencelePrediction()
        {
            if (!IsReady()) return;
            try
            {
                var predictions = await _api.Helix.Predictions.GetPredictionsAsync(_broadcasterID).ConfigureAwait(false);
                if (predictions.Data.Length == 0) return;

                string currentPredID = predictions.Data.First().Id;
                if (currentPredID == _predID)
                {
                    await _api.Helix.Predictions.EndPredictionAsync(_broadcasterID, _predID, PredictionEndStatus.CANCELED).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CencelePrediction");
            }
        }

        public async Task<TwitchLib.Api.Helix.Models.Predictions.GetPredictions.GetPredictionsResponse> GetCurrentPredPublic()
        {
            if (!IsReady()) return null;
            try
            {
                return await _api.Helix.Predictions.GetPredictionsAsync(_broadcasterID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetCurrentPredPublic");
                return null;
            }
        }

        #endregion

        #region Rewards

        public async Task<TwitchLib.Api.Helix.Models.ChannelPoints.GetCustomReward.GetCustomRewardsResponse> GetAllRewards()
        {
            if (!IsReady()) return null;
            try
            {
                return await _api.Helix.ChannelPoints.GetCustomRewardAsync(_broadcasterID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "getAllRewards");
                return null;
            }
        }

        public async Task<TwitchLib.Api.Helix.Models.ChannelPoints.CustomReward> GetReward(string id)
        {
            if (!IsReady()) return null;
            try
            {
                var rewards = await _api.Helix.ChannelPoints.GetCustomRewardAsync(_broadcasterID, new List<string> { id }).ConfigureAwait(false);
                return rewards.Data.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetReward(id)");
                return null;
            }
        }

        public async Task<TwitchLib.Api.Helix.Models.ChannelPoints.CustomReward> GetReward(string title, string overloadParam)
        {
            if (!IsReady()) return null;
            try
            {
                var rewards = await _api.Helix.ChannelPoints.GetCustomRewardAsync(_broadcasterID).ConfigureAwait(false);
                return rewards.Data.FirstOrDefault(r => r.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetReward(title)");
                return null;
            }
        }

        public async Task UpdateReward(string rewardID, string title, int cost, string prompt, bool enable, bool isUserInputRequired)
        {
            if (!IsReady()) return;
            try
            {
                await _api.Helix.ChannelPoints.UpdateCustomRewardAsync(_broadcasterID, rewardID, new UpdateCustomRewardRequest
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
                _logger.LogError(ex, "updateReward");
            }
        }

        public async Task DeleteReward(string rewardID)
        {
            if (!IsReady()) return;
            try
            {
                await _api.Helix.ChannelPoints.DeleteCustomRewardAsync(_broadcasterID, rewardID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteReward");
            }
        }

        public async Task<string> CreateReward(string title, int cost, string prompt, bool enabled, bool userinput)
        {
            if (!IsReady()) return null;
            try
            {
                var response = await _api.Helix.ChannelPoints.CreateCustomRewardsAsync(_broadcasterID, new CreateCustomRewardsRequest
                {
                    Title = title,
                    Cost = cost,
                    Prompt = prompt,
                    IsEnabled = enabled,
                    IsUserInputRequired = userinput,
                    ShouldRedemptionsSkipRequestQueue = false
                }).ConfigureAwait(false);
                return response.Data.FirstOrDefault()?.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateReward");
                return null;
            }
        }

        public async Task CencelReward(string rewardID, string redemID)
        {
            if (!IsReady()) return;
            try
            {
                await _api.Helix.ChannelPoints.UpdateRedemptionStatusAsync(_broadcasterID, rewardID, new List<string> { redemID }, new UpdateCustomRewardRedemptionStatusRequest
                {
                    Status = CustomRewardRedemptionStatus.CANCELED
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CencelReward");
            }
        }

        public async Task ApproveReward(string rewardID, string redemID)
        {
            if (!IsReady()) return;
            try
            {
                await _api.Helix.ChannelPoints.UpdateRedemptionStatusAsync(_broadcasterID, rewardID, new List<string> { redemID }, new UpdateCustomRewardRedemptionStatusRequest
                {
                    Status = CustomRewardRedemptionStatus.FULFILLED
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ApproveReward");
            }
        }

        public async Task<string> GetCustomReward(string rewardID, string userID)
        {
            if (!IsReady()) return null;
            try
            {
                var redemption = await _api.Helix.ChannelPoints.GetCustomRewardRedemptionAsync(_broadcasterID, rewardID).ConfigureAwait(false);
                return redemption.Data.FirstOrDefault(r => r.UserId == userID)?.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetCustomReward");
                return null;
            }
        }

        public async Task DisableRewardAsync(string rewardID)
        {
            if (!IsReady()) return;
            var reward = await GetReward(rewardID).ConfigureAwait(false);
            if (reward != null)
            {
                await UpdateReward(reward.Id, reward.Title, reward.Cost, reward.Prompt, false, reward.IsUserInputRequired).ConfigureAwait(false);
            }
            else
                _logger.LogError("DisableRewardAsync -> null. Id: {RewardID}", rewardID);
        }

        public async Task EnableRewardAsync(string rewardID)
        {
            if (!IsReady()) return;
            var reward = await GetReward(rewardID).ConfigureAwait(false);
            if (reward != null)
            {
                await UpdateReward(reward.Id, reward.Title, reward.Cost, reward.Prompt, true, reward.IsUserInputRequired).ConfigureAwait(false);
            }
            else
                _logger.LogError("EnableRewardAsync -> null. Id: {RewardID}", rewardID);
        }

        #endregion

        #region User Management & Chat

        // Corresponds to internal TimeOutUserAsync in original
        private async Task PerformTimeOutUserAsync(string userId, int duration, string reason)
        {
            if (!IsReady()) return;
            try
            {
                await _api.Helix.Moderation.BanUserAsync(_broadcasterID, _broadcasterID, new BanUserRequest
                {
                    UserId = userId,
                    Duration = duration,
                    Reason = reason
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TimeOutUserAsync");
            }
        }

        public async Task TimeOutUser(UserObject user, int duration, string reason)
        {
            if (!IsReady()) return;
            if (user.isMod == 1) return;
            try
            {
                await PerformTimeOutUserAsync(user.TwitchID.ToString(), duration, reason).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TimeOutUser");
            }
        }

        public async Task TimeOutModerator(UserObject user, int duration, string reason)
        {
            if (!IsReady()) return;
            try
            {
                await PerformTimeOutUserAsync(user.TwitchID.ToString(), duration, reason).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TimeOutModerator");
            }
        }

        public async Task BanUser(string userID, string reason)
        {
            if (!IsReady()) return;
            try
            {
                await _api.Helix.Moderation.BanUserAsync(_broadcasterID, _broadcasterID, new BanUserRequest
                {
                    UserId = userID,
                    Reason = reason
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BanUser");
            }
        }

        public async Task UnBanUser(string userID)
        {
            if (!IsReady()) return;
            try
            {
                await _api.Helix.Moderation.UnbanUserAsync(_broadcasterID, _broadcasterID, userID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UnBanUser");
            }
        }

        public async Task<bool> AddChannelModerator(string userID)
        {
            if (!IsReady()) return true; // Original logic returns true on IsReady check failure? Preserving behavior.
            try
            {
                await _api.Helix.Moderation.AddChannelModeratorAsync(_broadcasterID, userID).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddChannelModerator");
                return false;
            }
        }

        public async Task DeleteChannelModerator(string userID)
        {
            if (!IsReady()) return;
            try
            {
                await _api.Helix.Moderation.DeleteChannelModeratorAsync(_broadcasterID, userID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteChannelModerator");
            }
        }

        public async Task<TwitchLib.Api.Helix.Models.Moderation.GetModerators.Moderator[]> GetAllMods()
        {
            if (!IsReady()) return null;
            try
            {
                var response = await _api.Helix.Moderation.GetModeratorsAsync(_broadcasterID, null, 100).ConfigureAwait(false);
                return response.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAllMods");
                return null;
            }
        }

        public async Task<string> GetUsetIDByName(string userLogin)
        {
            if (!IsReady()) return null;
            try
            {
                var response = await _api.Helix.Users.GetUsersAsync(null, new List<string> { userLogin }).ConfigureAwait(false);
                return response.Users?.FirstOrDefault()?.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetUsetIDByName");
                return null;
            }
        }

        public async Task SendWhisper(string toUserID, string message, bool newRec = true)
        {
            if (!IsReady()) return;
            try
            {
                await _api.Helix.Whispers.SendWhisperAsync(_broadcasterID, toUserID, message, newRec).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SendWhisper");
            }
        }

        public async Task<TwitchLib.Api.Helix.Models.Channels.GetChannelVIPs.GetChannelVIPsResponse> GetVIPs()
        {
            if (!IsReady()) return null;
            try
            {
                return await _api.Helix.Channels.GetVIPsAsync(_broadcasterID, null, 100).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetVIPs");
                return null;
            }
        }

        public async Task AddChannelVIP(string userID)
        {
            if (!IsReady()) return;
            try
            {
                await _api.Helix.Channels.AddChannelVIPAsync(_broadcasterID, userID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddChannelVIP");
            }
        }

        public async Task DeleteChannelVIP(string userID)
        {
            if (!IsReady()) return;
            try
            {
                await _api.Helix.Channels.RemoveChannelVIPAsync(_broadcasterID, userID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteChannelVIP");
            }
        }

        #endregion

        #region Stream, Chat & Clips

        public async Task<bool> GetStreamStatus()
        {
            if (!IsReady()) return false;
            try
            {
                var streams = await _api.Helix.Streams.GetStreamsAsync(null, 1, null, null, new List<string> { _broadcasterID }, null, null);
                return streams != null && streams.Streams.Any();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetStreamStatus()");
                return false;
            }
        }

        public async Task<TwitchLib.Api.Helix.Models.Streams.GetStreams.Stream> GetStreamInfo()
        {
            if (!IsReady()) return null;
            try
            {
                var response = await _api.Helix.Streams.GetStreamsAsync(null, 1, null, null, new List<string> { _broadcasterID }).ConfigureAwait(false);
                return response.Streams?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetStreamInfo");
                return null;
            }
        }

        public async Task<TwitchLib.Api.Helix.Models.Channels.GetChannelInformation.ChannelInformation> GetChannelInformationAsync()
        {
            if (!IsReady()) return null;
            try
            {
                var response = await _api.Helix.Channels.GetChannelInformationAsync(_broadcasterID).ConfigureAwait(false);
                return response.Data?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetChannelInformationAsync");
                return null;
            }
        }

        public async Task<TwitchLib.Api.Helix.Models.Chat.GetChatters.GetChattersResponse> GetChattersAsync()
        {
            if (!IsReady()) return null;
            try
            {
                return await _api.Helix.Chat.GetChattersAsync(_broadcasterID, _broadcasterID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetChattersAsync");
                return null;
            }
        }

        public async Task DeleteMessage(string messageID)
        {
            if (!IsReady()) return;
            try
            {
                await _api.Helix.Moderation.DeleteChatMessagesAsync(_broadcasterID, _broadcasterID, messageID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteMessage");
            }
        }

        public async Task DeleteAllMessages()
        {
            if (!IsReady()) return;
            try
            {
                await _api.Helix.Moderation.DeleteChatMessagesAsync(_broadcasterID, _broadcasterID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteAllMessages");
            }
        }

        public async Task<bool> Announce(string message)
        {
            if (!IsReady()) return false;
            try
            {
                await _api.Helix.Chat.SendChatAnnouncementAsync(_broadcasterID, _broadcasterID, message).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Announce");
                return false;
            }
        }

        public async Task<TwitchLib.Api.Helix.Models.Clips.CreateClip.CreatedClipResponse> CreateClip()
        {
            if (!IsReady()) return null;
            try
            {
                return await _api.Helix.Clips.CreateClipAsync(_broadcasterID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateClip");
                return null;
            }
        }

        public async Task<bool> CheckClipExistence(string clipID)
        {
            if (!IsReady()) return false;
            try
            {
                var clips = await _api.Helix.Clips.GetClipsAsync(new List<string> { clipID }).ConfigureAwait(false);
                if (clips.Clips.Length == 0 || clips.Clips[0].BroadcasterId != _broadcasterID)
                    return false;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CheckClipExistence");
                return false;
            }
        }

        public async Task SetEmoteOnlyMode(bool isEmoteOnly)
        {
            if (!IsReady()) return;
            try
            {
                await _api.Helix.Chat.UpdateChatSettingsAsync(_broadcasterID, _broadcasterID, new ChatSettings
                {
                    EmoteMode = isEmoteOnly
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SetEmoteOnlyMode");
            }
        }
        #endregion
    }
}