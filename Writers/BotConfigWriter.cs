using IllSkillzBot;
using Newtonsoft.Json;
using SkillzBot.JSON.Settings;
using SkillzBot.MODELS;
using SkillzBot.Singleton;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace SkillzBot.Writers
{
    internal class BotConfigWriter
    {
        private static readonly Mutex mutexObj = new Mutex();
        private static readonly ConfPathes dataPath = IllSkillzBotMain.GetDataPath();
        private static readonly IllSingleton singleton = IllSingleton.GetInstance();
        private static readonly string filePath = Path.Combine(dataPath.uniquePath, $"{singleton.ChannelName}.ini");

        public static void Write()
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException();
            }
            mutexObj.WaitOne();
            FileInfo file = new FileInfo(filePath);
            while (IsFileLocked(file))
            {
                Thread.Sleep(100);
            }
            try
            {
                SettingsJson Settings = new SettingsJson
                {
                    SummonerName = singleton.SUMMONER_NAME,
                    ChannelName = singleton.ChannelName,
                    BotTwitchName = singleton.BotTwitchName,
                    BotTwitchAuth = singleton.BotTwitchAuth,
                    TApiAccessToken = singleton.TApiAccessToken,
                    TApiClientId = singleton.TApiClientId,
                    YouTubeApiToken = singleton.YouTubeApiToken,
                    RiotApiToken = singleton.RiotApiToken,
                    BrodcasterId = singleton.BrodcasterId,
                    CenceleUval = singleton.CenceleUval,
                    EmoteModeId = singleton.EmoteModeId,
                    uvalMod = singleton.uvalMod,
                    UvalId = singleton.UvalId,
                    Pi4KaId = singleton.Pi4KaId,
                    ZakazTrekaId = singleton.ZakazTrekaId,
                    UvalSabId = singleton.UvalSabId,
                    UvalVipId = singleton.UvalVipId,
                    MySQL_User = singleton.MySQL_User,
                    MySQL_password = singleton.MySQL_password,
                    StreamElementsApiToken = singleton.StreamElementsApiToken,
                    StreamElementsID = singleton.StreamElementsID,
                    ChatWithBot = singleton.ChatWithBot,
                    ReleaseBot = singleton.ReleaseBot,
                    GPTApiToken = singleton.GPTApiToken,
                    SummonerRegion = singleton.SummonerRegion,
                    MySQL_IP = singleton.MySQL_IP,
                    MySQL_Port = singleton.MySQL_Port
                };
                File.WriteAllText(filePath, JsonConvert.SerializeObject(Settings, Formatting.Indented));               
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            }
            finally
            {
                mutexObj.ReleaseMutex();
            }
        }
        private static bool IsFileLocked(FileInfo file)
        {
            try
            {
                using (FileStream stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    stream.Close();
                }
            }
            catch (IOException)
            {
                return true;
            }
            return false;
        }
    }
}
