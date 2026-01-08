using IllSkillzBot;
using Microsoft.Extensions.Logging;
using SkillzBot.Hosts;
using SkillzBot.WRITERS;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.WRITERS
{
    internal class MediaBlackListWriter
    {
        // Fix: Use SemaphoreSlim for Async waiting
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        readonly static string dataPath = IllSkillzBotMain.GetDataPath().sharedPath;
        readonly static string filePath = Path.Combine(dataPath, "mediaList.txt");
        private static readonly ILogger<MediaBlackListWriter> _logger = IllServiceProvider.GetLogger<MediaBlackListWriter>();

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