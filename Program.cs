using System.Text;
using System;
using System.IO;
using SkillzBot.WRITERS;
using SkillzBot.IRC;
using Newtonsoft.Json;
using SkillzBot.MODELS;
using System.Threading.Tasks;
using SkillzBot.PubSub;
using SkillzBot.Singleton;
using SkillzBot.MYSQL;
using SkillzBot.Readers;
using SkillzBot.API.StreamElements;
using SkillzBot.API.Riot;
using SkillzBot.API.Twitch;
using SkillzBot.API.YouTube;
using System.Resources;
using System.Globalization;
using SkillzBot.IllSTRINGS;
using System.Threading;
using SkillzBot.JSON.Settings;

namespace IllSkillzBot
{
    class IllSkillzBotMain
    {
        static private int PubSubReconnects = 0;
        static string dataPath;
        private static PubSubClient PubSubClientInst;
        private static readonly IllSingleton singleton = IllSingleton.GetInstance();
        static async Task Main()
        {
            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("ru-RU");
            CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("ru-RU");

            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += new UnhandledExceptionEventHandler(MainHandler);
            Console.OutputEncoding = Encoding.UTF8;
            

            Console.WriteLine("Введите название канала");
            string channelName = Console.ReadLine().ToLower();

            dataPath = AppDomain.CurrentDomain.BaseDirectory + $"Channels_Data/{channelName}/DATA/";
            Directory.CreateDirectory(dataPath);

            string ConfigPath = dataPath + channelName + ".ini";
            if (!File.Exists(ConfigPath))
            {
                Console.WriteLine("Файл конфигурации не найдет. Первое подключение к каналу?");
                Console.WriteLine($"Заполните папку {dataPath} и повторите попытку");
                Console.WriteLine($"Нужна помощь? да/нет");
                string com = Console.ReadLine().ToLower();                    
                if (com == "да" || com == "д" || com == "yes" || com == "y")
                {
                    Console.WriteLine("Создаю дефолтные файлы");
                    Console.WriteLine(CreadDefoults(dataPath));
                    Console.WriteLine("Файл dic.txt содержит словарь запрещенных слов. Заполнять крайне аккуратно!");
                    Console.WriteLine("Файл dicWhiteList.txt содержит словарь разрешенных слов. Заполнять крайне аккуратно!");
                    Console.WriteLine("Оба этих файла работают в тандеме друг с другом для модерации запрещенных слов в чате.");
                    Console.WriteLine("Для получания отлаженных файлов dic.txt и dicWhiteList.txt, обратитесь к RNG. Их также можно скопировать из папок другик каналов, если такие имеются");
                    Console.WriteLine("Файл mediaqueue.txt содержит текущую очередь треков. Файл не нуждается в заполнении");
                    Console.WriteLine("Файл userblacklist.txt содержит список людей (их ID), которым запрещен заказ треков");
                    Console.WriteLine("Файл mediaList.txt содержит список запрещенных треков (ID треков)");
                    Console.WriteLine("Файл channelList.txt содержит список запрещенных ютуб каналов (названий каналов)");
                    Console.WriteLine("Файл pichkaList.txt содержит список 18+ пичек. Следует добавлять всего 1 строчку из пички, желательно самую выделяющуюся");
                    Console.WriteLine("Файл dailyStats.txt содержит буфер статистики призывателя. Файл не нуждается в заполнении");
                    Console.WriteLine("Все файлы должны заполняться построчно");
                    Console.WriteLine("Создаю дефолтный файл конфигурации");
                    Console.WriteLine(CreateDefoultConfig(ConfigPath, channelName));
                    Console.WriteLine($"Откройте файл {ConfigPath} и заполните его. После чего можно будет повторить попытку запуска бота");
                    Console.WriteLine("Окно сейчас закроется. Нажмите Enter");
                    Console.ReadLine();
                    Environment.Exit(1);
                }
                else
                    Environment.Exit(1);
            }

            Config config = new Config(ConfigPath);            
            await SetUpSingleton(config).ConfigureAwait(false);

            MySQL MySQLClientInst = new MySQL();

            await StartUpConfigs().ConfigureAwait(false);
            TtvIRCClient TtvIRCClientInst = new TtvIRCClient();
            PubSubClientInst = new PubSubClient();
            QuartzBackgroundTaskManager quartzBackgroundTaskManager = new QuartzBackgroundTaskManager();
            await quartzBackgroundTaskManager.ScheduleTasks().ConfigureAwait(false);
            while (true)
            {
                string input = Console.ReadLine();
                Console.Clear();
                Console.WriteLine(channelName);
                switch (input)
                {
                    case "connect":
                        PubSubReconnects = 0;
                        PubSubReconnect();
                        break;
                    case "reward":
                        await TtvAPI.UpdateReward(singleton.CenceleUval, STRINGS.UpdateRewardTitleOrig, 10000, STRINGS.UpdateRewardPromptOrig, true, true).ConfigureAwait(false);
                        break;
                }                    
            }
        }
        private static async Task StartUpConfigs()
        {            
            if (await TtvAPI.GetStreamStatus().ConfigureAwait(false))
            {
                singleton.BroadcasterIsOnline = true;
                Console.WriteLine($"{singleton.ChannelName} is LIVE!");
            }
            else
            {
                singleton.BroadcasterIsOnline = false;
                Console.WriteLine($"{singleton.ChannelName} is Offline!");
            }
        }
        static async Task SetUpSingleton(Config config)
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
            singleton.EnglishWis = config.GetBotConfigs().EnglishWis;
            singleton.MySQL_User = config.GetBotConfigs().MySQL_User;
            singleton.MySQL_password = config.GetBotConfigs().MySQL_password;
            singleton.StreamElementsApiToken = config.GetBotConfigs().StreamElementsApiToken;
            singleton.StreamElementsID = config.GetBotConfigs().StreamElementsID;
            singleton.ChatWithBot = config.GetBotConfigs().ChatWithBot;
            singleton.ReleaseBot = config.GetBotConfigs().ReleaseBot;
            singleton.GPTApiToken = config.GetBotConfigs().GPTApiToken;
            singleton.autoPred = true;
            singleton.BroadcasterIsOnline = false;
            singleton.FirstQuizzOfTheDay = true;
            singleton.AntiBotProtectionLvL = 0;
            singleton.wisEnabled = true;
            singleton.rootUser = "rng_backtrack";
            await TempDataReader.ReadGameStats().ConfigureAwait(false);
        }
        static string CreadDefoults(string DataPath)
        {
            string dicDir = DataPath + "dic.txt";
            string dicDirWhite = DataPath + "dicWhiteList.txt";
            string mediaQueueDir = DataPath + "mediaqueue.txt";
            string userBlackListDir = DataPath + "userblacklist.txt";
            string mediaBlackList = DataPath + "mediaList.txt";
            string channelBlackList = DataPath + "channelList.txt";
            string pichkaList = DataPath + "pichkaList.txt";
            string dailyStatsDir = DataPath + "dailyStats.txt";

            try
            {
                if (!File.Exists(dicDir))
                    File.Create(dicDir);

                if (!File.Exists(dicDirWhite))
                    File.Create(dicDirWhite);

                if (!File.Exists(mediaQueueDir))
                    File.Create(mediaQueueDir);

                if (!File.Exists(userBlackListDir))
                    File.Create(userBlackListDir);

                if (!File.Exists(mediaBlackList))
                    File.Create(mediaBlackList);

                if (!File.Exists(channelBlackList))
                    File.Create(channelBlackList);

                if (!File.Exists(dailyStatsDir))
                    File.Create(dailyStatsDir);

                if (!File.Exists(pichkaList))
                    File.Create(pichkaList);
            }
            catch (Exception e)
            {
                return e.Message;
            }
            return "Файлы были успешно созданны";

        }
        static string CreateDefoultConfig(string ConfPath,string ChannelName)
        {
            try
            {
                SettingsJson Settings = new SettingsJson
                {
                    SummonerName = "Имя Призывателя",
                    ChannelName = ChannelName,
                    BotTwitchName = "Имя бота",
                    BotTwitchAuth = "oAuth для аккаунта бота",
                    TApiAccessToken = "Token для доступа к API Twitch",
                    TApiClientId = "ClientId для доступа к API Twitch",
                    YouTubeApiToken = "Token для доступа а API YouTube",
                    RiotApiToken = "Token для доступа а API Riot Games",
                    BrodcasterId = "ID",
                    CenceleUval = "ID",
                    EmoteModeId = "ID",
                    EnglishWis = "ID",
                    UvalId = "ID",
                    Pi4KaId = "ID",
                    ZakazTrekaId = "ID",
                    UvalSabId = "ID",
                    UvalVipId = "ID",
                    MySQL_User = "MySQL username",
                    MySQL_password = "MySQL password"
                };
                File.WriteAllText(ConfPath, JsonConvert.SerializeObject(Settings, Formatting.Indented));
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            return "Создан дефолтный файл конфигурации";
        }
        static void MainHandler(object sender, UnhandledExceptionEventArgs args)
        {
            Exception e = (Exception)args.ExceptionObject;
            Log.WriteLog(e, "MainHandler caught : ");
        }
        public static string GetChannelName()
        {
            return dataPath;
        }       
        public static void PubSubReconnect()
        {
            if (PubSubClientInst != null)
            {
                PubSubClientInst.Dispose();
                PubSubClientInst = null;
                GC.Collect();
                Thread.Sleep(10000);
                PubSubReconnects++;
                if (PubSubReconnects < 15)
                    PubSubClientInst = new PubSubClient();
                else
                    Log.WriteLog(null, "PubSub reconnection ERROR");
            }
            else
            {
                PubSubClientInst = new PubSubClient();
            }
        }
    }
}