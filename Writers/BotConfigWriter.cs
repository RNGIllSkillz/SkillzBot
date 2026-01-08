using IllSkillzBot;
using Newtonsoft.Json;
using SkillzBot.JSON.Settings;
using SkillzBot.MODELS;
using System;
using System.IO;
using SkillzBot.Singleton;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Writers
{
    internal class BotConfigWriter
    {
        private static readonly SemaphoreSlim _fileSemaphore = new SemaphoreSlim(1, 1);
        private static readonly ConfPathes dataPath = IllSkillzBotMain.GetDataPath();
        private static readonly string filePath = Path.Combine(dataPath.uniquePath, $"{IllSingleton.Config.ChannelName}.ini");

        public static async Task WriteAsync()
        {
            await _fileSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));

                SettingsJson Settings = new SettingsJson
                {
                    SummonerName = IllSingleton.Game.SummonerName,
                    ChannelName = IllSingleton.Config.ChannelName,
                    BotTwitchName = IllSingleton.Config.BotTwitchName,
                    BotTwitchAuth = IllSingleton.Config.BotTwitchAuth,
                    TApiAccessToken = IllSingleton.Config.TApiAccessToken,
                    TApiClientId = IllSingleton.Config.TApiClientId,
                    YouTubeApiToken = IllSingleton.Config.YouTubeApiToken,
                    RiotApiToken = IllSingleton.Config.RiotApiToken,
                    BrodcasterId = IllSingleton.Config.BroadcasterId,
                    CenceleUval = IllSingleton.Config.ChannelIds.CenceleUval,
                    EmoteModeId = IllSingleton.Config.ChannelIds.EmoteModeId,
                    uvalMod = IllSingleton.Config.ChannelIds.UvalMod,
                    UvalId = IllSingleton.Config.ChannelIds.UvalId,
                    Pi4KaId = IllSingleton.Config.ChannelIds.Pi4KaId,
                    ZakazTrekaId = IllSingleton.Config.ChannelIds.ZakazTrekaId,
                    UvalSabId = IllSingleton.Config.ChannelIds.UvalSabId,
                    UvalVipId = IllSingleton.Config.ChannelIds.UvalVipId,
                    MySQL_User = IllSingleton.Config.Database.Username,
                    MySQL_password = IllSingleton.Config.Database.Password,
                    StreamElementsApiToken = IllSingleton.Config.StreamElementsApiToken,
                    StreamElementsID = IllSingleton.Config.StreamElementsID,
                    GPTApiToken = IllSingleton.Config.GPTApiToken,
                    SummonerRegion = IllSingleton.Game.SummonerRegion,
                    MySQL_IP = IllSingleton.Config.Database.Host,
                    MySQL_Port = IllSingleton.Config.Database.Port,
                    ChatFilterLvl = IllSingleton.State.ChatFilterLvl,
                    DiscordBotToken = IllSingleton.Config.DiscordBotToken,
                    DiscordNoteID = IllSingleton.Config.DiscordNoteID,
                    DiscordSpamID = IllSingleton.Config.DiscordSpamID
                };

                string json = JsonConvert.SerializeObject(Settings, Formatting.Indented);
                await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error writing config: {e.Message}");
            }
            finally
            {
                _fileSemaphore.Release();
            }
        }
    }
}