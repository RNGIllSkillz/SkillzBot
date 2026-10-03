using SkillzBot.MODELS;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SkillzBot.Interfaces
{
    public record DatabaseStats(
        long SessionQueries,
        long SessionNewUsers,
        long SessionMessagesSaved,
        long TotalUsers,
        long TotalMessages
    );
    public interface IDatabaseService
    {
        Task InitializeAsync();
        Task<UserObject> GetUserAsync(long twitchId);
        Task<UserObject> GetUserAsync(string name);
        Task AddOrUpdateUserAsync(UserObject user);
        Task UpdateUserAsync(UserObject user);
        Task SaveMessageAsync(long twitchId, string name, string message, double timestamp);
        Task SaveMessagesAsync(List<MessageBuffer> messages);
        Task<List<UserObject>> GetTopUsersAsync(string flag, int limit = 3);
        Task<int[]> GetUserPositionAsync(string userName, string columnName);
        Task DeleteUserAsync(string userName);
        Task AddPointsAsync(int amount, long? twitchId = null);
        Task<QuizzObject> GetQuizAsync(int id);
        /// <summary>A random row from dbQuiz, or null when the table is empty.</summary>
        Task<QuizzObject> GetRandomQuizAsync();
        Task AddQuizPointsAsync(int amount, long twitchId);
        Task SpendQuizPointsAsync(int amount, long twitchId);
        Task UpdateOnlineStatusAsync(List<string> chatters);
        Task<TrackUser> TrackUserAsync(string userName);
        Task<DatabaseStats> GetStatsAsync();
    }
}