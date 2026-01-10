using Microsoft.Extensions.Logging;
using SkillzBot.IllConfiguration; 
using System;
using System.IO;
using System.Threading.Tasks;

namespace SkillzBot.SubUtils
{
    public class SubCheck
    {
        private readonly IBotStateService _botState;
        private readonly ILogger<SubCheck> _logger;
        private readonly string _filePath;

        public SubCheck(IPathProvider pathProvider, IBotStateService botState, ILogger<SubCheck> logger, BotConfigModel config)
        {
            _botState = botState;
            _logger = logger;
            _filePath = Path.GetFullPath(config.FilePaths.SubscriptionFileName);
        }

        public async Task<bool> RunCheckerAsync()
        {
            bool isActive;
            var (success, savedDateTime) = await TryReadDateTimeFromFileAsync();

            if (success)
            {
                DateTime currentDateTime = DateTime.Now;
                isActive = currentDateTime < savedDateTime;
            }
            else
            {
                // Fallback behavior from original code: Default to true if file missing/corrupt
                isActive = true;
                _logger.LogWarning("Subscription check failed to read file. Defaulting IsSubActive to True.");
            }

            // Update the global state safely
            await _botState.UpdateStateAsync(state => state.IsSubActive = isActive);

            return isActive;
        }

        private async Task<(bool success, DateTime result)> TryReadDateTimeFromFileAsync()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string dateTimeString = await File.ReadAllTextAsync(_filePath);
                    if (DateTime.TryParse(dateTimeString, out DateTime result))
                    {
                        return (true, result);
                    }
                    else
                    {
                        _logger.LogError("Failed to parse DateTime from file content: {Content}", dateTimeString);
                        return (false, DateTime.MinValue);
                    }
                }
                else
                {
                    _logger.LogWarning("Subscription file not found at: {Path}", _filePath);
                    return (false, DateTime.MinValue);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while reading subscription file.");
                return (false, DateTime.MinValue);
            }
        }
    }
}