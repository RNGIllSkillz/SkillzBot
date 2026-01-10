using SkillzBot.API.Twitch;
using SkillzBot.Hosts;
using SkillzBot.IllSTRINGS;
using SkillzBot.Interfaces;
using SkillzBot.IRC;
using SkillzBot.MODELS;
using SkillzBot.MySQL;
using System;
using System.IO;
using System.Threading.Tasks;
using SkillzBot.IllConfiguration; 

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

        public IllModeratorsInteractions(
            IDatabaseService database,
            ITtvIRCClient ircClient,
            ITwitchService twitchService,
            IIllAccess illAccess,
            IBotStateService botState,
            BotConfigModel config)
        {
            _database = database;
            _ircClient = ircClient;
            _twitchService = twitchService;
            _illAccess = illAccess;
            _botState = botState;
            _config = config;
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
        public async Task UserUntimeoutTrigger(string UserName)
        {
            await Task.Delay(2000);
            while (true)
            {
                var user = await _database.GetUserAsync(UserName);
                // Check if current time > UvalTimer
                if (user.UvalTimer <= DateTimeOffset.Now.ToUnixTimeSeconds())
                {
                    // Attempt to add mod until successful
                    while (!await _twitchService.AddChannelModerator(user.TwitchID.ToString()).ConfigureAwait(false))
                    {
                        await Task.Delay(1000);
                    }
                    return;
                }
                await Task.Delay(1000);
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