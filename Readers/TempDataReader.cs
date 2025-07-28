using IllSkillzBot;
using SkillzBot.WRITERS;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkillzBot.Singleton;
using SkillzBot.API.RiotGames;
using Microsoft.Extensions.Logging;
using SkillzBot.Hosts;

namespace SkillzBot.Readers
{
    internal class TempDataReader
    {
        static readonly string dataPath = IllSkillzBotMain.GetDataPath().uniquePath;
        static readonly string mediaQueueDir = Path.Combine(dataPath, "mediaqueue.txt");
        readonly private static string dailyStatsDir = Path.Combine(dataPath, "dailyStats.txt");
        private static readonly ILogger<TempDataReader> _logger = IllServiceProvider.GetLogger<TempDataReader>();
        public static int GetUserIDByTreckID(string treckID)
        {
            IEnumerable<String> QueueList;
            try
            {
                QueueList = File.ReadLines(mediaQueueDir);
                foreach (string userQ in QueueList)
                {
                    var t = userQ.Split(' ');
                    if (t[1] == treckID)
                        return int.Parse(t[0]);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "GetUserIDByTreckID()");
                return -1;
            }
            return -1;
        }
        public static async Task<string> ReadGameStats()
        {
            try
            {
                IEnumerable<String> stats = File.ReadLines(dailyStatsDir);
                if (stats.Count() == 0)
                {                    
                    var t = await RiotAPI.GetRankBySummonerAsync().ConfigureAwait(false);
                    return string.Join(" ", int.Parse(t[1]), t[0], "0", "0", "0", "0", t[2]);
                    //IllSingleton.Game.StartLP = int.Parse(t[1]);
                    //IllSingleton.Game.Elo = t[0];
                    //IllSingleton.Game.EarnedLP = 0;
                    //IllSingleton.Game.NumLosses = 0;
                    //IllSingleton.Game.NumGames = 0;
                    //IllSingleton.Game.NumWins = 0;
                    //IllSingleton.Game.Tier = t[2];
                }
                else
                {
                    return stats.First();
                    //IllSingleton.Game.StartLP = int.Parse(subs[0]);
                    //IllSingleton.Game.Elo = subs[1];
                    //IllSingleton.Game.EarnedLP = int.Parse(subs[2]);
                    //IllSingleton.Game.NumLosses = int.Parse(subs[3]);
                    //IllSingleton.Game.NumGames = int.Parse (subs[4]);
                    //IllSingleton.Game.NumWins = int.Parse(subs[5]);
                    //IllSingleton.Game.Tier = subs[6];
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "readGameStats()");
                Console.WriteLine($"readGameStats() {e.Message}");
                return null;
            }
        }
    }
}