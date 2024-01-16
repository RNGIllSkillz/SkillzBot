using IllSkillzBot;
using System;
using System.IO;
using System.Threading;
namespace SkillzBot.Writers
{
    internal sealed class GameStatsWriter
    {
        readonly static Mutex mutexObj = new Mutex();
        readonly static string dataPath = IllSkillzBotMain.GetDataPath().uniquePath;
        readonly static string filePath = Path.Combine(dataPath, "dailyStats.txt");

        public static void Write(string stats)
        {
            string input = stats + Environment.NewLine;
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
                File.WriteAllText(filePath, input);
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
