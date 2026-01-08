namespace SkillzBot.MODELS
{
    internal class TtvCustomReward
    {
        public string ID { get; set; }
        public string Title { get; set; }
        public long Cost { get; set; }
        public string Prompt { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsUserInputRequired { get; set; }
    }
}