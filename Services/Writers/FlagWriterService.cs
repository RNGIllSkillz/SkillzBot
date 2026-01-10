using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Services.Writers
{
    public class FlagWriterService
    {
        private readonly string _filePath;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly ILogger<FlagWriterService> _logger;

        public FlagWriterService(IPathProvider paths, ILogger<FlagWriterService> logger)
        {
            _filePath = Path.Combine(paths.DataPath, "Flags.txt");
            _logger = logger;
        }

        public async Task WriteAsync(string message)
        {
            await _semaphore.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(_filePath, $"{DateTime.Now} {message}{Environment.NewLine}");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "FlagWriterService WriteAsync failed");
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
} 