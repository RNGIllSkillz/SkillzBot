using System;
using System.IO;

namespace SkillzBot.Services.Infrastructure
{
    public class PathProvider : IPathProvider
    {
        private readonly string _channelName;

        public string DataPath { get; private set; }
        public string SharedPath { get; private set; }
        public string ConfigPath { get; private set; }

        public PathProvider()
        {
            _channelName = Environment.GetEnvironmentVariable("ENV_CHANNEL_NAME");
            if (string.IsNullOrWhiteSpace(_channelName))
                throw new InvalidOperationException("ENV_CHANNEL_NAME environment variable is required.");

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            DataPath = Path.Combine(baseDir, $"Channels_Data/{_channelName}/DATA/");
            SharedPath = Path.Combine(baseDir, "Channels_Data/_shared/");
            ConfigPath = Path.Combine(DataPath, $"{_channelName}.ini");

            EnsureDirectoriesExist();
        }

        private void EnsureDirectoriesExist()
        {
            Directory.CreateDirectory(DataPath);
            Directory.CreateDirectory(SharedPath);
            Directory.CreateDirectory(Path.Combine(DataPath, "logs"));
        }

        public string GetFullPath(string fileName, bool isShared = false)
        {
            return Path.Combine(isShared ? SharedPath : DataPath, fileName);
        }
    }
}