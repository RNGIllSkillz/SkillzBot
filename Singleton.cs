using IllSkillzBot;
using SkillzBot.Readers;
using System.Threading.Tasks;

namespace SkillzBot.Singleton
{
    public sealed class IllSingleton
    {
        private static IllSingleton _instance;
        private static readonly object lockObject = new object();
        private static readonly object lockAtCreationObject = new object();

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
        private bool _wisEnabled;
        public bool wisEnabled
        {
            get
            {
                lock (lockObject)
                {
                    return _wisEnabled;
                }
            }
            set
            {
                lock (lockObject)
                {
                    _wisEnabled = value;
                }
            }
        }
        private double _WisCD;
        public double WisCD
        {
            get
            {
                lock (lockObject)
                {
                    return _WisCD;
                }
            }
            set
            {
                lock (lockObject)
                {
                    _WisCD = value;
                }
            }
        }
        private string _SUMMONER_NAME;
        public string SUMMONER_NAME
        {
            get
            {
                lock (lockObject)
                {
                    return _SUMMONER_NAME;
                }
            }
            set
            {
                lock (lockObject)
                {
                    _SUMMONER_NAME = value;
                }
            }
        }
        private bool _inAmatch;
        public bool inAmatch
        {
            get
            {
                lock (lockObject)
                {
                    return _inAmatch;
                }
            }
            set
            {
                lock (lockObject)
                {
                    _inAmatch = value;
                }
            }
        }
        private bool _debug;
        public bool debug
        {
            get
            {
                lock (lockObject)
                {
                    return _debug;
                }
            }
            set
            {
                lock (lockObject)
                {
                    _debug = value;
                }
            }
        }
        private int _startLP;
        public int startLP
        {
            get
            {
                lock (lockObject)
                {
                    return _startLP;
                }
            }
            set
            {
                lock (lockObject)
                {
                    _startLP = value;
                }
            }
        }
        private string _elo;
        public string elo
        {
            get
            {
                lock (lockObject)
                {
                    return _elo;
                }
            }
            set
            {
                lock (lockObject)
                {
                    _elo = value;
                }
            }
        }
        private int _earnedLP;
        public int earnedLP
        {
            get
            {
                lock (lockObject)
                {
                    return _earnedLP;
                }
            }
            set
            {
                lock (lockObject)
                {
                    _earnedLP = value;
                }
            }
        }
        private int _numLoose;
        public int numLoose
        {
            get
            {
                lock (lockObject)
                {
                    return _numLoose;
                }
            }
            set
            {
                lock (lockObject)
                {
                    _numLoose = value;
                }
            }
        }
        private int _numGames;
        public int numGames
        {
            get
            {
                lock (lockObject)
                {
                    return _numGames;
                }
            }
            set
            {
                lock (lockObject)
                {
                    _numGames = value;
                }
            }
        }
        private int _numWins;
        public int numWins
        {
            get
            {
                lock (lockObject)
                {
                    return _numWins;
                }
            }
            set
            {
                lock (lockObject)
                {
                    _numWins = value;
                }
            }
        }
        private string _tier;
        public string tier
        {
            get
            {
                lock (lockObject)
                {
                    return _tier;
                }
            }
            set
            {
                lock (lockObject)
                {
                    _tier = value;
                }
            }
        }
        private bool _autoPred;
        public bool autoPred
        {
            get
            {
                lock (lockObject)
                {
                    return _autoPred;
                }
            }
            set
            {
                lock (lockObject)
                {
                    _autoPred = value;
                }
            }
        }
        private bool _QuizIsRunning;
        public bool QuizIsRunning
        {
            get
            {
                lock (lockObject)
                {
                    return _QuizIsRunning;
                }
            }
            set
            {
                lock (lockObject)
                {
                    _QuizIsRunning = value;
                }
            }
        }
        private bool _BroadcasterIsOnline;
        public bool BroadcasterIsOnline
        {
            get
            {
                lock (lockObject)
                {
                    return _BroadcasterIsOnline;
                }
            }
            set
            {
                lock (lockObject)
                {
                    _BroadcasterIsOnline = value;
                }
            }
        }
        private bool _FirstQuizzOfTheDay;
        public bool FirstQuizzOfTheDay
        {
            get
            {
                lock (lockObject)
                {
                    return _FirstQuizzOfTheDay;
                }
            }
            set
            {
                lock (lockObject)
                {
                    _FirstQuizzOfTheDay = value;
                }
            }
        }
        private int _AntiBotProtectionLvL;
        public int AntiBotProtectionLvL
        {
            get
            {
                lock (lockObject)
                {
                    return _AntiBotProtectionLvL;
                }
            }
            set
            {
                lock (lockObject)
                {
                    _AntiBotProtectionLvL = value;
                }
            }
        }
        public string ChatWithBot { get; private set; }
        public string ReleaseBot { get; private set; }
        public bool isActiveSub {  get; set; } 
        public string GPTApiToken { get; private set; }
        private string _SummonerRegion;
        public string SummonerRegion
        {
            get
            {
                lock (lockObject)
                {
                    return _SummonerRegion;
                }
            }
            set
            {
                lock (lockObject)
                {
                    _SummonerRegion = value;
                }
            }
        }
        private bool _IsSilent;
        public bool IsSilent
        {
            get
            {
                lock (lockObject)
                {
                    return _IsSilent;
                }
            }
            set
            {
                lock (lockObject)
                {
                    _IsSilent = value;
                }
            }
        }
        public string MySQL_IP { get; private set; }
        public int MySQL_Port { get; private set; }
        public string PichkaListFileName { get; private set; }
        public string MediaListFileName { get; private set; }
        public string ChannelListFileName { get; private set; }
        public string DicFileName { get; private set; }
        public string DicWhiteListFileName { get; private set; }
        public string UserblacklistFileName { get; private set; }
        private IllSingleton() { }

        public static IllSingleton GetInstance()
        {
            if (_instance == null)
            {
                lock (lockAtCreationObject)
                {
                    _instance = new IllSingleton
                    {
                        inAmatch = false,
                        debug = true,
                        autoPred = true,
                        BroadcasterIsOnline = false,
                        FirstQuizzOfTheDay = true,
                        AntiBotProtectionLvL = 0,
                        wisEnabled = true,
                        rootUser = "bot_illskillz",
                        IsSilent = false,
                        PichkaListFileName = "pichkaList.txt",
                        MediaListFileName = "mediaList.txt",
                        ChannelListFileName = "channelList.txt",
                        DicFileName = "dic.txt",
                        DicWhiteListFileName = "dicWhiteList.txt",
                        UserblacklistFileName = "userblacklist.txt"
                    };
                    Config config = new Config(IllSkillzBotMain.GetConfigPath());
                    SetUpSingleton(_instance, config).GetAwaiter().GetResult();
                }
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
            singleton.MySQL_IP = config.GetBotConfigs().MySQL_IP;
            singleton.MySQL_Port = config.GetBotConfigs().MySQL_Port;
            singleton.isActiveSub = true;
            await TempDataReader.ReadGameStats().ConfigureAwait(false);
        }
    }
}
