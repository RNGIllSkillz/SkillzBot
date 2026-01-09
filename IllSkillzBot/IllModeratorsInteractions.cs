using SkillzBot.API.Twitch;
using SkillzBot.Hosts;
using SkillzBot.IllSTRINGS;
using SkillzBot.Interfaces;
using SkillzBot.IRC;
using SkillzBot.MODELS;
using SkillzBot.MySQL;
using System;
using System.Threading.Tasks;

namespace SkillzBot.IllSkillzBot
{
    internal class IllModeratorsInteractions
    {
        private readonly IDatabaseService _database;
        private readonly ITtvIRCClient _ircClient;

        public IllModeratorsInteractions(IDatabaseService database, ITtvIRCClient ircClient)
        {
            _database = database;
            _ircClient = ircClient;
        }

        public async Task UserUntimeoutTrigger(string UserName)
        {
            await Task.Delay(2000).ConfigureAwait(false);
            while (true)
            {
                var user = await _database.GetUserAsync(UserName).ConfigureAwait(false);
                // Check if current time > UvalTimer
                if (user.UvalTimer <= DateTimeOffset.Now.ToUnixTimeSeconds())
                {
                    // Attempt to add mod until successful
                    while (!await TtvAPI.AddChannelModerator(user.TwitchID.ToString()).ConfigureAwait(false))
                    {
                        await Task.Delay(1000).ConfigureAwait(false);
                    }
                    return;
                }
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }
        public async Task IllAllModsNotification(string message)
        {
            var mIds = await TtvAPI.GetAllMods().ConfigureAwait(false);
            if (mIds == null) return;
            foreach (var mId in mIds)
            {
                await TtvAPI.SendWhisper(mId.UserId, message).ConfigureAwait(false);
                await Task.Delay(100).ConfigureAwait(false);
            }
        }
        public async Task IllAddModerator(UserObject user, string[] UserInput)
        {
            if (!IllAccess.Root(user)) return;
            if (UserInput.Length == 2)
            {
                var aUser = await _database.GetUserAsync(UserInput[1]).ConfigureAwait(false);
                if (aUser.dbID != -404)
                {
                    if (aUser.isVip == 1)
                        await TtvAPI.DeleteChannelVIP(aUser.TwitchID.ToString()).ConfigureAwait(false);

                    if (await TtvAPI.AddChannelModerator(aUser.TwitchID.ToString()).ConfigureAwait(false))
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
            if (!IllAccess.Root(user)) return;
            if (UserInput.Length == 2)
            {
                var aUser = await _database.GetUserAsync(UserInput[1]).ConfigureAwait(false);
                if (aUser.dbID != -404)
                {
                    await TtvAPI.DeleteChannelModerator(aUser.TwitchID.ToString()).ConfigureAwait(false);
                    await _ircClient.SendMessage(string.Format(STRINGS.DeleteModSuccess, aUser.Name));
                }
                else
                {
                    var uID = await TtvAPI.GetUsetIDByName(UserInput[1]).ConfigureAwait(false);
                    if (uID != null)
                    {
                        await TtvAPI.DeleteChannelModerator(uID).ConfigureAwait(false);
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