namespace SkillzBot.MODELS
{
    public class BotStateModel
    {
        public bool GodMode { get; set; }
        public bool WisEnabled { get; set; }
        public bool InMatch { get; set; }
        public bool Debug { get; set; }
        public bool AutoPred { get; set; } = true;
        public bool QuizIsRunning { get; set; }
        public bool BroadcasterIsOnline { get; set; }
        public bool FirstQuizOfTheDay { get; set; }
        public bool IsSilent { get; set; }
        public bool IsSubActive { get; set; }
        public int ChatFilterLvl { get; set; }
        public int AntiBotProtectionLvl { get; set; }
        public bool PerformanceDebugMode { get; set; }

        /// <summary>Prediction type chosen by chat for the next game; null means win/lose.</summary>
        public string NextPredictionKey { get; set; }
        /// <summary>When the last "which prediction next" poll was started (UTC).</summary>
        public System.DateTime LastPredictionPollUtc { get; set; }
        public bool PredictionPollEnabled { get; set; } = true;
        /// <summary>Live seconds accumulated since the last prediction poll, over finished live segments.</summary>
        public double LiveSecondsBank { get; set; }
        /// <summary>Start of the current live segment, or null while offline.</summary>
        public System.DateTime? LiveSinceUtc { get; set; }
        /// <summary>Live seconds required before the next poll; drawn at random after each poll.</summary>
        public double NextPollAfterLiveSec { get; set; }

        /// <summary>Prediction the bot still owes a resolution for; survives restarts.</summary>
        public ActivePredictionState ActivePrediction { get; set; }
        /// <summary>Poll whose result has not been applied yet; survives restarts.</summary>
        public ActivePollState ActivePoll { get; set; }
    }

    public class ActivePredictionState
    {
        public string MatchId { get; set; }
        /// <summary>Twitch prediction id, or null when the game is tracked for stats only.</summary>
        public string PredictionId { get; set; }
        public string KindKey { get; set; }
        public System.DateTime StartedUtc { get; set; }
        /// <summary>Champion id (as string) to the outcome title shown on Twitch.</summary>
        public System.Collections.Generic.Dictionary<string, string> Outcomes { get; set; }
        public bool StatsRecorded { get; set; }
    }

    public class ActivePollState
    {
        public string PollId { get; set; }
        public System.DateTime StartedUtc { get; set; }
        public System.Collections.Generic.List<string> OptionKeys { get; set; }
    }
}