using System.Text;
using System;
using System.IO;
using SkillzBot.WRITERS;
using SkillzBot.IRC;
using Newtonsoft.Json;
using System.Threading.Tasks;
using SkillzBot.Singleton;
using SkillzBot.MYSQL;
using SkillzBot.Readers;
using SkillzBot.API.Twitch;
using System.Globalization;
using SkillzBot.IllSTRINGS;
using System.Threading;
using SkillzBot.JSON.Settings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore;
using System.Collections.Generic;
using SkillzBot.MODELS;

namespace SkillzBot
{
    internal class CreateRootInstance
    {
        static string dataPath;
        static string sharedPath;
        static string ConfigPath;
        private static IllSingleton singleton;
        public static async Task CreateRootInsrance(string channelName, AppDomain currentDomain)
        {
            //string channelName = Environment.GetEnvironmentVariable("ENV_CHANNEL_NAME");
            dataPath = Path.Combine(currentDomain.BaseDirectory, $"Channels_Data/{channelName}/DATA/");
            sharedPath = Path.Combine(currentDomain.BaseDirectory, $"Channels_Data/_shared/");
            Directory.CreateDirectory(dataPath);
            ConfigPath = Path.Combine(dataPath, $"{channelName}.ini");

            if (!File.Exists(ConfigPath))
            {
                Console.WriteLine(CreateDefoults(dataPath, sharedPath));
                Console.WriteLine(CreateDefoultConfig(ConfigPath, channelName));
            }

            singleton = IllSingleton.GetInstance();
            MySQL MySQLClientInst = new MySQL();

            await StartUpConfigs().ConfigureAwait(false);
            TtvIRCClient TtvIRCClientInst = new TtvIRCClient();
            QuartzBackgroundTaskManager quartzBackgroundTaskManager = new QuartzBackgroundTaskManager();
            await quartzBackgroundTaskManager.ScheduleTasks().ConfigureAwait(false);
        }
       
        public static ConfPathes GetDataPath()
        {
            return new ConfPathes
            {
                sharedPath = sharedPath,
                uniquePath = dataPath
            };
        }
        public static string GetConfigPath()
        {
            return ConfigPath;
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
        static string CreateDefoults(string DataPath, string SharedPath)
        {
            string dicDir = SharedPath + "dic.txt";
            string dicDirWhite = SharedPath + "dicWhiteList.txt";
            string mediaQueueDir = DataPath + "mediaqueue.txt";
            string userBlackListDir = DataPath + "userblacklist.txt";
            string mediaBlackList = DataPath + "mediaList.txt";
            string channelBlackList = DataPath + "channelList.txt";
            string pichkaList = SharedPath + "pichkaList.txt";
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
        static string CreateDefoultConfig(string ConfPath, string ChannelName)
        {
            try
            {/*
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
                };*/
                SettingsJson Settings = new SettingsJson();
                File.WriteAllText(ConfPath, JsonConvert.SerializeObject(Settings, Formatting.Indented));
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            return "Создан дефолтный файл конфигурации";
        }
    }
}
