using System;
using System.IO;
using System.Threading;
using IllSkillzBot;
using Microsoft.Extensions.Logging;
using SkillzBot.Hosts;

namespace SkillzBot.WRITERS
{
    internal class UserBlackListWriter
    {
        readonly static Mutex mutexObj = new Mutex();
        #region Getting Directories

        readonly static string dataPath = IllSkillzBotMain.GetDataPath().uniquePath;
        readonly static string filePath = Path.Combine(dataPath, "userblacklist.txt");
        private static readonly ILogger<UserBlackListWriter> _logger = IllServiceProvider.GetLogger<UserBlackListWriter>();
        #endregion

        public static void Write(string Message)
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
                File.AppendAllText(filePath, Message + Environment.NewLine);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "UserBlackListWriter");
            }
            mutexObj.ReleaseMutex();
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
