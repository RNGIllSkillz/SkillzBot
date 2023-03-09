using IllSkillzBot;
using Newtonsoft.Json;
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
        static readonly Mutex mutexObj = new Mutex();
        static readonly string dataPath = IllSkillzBotMain.GetChannelName();
        static readonly string filePath = Path.Combine(dataPath, $"{IllSingleton.GetInstance().ChannelName}.ini");

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
                SettingsObject Settings = new SettingsObject
                {
                    Summoner_Name = IllSingleton.GetInstance().SUMMONER_NAME,
                    ChannelName = IllSingleton.GetInstance().ChannelName,
                    BotTwitchName = IllSingleton.GetInstance().BotTwitchName,
                    BotTwitchAuth = IllSingleton.GetInstance().BotTwitchAuth,
                    TApiAccessToken = IllSingleton.GetInstance().TApiAccessToken,
                    TApiClientId = IllSingleton.GetInstance().TApiClientId,
                    YouTubeApiToken = IllSingleton.GetInstance().YouTubeApiToken,
                    RiotApiToken = IllSingleton.GetInstance().RiotApiToken,
                    BrodcasterId = IllSingleton.GetInstance().BrodcasterId,
                    CenceleUval = IllSingleton.GetInstance().CenceleUval,
                    EmoteModeId = IllSingleton.GetInstance().EmoteModeId,
                    EnglishWis = IllSingleton.GetInstance().EnglishWis,
                    UvalId = IllSingleton.GetInstance().UvalId,
                    Pi4KaId = IllSingleton.GetInstance().Pi4KaId,
                    ZakazTrekaId = IllSingleton.GetInstance().ZakazTrekaId,
                    UvalSabId = IllSingleton.GetInstance().UvalSabId,
                    UvalVipId = IllSingleton.GetInstance().UvalVipId,
                    MySQL_User = IllSingleton.GetInstance().MySQL_User,
                    MySQL_password = IllSingleton.GetInstance().MySQL_password,
                    StreamElementsApiToken = IllSingleton.GetInstance().StreamElementsApiToken
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
