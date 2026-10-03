using Microsoft.Extensions.Logging;
using SkillzBot.IllConfiguration;
using SkillzBot.IllSTRINGS;
using SkillzBot.Interfaces;
using SkillzBot.MODELS;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace SkillzBot.IllSkillzBot
{
    public class IllModeratorsInteractions
    {
        private readonly IDatabaseService _database;
        private readonly ITtvIRCClient _ircClient;
        private readonly ITwitchService _twitchService;
        private readonly IIllAccess _illAccess;
        private readonly IBotStateService _botState;
        private readonly BotConfigModel _config;
        private readonly ILogger<IllModeratorsInteractions> _logger;

        // Moderators whose mod status is waiting to be restored after a timeout, keyed by login.
        private readonly ConcurrentDictionary<string, Task> _pendingModRestores = new(StringComparer.OrdinalIgnoreCase);

        public IllModeratorsInteractions(
            IDatabaseService database,
            ITtvIRCClient ircClient,
            ITwitchService twitchService,
            IIllAccess illAccess,
            IBotStateService botState,
            BotConfigModel config,
            ILogger<IllModeratorsInteractions> logger)
        {
            _database = database;
            _ircClient = ircClient;
            _twitchService = twitchService;
            _illAccess = illAccess;
            _botState = botState;
            _config = config;
            _logger = logger;
        }

        public async Task<UserObject> IllFilterTrigger(UserObject user, string messageID = null)
        {
            if (user.banCount == 35)
            {
                await _twitchService.BanUser(user.TwitchID.ToString(), STRINGS.PermaBanReason);
                user.banCount = 0;
            }
            else
            {
                switch (_botState.Current.ChatFilterLvl)
                {
                    case 0: break;
                    case 1:
                        if (messageID != null) await _twitchService.DeleteMessage(messageID);
                        break;
                    case 2:
                        if (messageID != null) await _twitchService.DeleteMessage(messageID);
                        string ModsZapMsg = $"Найдена запретка на канале {_config.ChannelName} от пользователя @{user.Name}. Модерам на проверку";
                        await IllAllModsNotification(ModsZapMsg);
                        break;
                    case 3:
                        await _twitchService.TimeOutUser(user, 86400, STRINGS.TimeOut1wReason);
                        user.banCount++;
                        break;
                    case 4:
                        await _twitchService.TimeOutUser(user, 604800, STRINGS.TimeOut1wReason);
                        user.banCount++;
                        break;
                    case 5:
                        await _twitchService.BanUser(user.TwitchID.ToString(), STRINGS.PermaBanReason);
                        user.banCount = 0;
                        break;
                }
            }
            return user;
        }

        /// <summary>True while a timed-out moderator is waiting to get the sword back.</summary>
        public bool IsModPendingRestore(string userName) =>
            !string.IsNullOrEmpty(userName) && _pendingModRestores.ContainsKey(userName);

        /// <summary>
        /// Times out a user even if they are a moderator. Twitch strips moderator status on
        /// timeout, so for moderators a background task re-adds it once the timeout expires.
        /// The caller is never blocked for the duration of the timeout.
        /// </summary>
        public async Task TimeOutModeratorAsync(UserObject user, int durationSec, string reason)
        {
            await _twitchService.TimeOutModerator(user, durationSec, reason);
            if (user.isMod == 1)
                ScheduleModRestore(user, durationSec);
        }

        private void ScheduleModRestore(UserObject user, int durationSec)
        {
            if (_pendingModRestores.ContainsKey(user.Name)) return;

            var task = Task.Run(() => RestoreModAfterTimeoutAsync(user.Name, user.TwitchID, durationSec));
            if (!_pendingModRestores.TryAdd(user.Name, task)) return;

            _ = task.ContinueWith(_ => _pendingModRestores.TryRemove(user.Name, out _), TaskScheduler.Default);
        }

        private async Task RestoreModAfterTimeoutAsync(string userName, long twitchId, int durationSec)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(durationSec + 2)).ConfigureAwait(false);

                // The timeout may have been extended by another redemption; wait it out.
                for (int i = 0; i < 120; i++)
                {
                    double remaining = 0;
                    try
                    {
                        var user = await _database.GetUserAsync(userName).ConfigureAwait(false);
                        if (user.dbID != -404)
                            remaining = user.UvalTimer - DateTimeOffset.Now.ToUnixTimeSeconds();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not read timeout state for {User}; retrying shortly.", userName);
                        remaining = 5;
                    }

                    if (remaining <= 0) break;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(remaining + 1, 60))).ConfigureAwait(false);
                }

                for (int attempt = 1; attempt <= 6; attempt++)
                {
                    if (await _twitchService.AddChannelModerator(twitchId.ToString()).ConfigureAwait(false))
                    {
                        _logger.LogInformation("Restored moderator status for {User}.", userName);
                        return;
                    }
                    await Task.Delay(5000).ConfigureAwait(false);
                }

                await _ircClient.SendMessage($"Ошибка: Не удалось вернуть права модератора для @{userName}. Возможно пользователь забанен или произошла ошибка API.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Moderator restore task for {User} failed.", userName);
            }
        }

        public async Task IllAllModsNotification(string message)
        {
            var mIds = await _twitchService.GetAllMods();
            if (mIds == null) return;
            foreach (var mId in mIds)
            {
                await _twitchService.SendWhisper(mId.UserId, message);
                await Task.Delay(100);
            }
        }

        public async Task IllAddModerator(UserObject user, string[] UserInput)
        {
            if (!_illAccess.Root(user)) return;
            if (UserInput.Length == 2)
            {
                var aUser = await _database.GetUserAsync(UserInput[1]);
                if (aUser.dbID != -404)
                {
                    if (aUser.isVip == 1)
                        await _twitchService.DeleteChannelVIP(aUser.TwitchID.ToString());

                    if (await _twitchService.AddChannelModerator(aUser.TwitchID.ToString()).ConfigureAwait(false))
                        await _ircClient.SendMessage(string.Format(STRINGS.AddModSuccess, aUser.Name));
                    else
                        await _ircClient.SendMessage("Модерытор не добавлен, произошла ошибка.");
                }
                else
                    await _ircClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, user.Name, UserInput[1]));
            }
            else
                await _ircClient.SendMessage(STRINGS.InputERROR);
        }

        public async Task IllDeleteModerator(UserObject user, string[] UserInput)
        {
            if (!_illAccess.Root(user)) return;
            if (UserInput.Length == 2)
            {
                var aUser = await _database.GetUserAsync(UserInput[1]);
                if (aUser.dbID != -404)
                {
                    await _twitchService.DeleteChannelModerator(aUser.TwitchID.ToString());
                    await _ircClient.SendMessage(string.Format(STRINGS.DeleteModSuccess, aUser.Name));
                }
                else
                {
                    var uID = await _twitchService.GetUsetIDByName(UserInput[1]);
                    if (uID != null)
                    {
                        await _twitchService.DeleteChannelModerator(uID);
                        await _ircClient.SendMessage(string.Format(STRINGS.DeleteModSuccess, UserInput[1]));
                    }
                    else
                        await _ircClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, user.Name, UserInput[1]));
                }
            }
            else
                await _ircClient.SendMessage(STRINGS.InputERROR);
        }
    }
}
