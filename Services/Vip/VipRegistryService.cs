using Microsoft.Extensions.Logging;
using SkillzBot.IllConfiguration;
using SkillzBot.Interfaces;
using SkillzBot.MODELS;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Services.Vip
{
    /// <summary>
    /// Keeps the list of channel VIPs with the date each one was granted, and frees a slot
    /// by removing the longest-standing VIP when the channel is at its VIP limit.
    /// Twitch only reports who is a VIP, so dates come from the bot's own grants, EventSub
    /// channel.vip.add events, VIP badges seen in chat and periodic syncs. VIPs that existed
    /// before tracking have no date and count as the oldest, ordered by how long they have
    /// been in the bot's user table.
    /// </summary>
    public class VipRegistryService
    {
        private const string FileName = "Vips.json";
        private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(30);
        private const int DbIdLookupsPerSync = 20;

        private readonly ITwitchService _twitch;
        private readonly IDatabaseService _database;
        private readonly IPathProvider _paths;
        private readonly IBotStateService _botState;
        private readonly BotConfigModel _config;
        private readonly ILogger<VipRegistryService> _logger;

        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private readonly JsonSerializerOptions _json = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
        private VipRegistryModel _model = new VipRegistryModel();
        private bool _loaded;

        public VipRegistryService(ITwitchService twitch, IDatabaseService database, IPathProvider paths, IBotStateService botState, BotConfigModel config, ILogger<VipRegistryService> logger)
        {
            _twitch = twitch;
            _database = database;
            _paths = paths;
            _botState = botState;
            _config = config;
            _logger = logger;
        }

        public int Limit => _config.VipLimit > 0 ? _config.VipLimit : 100;
        public bool AutoRotate => _botState.Current.VipAutoRotate;

        public record SyncResult(int Total, int Added, int Removed);
        public record GrantResult(bool Success, string Message, VipRecord Removed);

        #region Persistence

        private string FilePath => _paths.GetFullPath(FileName);

        private async Task EnsureLoadedAsync()
        {
            if (_loaded) return;
            await _lock.WaitAsync();
            try
            {
                if (_loaded) return;
                if (File.Exists(FilePath))
                {
                    var json = await File.ReadAllTextAsync(FilePath);
                    var loaded = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<VipRegistryModel>(json, _json);
                    if (loaded != null) _model = loaded;
                }
                _loaded = true;
                _logger.LogInformation("VIP registry loaded: {Count} VIPs, seeded={Seeded}.", _model.Vips.Count, _model.Seeded);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not load {File}; starting with an empty VIP registry.", FileName);
                _loaded = true;
            }
            finally { _lock.Release(); }
        }

        /// <summary>Runs <paramref name="mutate"/> under the lock and persists the registry.</summary>
        private async Task<T> MutateAsync<T>(Func<VipRegistryModel, T> mutate)
        {
            await EnsureLoadedAsync();
            await _lock.WaitAsync();
            try
            {
                var result = mutate(_model);
                try
                {
                    var json = JsonSerializer.Serialize(_model, _json);
                    await File.WriteAllTextAsync(FilePath, json);
                }
                catch (Exception ex) { _logger.LogError(ex, "Failed to save {File}", FileName); }
                return result;
            }
            finally { _lock.Release(); }
        }

        public async Task<List<VipRecord>> SnapshotAsync()
        {
            await EnsureLoadedAsync();
            await _lock.WaitAsync();
            try { return _model.Vips.Select(Clone).ToList(); }
            finally { _lock.Release(); }
        }

        public async Task<DateTime?> LastSyncAsync()
        {
            await EnsureLoadedAsync();
            return _model.LastSyncUtc;
        }

        private static VipRecord Clone(VipRecord r) => new VipRecord
        {
            TwitchId = r.TwitchId, Login = r.Login, DisplayName = r.DisplayName, Since = r.Since,
            Source = r.Source, DbId = r.DbId, FirstSeenUtc = r.FirstSeenUtc, Pinned = r.Pinned
        };

        #endregion

        #region Ordering

        /// <summary>Oldest first: unknown dates (pre-tracking VIPs) come first, by seniority in the user table, then by grant date.</summary>
        public static IEnumerable<VipRecord> Oldest(IEnumerable<VipRecord> vips) => vips
            .OrderBy(v => v.Since.HasValue ? 1 : 0)
            .ThenBy(v => v.Since ?? DateTime.MinValue)
            .ThenBy(v => v.DbId ?? int.MaxValue)
            .ThenBy(v => v.FirstSeenUtc)
            .ThenBy(v => v.Login, StringComparer.Ordinal);

        public static string Describe(VipRecord v)
        {
            string since = v.Since.HasValue ? $"с {v.Since.Value:yyyy-MM-dd}" : "дата неизвестна";
            return $"{v.DisplayName ?? v.Login} ({since}{(v.Pinned ? ", закреплен" : "")})";
        }

        #endregion

        #region Recording

        /// <summary>A VIP was granted now (by the bot, seen via EventSub, or a badge appeared in chat).</summary>
        public Task RecordGrantAsync(long twitchId, string login, string displayName, string source) => MutateAsync(m =>
        {
            var now = DateTime.UtcNow;
            var existing = m.Vips.FirstOrDefault(v => v.TwitchId == twitchId);
            if (existing == null)
            {
                bool preTracking = source == "chat" && !m.Seeded; // badge seen before the first sync: date unknown
                m.Vips.Add(new VipRecord
                {
                    TwitchId = twitchId, Login = Normalize(login), DisplayName = displayName ?? login,
                    Since = preTracking ? null : now, Source = preTracking ? "initial" : source, FirstSeenUtc = now
                });
                _logger.LogInformation("VIP granted: {Login} ({Source}).", login, source);
            }
            else
            {
                // A grant event for someone already listed without a date: the date is known now.
                if (!existing.Since.HasValue && source != "chat") { existing.Since = now; existing.Source = source; }
                existing.Login = Normalize(login); existing.DisplayName = displayName ?? existing.DisplayName;
            }
            return true;
        });

        public Task<bool> RecordRevokeAsync(long twitchId, string source) => MutateAsync(m =>
        {
            int removed = m.Vips.RemoveAll(v => v.TwitchId == twitchId);
            if (removed > 0) _logger.LogInformation("VIP removed: {Id} ({Source}).", twitchId, source);
            return removed > 0;
        });

        /// <summary>Called when a chat message shows the VIP badge state changed for a known user.</summary>
        public async Task NoteChatBadgeAsync(UserObject user, bool isVip)
        {
            try
            {
                if (isVip) await RecordGrantAsync(user.TwitchID, user.Name, user.Name, "chat");
                else await RecordRevokeAsync(user.TwitchID, "chat");
            }
            catch (Exception ex) { _logger.LogWarning("VIP badge note failed: {Error}", ex.Message); }
        }

        public async Task<string> SetPinnedAsync(string login, bool pinned)
        {
            login = Normalize(login);
            return await MutateAsync(m =>
            {
                var v = m.Vips.FirstOrDefault(x => x.Login == login);
                if (v == null) return $"@{login} нет в списке VIP (сделай !vips sync).";
                v.Pinned = pinned;
                return pinned ? $"@{v.DisplayName} закреплен: авторотация его не снимет." : $"@{v.DisplayName} откреплен.";
            });
        }

        public async Task<string> SetSinceAsync(string login, string date)
        {
            login = Normalize(login);
            if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var since))
                return "Дата в формате ГГГГ-ММ-ДД.";
            return await MutateAsync(m =>
            {
                var v = m.Vips.FirstOrDefault(x => x.Login == login);
                if (v == null) return $"@{login} нет в списке VIP.";
                v.Since = since; v.Source = "manual";
                return $"@{v.DisplayName}: VIP с {since:yyyy-MM-dd}.";
            });
        }

        #endregion

        #region Sync with Twitch

        /// <summary>Periodic entry point: syncs when the last sync is older than the interval.</summary>
        public async Task PeriodicSyncAsync()
        {
            await EnsureLoadedAsync();
            if (_model.LastSyncUtc.HasValue && DateTime.UtcNow - _model.LastSyncUtc.Value < SyncInterval) return;
            await SyncAsync("periodic");
        }

        /// <summary>Reconciles the registry with the Helix VIP list. Returns null when Twitch could not be read.</summary>
        public async Task<SyncResult> SyncAsync(string reason)
        {
            await EnsureLoadedAsync();
            var vips = await _twitch.GetAllVipsAsync();
            if (vips == null) { _logger.LogWarning("VIP sync ({Reason}) skipped: Helix VIP list unavailable.", reason); return null; }

            var result = await MutateAsync(m =>
            {
                var now = DateTime.UtcNow;
                bool firstSync = !m.Seeded;
                var seen = new HashSet<long>();
                int added = 0;
                foreach (var v in vips)
                {
                    if (!long.TryParse(v.UserId, out long id)) continue;
                    seen.Add(id);
                    var existing = m.Vips.FirstOrDefault(x => x.TwitchId == id);
                    if (existing == null)
                    {
                        m.Vips.Add(new VipRecord
                        {
                            TwitchId = id, Login = Normalize(v.UserLogin), DisplayName = v.UserName ?? v.UserLogin,
                            // After the first sync a newcomer must have been granted since the previous sync.
                            Since = firstSync ? null : now, Source = firstSync ? "initial" : "sync", FirstSeenUtc = now
                        });
                        added++;
                    }
                    else { existing.Login = Normalize(v.UserLogin); existing.DisplayName = v.UserName ?? existing.DisplayName; }
                }
                int removed = m.Vips.RemoveAll(x => !seen.Contains(x.TwitchId));
                m.Seeded = true;
                m.LastSyncUtc = now;
                return new SyncResult(m.Vips.Count, added, removed);
            });

            if (result.Added > 0 || result.Removed > 0)
                _logger.LogInformation("VIP sync ({Reason}): {Total} VIPs, +{Added} -{Removed}.", reason, result.Total, result.Added, result.Removed);
            await FillDbIdsAsync();
            return result;
        }

        /// <summary>Looks up the user-table id for VIPs with an unknown date, a few per sync.</summary>
        private async Task FillDbIdsAsync()
        {
            List<VipRecord> missing;
            await _lock.WaitAsync();
            try { missing = _model.Vips.Where(v => !v.Since.HasValue && !v.DbId.HasValue).Take(DbIdLookupsPerSync).Select(Clone).ToList(); }
            finally { _lock.Release(); }
            if (missing.Count == 0) return;

            var found = new Dictionary<long, int>();
            foreach (var v in missing)
            {
                try
                {
                    var u = await _database.GetUserAsync(v.Login);
                    // Not in the user table (never chatted): mark as checked and least senior.
                    found[v.TwitchId] = u != null && u.dbID != -404 ? u.dbID : int.MaxValue;
                }
                catch (Exception ex) { _logger.LogDebug("DbId lookup for {Login} failed: {Error}", v.Login, ex.Message); break; }
            }
            if (found.Count == 0) return;
            await MutateAsync(m =>
            {
                foreach (var v in m.Vips) if (found.TryGetValue(v.TwitchId, out int id)) v.DbId = id;
                return true;
            });
        }

        #endregion

        #region Grant with rotation

        /// <summary>
        /// Makes <paramref name="target"/> a VIP. At the limit, removes the oldest unpinned VIP first
        /// when auto-rotation is on; otherwise reports who would be next.
        /// </summary>
        public async Task<GrantResult> GrantAsync(UserObject target)
        {
            var sync = await SyncAsync("grant");
            if (sync == null) return new GrantResult(false, "Не удалось получить список VIP от Twitch, випка не выдана.", null);

            var current = await SnapshotAsync();
            if (current.Any(v => v.TwitchId == target.TwitchID))
                return new GrantResult(false, $"@{target.Name} уже VIP.", null);

            VipRecord removed = null;
            if (current.Count >= Limit)
            {
                var candidate = Oldest(current).FirstOrDefault(v => !v.Pinned);
                if (candidate == null)
                    return new GrantResult(false, $"Лимит VIP ({Limit}) достигнут, и все VIP закреплены.", null);
                if (!AutoRotate)
                    return new GrantResult(false, $"Лимит VIP ({Limit}) достигнут. Самый давний: {Describe(candidate)}. Авторотация выключена (!vips auto on).", null);

                if (!await _twitch.TryRemoveChannelVIPAsync(candidate.TwitchId.ToString()))
                    return new GrantResult(false, $"Не удалось снять випку с @{candidate.DisplayName}; новая не выдана.", null);
                await RecordRevokeAsync(candidate.TwitchId, "rotation");
                _logger.LogInformation("VIP rotation: removed {Old} ({Since}) to make room for {New}.", candidate.Login, candidate.Since?.ToString("yyyy-MM-dd") ?? "unknown", target.Name);
                removed = candidate;
            }

            if (!await _twitch.TryAddChannelVIPAsync(target.TwitchID.ToString()))
            {
                if (removed != null && await _twitch.TryAddChannelVIPAsync(removed.TwitchId.ToString()))
                {
                    await RestoreAsync(removed);
                    return new GrantResult(false, $"Twitch не выдал випку @{target.Name} (см. лог); випка @{removed.DisplayName} возвращена.", null);
                }
                return new GrantResult(false, $"Twitch не выдал випку @{target.Name} (см. лог).", removed);
            }
            await RecordGrantAsync(target.TwitchID, target.Name, target.Name, "bot");
            return new GrantResult(true, null, removed);
        }

        /// <summary>Puts a record back unchanged (after a rotation whose grant failed).</summary>
        private Task RestoreAsync(VipRecord record) => MutateAsync(m =>
        {
            m.Vips.RemoveAll(v => v.TwitchId == record.TwitchId);
            m.Vips.Add(Clone(record));
            return true;
        });

        public async Task<bool> RevokeAsync(UserObject target)
        {
            if (!await _twitch.TryRemoveChannelVIPAsync(target.TwitchID.ToString())) return false;
            await RecordRevokeAsync(target.TwitchID, "bot");
            return true;
        }

        #endregion

        public async Task<string> StatusLineAsync()
        {
            var vips = await SnapshotAsync();
            var oldest = Oldest(vips).Where(v => !v.Pinned).Take(3).Select(Describe);
            var last = await LastSyncAsync();
            string sync = last.HasValue ? HealthState.FormatAge(DateTime.UtcNow - last.Value) + " назад" : "не было";
            return $"VIP: {vips.Count}/{Limit} | авторотация: {(AutoRotate ? "on" : "off")} | закреплено: {vips.Count(v => v.Pinned)} | " +
                   $"без даты: {vips.Count(v => !v.Since.HasValue)} | следующие на снятие: {string.Join(", ", oldest)} | синк: {sync}";
        }

        private static string Normalize(string login) => (login ?? "").Trim().TrimStart('@').ToLowerInvariant();
    }
}
