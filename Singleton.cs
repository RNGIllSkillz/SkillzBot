using IllSkillzBot;
using SkillzBot.Readers;
using System.Threading.Tasks;

namespace SkillzBot.Singleton
{
    public sealed class IllSingleton
    {
        private static IllSingleton _instance;
        public string BotTwitchName { get; private set; }
        public string BotTwitchAuth { get; private set; }
        public string ChannelName { get; private set; }
        public string TApiAccessToken { get; private set; }
        public string TApiClientId { get; private set; }
        public string StreamElementsApiToken { get; private set; }
        public string StreamElementsID { get; private set; }
        public string YouTubeApiToken { get; private set; }
        public string RiotApiToken { get; private set; }
        public string BrodcasterId { get; private set; }
        public string CenceleUval { get; private set; }
        public string EmoteModeId { get; private set; }
        public string uvalMod { get; private set; }
        public string UvalId { get; private set; }
        public string Pi4KaId { get; private set; }
        public string ZakazTrekaId { get; private set; }
        public string UvalSabId { get; private set; }
        public string UvalVipId { get; private set; }
        public string MySQL_User { get; private set; }
        public string MySQL_password { get; private set; }
        public string rootUser { get; private set; }        
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
        public string ChatWithBot { get; private set; }
        public string ReleaseBot { get; private set; }
        public string GPTApiToken { get; private set; }
        public string SummonerRegion { get; set; }
        public bool IsSilent { get; set; }
        private IllSingleton() { }

        public static IllSingleton GetInstance()
        {
            if (_instance == null)
            {
                _instance = new IllSingleton
                {
                    inAmatch = false,
                    debug = false,
                    autoPred = true,
                    BroadcasterIsOnline = false,
                    FirstQuizzOfTheDay = true,
                    AntiBotProtectionLvL = 0,
                    wisEnabled = true,
                    rootUser = "ronink",
                    IsSilent = false
                };
                Config config = new Config(IllSkillzBotMain.GetConfigPath());
                SetUpSingleton(_instance, config).GetAwaiter().GetResult();
            }
            return _instance;
        }
        private static async Task SetUpSingleton(IllSingleton singleton, Config config)
        {
            singleton.BotTwitchName = config.GetBotConfigs().BotTwitchName;
            singleton.BotTwitchAuth = config.GetBotConfigs().BotTwitchAuth;
            singleton.RiotApiToken = config.GetBotConfigs().RiotApiToken;
            singleton.YouTubeApiToken = config.GetBotConfigs().YouTubeApiToken;
            singleton.TApiClientId = config.GetBotConfigs().TApiClientId;
            singleton.TApiAccessToken = config.GetBotConfigs().TApiAccessToken;
            singleton.ChannelName = config.GetBotConfigs().ChannelName;
            singleton.BrodcasterId = config.GetBotConfigs().BrodcasterId;
            singleton.ZakazTrekaId = config.GetBotConfigs().ZakazTrekaId;
            singleton.Pi4KaId = config.GetBotConfigs().Pi4KaId;
            singleton.UvalId = config.GetBotConfigs().UvalId;
            singleton.UvalSabId = config.GetBotConfigs().UvalSabId;
            singleton.UvalVipId = config.GetBotConfigs().UvalVipId;
            singleton.EmoteModeId = config.GetBotConfigs().EmoteModeId;
            singleton.SUMMONER_NAME = config.GetBotConfigs().SummonerName;
            singleton.CenceleUval = config.GetBotConfigs().CenceleUval;
            singleton.uvalMod = config.GetBotConfigs().uvalMod;
            singleton.MySQL_User = config.GetBotConfigs().MySQL_User;
            singleton.MySQL_password = config.GetBotConfigs().MySQL_password;
            singleton.StreamElementsApiToken = config.GetBotConfigs().StreamElementsApiToken;
            singleton.StreamElementsID = config.GetBotConfigs().StreamElementsID;
            singleton.ChatWithBot = config.GetBotConfigs().ChatWithBot;
            singleton.ReleaseBot = config.GetBotConfigs().ReleaseBot;
            singleton.GPTApiToken = config.GetBotConfigs().GPTApiToken;
            singleton.SummonerRegion = config.GetBotConfigs().SummonerRegion;
            await TempDataReader.ReadGameStats().ConfigureAwait(false);
        }
    }
}
