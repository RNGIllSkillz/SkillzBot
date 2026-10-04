using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Services.Infrastructure
{
    /// <summary>What the panel shows: whether the channel is paid for, until when, and how many days remain.</summary>
    public record SubscriptionStatus(bool Active, bool Unlimited, DateTime? Due, int? DaysLeft, DateTime CheckedUtc);

    /// <summary>
    /// The channel's subscription is a single due date in Subscription.txt; an empty file means no limit.
    /// IsSubActive in the bot state follows that date: it is re-evaluated every 5 minutes and after every change.
    /// </summary>
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

        public async Task<SubscriptionStatus> GetStatusAsync() => Describe(await ReadDueAsync());

        /// <summary>
        /// Parses a due date from the panel: "yyyy-MM-dd" means the end of that day (local time);
        /// anything else must be an ISO 8601 date-time.
        /// </summary>
        public static bool TryParseDue(string text, out DateTime due)
        {
            due = default;
            text = text?.Trim();
            if (string.IsNullOrEmpty(text)) return false;
            if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var day))
            {
                due = DateTime.SpecifyKind(day.Date.AddDays(1).AddSeconds(-1), DateTimeKind.Local);
                return true;
            }
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var moment))
            {
                due = moment.Kind == DateTimeKind.Utc ? moment.ToLocalTime() : DateTime.SpecifyKind(moment, DateTimeKind.Local);
                return true;
            }
            return false;
        }

        /// <summary>Sets the due date; null removes the limit. Applies to the bot state right away.</summary>
        public async Task<SubscriptionStatus> SetDueAsync(DateTime? due, string by)
        {
            await WriteDueAsync(due);
            _logger.LogWarning("[Subscription] {By} set the due date to {Due}.", by, due?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "none (unlimited)");
            await CheckSubscriptionAsync();
            return await GetStatusAsync();
        }

        /// <summary>Adds whole months on top of the current due date (or today when it already passed).</summary>
        public async Task<DateTime> ExtendAsync(int months, string by)
        {
            DateTime newDue = (await GetCurrentExpirationOrNowAsync()).AddMonths(months);
            await WriteDueAsync(newDue);
            _logger.LogWarning("[Subscription] {By} added {Months} month(s); due {Due}.", by, months, newDue.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            await CheckSubscriptionAsync();
            return newDue;
        }

        // Standard 1 month add (used by !sub)
        public Task<DateTime> AddSubscriptionAsync() => ExtendAsync(1, "chat command");

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

            await WriteDueAsync(newTimestamp);
            await CheckSubscriptionAsync();
            return newTimestamp;
        }

        public async Task<bool> CheckSubscriptionAsync()
        {
            try
            {
                DateTime? due = await ReadDueAsync();
                bool isActive = due == null || DateTime.Now < due.Value;
                await _botState.UpdateStateAsync(s => s.IsSubActive = isActive);
                return isActive;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CheckSubscriptionAsync failed");
                await _botState.UpdateStateAsync(s => s.IsSubActive = true);
                return true;
            }
        }

        private static SubscriptionStatus Describe(DateTime? due)
        {
            if (due == null) return new SubscriptionStatus(true, true, null, null, DateTime.UtcNow);
            double days = (due.Value - DateTime.Now).TotalDays;
            return new SubscriptionStatus(days > 0, false, due, Math.Max(0, (int)Math.Ceiling(days)), DateTime.UtcNow);
        }

        private async Task<DateTime> GetCurrentExpirationOrNowAsync()
        {
            try
            {
                DateTime? due = await ReadDueAsync();
                return due != null && DateTime.Now < due.Value ? due.Value : DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading subscription date");
                return DateTime.Now;
            }
        }

        /// <summary>
        /// The stored due date as local time, or null when there is none. New files hold ISO 8601 with an offset;
        /// older ones hold whatever DateTime.ToString() produced under the culture of the day, so that is tried first.
        /// </summary>
        private async Task<DateTime?> ReadDueAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (!File.Exists(_filePath)) return null;
                string content = (await File.ReadAllTextAsync(_filePath)).Trim();
                if (content.Length == 0) return null;
                if (DateTime.TryParse(content, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var d) ||
                    DateTime.TryParse(content, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out d))
                    return d.Kind == DateTimeKind.Utc ? d.ToLocalTime() : DateTime.SpecifyKind(d, DateTimeKind.Local);
                _logger.LogWarning("Subscription.txt holds an unreadable date '{Content}'; treating the subscription as unlimited.", content);
                return null;
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task WriteDueAsync(DateTime? due)
        {
            await _lock.WaitAsync();
            try
            {
                await File.WriteAllTextAsync(_filePath, due?.ToString("o", CultureInfo.InvariantCulture) ?? string.Empty);
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
