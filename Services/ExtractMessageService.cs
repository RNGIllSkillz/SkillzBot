using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Services.Writers
{
    public class ExtractMessageService
    {
        private readonly string _filePath;
        private readonly ILogger<ExtractMessageService> _logger;

        public ExtractMessageService(IPathProvider paths, ILogger<ExtractMessageService> logger)
        {
            _filePath = Path.Combine(paths.DataPath, "MessageExtracted.txt");
            _logger = logger;
        }

        public async Task WriteAsync(string message)
        {
            try
            {
                await File.AppendAllTextAsync(_filePath, $"{DateTime.Now} {message}{Environment.NewLine}");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "ExtractMessageService WriteAsync failed");
            }
        }
    }
}