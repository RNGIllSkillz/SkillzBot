using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Services.Infrastructure
{
    public class SubscriptionService
    {
        private readonly string _filePath;
        private readonly IBotStateService _botState;
        private readonly ILogger<SubscriptionService> _logger;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public SubscriptionService(IPathProvider paths, IBotStateService botState, ILogger<SubscriptionService> logger)
        {
            _filePath = Path.Combine(paths.DataPath, "Subscription.txt");
            _botState = botState;
            _logger = logger;
        }

        // Standard 1 month add (used by IllCommands)
        public async Task<DateTime> AddSubscriptionAsync()
        {
            DateTime baseDate = await GetCurrentExpirationOrNowAsync();
            DateTime newTimestamp = baseDate.AddMonths(1);

            await WriteDateAsync(newTimestamp);
            await CheckSubscriptionAsync();
            return newTimestamp;
        }

        // Variable amount/rate add (Used by SubCall)
        public async Task<DateTime> AddSubscriptionAsync(int amount, int rate)
        {
            DateTime baseDate = await GetCurrentExpirationOrNowAsync();
            DateTime newTimestamp;
            const int daysInMonth = 31;

            if (amount != rate)
            {
                // Calculate proportional days
                double dailyRate = (double)rate / daysInMonth;
                // Avoid division by zero if rate is somehow 0
                if (dailyRate <= 0) dailyRate = 1;

                int daysPaid = (int)(amount / dailyRate);
                newTimestamp = baseDate.AddDays(daysPaid);
            }
            else
            {
                newTimestamp = baseDate.AddMonths(1);
            }

            await WriteDateAsync(newTimestamp);
            await CheckSubscriptionAsync();
            return newTimestamp;
        }

        public async Task<bool> CheckSubscriptionAsync()
        {
            await _lock.WaitAsync();
            try
            {
                bool isActive = true;
                if (File.Exists(_filePath))
                {
                    string content = await File.ReadAllTextAsync(_filePath);
                    if (DateTime.TryParse(content, out DateTime savedDate))
                    {
                        isActive = DateTime.Now < savedDate;
                    }
                }

                await _botState.UpdateStateAsync(s => s.IsSubActive = isActive);
                return isActive;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CheckSubscriptionAsync failed");
                await _botState.UpdateStateAsync(s => s.IsSubActive = true);
                return true;
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task<DateTime> GetCurrentExpirationOrNowAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (File.Exists(_filePath))
                {
                    string content = await File.ReadAllTextAsync(_filePath);
                    if (DateTime.TryParse(content, out DateTime savedDate))
                    {
                        if (DateTime.Now < savedDate)
                            return savedDate;
                    }
                }
                return DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading subscription date");
                return DateTime.Now;
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task WriteDateAsync(DateTime date)
        {
            await _lock.WaitAsync();
            try
            {
                await File.WriteAllTextAsync(_filePath, date.ToString());
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}