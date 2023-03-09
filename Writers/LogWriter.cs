using System;
using System.IO;
using System.Threading;
using IllSkillzBot;
namespace SkillzBot.WRITERS
{
    internal class LogWriter : IDisposable
    {
        private readonly Mutex _mutex = new Mutex();
        private readonly string _logFilePath;

        public LogWriter()
        {
            string channelName = IllSkillzBotMain.GetChannelName();
            _logFilePath = Path.Combine(channelName, "log.txt");
        }

        public void WriteLog(Exception ex, string message)
        {
            EnsureLogFileExists();
            _mutex.WaitOne();
            try
            {
                using var fileStream = File.Open(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(fileStream);
                writer.WriteLine(DateTime.Now);
                writer.WriteLine(message);
                if (ex != null)
                {
                    writer.WriteLine(ex.Message);
                    if (ex.InnerException != null)
                    {
                        writer.WriteLine($"Inner: {ex.InnerException.Message}");
                        writer.WriteLine($"Inner: {ex.InnerException.StackTrace}");
                    }
                    writer.WriteLine(ex.StackTrace);
                }
                writer.WriteLine("////////////////////////////////////////////////////////////////");
            }
            finally
            {
                _mutex.ReleaseMutex();
            }
        }
        private void EnsureLogFileExists()
        {
            if (!File.Exists(_logFilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath));
                File.Create(_logFilePath).Dispose();
            }
        }
        public void Dispose()
        {
            _mutex.Dispose();
        }
        
    }

    internal class Log
    {
        public static void WriteLog(Exception ex, string message)
        {
            using LogWriter logWriter = new LogWriter();
            logWriter.WriteLog(ex, message);
        }
    }
}