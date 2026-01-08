
using IllSkillzBot;
using Microsoft.Extensions.Logging;
using SkillzBot.Hosts;
using SkillzBot.WRITERS;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkillzBot.Singleton;

namespace SkillzBot.WRITERS
{
    internal class MediaBlackListWriter
    {
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private static readonly ILogger<MediaBlackListWriter> _logger = IllServiceProvider.GetLogger<MediaBlackListWriter>();

        public static async Task Write(string Message)
        {
            string filePath = Path.Combine(IllSkillzBotMain.GetDataPath().sharedPath, IllSingleton.Config.FilePaths.MediaListFileName);
            await _semaphore.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(filePath, $"{Message}{Environment.NewLine}");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "MediaBlackListWriter Write()");
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}