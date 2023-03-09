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
        static readonly string dataPath = IllSkillzBotMain.GetChannelName();
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
                        return Convert.ToInt32(t[0]);
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
                IEnumerable<String> stats = File.ReadLines(dailyStatsDir);
                if (stats.Count() == 0)
                {
                    var t = await RiotAPI.GetRankBySummonerAsync().ConfigureAwait(false);
                    IllSingleton.GetInstance().startLP = Convert.ToInt32(t[1]);
                    IllSingleton.GetInstance().elo = t[0];
                    IllSingleton.GetInstance().earnedLP = 0;
                    IllSingleton.GetInstance().numLoose = 0;
                    IllSingleton.GetInstance().numGames = 0;
                    IllSingleton.GetInstance().numWins = 0;
                    IllSingleton.GetInstance().tier = t[2];
                }
                else
                {
                    char[] separators = new char[] { ' ' };
                    string[] subs = stats.First().Split(separators, StringSplitOptions.RemoveEmptyEntries);
                    IllSingleton.GetInstance().startLP = Convert.ToInt32(subs[0]);
                    IllSingleton.GetInstance().elo = subs[1];
                    IllSingleton.GetInstance().earnedLP = Convert.ToInt32(subs[2]);
                    IllSingleton.GetInstance().numLoose = Convert.ToInt32(subs[3]);
                    IllSingleton.GetInstance().numGames = Convert.ToInt32(subs[4]);
                    IllSingleton.GetInstance().numWins = Convert.ToInt32(subs[5]);
                    IllSingleton.GetInstance().tier = subs[6];
                }
            }
            catch (Exception e)
            {
                Log.WriteLog(e, "readGameStats()");
            }
        }
    }
}