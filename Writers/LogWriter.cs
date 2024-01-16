using System;
using System.IO;
using System.Threading;
using IllSkillzBot;
using SkillzBot.Singleton;

namespace SkillzBot.WRITERS
{
    internal class LogWriter : IDisposable
    {
        private readonly Mutex _mutex = new Mutex();
        private readonly string _logFilePath;
        private readonly IllSingleton _singleton = IllSingleton.GetInstance();

        public LogWriter()
        {
            string channelName = IllSkillzBotMain.GetDataPath().uniquePath;
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
                if (_singleton.debug)
                {
                    Console.WriteLine(DateTime.Now);
                    Console.WriteLine(message);
                }
                if (ex != null)
                {
                    if (_singleton.debug)
                    {
                        Console.WriteLine(ex.Message);
                        Console.WriteLine(ex.StackTrace);
                    }
                    writer.WriteLine(ex.Message);
                    writer.WriteLine(ex.StackTrace);
                    if (ex.InnerException != null)
                    {
                        if (_singleton.debug)
                        {
                            Console.WriteLine($"Inner: {ex.InnerException.Message}");
                            Console.WriteLine($"Inner: {ex.InnerException.StackTrace}");
                        }
                        writer.WriteLine($"Inner: {ex.InnerException.Message}");
                        writer.WriteLine($"Inner: {ex.InnerException.StackTrace}");
                    }
                }
                if (_singleton.debug)
                    Console.WriteLine("////////////////////////////////////////////////////////////////");
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