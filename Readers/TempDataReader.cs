using IllSkillzBot;
using SkillzBot.WRITERS;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkillzBot.API.Riot;
using SkillzBot.Singleton;

namespace SkillzBot.Readers
{
    internal class TempDataReader
    {
        static readonly string dataPath = IllSkillzBotMain.GetDataPath().uniquePath;
        static readonly string mediaQueueDir = Path.Combine(dataPath, "mediaqueue.txt");
        readonly private static string dailyStatsDir = Path.Combine(dataPath, "dailyStats.txt");
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
                Log.WriteLog(e, "GetUserIDByTreckID()");
                return -1;
            }
            return -1;
        }
        public static async Task ReadGameStats()
        {
            try
            {
                var singleton = IllSingleton.GetInstance();
                IEnumerable<String> stats = File.ReadLines(dailyStatsDir);
                if (stats.Count() == 0)
                {
                    var t = await RiotAPI.GetRankBySummonerAsync().ConfigureAwait(false);
                    singleton.startLP = int.Parse(t[1]);
                    singleton.elo = t[0];
                    singleton.earnedLP = 0;
                    singleton.numLoose = 0;
                    singleton.numGames = 0;
                    singleton.numWins = 0;
                    singleton.tier = t[2];
                }
                else
                {
                    string[] subs = stats.First().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    singleton.startLP = int.Parse(subs[0]);
                    singleton.elo = subs[1];
                    singleton.earnedLP = int.Parse(subs[2]);
                    singleton.numLoose = int.Parse(subs[3]);
                    singleton.numGames = int.Parse (subs[4]);
                    singleton.numWins = int.Parse(subs[5]);
                    singleton.tier = subs[6];
                }
            }
            catch (Exception e)
            {
                Log.WriteLog(e, "readGameStats()");
            }
        }
    }
}