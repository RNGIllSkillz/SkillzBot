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
    }
}