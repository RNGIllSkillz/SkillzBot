namespace SkillzBot.MODELS
{
    public class BotGameStateModel
    {
        public string SummonerName { get; set; }
        public string SummonerRegion { get; set; }
        public int StartLP { get; set; }
        public string Elo { get; set; }
        public int EarnedLP { get; set; }
        public int NumLosses { get; set; }
        public int NumWins { get; set; }
        public int NumGames { get; set; }
        public string Tier { get; set; }
    }
}