using System.Collections.Generic;
using System;
using System.Threading;

namespace SkillzBot.Services
{
    /// <summary>
    /// Lock-free snapshot of connection health, written by the components themselves and
    /// read by the heartbeat logger and the !service command.
    /// </summary>
    public sealed class HealthState
    {
        private int _eventSubConnected;
        private long _eventSubSinceTicks;
        private long _eventSubLastEventTicks;
        private long _eventSubReconnects;
        private volatile string _eventSubSessionId = "";

        private long _dbCircuitOpenUntilTicks;
        private long _dbFailures;

        private long _seFailures;      // consecutive StreamElements send failures
        private readonly HashSet<string> _eventSubSubscriptions = new HashSet<string>(StringComparer.Ordinal);
        private long _seLastOkTicks;

        public bool EventSubConnected => Volatile.Read(ref _eventSubConnected) == 1;
        public string EventSubSessionId => _eventSubSessionId;
        public long EventSubReconnects => Interlocked.Read(ref _eventSubReconnects);
        public DateTime? EventSubSinceUtc => ToDate(Interlocked.Read(ref _eventSubSinceTicks));
        public DateTime? EventSubLastEventUtc => ToDate(Interlocked.Read(ref _eventSubLastEventTicks));

        public bool DbCircuitOpen => DateTime.UtcNow.Ticks < Interlocked.Read(ref _dbCircuitOpenUntilTicks);
        public long DbFailures => Interlocked.Read(ref _dbFailures);

        public long StreamElementsFailures => Interlocked.Read(ref _seFailures);
        public DateTime? StreamElementsLastOkUtc => ToDate(Interlocked.Read(ref _seLastOkTicks));

        public void MarkStreamElementsResult(bool ok)
        {
            if (ok)
            {
                Interlocked.Exchange(ref _seFailures, 0);
                Interlocked.Exchange(ref _seLastOkTicks, DateTime.UtcNow.Ticks);
            }
            else Interlocked.Increment(ref _seFailures);
        }

        public void SetEventSubConnected(bool connected, string sessionId)
        {
            bool wasConnected = Interlocked.Exchange(ref _eventSubConnected, connected ? 1 : 0) == 1;
            _eventSubSessionId = sessionId ?? "";
            if (connected)
            {
                // Only a connect that follows an earlier session counts as a reconnect.
                long previousSince = Interlocked.Exchange(ref _eventSubSinceTicks, DateTime.UtcNow.Ticks);
                if (!wasConnected && previousSince != 0)
                    Interlocked.Increment(ref _eventSubReconnects);
            }
        }

        public void MarkEventSubEvent() => Interlocked.Exchange(ref _eventSubLastEventTicks, DateTime.UtcNow.Ticks);

        /// <summary>True while this subscription type is confirmed on the current session (cleared on disconnect, removed on revocation).</summary>
        public bool IsEventSubSubscriptionActive(string type) { lock (_eventSubSubscriptions) return _eventSubSubscriptions.Contains(type); }
        public void SetEventSubSubscription(string type, bool active) { lock (_eventSubSubscriptions) { if (active) _eventSubSubscriptions.Add(type); else _eventSubSubscriptions.Remove(type); } }
        public void ClearEventSubSubscriptions() { lock (_eventSubSubscriptions) _eventSubSubscriptions.Clear(); }
        public int EventSubSubscriptionCount { get { lock (_eventSubSubscriptions) return _eventSubSubscriptions.Count; } }

        public void MarkDbFailure(DateTime circuitOpenUntilUtc)
        {
            Interlocked.Increment(ref _dbFailures);
            Interlocked.Exchange(ref _dbCircuitOpenUntilTicks, circuitOpenUntilUtc.Ticks);
        }

        private static DateTime? ToDate(long ticks) => ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);

        /// <summary>Compact age such as "3s", "4m12s", "1h05m", "2d03h".</summary>
        public static string FormatAge(TimeSpan age)
        {
            if (age < TimeSpan.Zero) age = TimeSpan.Zero;
            if (age.TotalSeconds < 60) return $"{(int)age.TotalSeconds}s";
            if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes}m{age.Seconds:00}s";
            if (age.TotalHours < 24) return $"{(int)age.TotalHours}h{age.Minutes:00}m";
            return $"{(int)age.TotalDays}d{age.Hours:00}h";
        }

        public static string FormatAge(DateTime? sinceUtc) => sinceUtc.HasValue ? FormatAge(DateTime.UtcNow - sinceUtc.Value) : "never";
    }
}
