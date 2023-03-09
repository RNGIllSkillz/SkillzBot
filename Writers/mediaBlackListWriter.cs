using System;
using System.IO;
using System.Threading;
using IllSkillzBot;

namespace SkillzBot.WRITERS
{
    internal class MediaBlackListWriter
    {
        readonly static Mutex mutexObj = new Mutex();
        readonly static string dataPath = IllSkillzBotMain.GetChannelName();
        readonly static string filePath = Path.Combine(dataPath, "mediaList.txt");

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
