using IllSkillzBot;
using Microsoft.Extensions.Logging;
using SkillzBot.Hosts;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.WRITERS
{
    internal class UserBlackListWriter
    {
        // Fix: Use SemaphoreSlim for Async waiting
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private static readonly string filePath = Path.Combine(IllSkillzBotMain.GetDataPath().uniquePath, "userblacklist.txt");
        private static readonly ILogger<UserBlackListWriter> _logger = IllServiceProvider.GetLogger<UserBlackListWriter>();

        public static async Task Write(string Message)
        {
            await _semaphore.WaitAsync(); // Async wait
            try
            {
                // Simple append is safe inside Semaphore
                await File.AppendAllTextAsync(filePath, $"{Message}{Environment.NewLine}");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "FlagWriterTask()");
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
