using System;
using System.IO;
using System.Threading;
using IllSkillzBot;
using Microsoft.Extensions.Logging;
using SkillzBot.Hosts;
using SkillzBot.Utils;

namespace SkillzBot.WRITERS
{
    internal class FlagWriter
    {
        readonly static Mutex mutexObj = new Mutex();
        readonly static string dataPath = IllSkillzBotMain.GetDataPath().uniquePath;
        readonly static string filePath = Path.Combine(dataPath, "Flags.txt");
        private static readonly ILogger<FlagWriter> _logger = IllServiceProvider.GetLogger<FlagWriter>();
        public static void FlagWriterTask(string Message)
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
                using var fileStream = File.Open(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(fileStream);
                writer.WriteLine(DateTime.Now + " " + Message);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "FlagWriterTask()");
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
                using (FileStream stream = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
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