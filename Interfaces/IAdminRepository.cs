using SkillzBot.Api;
using SkillzBot.MODELS;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SkillzBot.Interfaces
{
    /// <summary>Queries used only by the web panel and the !editor command.</summary>
    public interface IAdminRepository
    {
        Task<bool> IsEditorAsync(long twitchId);
        Task<List<EditorRow>> GetEditorsAsync();
        Task AddEditorAsync(long twitchId, string login, string addedBy);
        Task<bool> RemoveEditorAsync(long twitchId);

        Task<PagedResult<UserObject>> SearchUsersAsync(string query, string sort, bool descending, int page, int pageSize);
        Task<PagedResult<MessageRow>> SearchMessagesAsync(string user, string text, DateTime? fromUtc, DateTime? toUtc, int page, int pageSize);
        Task<List<ActivityBucket>> GetActivityAsync(DateTime fromUtc, int bucketSeconds);

        Task<List<QuizRow>> GetQuizzesAsync();
        Task<int> AddQuizAsync(string question, string answer, int prize);
        Task<bool> UpdateQuizAsync(int id, string question, string answer, int prize);
        Task<bool> DeleteQuizAsync(int id);

        Task<List<PredictionRow>> GetPredictionsAsync(int days, int limit);
        Task<List<PollRow>> GetPollsAsync(int days, int limit);
    }
}
