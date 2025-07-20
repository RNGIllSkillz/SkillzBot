using System;
using System.IO;
using System.Threading;
using IllSkillzBot;
using System.Threading.Tasks;

namespace SkillzBot.WRITERS
{
    internal class ExtractMessage
    {
        static Mutex mutexObj = new Mutex();
        readonly static string dataPath = IllSkillzBotMain.GetDataPath().uniquePath;
        readonly static string filePath = Path.Combine(dataPath, "MessageExtracted.txt");


        public void ExtractMessageTask(string Message)
        {
            if (!File.Exists(filePath))
            {
                try
                {
                    File.Create(filePath);
                }
                catch (Exception)
                {
                    throw;
                }
            }
            mutexObj.WaitOne();
            FileInfo file = new FileInfo(filePath);
            while (IsFileLocked(file))
            {
                Task.Delay(100);
            }
            try
            {
                File.AppendAllText(filePath, $"{Message}" + Environment.NewLine);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            }
            mutexObj.ReleaseMutex();
        }
        protected virtual bool IsFileLocked(FileInfo file)
        {
            try
            {
                using FileStream stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None);
                stream.Close();
            }
            catch (IOException)
            {
                return true;
            }
            return false;
        }
    }
}
