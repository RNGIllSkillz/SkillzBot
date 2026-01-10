using Microsoft.Extensions.Logging;
using SkillzBot.Interfaces;
using SkillzBot.IllConfiguration; 
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Services.Infrastructure
{
    public class MediaQueueService
    {
        private readonly string _filePath;
        private readonly IStreamElementsService _streamElementsService;
        private readonly ILogger<MediaQueueService> _logger;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public MediaQueueService(
            IPathProvider paths,
            BotConfigModel config,
            IStreamElementsService streamElementsService,
            ILogger<MediaQueueService> logger)
        {
            _filePath = paths.GetFullPath(config.FilePaths.MediaQueueFileName, isShared: false);
            _streamElementsService = streamElementsService;
            _logger = logger;
        }

        // Used by IllCommands to ban users based on track ID
        public async Task<int> GetUserIdByTrackIdAsync(string trackId)
        {
            await _lock.WaitAsync();
            try
            {
                if (!File.Exists(_filePath)) return -1;

                string[] lines = await File.ReadAllLinesAsync(_filePath);
                foreach (string line in lines)
                {
                    var parts = line.Split(' ');
                    if (parts.Length > 1 && parts[1] == trackId)
                    {
                        if (int.TryParse(parts[0], out int userId))
                            return userId;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading media queue");
            }
            finally
            {
                _lock.Release();
            }
            return -1;
        }

        // Used by RewardsRedemption to add tracks
        public async Task WriteAsync(int userId, string trackId)
        {
            await _lock.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(_filePath, $"{userId} {trackId}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing to media queue");
            }
            finally
            {
                _lock.Release();
            }
        }

        // Used by BackgroundTasks (Quartz) to clean up the queue
        public async Task FlushQueueAsync()
        {
            // We do NOT lock here immediately because GetCurrentSong is an HTTP call.
            // We don't want to block file I/O while waiting for the network.

            try
            {
                // Check if a song is playing via API
                var currentSong = await _streamElementsService.GetCurrentSong();

                // If null, it means queue is empty/inactive, so we clear the local file
                if (currentSong == null)
                {
                    await _lock.WaitAsync();
                    try
                    {
                        await File.WriteAllTextAsync(_filePath, string.Empty);
                    }
                    finally
                    {
                        _lock.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing media queue");
            }
        }
    }
}