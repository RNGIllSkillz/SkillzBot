using IllSkillzBot;
using Microsoft.Extensions.Logging;
using Quartz.Logging;
using SkillzBot.Hosts;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.WRITERS
{
    internal class ExtractMessage
    {
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private static readonly string filePath = Path.Combine(IllSkillzBotMain.GetDataPath().uniquePath, "MessageExtracted.txt");
        private static readonly ILogger<FlagWriter> _logger = IllServiceProvider.GetLogger<FlagWriter>();
        public static async Task ExtractMessageTask(string Message)
        {
            await _semaphore.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(filePath, $"{DateTime.Now} {Message}{Environment.NewLine}");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "ExtractMessageTask()");
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}