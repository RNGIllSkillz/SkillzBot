using SkillzBot.API.Twitch;
using SkillzBot.IRC;
using SkillzBot.MODELS;
using SkillzBot.IllSTRINGS;
using System.Threading.Tasks;
using SkillzBot.MySQL;
using SkillzBot.Hosts;

namespace SkillzBot.IllSkillzBot
{
    internal static class IllModeratorsInteractions
    {
        public static async Task IllAllModsNotification(string message)
        {
            var mIds = await TtvAPI.GetAllMods().ConfigureAwait(false);
            if (mIds == null) return;
            foreach (var mId in mIds)
            {
                await TtvAPI.SendWhisper(mId.UserId, message).ConfigureAwait(false);
                await Task.Delay(100).ConfigureAwait(false);
            }
        }
        public static async Task IllAddModerator(UserObject user, string[] UserInput)
        {
            if (!IllAccess.Root(user)) return;
            if (UserInput.Length == 2)
            {
                var aUser = await IllServiceProvider.Database.GetUserAsync(UserInput[1]).ConfigureAwait(false);
                if (aUser.dbID != -404)
                {
                    if (aUser.isVip == 1)
                        await TtvAPI.DeleteChannelVIP(aUser.TwitchID.ToString()).ConfigureAwait(false);

                    if (await TtvAPI.AddChannelModerator(aUser.TwitchID.ToString()).ConfigureAwait(false))
                        TtvIRCClient.SendMessage(string.Format(STRINGS.AddModSuccess, aUser.Name));
                    else
                        TtvIRCClient.SendMessage("Модерытор не добавлен, произошла ошибка.");
                }
                else
                    TtvIRCClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, user.Name, UserInput[1]));
            }
            else
                TtvIRCClient.SendMessage(STRINGS.InputERROR);
        }
        public static async Task IllDeleteModerator(UserObject user, string[] UserInput)
        {
            if (!IllAccess.Root(user)) return;
            if (UserInput.Length == 2)
            {
                var aUser = await IllServiceProvider.Database.GetUserAsync(UserInput[1]).ConfigureAwait(false);
                if (aUser.dbID != -404)
                {
                    await TtvAPI.DeleteChannelModerator(aUser.TwitchID.ToString()).ConfigureAwait(false);
                    TtvIRCClient.SendMessage(string.Format(STRINGS.DeleteModSuccess, aUser.Name));
                }
                else
                {
                    var uID = await TtvAPI.GetUsetIDByName(UserInput[1]).ConfigureAwait(false);
                    if (uID != null)
                    {
                        await TtvAPI.DeleteChannelModerator(uID).ConfigureAwait(false);
                        TtvIRCClient.SendMessage(string.Format(STRINGS.DeleteModSuccess, aUser.Name));
                    }
                    else
                        TtvIRCClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, user.Name, UserInput[1]));
                }
            }
            else
                TtvIRCClient.SendMessage(STRINGS.InputERROR);
        }
    }
}
