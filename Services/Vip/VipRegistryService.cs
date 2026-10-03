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
    /// Keeps the list of channel VIPs with the date each one was granted (table dbVipTable), and
    /// frees a slot by removing the longest-standing VIP when the channel is at its VIP limit.
    /// Twitch only reports who is a VIP, so dates come from the bot's own grants, EventSub
    /// channel.vip.add events, VIP badges seen in chat and periodic syncs. VIPs that existed
    /// before tracking have no date and count as the oldest, ordered by how long they have
    /// been in the bot's user table.
    /// </summary>
    public class VipRegistryService
    {
        private const string LegacyFileName = "Vips.json"; // pre-database registry, imported once
        private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(30);
        private const int DbIdLookupsPerSync = 200;

        private readonly ITwitchService _twitch;
        private readonly IVipRepository _repo;
        private readonly IDatabaseService _database;
        private readonly IPathProvider _paths;
        private readonly IBotStateService _botState;
        private readonly BotConfigModel _config;
        private readonly ILogger<VipRegistryService> _logger;
        private readonly SemaphoreSlim _syncLock = new SemaphoreSlim(1, 1);
        private int _legacyImportChecked;

        public VipRegistryService(ITwitchService twitch, IVipRepository repo, IDatabaseService database, IPathProvider paths, IBotStateService botState, BotConfigModel config, ILogger<VipRegistryService> logger)
        {
            _twitch = twitch;
            _repo = repo;
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

        #region Reading

        public async Task<List<VipRecord>> SnapshotAsync()
        {
            await ImportLegacyFileOnceAsync();
            return await _repo.GetVipsAsync();
        }

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

        public async Task<string> StatusLineAsync()
        {
            try
            {
                var vips = await SnapshotAsync();
                var oldest = Oldest(vips).Where(v => !v.Pinned).Take(3).Select(Describe);
                var last = _botState.Current.VipLastSyncUtc;
                string sync = last.HasValue ? HealthState.FormatAge(DateTime.UtcNow - last.Value) + " назад" : "не было";
                return $"VIP: {vips.Count}/{Limit} | авторотация: {(AutoRotate ? "on" : "off")} | закреплено: {vips.Count(v => v.Pinned)} | " +
                       $"без даты: {vips.Count(v => !v.Since.HasValue)} | следующие на снятие: {string.Join(", ", oldest)} | синк: {sync}";
            }
            catch (Exception ex)
            {
                _logger.LogWarning("VIP status unavailable: {Error}", ex.Message);
                return "Реестр VIP недоступен (база данных).";
            }
        }

        #endregion

        #region Recording

        /// <summary>A VIP was granted now (by the bot, seen via EventSub, or a badge appeared in chat).</summary>
        public async Task RecordGrantAsync(long twitchId, string login, string displayName, string source)
        {
            var now = DateTime.UtcNow;
            var existing = (await SnapshotAsync()).FirstOrDefault(v => v.TwitchId == twitchId);
            if (existing == null)
            {
                bool preTracking = source == "chat" && !_botState.Current.VipRegistrySeeded; // badge seen before the first sync: date unknown
                await _repo.UpsertVipAsync(new VipRecord
                {
                    TwitchId = twitchId, Login = Normalize(login), DisplayName = displayName ?? login,
                    Since = preTracking ? null : now, Source = preTracking ? "initial" : source, FirstSeenUtc = now
                });
                _logger.LogInformation("VIP granted: {Login} ({Source}).", login, source);
                return;
            }
            bool changed = false;
            // A grant event for someone already listed without a date: the date is known now.
            if (!existing.Since.HasValue && source != "chat") { existing.Since = now; existing.Source = source; changed = true; }
            if (existing.Login != Normalize(login) || (displayName != null && existing.DisplayName != displayName))
            { existing.Login = Normalize(login); existing.DisplayName = displayName ?? existing.DisplayName; changed = true; }
            if (changed) await _repo.UpsertVipAsync(existing);
        }

        public async Task<bool> RecordRevokeAsync(long twitchId, string source)
        {
            bool removed = await _repo.DeleteVipAsync(twitchId);
            if (removed) _logger.LogInformation("VIP removed: {Id} ({Source}).", twitchId, source);
            return removed;
        }

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
            var v = (await SnapshotAsync()).FirstOrDefault(x => x.Login == login);
            if (v == null) return $"@{login} нет в списке VIP (сделай !vips sync).";
            v.Pinned = pinned;
            await _repo.UpsertVipAsync(v);
            return pinned ? $"@{v.DisplayName} закреплен: авторотация его не снимет." : $"@{v.DisplayName} откреплен.";
        }

        public async Task<string> SetSinceAsync(string login, string date)
        {
            login = Normalize(login);
            if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var since))
                return "Дата в формате ГГГГ-ММ-ДД.";
            var v = (await SnapshotAsync()).FirstOrDefault(x => x.Login == login);
            if (v == null) return $"@{login} нет в списке VIP.";
            v.Since = since; v.Source = "manual";
            await _repo.UpsertVipAsync(v);
            return $"@{v.DisplayName}: VIP с {since:yyyy-MM-dd}.";
        }

        #endregion

        #region Sync with Twitch

        /// <summary>Periodic entry point: syncs when the last sync is older than the interval.</summary>
        public async Task PeriodicSyncAsync()
        {
            var last = _botState.Current.VipLastSyncUtc;
            if (last.HasValue && DateTime.UtcNow - last.Value < SyncInterval) return;
            await SyncAsync("periodic");
        }

        /// <summary>Reconciles the registry with the Helix VIP list. Returns null when Twitch or the database could not be read.</summary>
        public async Task<SyncResult> SyncAsync(string reason)
        {
            var vips = await _twitch.GetAllVipsAsync();
            if (vips == null) { _logger.LogWarning("VIP sync ({Reason}) skipped: Helix VIP list unavailable.", reason); return null; }

            await _syncLock.WaitAsync();
            try
            {
                var current = await SnapshotAsync();
                var now = DateTime.UtcNow;
                bool firstSync = !_botState.Current.VipRegistrySeeded;
                var seen = new HashSet<long>();
                int added = 0, removed = 0;
                foreach (var v in vips)
                {
                    if (!long.TryParse(v.UserId, out long id)) continue;
                    seen.Add(id);
                    var existing = current.FirstOrDefault(x => x.TwitchId == id);
                    if (existing == null)
                    {
                        await _repo.UpsertVipAsync(new VipRecord
                        {
                            TwitchId = id, Login = Normalize(v.UserLogin), DisplayName = v.UserName ?? v.UserLogin,
                            // After the first sync a newcomer must have been granted since the previous sync.
                            Since = firstSync ? null : now, Source = firstSync ? "initial" : "sync", FirstSeenUtc = now
                        });
                        added++;
                    }
                    else if (existing.Login != Normalize(v.UserLogin) || (v.UserName != null && existing.DisplayName != v.UserName))
                    {
                        existing.Login = Normalize(v.UserLogin); existing.DisplayName = v.UserName ?? existing.DisplayName;
                        await _repo.UpsertVipAsync(existing);
                    }
                }
                foreach (var stale in current.Where(x => !seen.Contains(x.TwitchId)))
                    if (await _repo.DeleteVipAsync(stale.TwitchId)) removed++;

                await _botState.UpdateStateAsync(s => { s.VipRegistrySeeded = true; s.VipLastSyncUtc = now; });
                var result = new SyncResult(current.Count + added - removed, added, removed);
                if (added > 0 || removed > 0)
                    _logger.LogInformation("VIP sync ({Reason}): {Total} VIPs, +{Added} -{Removed}.", reason, result.Total, added, removed);
                await FillDbIdsAsync();
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("VIP sync ({Reason}) failed: {Error}", reason, ex.Message);
                return null;
            }
            finally { _syncLock.Release(); }
        }

        /// <summary>Looks up the user-table id for VIPs with an unknown date (their seniority proxy).</summary>
        private async Task FillDbIdsAsync()
        {
            var missing = (await _repo.GetVipsAsync()).Where(v => !v.Since.HasValue && !v.DbId.HasValue).Take(DbIdLookupsPerSync).ToList();
            foreach (var v in missing)
            {
                var u = await _database.GetUserAsync(v.Login);
                // Not in the user table (never chatted): mark as checked and least senior.
                v.DbId = u != null && u.dbID != -404 ? u.dbID : int.MaxValue;
                await _repo.UpsertVipAsync(v);
            }
        }

        /// <summary>Imports the pre-database Vips.json once, then renames it.</summary>
        private async Task ImportLegacyFileOnceAsync()
        {
            if (Interlocked.Exchange(ref _legacyImportChecked, 1) == 1) return;
            var path = _paths.GetFullPath(LegacyFileName);
            if (!File.Exists(path)) return;
            try
            {
                var existing = await _repo.GetVipsAsync();
                if (existing.Count == 0)
                {
                    var json = await File.ReadAllTextAsync(path);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("Vips", out var arr))
                    {
                        var records = JsonSerializer.Deserialize<List<VipRecord>>(arr.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<VipRecord>();
                        foreach (var r in records) await _repo.UpsertVipAsync(r);
                        bool seeded = doc.RootElement.TryGetProperty("Seeded", out var s) && s.ValueKind == JsonValueKind.True;
                        await _botState.UpdateStateAsync(st => { if (seeded) st.VipRegistrySeeded = true; });
                        _logger.LogInformation("Imported {Count} VIPs from {File} into dbVipTable.", records.Count, LegacyFileName);
                    }
                }
                File.Move(path, path + ".imported", true);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _legacyImportChecked, 0); // retry next time
                _logger.LogWarning("Legacy VIP file import failed: {Error}", ex.Message);
                throw;
            }
        }

        #endregion

        #region Grant with rotation

        /// <summary>
        /// Makes <paramref name="target"/> a VIP. At the limit, removes the oldest unpinned VIP first
        /// when auto-rotation is on; otherwise reports who would be next.
        /// </summary>
        public async Task<GrantResult> GrantAsync(UserObject target)
        {
            try
            {
                var sync = await SyncAsync("grant");
                if (sync == null) return new GrantResult(false, "Не удалось сверить список VIP (Twitch или база), випка не выдана.", null);

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
                        await _repo.UpsertVipAsync(removed);
                        return new GrantResult(false, $"Twitch не выдал випку @{target.Name} (см. лог); випка @{removed.DisplayName} возвращена.", null);
                    }
                    return new GrantResult(false, $"Twitch не выдал випку @{target.Name} (см. лог).", removed);
                }
                await RecordGrantAsync(target.TwitchID, target.Name, target.Name, "bot");
                return new GrantResult(true, null, removed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GrantAsync failed for {User}", target.Name);
                return new GrantResult(false, "Ошибка при выдаче випки (см. лог).", null);
            }
        }

        public async Task<bool> RevokeAsync(UserObject target)
        {
            if (!await _twitch.TryRemoveChannelVIPAsync(target.TwitchID.ToString())) return false;
            try { await RecordRevokeAsync(target.TwitchID, "bot"); }
            catch (Exception ex) { _logger.LogWarning("VIP revoke recorded on Twitch but not in the registry: {Error}", ex.Message); }
            return true;
        }

        #endregion

        private static string Normalize(string login) => (login ?? "").Trim().TrimStart('@').ToLowerInvariant();
    }
}
