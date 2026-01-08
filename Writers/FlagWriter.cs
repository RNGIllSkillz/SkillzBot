using IllSkillzBot;
using Microsoft.Extensions.Logging;
using SkillzBot.Hosts;
using SkillzBot.Utils;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.WRITERS
{
    internal class FlagWriter
    {
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private static readonly string filePath = Path.Combine(IllSkillzBotMain.GetDataPath().uniquePath, "Flags.txt");
        private static readonly ILogger<FlagWriter> _logger = IllServiceProvider.GetLogger<FlagWriter>();

        public static async Task FlagWriterTask(string Message)
        {
            await _semaphore.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(filePath, $"{DateTime.Now} {Message}{Environment.NewLine}");
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