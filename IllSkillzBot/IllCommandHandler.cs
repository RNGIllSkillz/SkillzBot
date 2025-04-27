using SkillzBot.IRC;
using SkillzBot.MODELS;
using SkillzBot.Singleton;
using SkillzBot.Utils;
using System.Threading.Tasks;


namespace SkillzBot.IllSkillzBot
{
    internal class IllCommandHandler
    {
        private static readonly IllSingleton singleton = IllSingleton.GetInstance();
        public static async Task<UserObject> CommandHandler(UserObject user, string message) 
        {
            if (singleton.isActiveSub || user.Name == singleton.rootUser)
            {
                var Command = StringUtil.SplitAllWords(message);
                Command[0] = Command[0].ToLower();
                switch (Command[0])
                {
                    case "!help":
                        IllCommands.Help(user);
                        break;
                    case "!ttvgg":
                        await IllCommands.Ttvgg(user).ConfigureAwait(false);
                        break;
                    case "!points":
                        await IllCommands.Points(user).ConfigureAwait(false);
                        break;

                    case "!рулетка":
                        return await IllGames.Rulette(user).ConfigureAwait(false);

                    case "!ртоп":
                        await IllCommands.RouletteTop(user).ConfigureAwait(false);
                        break;

                    case "!prediction":
                        IllCommands.Prediction(user, Command);
                        break;

                    case "!лп":
                    case "!lp":
                    case "!rank":
                        await IllCommands.LpCommand(user, Command).ConfigureAwait(false);
                        break;

                    case "!ммр":
                    case "!mmr":
                        //await IllCommands.GetMMR(user).ConfigureAwait(false);
                        break;

                    case "!топ":
                        await IllCommands.GetTopChat(user).ConfigureAwait(false);
                        break;

                    case "!история":
                        //await IllCommands.GetMatchHistory(user).ConfigureAwait(false);
                        break;

                    case "!очередь":
                        //await IllCommands.GetTrackQueue(user).ConfigureAwait(false);
                        break;

                    case "!opgg":
                    case "!опгг":
                        IllCommands.OpGG(user);
                        break;

                    case "!трек":
                    case "!песня":
                    case "!song":
                        //await IllCommands.GetTreck(user).ConfigureAwait(false);
                        break;

                    case "!clip":
                        await IllCommands.CreateClip(user).ConfigureAwait(false);
                        break;

                    case "!deleteall":
                        await IllCommands.FlushChat(user).ConfigureAwait(false);
                        break;

                    case "!sr":
                        user = await IllCommands.QuizzMediaReward(user, Command).ConfigureAwait(false);
                        break;

                    case "!quizz":
                        if (user.Name == IllSingleton.GetInstance().rootUser)
                            await IllCommands.StartQuizz().ConfigureAwait(false);
                        break;

                    case "!shuffle":
                        break;

                    case "!ban":
                        await IllCommands.BanUserForTrack(user).ConfigureAwait(false);
                        break;

                    case "!ping":
                        if (user.Name == IllSingleton.GetInstance().rootUser)
                            TtvIRCClient.SendMessage("pong");
                        break;

                    case "!find":
                        await IllCommands.FindUser(user, Command).ConfigureAwait(false);
                        break;

                    case "!getallrewards":
                        await IllCommands.GetAllRewards(user).ConfigureAwait(false);
                        break;

                    case "!antibot":
                        IllCommands.SetAntiBotLvl(user, Command);
                        break;

                    case "!trackuser":
                        await IllCommands.TrackUser(user, Command).ConfigureAwait(false);
                        break;

                    case "!sudo":
                        await IllCommands.InjectSQL(user, Command).ConfigureAwait(false);
                        break;

                    case "!enablereward":
                        await IllCommands.EnableReward(user, message).ConfigureAwait(false);
                        break;

                    case "!disablereward":
                        await IllCommands.DisableReward(user, Command).ConfigureAwait(false);
                        break;

                    case "!updatereward":
                        await IllCommands.UpdateReward(user, message).ConfigureAwait(false);
                        break;

                    case "!deletereward":
                        await IllCommands.DeleteReward(user, Command).ConfigureAwait(false);
                        break;

                    case "!createreward":
                        await IllCommands.CreateReward(user, message).ConfigureAwait(false);
                        break;

                    case "!deletemod":
                        await IllModeratorsInteractions.IllDeleteModerator(user, Command).ConfigureAwait(false);
                        break;

                    case "!deletevip":
                        await IllCommands.DeleteVIP(user, Command).ConfigureAwait(false);
                        break;

                    case "!addvip":
                        await IllCommands.AddVIP(user, Command).ConfigureAwait(false);
                        break;

                    case "!addmod":
                        await IllModeratorsInteractions.IllAddModerator(user, Command).ConfigureAwait(false);
                        break;

                    case "!cron":
                        await IllCommands.StartCronTask(user, message).ConfigureAwait(false);
                        break;

                    case "!getalljobs":
                        await IllCommands.GetAllJobs(user).ConfigureAwait(false);
                        break;
                    case "!lang":
                        IllCommands.ChangeLanguage(user, Command);
                        break;
                    case "!test":
                        await IllCommands.TestingMethod(user).ConfigureAwait(false);
                        break;
                    case "!connect":
                        //IllCommands.ReconnectToPubSub(user);
                        break;
                    case "!debug":
                        IllCommands.ToggleDebug(user);
                        break;
                    case "!silent":
                        IllCommands.ToggleSilentMode(user);
                        break;
                    case "!unban":
                        await IllCommands.RemoveUserFromBlacklist(user, Command).ConfigureAwait(false);
                        break;
                    case "!addwhite":
                        IllCommands.AddTowhiteList(user, Command);
                        break;
                    case "!sub":
                        IllCommands.AddSubscription(user);
                        break;
                    case "!subcheck":
                        IllCommands.CheckSubscription(user);
                        break;
                    case "!chatfilter":
                        IllCommands.SetChatfilterLvl(user, Command);
                        break;
                    case "!getmods":
                        await IllCommands.GetMods(user).ConfigureAwait(false);
                        break;
                    case "!шептун":
                        await IllCommands.Sheptun(user).ConfigureAwait(false);
                        break;
                    default:
                        break;
                }                
            }
            return user;
        }
    }
}
