using SkillzBot.MODELS;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SkillzBot.Interfaces
{
    /// <summary>Prediction and poll engagement history (tables dbPrediction*, dbPoll*).</summary>
    public interface IEngagementRepository
    {
        Task PredictionStartedAsync(string predictionId, string kind, string source, string title, string matchId, DateTime startedUtc);
        Task PredictionEndedAsync(PredictionResultRecord result);
        Task PollStartedAsync(string pollId, string source, string title, DateTime startedUtc, IReadOnlyList<PollChoiceRecord> choices);
        Task PollEndedAsync(PollResultRecord result);
        Task<EngagementSummary> GetEngagementSummaryAsync(int days);
    }
}
