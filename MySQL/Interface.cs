using SkillzBot.MODELS;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SkillzBot.MySQL
{
    public interface IDatabaseService
    {
        Task InitializeAsync();
        Task<UserObject> GetUserAsync(int twitchId);
        Task<UserObject> GetUserAsync(string name);
        Task AddOrUpdateUserAsync(UserObject user);
        Task UpdateUserAsync(UserObject user);
        Task SaveMessageAsync(int twitchId, string name, string message, double timestamp);
        Task SaveMessagesAsync(List<MessageBuffer> messages);
        Task<List<UserObject>> GetTopUsersAsync(string flag, int limit = 3);
        Task<int[]> GetUserPositionAsync(string userName, string columnName);
        Task DeleteUserAsync(string userName);
        Task AddPointsAsync(int amount, int? twitchId = null);
        Task<QuizzObject> GetQuizAsync(int id);
        Task AddQuizPointsAsync(int amount, int twitchId);
        Task SpendQuizPointsAsync(int amount, int twitchId);
        Task UpdateOnlineStatusAsync(List<string> chatters);
        Task<TrackUser> TrackUserAsync(string userName);
    }
}
