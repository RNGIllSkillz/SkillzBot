using System.Text;
using System;
using System.IO;
using SkillzBot.WRITERS;
using SkillzBot.IRC;
using Newtonsoft.Json;
using System.Threading.Tasks;
using SkillzBot.PubSub;
using SkillzBot.Singleton;
using SkillzBot.MYSQL;
using SkillzBot.Readers;
using SkillzBot.API.Twitch;
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
        static string ConfigPath;
        private static PubSubClient PubSubClientInst;
        private static IllSingleton singleton;
        private static ManualResetEventSlim _resetEvent = new ManualResetEventSlim(false);
        static async Task Main()
        {
            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("ru-RU");
            CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("ru-RU");

            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += new UnhandledExceptionEventHandler(MainHandler);
            Console.OutputEncoding = Encoding.UTF8;

            ///docker
            dataPath = Path.Combine(currentDomain.BaseDirectory, "Channels_Data/general_hs_/DATA/");
            Directory.CreateDirectory(dataPath);
            //string channelName = "general_hs_";
            ConfigPath = Path.Combine(dataPath, "general_hs_.ini");
            singleton = IllSingleton.GetInstance();

            /*
            Console.WriteLine("Введите название канала");
            string channelName = Console.ReadLine().ToLower();

            dataPath = AppDomain.CurrentDomain.BaseDirectory + $"Channels_Data/{channelName}/DATA/";
            Directory.CreateDirectory(dataPath);

            ConfigPath = dataPath + channelName + ".ini";

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

            singleton = IllSingleton.GetInstance();
            */
            MySQL MySQLClientInst = new MySQL();

            await StartUpConfigs().ConfigureAwait(false);
            TtvIRCClient TtvIRCClientInst = new TtvIRCClient();
            PubSubClientInst = new PubSubClient();
            QuartzBackgroundTaskManager quartzBackgroundTaskManager = new QuartzBackgroundTaskManager();
            await quartzBackgroundTaskManager.ScheduleTasks().ConfigureAwait(false);
            _resetEvent.Wait();
            /*
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
                    case "addmod":
                        await TtvAPI.AddChannelModerator("909916537").ConfigureAwait(false);
                        break;
                }
        }
        */
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
                    BrodcasterId = "Brodcaster TTV ID",
                    CenceleUval = "Reward ID",
                    EmoteModeId = "Reward ID",
                    uvalMod = "Reward ID",
                    UvalId = "Reward ID",
                    Pi4KaId = "Reward ID",
                    ZakazTrekaId = "Reward ID",
                    UvalSabId = "Reward ID",
                    UvalVipId = "Reward ID",
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
        public static string GetDataPath()
        {
            return dataPath;
        }
        public static string GetConfigPath()
        {
            return ConfigPath;
        }
        public static void PubSubReconnect()
        {
            if (PubSubClientInst != null)
            {
                PubSubClientInst.Dispose();
                PubSubClientInst = null;
                GC.Collect();
                Thread.Sleep(2000);
                PubSubReconnects++;
                if (PubSubReconnects < 15)
                    PubSubClientInst = new PubSubClient();
                else
                {
                    Log.WriteLog(null, "PubSub reconnection ERROR! Will try to reconnect in 10 min");
                    Thread.Sleep(60000);
                    PubSubReconnect();
                }
            }
            else
            {
                PubSubClientInst = new PubSubClient();
            }
        }
    }
}