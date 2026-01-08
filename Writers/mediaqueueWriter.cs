using IllSkillzBot;
using Microsoft.Extensions.Logging;
using SkillzBot.API.StreamElements;
using SkillzBot.Hosts;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.WRITERS
{
    internal class MediaqueueWriter
    {
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private static readonly string filePath = Path.Combine(IllSkillzBotMain.GetDataPath().uniquePath, "mediaqueue.txt");
        private static readonly ILogger<MediaqueueWriter> _logger = IllServiceProvider.GetLogger<MediaqueueWriter>();

        public static async Task Write(int userID, string trackID)
        {
            await _semaphore.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(filePath, $"{userID} {trackID}{Environment.NewLine}");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "MediaqueueWriter Write()");
            }
            finally
            {
                _semaphore.Release();
            }
        }
        public static async Task MediaQueueFlush()
        {
            await _semaphore.WaitAsync();
            try
            {
                var checkqueue = await StreamElementsAPI.GetCurrentSong().ConfigureAwait(false);
                if (checkqueue == null)
                {
                    await File.WriteAllTextAsync(filePath, String.Empty);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}