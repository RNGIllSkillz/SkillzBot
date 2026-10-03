using Microsoft.Extensions.Logging;
using SkillzBot.IllConfiguration;
using SkillzBot.Interfaces;
using SkillzBot.MODELS;
using SkillzBot.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Core.Exceptions;
using TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomReward;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomRewardRedemptionStatus;
using TwitchLib.Api.Helix.Models.Chat.ChatSettings;
using TwitchLib.Api.Helix.Models.Moderation.BanUser;
using TwitchLib.Api.Helix.Models.Polls.CreatePoll;
using TwitchLib.Api.Helix.Models.Predictions.CreatePrediction;

namespace SkillzBot.API.Twitch
{
    public class TwitchApiService : ITwitchService
    {
        private readonly TwitchAPI _api;
        private readonly ILogger<TwitchApiService> _logger;
        private readonly BotConfigModel _config;

        private string _predID;
        private string _winID;
        private string _looseID;
        private readonly string _broadcasterID;
        private readonly bool _isValidToken;

        // Fail fast setting: 5 seconds
        private readonly TimeSpan _apiTimeout = TimeSpan.FromSeconds(5);

        public TwitchApiService(BotConfigModel config, ILogger<TwitchApiService> logger)
        {
            _config = config;
            _logger = logger;
            _broadcasterID = _config.BroadcasterId;

            _logger.LogInformation("Initializing Twitch API Service...");

            _api = new TwitchAPI();
            _api.Settings.ClientId = _config.TApiClientId;
            _api.Settings.AccessToken = _config.TApiAccessToken;

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

        private async Task ExecuteWithRetryAsync(Func<Task> action, string operationName)
        {
            int retries = 0;
            const int maxRetries = 3;

            while (retries <= maxRetries)
            {
                try
                {
                    await action().WaitAsync(_apiTimeout);
                    return; 
                }
                catch (TooManyRequestsException)
                {
                    retries++;
                    if (retries > maxRetries) throw;
                    _logger.LogWarning("429 Too Many Requests in {Operation}. Waiting 2s...", operationName);
                }
                // 1. TIMEOUTS
                catch (TimeoutException)
                {
                    retries++;
                    if (retries > maxRetries)
                    {
                        _logger.LogError("Twitch API timed out locally ({Timeout}s) in {Operation} after {Retries} retries.", _apiTimeout.TotalSeconds, operationName, retries);
                        break;
                    }
                    _logger.LogWarning("Twitch API slow response (>5s). Retrying {Count}...", retries);
                }
                // 3. CANCELLED REQUESTS
                catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
                {
                    retries++;
                    if (retries > maxRetries)
                    {
                        _logger.LogError("Twitch API Connection Dropped in {Operation}.", operationName);
                        break;
                    }
                    _logger.LogWarning("Twitch API Request Canceled/Dropped. Retrying {Count}...", retries);
                    await Task.Delay(1000);
                }
                // 4. HTTP ERRORS
                catch (System.Net.Http.HttpRequestException ex)
                {
                    retries++;
                    if (retries > maxRetries)
                    {
                        _logger.LogError("HTTP Error in {Operation}: {Message}", operationName, ex.Message);
                        break;
                    }
                    _logger.LogWarning("Twitch API HTTP Error. Retrying {Count}...", retries);
                    await Task.Delay(1000);
                }
                // 5. NOT FOUND (Resource gone)
                catch (BadResourceException ex)
                {
                    if (retries > 0)
                    {
                        _logger.LogWarning("BadResourceException during retry for {Operation}. Assuming success. Error: {Msg}", operationName, ex.Message);
                        break;
                    }
                    _logger.LogWarning("Bad Resource (404) in {Operation}: {Msg}", operationName, ex.Message);
                    break;
                }
                // 6a. MISSING SCOPE
                catch (BadScopeException ex)
                {
                    _logger.LogError("Twitch token lacks the scope required for {Operation}: {Msg}", operationName, ex.Message);
                    break;
                }
                // 6. FORBIDDEN (Ownership issues / Bad Token)
                catch (BadTokenException ex)
                {
                    _logger.LogWarning("Skipping {Operation}: Bot does not own this reward or Token invalid. ({Msg})", operationName, ex.Message);
                    break;
                }
                // 7. BAD REQUEST (Invalid args)
                catch (BadRequestException ex)
                {
                    if (ex.Message.Contains("may not be banned/timed out") ||
                        ex.Message.Contains("user is banned") ||
                        ex.Message.Contains("cannot be banned"))
                    {
                        _logger.LogWarning("Twitch API Action Skipped: Target cannot be banned/timed out (already banned or is mod/broadcaster). Operation: {Operation}", operationName);
                        break; 
                    }

                    _logger.LogError(ex, "Bad Request in {Operation}: {Msg}", operationName, ex.Message);
                    break;
                }
                // 8. UNKNOWN
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected Error in {Operation}", operationName);
                    break;
                }
                await Task.Delay(2000);
            }
        }           
        

        #region Predictions

        private async Task GetCurrentPred()
        {
            if (!IsReady()) return;
            await ExecuteWithRetryAsync(async () =>
            {
                var predictions = await _api.Helix.Predictions.GetPredictionsAsync(_broadcasterID).WaitAsync(_apiTimeout);
                if (predictions.Data.Length > 0)
                {
                    var current = predictions.Data.First();
                    _predID = current.Id;
                    _winID = current.Outcomes.First().Id;
                    _looseID = current.Outcomes.Last().Id;
                }
            }, "GetCurrentPred");
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

            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.Predictions.CreatePredictionAsync(request).WaitAsync(_apiTimeout);
            }, "Start_2_Prediction");

            await GetCurrentPred();
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

            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.Predictions.CreatePredictionAsync(request).WaitAsync(_apiTimeout);
            }, "Start_10_Prediction");

            await GetCurrentPred();
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

            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.Predictions.CreatePredictionAsync(request).WaitAsync(_apiTimeout);
            }, "Start_5_Prediction");

            await GetCurrentPred();
        }

        public async Task<string> End_Multy_Prediction(string champ)
        {
            if (!IsReady()) return "Invalid AccessToken";
            string result = "ERR";

            await ExecuteWithRetryAsync(async () =>
            {
                var predictions = await _api.Helix.Predictions.GetPredictionsAsync(_broadcasterID).WaitAsync(_apiTimeout);
                if (predictions.Data.Length == 0) return;

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

                if (currentPredID == _predID && !string.IsNullOrEmpty(outcomeID))
                {
                    await _api.Helix.Predictions.EndPredictionAsync(_broadcasterID, _predID, predictionStatus, outcomeID).WaitAsync(_apiTimeout);
                    result = "OK";
                }
                else
                {
                    _logger.LogError("(Task EndPrediction) currentPredID != PredID or Outcome not found");
                }
            }, "End_Multy_Prediction");

            return result;
        }

        public async Task End_WinLoose_Prediction(bool win, int tryes = 0)
        {
            if (!IsReady()) return;

            await ExecuteWithRetryAsync(async () =>
            {
                var predictions = await _api.Helix.Predictions.GetPredictionsAsync(_broadcasterID).WaitAsync(_apiTimeout);
                if (predictions.Data.Length == 0) return;

                string currentPredID = predictions.Data.First().Id;
                if (currentPredID == _predID)
                {
                    var status = PredictionEndStatus.RESOLVED;
                    await _api.Helix.Predictions.EndPredictionAsync(_broadcasterID, _predID, status, win ? _winID : _looseID).WaitAsync(_apiTimeout);
                }
                else
                {
                    _logger.LogError("(Task EndPrediction) currentPredID != PredID");
                }
            }, "End_WinLoose_Prediction");
        }

        public async Task CencelePrediction()
        {
            if (!IsReady()) return;
            await ExecuteWithRetryAsync(async () =>
            {
                var predictions = await _api.Helix.Predictions.GetPredictionsAsync(_broadcasterID).WaitAsync(_apiTimeout);
                if (predictions.Data.Length == 0) return;

                string currentPredID = predictions.Data.First().Id;
                if (currentPredID == _predID)
                {
                    await _api.Helix.Predictions.EndPredictionAsync(_broadcasterID, _predID, PredictionEndStatus.CANCELED).WaitAsync(_apiTimeout);
                }
            }, "CencelePrediction");
        }

        public async Task<TwitchLib.Api.Helix.Models.Predictions.GetPredictions.GetPredictionsResponse> GetCurrentPredPublic()
        {
            if (!IsReady()) return null;
            try
            {
                return await _api.Helix.Predictions.GetPredictionsAsync(_broadcasterID).WaitAsync(_apiTimeout);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetCurrentPredPublic");
                return null;
            }
        }

        public string CurrentPredictionId => _predID;

        public async Task<PredictionStatus?> GetPredictionStatusAsync(string predictionId)
        {
            if (!IsReady() || string.IsNullOrEmpty(predictionId)) return null;
            try
            {
                var response = await _api.Helix.Predictions.GetPredictionsAsync(_broadcasterID, new List<string> { predictionId }).WaitAsync(_apiTimeout);
                return response?.Data?.FirstOrDefault(p => p.Id == predictionId)?.Status;
            }
            catch (BadResourceException)
            {
                return null; // 404: the prediction does not exist
            }
            catch (Exception ex)
            {
                // Transient failure: let the caller decide to retry instead of treating it as "not found".
                _logger.LogError(ex, "GetPredictionStatus({PredictionId})", predictionId);
                throw;
            }
        }

        public async Task<bool> AdoptPredictionAsync(string predictionId)
        {
            if (!IsReady() || string.IsNullOrEmpty(predictionId)) return false;
            try
            {
                var response = await _api.Helix.Predictions.GetPredictionsAsync(_broadcasterID, new List<string> { predictionId }).WaitAsync(_apiTimeout);
                var prediction = response?.Data?.FirstOrDefault(p => p.Id == predictionId);
                if (prediction == null) return false;
                if (prediction.Status != PredictionStatus.ACTIVE && prediction.Status != PredictionStatus.LOCKED) return false;

                _predID = prediction.Id;
                _winID = prediction.Outcomes.First().Id;
                _looseID = prediction.Outcomes.Last().Id;
                _logger.LogInformation("Adopted prediction {PredictionId} ({Status}) with {Count} outcomes.", prediction.Id, prediction.Status, prediction.Outcomes.Length);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AdoptPrediction({PredictionId})", predictionId);
                return false;
            }
        }

        public async Task<string> CreatePollAsync(string title, IReadOnlyList<string> choices, int durationSec)
        {
            if (!IsReady()) return null;
            if (choices == null || choices.Count < 2 || choices.Count > 5)
            {
                _logger.LogError("A poll needs 2 to 5 choices; got {Count}.", choices?.Count ?? 0);
                return null;
            }

            string pollId = null;
            await ExecuteWithRetryAsync(async () =>
            {
                var response = await _api.Helix.Polls.CreatePollAsync(new CreatePollRequest
                {
                    BroadcasterId = _broadcasterID,
                    Title = title.Length > 60 ? title.Substring(0, 60) : title,
                    Choices = choices.Select(c => new TwitchLib.Api.Helix.Models.Polls.CreatePoll.Choice { Title = c.Length > 25 ? c.Substring(0, 25) : c }).ToArray(),
                    DurationSeconds = Math.Clamp(durationSec, 15, 1800),
                    ChannelPointsVotingEnabled = false,
                });
                pollId = response?.Data?.FirstOrDefault()?.Id;
            }, "CreatePoll");
            return pollId;
        }

        public async Task<PollResult> GetPollAsync(string pollId)
        {
            if (!IsReady() || string.IsNullOrEmpty(pollId)) return null;
            try
            {
                var response = await _api.Helix.Polls.GetPollsAsync(_broadcasterID, new List<string> { pollId }).WaitAsync(_apiTimeout);
                var poll = response?.Data?.FirstOrDefault();
                if (poll == null) return null;
                var choices = poll.Choices.Select(c => new PollChoiceResult(c.Title, c.Votes)).ToList();
                return new PollResult(poll.Id, poll.Status, choices);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetPoll({PollId})", pollId);
                return null;
            }
        }

        #endregion

        #region Rewards

        public async Task<List<string>> DisableAllRewardsSafeAsync(string exceptionRewardId)
        {
            if (!IsReady()) return new List<string>();
            var successfullyDisabledIds = new List<string>();

            try
            {
                var allRewards = await _api.Helix.ChannelPoints.GetCustomRewardAsync(_broadcasterID).WaitAsync(_apiTimeout);
                if (allRewards?.Data == null) return successfullyDisabledIds;

                foreach (var reward in allRewards.Data)
                {
                    if (reward.Id == exceptionRewardId) continue;
                    if (!reward.IsEnabled) continue;

                    await ExecuteWithRetryAsync(async () =>
                    {
                        await _api.Helix.ChannelPoints.UpdateCustomRewardAsync(_broadcasterID, reward.Id, new UpdateCustomRewardRequest
                        {
                            IsEnabled = false,
                            Title = reward.Title,
                            Cost = reward.Cost,
                            Prompt = reward.Prompt,
                            IsUserInputRequired = reward.IsUserInputRequired
                        }).WaitAsync(_apiTimeout);
                        successfullyDisabledIds.Add(reward.Id);
                        _logger.LogInformation("Lockdown: Temporarily disabled reward '{Title}'", reward.Title);
                    }, $"DisableReward({reward.Title})");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DisableAllRewardsSafeAsync failed");
            }

            return successfullyDisabledIds;
        }

        public async Task RestoreRewardsAsync(List<string> rewardIdsToEnable)
        {
            if (!IsReady() || rewardIdsToEnable == null || rewardIdsToEnable.Count == 0) return;

            foreach (var id in rewardIdsToEnable)
            {
                await ExecuteWithRetryAsync(async () =>
                {
                    var rewardResponse = await _api.Helix.ChannelPoints.GetCustomRewardAsync(_broadcasterID, new List<string> { id }).WaitAsync(_apiTimeout);
                    var reward = rewardResponse.Data.FirstOrDefault();

                    if (reward != null)
                    {
                        await _api.Helix.ChannelPoints.UpdateCustomRewardAsync(_broadcasterID, id, new UpdateCustomRewardRequest
                        {
                            IsEnabled = true,
                            Title = reward.Title,
                            Cost = reward.Cost,
                            Prompt = reward.Prompt,
                            IsUserInputRequired = reward.IsUserInputRequired
                        }).WaitAsync(_apiTimeout);
                        _logger.LogInformation("Lockdown: Restored reward '{Title}'", reward.Title);
                    }
                }, $"RestoreReward({id})");
            }
        }

        public async Task<TwitchLib.Api.Helix.Models.ChannelPoints.GetCustomReward.GetCustomRewardsResponse> GetAllRewards()
        {
            if (!IsReady()) return null;
            try
            {
                return await _api.Helix.ChannelPoints.GetCustomRewardAsync(_broadcasterID).WaitAsync(_apiTimeout);
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
                var rewards = await _api.Helix.ChannelPoints.GetCustomRewardAsync(_broadcasterID, new List<string> { id }).WaitAsync(_apiTimeout);
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
                var rewards = await _api.Helix.ChannelPoints.GetCustomRewardAsync(_broadcasterID).WaitAsync(_apiTimeout);
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
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.ChannelPoints.UpdateCustomRewardAsync(_broadcasterID, rewardID, new UpdateCustomRewardRequest
                {
                    Title = title,
                    Cost = cost,
                    Prompt = prompt,
                    IsEnabled = enable,
                    IsUserInputRequired = isUserInputRequired,
                    ShouldRedemptionsSkipRequestQueue = false
                }).WaitAsync(_apiTimeout);
            }, "UpdateReward");
        }

        public async Task DeleteReward(string rewardID)
        {
            if (!IsReady()) return;
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.ChannelPoints.DeleteCustomRewardAsync(_broadcasterID, rewardID).WaitAsync(_apiTimeout);
            }, "DeleteReward");
        }

        public async Task<string> CreateReward(string title, int cost, string prompt, bool enabled, bool userinput)
        {
            if (!IsReady()) return null;
            string newId = null;
            await ExecuteWithRetryAsync(async () =>
            {
                var response = await _api.Helix.ChannelPoints.CreateCustomRewardsAsync(_broadcasterID, new CreateCustomRewardsRequest
                {
                    Title = title,
                    Cost = cost,
                    Prompt = prompt,
                    IsEnabled = enabled,
                    IsUserInputRequired = userinput,
                    ShouldRedemptionsSkipRequestQueue = false
                }).WaitAsync(_apiTimeout);
                newId = response.Data.FirstOrDefault()?.Id;
            }, "CreateReward");
            return newId;
        }

        public async Task CencelReward(string rewardID, string redemID)
        {
            if (!IsReady()) return;
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.ChannelPoints.UpdateRedemptionStatusAsync(_broadcasterID, rewardID, new List<string> { redemID }, new UpdateCustomRewardRedemptionStatusRequest
                {
                    Status = CustomRewardRedemptionStatus.CANCELED
                }).WaitAsync(_apiTimeout);
            }, "CencelReward");
        }

        public async Task ApproveReward(string rewardID, string redemID)
        {
            if (!IsReady()) return;
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.ChannelPoints.UpdateRedemptionStatusAsync(_broadcasterID, rewardID, new List<string> { redemID }, new UpdateCustomRewardRedemptionStatusRequest
                {
                    Status = CustomRewardRedemptionStatus.FULFILLED
                }).WaitAsync(_apiTimeout);
            }, "ApproveReward");
        }

        public async Task<string> GetCustomReward(string rewardID, string userID)
        {
            if (!IsReady()) return null;
            try
            {
                var redemption = await _api.Helix.ChannelPoints.GetCustomRewardRedemptionAsync(_broadcasterID, rewardID).WaitAsync(_apiTimeout);
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
            var reward = await GetReward(rewardID);
            if (reward != null)
            {
                await UpdateReward(reward.Id, reward.Title, reward.Cost, reward.Prompt, false, reward.IsUserInputRequired);
            }
            else
                _logger.LogError("DisableRewardAsync -> null. Id: {RewardID}", rewardID);
        }

        public async Task EnableRewardAsync(string rewardID)
        {
            if (!IsReady()) return;
            var reward = await GetReward(rewardID);
            if (reward != null)
            {
                await UpdateReward(reward.Id, reward.Title, reward.Cost, reward.Prompt, true, reward.IsUserInputRequired);
            }
            else
                _logger.LogError("EnableRewardAsync -> null. Id: {RewardID}", rewardID);
        }

        #endregion

        #region User Management & Chat

        private async Task PerformTimeOutUserAsync(string userId, string userName, int duration, string reason)
        {
            if (!IsReady()) return;
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.Moderation.BanUserAsync(_broadcasterID, _broadcasterID, new BanUserRequest
                {
                    UserId = userId,
                    Duration = duration,
                    Reason = reason
                }).WaitAsync(_apiTimeout);
            }, $"TimeOutUserAsync({userName} [{userId}])");
        }

        public async Task TimeOutUser(UserObject user, int duration, string reason)
        {
            if (!IsReady()) return;
            if (user.isMod == 1) return;
            await PerformTimeOutUserAsync(user.TwitchID.ToString(), user.Name, duration, reason);
        }

        public async Task TimeOutModerator(UserObject user, int duration, string reason)
        {
            if (!IsReady()) return;
            await PerformTimeOutUserAsync(user.TwitchID.ToString(), user.Name, duration, reason);
        }

        public async Task BanUser(string userID, string reason)
        {
            if (!IsReady()) return;
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.Moderation.BanUserAsync(_broadcasterID, _broadcasterID, new BanUserRequest
                {
                    UserId = userID,
                    Reason = reason
                }).WaitAsync(_apiTimeout);
            }, "BanUser");
        }

        public async Task UnBanUser(string userID)
        {
            if (!IsReady()) return;
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.Moderation.UnbanUserAsync(_broadcasterID, _broadcasterID, userID).WaitAsync(_apiTimeout);
            }, "UnBanUser");
        }

        public async Task<bool> AddChannelModerator(string userID)
        {
            if (!IsReady()) return false;
            bool success = false;
            try
            {
                await _api.Helix.Moderation.AddChannelModeratorAsync(_broadcasterID, userID).WaitAsync(_apiTimeout);
                success = true;
                await Task.Delay(100);
            }
            catch (BadRequestException ex) when (ex.Message.Contains("user is banned"))
            {
                _logger.LogWarning("Cannot Mod user {UserId} because they are banned. Attempting Unban...", userID);
                try
                {
                    await _api.Helix.Moderation.UnbanUserAsync(_broadcasterID, _broadcasterID, userID).WaitAsync(_apiTimeout);
                    await Task.Delay(500);
                    await _api.Helix.Moderation.AddChannelModeratorAsync(_broadcasterID, userID).WaitAsync(_apiTimeout);
                    success = true;
                }
                catch (Exception retryEx)
                {
                    _logger.LogError(retryEx, "Failed to AddMod even after Unban attempt for {UserId}", userID);
                    success = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddChannelModerator failed for {UserId}", userID);
                success = false;
            }
            return success;
        }

        public async Task DeleteChannelModerator(string userID)
        {
            if (!IsReady()) return;
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.Moderation.DeleteChannelModeratorAsync(_broadcasterID, userID).WaitAsync(_apiTimeout);
            }, "DeleteChannelModerator");
        }

        public async Task<TwitchLib.Api.Helix.Models.Moderation.GetModerators.Moderator[]> GetAllMods()
        {
            if (!IsReady()) return null;
            try
            {
                var response = await _api.Helix.Moderation.GetModeratorsAsync(_broadcasterID, null, 100).WaitAsync(_apiTimeout);
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
                var response = await _api.Helix.Users.GetUsersAsync(null, new List<string> { userLogin }).WaitAsync(_apiTimeout);
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
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.Whispers.SendWhisperAsync(_broadcasterID, toUserID, message, newRec).WaitAsync(_apiTimeout);
            }, "SendWhisper");
        }

        public async Task<TwitchLib.Api.Helix.Models.Channels.GetChannelVIPs.GetChannelVIPsResponse> GetVIPs()
        {
            if (!IsReady()) return null;
            try
            {
                return await _api.Helix.Channels.GetVIPsAsync(_broadcasterID, null, 100).WaitAsync(_apiTimeout);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetVIPs");
                return null;
            }
        }

        public async Task<List<MODELS.VipInfo>> GetAllVipsAsync()
        {
            if (!IsReady()) return null;
            var result = new List<MODELS.VipInfo>();
            string cursor = null;
            try
            {
                do
                {
                    var page = await _api.Helix.Channels.GetVIPsAsync(_broadcasterID, null, 100, cursor).WaitAsync(_apiTimeout);
                    if (page?.Data == null) break;
                    result.AddRange(page.Data.Select(v => new MODELS.VipInfo(v.UserId, v.UserLogin, v.UserName)));
                    cursor = page.Pagination?.Cursor;
                } while (!string.IsNullOrEmpty(cursor) && result.Count < 1000);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAllVipsAsync");
                return null;
            }
        }

        public async Task<bool> TryAddChannelVIPAsync(string userID)
        {
            if (!IsReady()) return false;
            bool ok = false;
            try
            {
                await ExecuteWithRetryAsync(async () =>
                {
                    await _api.Helix.Channels.AddChannelVIPAsync(_broadcasterID, userID).WaitAsync(_apiTimeout);
                    ok = true;
                }, "AddChannelVIP");
            }
            catch (Exception ex) { _logger.LogError(ex, "AddChannelVIP {UserId}", userID); }
            return ok;
        }

        public async Task<bool> TryRemoveChannelVIPAsync(string userID)
        {
            if (!IsReady()) return false;
            bool ok = false;
            try
            {
                await ExecuteWithRetryAsync(async () =>
                {
                    await _api.Helix.Channels.RemoveChannelVIPAsync(_broadcasterID, userID).WaitAsync(_apiTimeout);
                    ok = true;
                }, "DeleteChannelVIP");
            }
            catch (Exception ex) { _logger.LogError(ex, "DeleteChannelVIP {UserId}", userID); }
            return ok;
        }

        public async Task AddChannelVIP(string userID)
        {
            if (!IsReady()) return;
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.Channels.AddChannelVIPAsync(_broadcasterID, userID).WaitAsync(_apiTimeout);
            }, "AddChannelVIP");
        }

        public async Task DeleteChannelVIP(string userID)
        {
            if (!IsReady()) return;
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.Channels.RemoveChannelVIPAsync(_broadcasterID, userID).WaitAsync(_apiTimeout);
            }, "DeleteChannelVIP");
        }

        #endregion

        #region Stream, Chat & Clips

        public async Task<bool> GetStreamStatus()
        {
            if (!IsReady()) return false;
            try
            {
                var streams = await _api.Helix.Streams.GetStreamsAsync(null, 1, null, null, new List<string> { _broadcasterID }, null, null).WaitAsync(_apiTimeout);
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
                var response = await _api.Helix.Streams.GetStreamsAsync(null, 1, null, null, new List<string> { _broadcasterID }).WaitAsync(_apiTimeout);
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
                var response = await _api.Helix.Channels.GetChannelInformationAsync(_broadcasterID).WaitAsync(_apiTimeout);
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
                return await _api.Helix.Chat.GetChattersAsync(_broadcasterID, _broadcasterID).WaitAsync(_apiTimeout);
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
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.Moderation.DeleteChatMessagesAsync(_broadcasterID, _broadcasterID, messageID).WaitAsync(_apiTimeout);
            }, "DeleteMessage");
        }

        public async Task DeleteAllMessages()
        {
            if (!IsReady()) return;
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.Moderation.DeleteChatMessagesAsync(_broadcasterID, _broadcasterID).WaitAsync(_apiTimeout);
            }, "DeleteAllMessages");
        }

        public async Task<bool> Announce(string message)
        {
            if (!IsReady()) return false;
            bool success = false;
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.Chat.SendChatAnnouncementAsync(_broadcasterID, _broadcasterID, message).WaitAsync(_apiTimeout);
                success = true;
            }, "Announce");
            return success;
        }

        public async Task<TwitchLib.Api.Helix.Models.Clips.CreateClip.CreatedClipResponse> CreateClip()
        {
            if (!IsReady()) return null;
            TwitchLib.Api.Helix.Models.Clips.CreateClip.CreatedClipResponse result = null;
            await ExecuteWithRetryAsync(async () =>
            {
                result = await _api.Helix.Clips.CreateClipAsync(_broadcasterID).WaitAsync(_apiTimeout);
            }, "CreateClip");
            return result;
        }

        public async Task<bool> CheckClipExistence(string clipID)
        {
            if (!IsReady()) return false;
            try
            {
                var clips = await _api.Helix.Clips.GetClipsAsync(new List<string> { clipID }).WaitAsync(_apiTimeout);
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
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.Chat.UpdateChatSettingsAsync(_broadcasterID, _broadcasterID, new ChatSettings
                {
                    EmoteMode = isEmoteOnly
                }).WaitAsync(_apiTimeout);
            }, "SetEmoteOnlyMode");
        }
        #endregion
    }
}