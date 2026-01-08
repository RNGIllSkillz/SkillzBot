using IllSkillzBot;
using Microsoft.Extensions.Logging;
using SkillzBot.Hosts;
using System;
using System.Collections.Generic;
using System.IO;

namespace SkillzBot.Readers
{
    internal class TempDataReader
    {
        static readonly string dataPath = IllSkillzBotMain.GetDataPath().uniquePath;
        static readonly string mediaQueueDir = Path.Combine(dataPath, "mediaqueue.txt");
        private static readonly ILogger<TempDataReader> _logger = IllServiceProvider.GetLogger<TempDataReader>();

        public static int GetUserIDByTreckID(string treckID)
        {
            try
            {
                if (!File.Exists(mediaQueueDir)) return -1;

                IEnumerable<string> queueList = File.ReadLines(mediaQueueDir);
                foreach (string userQ in queueList)
                {
                    var t = userQ.Split(' ');
                    if (t.Length > 1 && t[1] == treckID)
                    {
                        if (int.TryParse(t[0], out int userId))
                            return userId;
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "GetUserIDByTreckID()");
                return -1;
            }
            return -1;
        }
    }
}