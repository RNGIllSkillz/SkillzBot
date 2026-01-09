using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks; 
using IllSkillzBot;

namespace SkillzBot.WRITERS
{
    internal class dicWhiteListWriter
    {
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private static readonly string dataPath = IllSkillzBotMain.GetDataPath().sharedPath;
        private static readonly string filePath = Path.Combine(dataPath, "dicWhiteList.txt");

        public async Task dicWhiteListWriterTask(string Message)
        {
            await _semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!File.Exists(filePath))
                {
                    await File.WriteAllTextAsync(filePath, string.Empty);
                }

                await File.AppendAllTextAsync(filePath, Message + Environment.NewLine).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error writing to whitelist: {e.Message}");
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}