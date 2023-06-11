using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IllSkillzBot;
using SkillzBot.API.StreamElements;

namespace SkillzBot.WRITERS
{
    internal class MediaqueueWriter
    {
        static Mutex mutexObj = new Mutex();
        readonly static string dataPath = IllSkillzBotMain.GetDataPath();
        readonly static string filePath = Path.Combine(dataPath, "mediaqueue.txt");

        public static void Write(int userID, string trackID)
        {
            string input = $"{userID} {trackID}" + Environment.NewLine;
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
                File.AppendAllText(filePath, input);
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
        public static async Task MediaQueueFlush()
        {
            var checkqueue = await StreamElementsAPI.GetCurrentSong().ConfigureAwait(false);
            if (checkqueue == null)
            {
                File.WriteAllText(filePath, String.Empty);
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
