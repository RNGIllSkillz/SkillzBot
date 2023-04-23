using System.Diagnostics;
using System.Resources;
namespace SkillzBot.Singleton
{
    public sealed class IllSingleton
    {
        private static IllSingleton _instance;
        public string BotTwitchName { get; set; }
        public string BotTwitchAuth { get; set; }
        public string ChannelName { get; set; }
        public string TApiAccessToken { get; set; }
        public string TApiClientId { get; set; }
        public string StreamElementsApiToken { get; set; }
        public string StreamElementsID { get; set; }
        public string YouTubeApiToken { get; set; }
        public string RiotApiToken { get; set; }
        public string BrodcasterId { get; set; }
        public string CenceleUval { get; set; }
        public string EmoteModeId { get; set; }
        public string uvalMod { get; set; }
        public string UvalId { get; set; }
        public string Pi4KaId { get; set; }
        public string ZakazTrekaId { get; set; }
        public string UvalSabId { get; set; }
        public string UvalVipId { get; set; }
        public string MySQL_User { get; set; }
        public string MySQL_password { get; set; }
        public string rootUser { get; set; }        
        public bool wisEnabled { get; set; }
        public double WisCD { get; set; }
        public string SUMMONER_NAME { get; set; }
        public bool inAmatch { get; set; }
        public bool debug { get; set; }
        public int startLP { get; set; }
        public string elo { get; set; }
        public int earnedLP { get; set; }
        public int numLoose { get; set; }
        public int numGames { get; set; }
        public int numWins { get; set; }
        public string tier { get; set; }
        public bool autoPred { get; set; }
        public bool QuizIsRunning { get; set; }
        public bool BroadcasterIsOnline { get; set; }
        public bool FirstQuizzOfTheDay { get; set; }
        public int AntiBotProtectionLvL { get; set; }
        public string ChatWithBot { get; set; }
        public string ReleaseBot { get; set; }
        public string GPTApiToken { get; set; }
        private IllSingleton() { }

        public static IllSingleton GetInstance()
        {
            if (_instance == null)
            {
                _instance = new IllSingleton();
                _instance.inAmatch = false;
                _instance.debug = false;
            }
            return _instance;
        }
    }
}
