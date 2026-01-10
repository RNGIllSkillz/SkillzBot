using SkillzBot.IllConfiguration; 
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Services.Infrastructure
{
    public class BlacklistService
    {
        private readonly IPathProvider _paths;
        private readonly BotConfigModel _config;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public BlacklistService(IPathProvider paths, BotConfigModel config)
        {
            _paths = paths;
            _config = config;
        }

        public async Task AddToMediaBlacklistAsync(string videoId)
        {
            var path = _paths.GetFullPath(_config.FilePaths.MediaListFileName, isShared: true);
            await AppendToFileAsync(path, videoId);
        }

        public async Task AddToUserBlacklistAsync(string twitchId)
        {
            var path = _paths.GetFullPath(_config.FilePaths.UserBlacklistFileName);
            await AppendToFileAsync(path, twitchId);
        }

        private async Task AppendToFileAsync(string path, string content)
        {
            await _lock.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(path, content + Environment.NewLine);
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}