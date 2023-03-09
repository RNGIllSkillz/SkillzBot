using System;
using System.IO;
using System.Threading;
using IllSkillzBot;

namespace SkillzBot.WRITERS
{
    internal class FlagWriter
    {
        readonly static Mutex mutexObj = new Mutex();
        readonly static string dataPath = IllSkillzBotMain.GetChannelName();
        readonly static string filePath = Path.Combine(dataPath, "Flags.txt");

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
                File.AppendAllText(filePath, Message, System.Text.Encoding.UTF8);
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