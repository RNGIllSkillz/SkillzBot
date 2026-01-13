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
    }
}