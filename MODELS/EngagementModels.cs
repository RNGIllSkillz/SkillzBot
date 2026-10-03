using System;
using System.Collections.Generic;

namespace SkillzBot.MODELS
{
    public class PredictorRecord
    {
        public long TwitchId { get; set; }
        public string Login { get; set; }
        public int PointsUsed { get; set; }
        public int? PointsWon { get; set; }
    }

    public class PredictionOutcomeRecord
    {
        public string OutcomeId { get; set; }
        public string Title { get; set; }
        public string Color { get; set; }
        public int Users { get; set; }
        public long ChannelPoints { get; set; }
        public bool IsWinner { get; set; }
        /// <summary>Twitch exposes at most the top 10 predictors per outcome.</summary>
        public List<PredictorRecord> TopPredictors { get; set; } = new List<PredictorRecord>();
    }

    public class PredictionResultRecord
    {
        public string PredictionId { get; set; }
        public string Title { get; set; }
        public string Status { get; set; }
        public string WinningOutcomeId { get; set; }
        public DateTime? StartedUtc { get; set; }
        public DateTime EndedUtc { get; set; }
        public List<PredictionOutcomeRecord> Outcomes { get; set; } = new List<PredictionOutcomeRecord>();
    }

    public class PollChoiceRecord
    {
        public string Title { get; set; }
        /// <summary>Catalog key behind a bot poll option (winlose, kda5...); null for manual polls.</summary>
        public string KindKey { get; set; }
        public int Votes { get; set; }
        public int ChannelPointsVotes { get; set; }
        public int BitsVotes { get; set; }
    }

    public class PollResultRecord
    {
        public string PollId { get; set; }
        public string Title { get; set; }
        public string Status { get; set; }
        public DateTime? StartedUtc { get; set; }
        public DateTime EndedUtc { get; set; }
        public List<PollChoiceRecord> Choices { get; set; } = new List<PollChoiceRecord>();
    }

    public record KindStat(string Kind, int Count, double AvgUsers, double AvgPoints);
    public record OutcomeStat(string Title, double AvgUsers, double AvgPoints, int Wins);

    public class EngagementSummary
    {
        public int Days { get; set; }
        public int Predictions { get; set; }
        public int ManualPredictions { get; set; }
        public int WinLose { get; set; }
        public double WinLoseAvgUsers { get; set; }
        public int WinLoseMaxUsers { get; set; }
        public double WinLoseAvgPoints { get; set; }
        public int Other { get; set; }
        public double OtherAvgUsers { get; set; }
        public long TotalPoints { get; set; }
        public int KnownBettors { get; set; }
        public List<KindStat> ByKind { get; set; } = new List<KindStat>();
        public List<OutcomeStat> WinLoseOutcomes { get; set; } = new List<OutcomeStat>();
        public int Polls { get; set; }
        public double PollAvgVotes { get; set; }
        public int PollMaxVotes { get; set; }
    }
}
