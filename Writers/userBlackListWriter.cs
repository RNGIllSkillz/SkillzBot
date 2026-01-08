using IllSkillzBot;
using Microsoft.Extensions.Logging;
using SkillzBot.Hosts;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkillzBot.Singleton;

namespace SkillzBot.WRITERS
{
    internal class UserBlackListWriter
    {
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private static readonly ILogger<UserBlackListWriter> _logger = IllServiceProvider.GetLogger<UserBlackListWriter>();

        public static async Task Write(string Message)
        {
            string filePath = Path.Combine(IllSkillzBotMain.GetDataPath().uniquePath, IllSingleton.Config.FilePaths.UserBlacklistFileName);
            await _semaphore.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(filePath, $"{Message}{Environment.NewLine}");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "UserBlackListWriter Write()");
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}