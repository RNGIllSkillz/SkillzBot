using SkillzBot.MODELS;
using System.Collections.Generic;
using System.Threading.Tasks;
using TwitchLib.Api.Helix.Models.ChannelPoints;
using TwitchLib.Api.Helix.Models.ChannelPoints.GetCustomReward;
using TwitchLib.Api.Helix.Models.Channels.GetChannelInformation;
using TwitchLib.Api.Helix.Models.Channels.GetChannelVIPs;
using TwitchLib.Api.Helix.Models.Chat.GetChatters;
using TwitchLib.Api.Helix.Models.Clips.CreateClip;
using TwitchLib.Api.Helix.Models.Moderation.GetModerators;
using TwitchLib.Api.Helix.Models.Predictions.GetPredictions;
using TwitchLib.Api.Helix.Models.Streams.GetStreams;

namespace SkillzBot.Interfaces
{
    public interface ITwitchService
    {
        // Status Checks
        bool IsReady();
        Task<bool> GetStreamStatus();
        Task<Stream> GetStreamInfo();
        Task<ChannelInformation> GetChannelInformationAsync();

        // Predictions
        ValueTask Start_2_Prediction(string title, string blue, string red, int windowSec);
        ValueTask Start_5_Prediction(List<string> champs, string title, int windowSec);
        ValueTask Start_10_Prediction(List<string> champs, string title, int windowSec);
        Task<string> End_Multy_Prediction(string champ);
        Task End_WinLoose_Prediction(bool win, int tryes = 0);
        Task CencelePrediction();
        Task<GetPredictionsResponse> GetCurrentPredPublic();

        // Rewards
        Task<GetCustomRewardsResponse> GetAllRewards();
        Task<CustomReward> GetReward(string id);
        Task<CustomReward> GetReward(string title, string overloadParam);
        Task UpdateReward(string rewardID, string title, int cost, string prompt, bool enable, bool isUserInputRequired);
        Task DeleteReward(string rewardID);
        Task<string> CreateReward(string title, int cost, string prompt, bool enabled, bool userinput);
        Task CencelReward(string rewardID, string redemID);
        Task ApproveReward(string rewardID, string redemID);
        Task<string> GetCustomReward(string rewardID, string userID);
        Task DisableRewardAsync(string rewardID);
        Task EnableRewardAsync(string rewardID);
        Task<List<string>> DisableAllRewardsSafeAsync(string exceptionRewardId);
        Task RestoreRewardsAsync(List<string> rewardIdsToEnable);

        // Moderation & Users
        Task TimeOutUser(UserObject user, int duration, string reason);
        Task TimeOutModerator(UserObject user, int duration, string reason);
        Task BanUser(string userID, string reason);
        Task UnBanUser(string userID);
        Task<bool> AddChannelModerator(string userID);
        Task DeleteChannelModerator(string userID);
        Task<Moderator[]> GetAllMods();
        Task<string> GetUsetIDByName(string userLogin);
        Task SendWhisper(string toUserID, string message, bool newRec = true);

        // Chat & Clips
        Task DeleteMessage(string messageID);
        Task DeleteAllMessages();
        Task<bool> Announce(string message);
        Task<CreatedClipResponse> CreateClip();
        Task<bool> CheckClipExistence(string clipID);
        Task<GetChattersResponse> GetChattersAsync();
        Task SetEmoteOnlyMode(bool isEmoteOnly);

        // VIPs
        Task<GetChannelVIPsResponse> GetVIPs();
        Task AddChannelVIP(string userID);
        Task DeleteChannelVIP(string userID);
    }
}