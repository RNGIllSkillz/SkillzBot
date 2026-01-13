using Microsoft.Extensions.Logging;
using SkillzBot.IllConfiguration;
using SkillzBot.Interfaces;
using SkillzBot.MODELS;
using SkillzBot.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Core.Exceptions;
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
        private readonly BotConfigModel _config;

        private string _predID;
        private string _winID;
        private string _looseID;
        private readonly string _broadcasterID;
        private readonly bool _isValidToken;

        private readonly SemaphoreSlim _apiSemaphore = new SemaphoreSlim(1, 1);

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

            await _apiSemaphore.WaitAsync();
            try
            {
                while (true)
                {
                    try
                    {
                        // If Twitch takes longer, we throw TimeoutException and retry/fail immediately
                        // instead of blocking the bot for 100 seconds.
                        await action().WaitAsync(_apiTimeout);

                        await Task.Delay(100); // Small rate-limit spacing
                        break;
                    }
                    // Catch the forced 5s timeout
                    catch (TimeoutException)
                    {
                        retries++;
                        if (retries > maxRetries)
                        {
                            _logger.LogError("Twitch API timed out locally ({Timeout}s) in {Operation} after {Retries} retries.", _apiTimeout.TotalSeconds, operationName, retries);
                            break;
                        }
                        _logger.LogWarning("Twitch API slow response (>5s). Retrying {Count}...", retries);
                        // No delay needed for timeout retry, maybe just network blip
                    }
                    // Catch standard 429 Rate Limits
                    catch (TooManyRequestsException)
                    {
                        retries++;
                        if (retries > maxRetries)
                        {
                            _logger.LogError("Rate Limit exceeded for {Operation} after {Retries} retries.", operationName, retries);
                            break;
                        }
                        _logger.LogWarning("429 Too Many Requests in {Operation}. Waiting 2s...", operationName);
                        await Task.Delay(2000);
                    }
                    // Catch internal HTTP timeouts (TaskCanceled) if they happen faster than 5s
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
                    // Catch HTTP Errors (500, 503, etc)
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
                    // Critical Errors - Do Not Retry
                    catch (BadRequestException ex)
                    {
                        _logger.LogError(ex, "Bad Request in {Operation}: {Msg}", operationName, ex.Message);
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected Error in {Operation}", operationName);
                        break;
                    }
                }
            }
            finally
            {
                _apiSemaphore.Release();
            }
        }

        #region Predictions

        private async Task GetCurrentPred()
        {
            if (!IsReady()) return;
            await ExecuteWithRetryAsync(async () =>
            {
                var predictions = await _api.Helix.Predictions.GetPredictionsAsync(_broadcasterID);
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
                await _api.Helix.Predictions.CreatePredictionAsync(request);
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
                await _api.Helix.Predictions.CreatePredictionAsync(request);
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
                await _api.Helix.Predictions.CreatePredictionAsync(request);
            }, "Start_5_Prediction");

            await GetCurrentPred();
        }

        public async Task<string> End_Multy_Prediction(string champ)
        {
            if (!IsReady()) return "Invalid AccessToken";
            string result = "ERR";

            await ExecuteWithRetryAsync(async () =>
            {
                var predictions = await _api.Helix.Predictions.GetPredictionsAsync(_broadcasterID);
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
                    await _api.Helix.Predictions.EndPredictionAsync(_broadcasterID, _predID, predictionStatus, outcomeID);
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
                var predictions = await _api.Helix.Predictions.GetPredictionsAsync(_broadcasterID);
                if (predictions.Data.Length == 0) return;

                string currentPredID = predictions.Data.First().Id;
                if (currentPredID == _predID)
                {
                    var status = PredictionEndStatus.RESOLVED;
                    await _api.Helix.Predictions.EndPredictionAsync(_broadcasterID, _predID, status, win ? _winID : _looseID);
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
                var predictions = await _api.Helix.Predictions.GetPredictionsAsync(_broadcasterID);
                if (predictions.Data.Length == 0) return;

                string currentPredID = predictions.Data.First().Id;
                if (currentPredID == _predID)
                {
                    await _api.Helix.Predictions.EndPredictionAsync(_broadcasterID, _predID, PredictionEndStatus.CANCELED);
                }
            }, "CencelePrediction");
        }

        public async Task<TwitchLib.Api.Helix.Models.Predictions.GetPredictions.GetPredictionsResponse> GetCurrentPredPublic()
        {
            if (!IsReady()) return null;
            try
            {
                return await _api.Helix.Predictions.GetPredictionsAsync(_broadcasterID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetCurrentPredPublic");
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
                var allRewards = await _api.Helix.ChannelPoints.GetCustomRewardAsync(_broadcasterID);
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
                        });
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
                    var rewardResponse = await _api.Helix.ChannelPoints.GetCustomRewardAsync(_broadcasterID, new List<string> { id });
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
                        });
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
                return await _api.Helix.ChannelPoints.GetCustomRewardAsync(_broadcasterID);
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
                var rewards = await _api.Helix.ChannelPoints.GetCustomRewardAsync(_broadcasterID, new List<string> { id });
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
                var rewards = await _api.Helix.ChannelPoints.GetCustomRewardAsync(_broadcasterID);
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
                });
            }, "UpdateReward");
        }

        public async Task DeleteReward(string rewardID)
        {
            if (!IsReady()) return;
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.ChannelPoints.DeleteCustomRewardAsync(_broadcasterID, rewardID);
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
                });
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
                });
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
                });
            }, "ApproveReward");
        }

        public async Task<string> GetCustomReward(string rewardID, string userID)
        {
            if (!IsReady()) return null;
            try
            {
                var redemption = await _api.Helix.ChannelPoints.GetCustomRewardRedemptionAsync(_broadcasterID, rewardID);
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

        private async Task PerformTimeOutUserAsync(string userId, int duration, string reason)
        {
            if (!IsReady()) return;
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.Moderation.BanUserAsync(_broadcasterID, _broadcasterID, new BanUserRequest
                {
                    UserId = userId,
                    Duration = duration,
                    Reason = reason
                });
            }, $"TimeOutUserAsync({userId})");
        }

        public async Task TimeOutUser(UserObject user, int duration, string reason)
        {
            if (!IsReady()) return;
            if (user.isMod == 1) return;
            await PerformTimeOutUserAsync(user.TwitchID.ToString(), duration, reason);
        }

        public async Task TimeOutModerator(UserObject user, int duration, string reason)
        {
            if (!IsReady()) return;
            await PerformTimeOutUserAsync(user.TwitchID.ToString(), duration, reason);
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
                });
            }, "BanUser");
        }

        public async Task UnBanUser(string userID)
        {
            if (!IsReady()) return;
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.Moderation.UnbanUserAsync(_broadcasterID, _broadcasterID, userID);
            }, "UnBanUser");
        }

        public async Task<bool> AddChannelModerator(string userID)
        {
            if (!IsReady()) return false;
            bool success = false;

            await _apiSemaphore.WaitAsync();
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
            finally
            {
                _apiSemaphore.Release();
            }

            return success;
        }

        public async Task DeleteChannelModerator(string userID)
        {
            if (!IsReady()) return;
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.Moderation.DeleteChannelModeratorAsync(_broadcasterID, userID);
            }, "DeleteChannelModerator");
        }

        public async Task<TwitchLib.Api.Helix.Models.Moderation.GetModerators.Moderator[]> GetAllMods()
        {
            if (!IsReady()) return null;
            try
            {
                var response = await _api.Helix.Moderation.GetModeratorsAsync(_broadcasterID, null, 100);
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
                var response = await _api.Helix.Users.GetUsersAsync(null, new List<string> { userLogin });
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
                await _api.Helix.Whispers.SendWhisperAsync(_broadcasterID, toUserID, message, newRec);
            }, "SendWhisper");
        }

        public async Task<TwitchLib.Api.Helix.Models.Channels.GetChannelVIPs.GetChannelVIPsResponse> GetVIPs()
        {
            if (!IsReady()) return null;
            try
            {
                return await _api.Helix.Channels.GetVIPsAsync(_broadcasterID, null, 100);
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
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.Channels.AddChannelVIPAsync(_broadcasterID, userID);
            }, "AddChannelVIP");
        }

        public async Task DeleteChannelVIP(string userID)
        {
            if (!IsReady()) return;
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.Channels.RemoveChannelVIPAsync(_broadcasterID, userID);
            }, "DeleteChannelVIP");
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
                var response = await _api.Helix.Streams.GetStreamsAsync(null, 1, null, null, new List<string> { _broadcasterID });
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
                var response = await _api.Helix.Channels.GetChannelInformationAsync(_broadcasterID);
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
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.Moderation.DeleteChatMessagesAsync(_broadcasterID, _broadcasterID, messageID);
            }, "DeleteMessage");
        }

        public async Task DeleteAllMessages()
        {
            if (!IsReady()) return;
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.Moderation.DeleteChatMessagesAsync(_broadcasterID, _broadcasterID);
            }, "DeleteAllMessages");
        }

        public async Task<bool> Announce(string message)
        {
            if (!IsReady()) return false;
            bool success = false;
            await ExecuteWithRetryAsync(async () =>
            {
                await _api.Helix.Chat.SendChatAnnouncementAsync(_broadcasterID, _broadcasterID, message);
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
                result = await _api.Helix.Clips.CreateClipAsync(_broadcasterID);
            }, "CreateClip");
            return result;
        }

        public async Task<bool> CheckClipExistence(string clipID)
        {
            if (!IsReady()) return false;
            try
            {
                var clips = await _api.Helix.Clips.GetClipsAsync(new List<string> { clipID });
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
                });
            }, "SetEmoteOnlyMode");
        }
        #endregion
    }
}