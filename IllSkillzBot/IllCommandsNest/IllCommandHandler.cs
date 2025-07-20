using SkillzBot.IRC;
using SkillzBot.MODELS;
using SkillzBot.Singleton;
using SkillzBot.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static SkillzBot.IllSkillzBot.IllEnums;



namespace SkillzBot.IllSkillzBot.IllCommandsNest
{
#nullable enable
    internal class IllCommandHandler
    {
        internal record IllCommand
            (
            Delegate Method,
            bool RequiresCooldown = false,
            string? CooldownKey = null,
            TimeSpan? Cooldown = null,
            bool IgnoreAccessLevel = false,
            AccessLevel RequiredAccessLevel = AccessLevel.Any,
            bool IsGlobal = false
            );
        private static readonly Dictionary<string, IllCommand> CommandRegistry = new();

        private static readonly IllSingleton singleton = IllSingleton.GetInstance();
        static readonly CooldownManager CooldownManager = new();        

        private static readonly string ttvggCommandName = "!ttvgg";
        private static readonly int TtvggCooldownSec = 600;

        private static readonly string lpCommandName = "!lp";
        private static readonly int lpCooldownSec = 60;

        private static readonly string ClipCommandName = "!clip";
        private static readonly int ClipCooldownSec = 600;

        private static readonly string BanCommandName = "!ban";
        private static readonly int BanCooldownSec = 60;

        private static readonly string OpggCommandName = "!opgg";
        private static readonly int OpggCooldownSec = 600;        

        private static readonly string RtopCommandName = "!ртоп";
        private static readonly int RtopCooldownSec = 4800;

        private static readonly string TopCommandName = "!топ";
        private static readonly int TopCooldownSec = 4800;

        private static readonly string QuizzCommandName = "!quizz";
        private static readonly int QuizzCooldownSec = 4800;

        private static readonly string SongCommandName = "!song";
        private static readonly int SongCooldownSec = 60;

        private static readonly string FlushCommandName = "!deleteall";

        private static readonly string SrCommandName = "!sr";
        private static readonly int SrCooldownSec = 60;

        private static readonly string PingCommandName = "!ping";

        private static readonly string GetRevCommandName = "!getallrewards";

        private static readonly string AntiBotCommandName = "!antibot";
        private static readonly int AntiBotCooldownSec = 10;

        private static readonly string PredictCommandName = "!prediction";

        private static readonly string HelpCommandName = "!help";
        private static readonly int HelpCooldownSec = 120;

        private static readonly string PointsCommandName = "!points";
        private static readonly int PointsCooldownSec = 600;

        private static readonly string MmrCommandName = "!mmr";
        private static readonly int MmrCooldownSec = 4800;

        private static readonly string HistoryCommandName = "!история";
        private static readonly int HistoryCooldownSec = 1800;

        private static readonly string QueueCommandName = "!очередь";
        private static readonly int QueueCooldownSec = 240;

        private static readonly string RuletteCommandName = "!рулетка";
        private static readonly string FindCommandName = "!find";
        private static readonly string getJobsCommandName = "!jobs";
        private static readonly string TrackCommandName = "!trackuser";
        private static readonly string SudoCommandName = "!sudo";
        private static readonly string EnRewardCommandName = "!enablereward";
        private static readonly string DisRewardCommandName = "!disablereward";
        private static readonly string UpRewardCommandName = "!updatereward";
        private static readonly string DelRewardCommandName = "!deletereward";
        private static readonly string CreateRewardCommandName = "!createreward";
        private static readonly string DelModCommandName = "!deletemod";
        private static readonly string DevVipCommandName = "!deletevip";
        private static readonly string AddVipCommandName = "!addvip";
        private static readonly string AddModCommandName = "!addmod";
        private static readonly string DebugToggleCommandName = "!debug";
        private static readonly string WhiteCommandName = "!addwhite";
        private static readonly string LangCommandName = "!lang";
        private static readonly string SilentCommandName = "!silent";
        private static readonly string UnbanCommandName = "!unban";
        private static readonly string TestCommandName = "!test";
        private static readonly string SubCommandName = "!sub";
        private static readonly string SubCheckCommandName = "!subcheck";
        private static readonly string ChatFilterCommandName = "!chatfilter";
        private static readonly string GetModsCommandName = "!getmods";
        private static readonly string WhisperCommandName = "!шептун";

        static IllCommandHandler()
        {
            //root commands
            RegisterCommand(TestCommandName, new IllCommand(IllCommands.TestingMethod, RequiredAccessLevel: AccessLevel.Root));             //method for testing stuff
            RegisterCommand(getJobsCommandName, new IllCommand(IllCommands.getJobs, RequiredAccessLevel: AccessLevel.Root));                //get started cron jobs
            RegisterCommand(FlushCommandName, new IllCommand(IllCommands.FlushChat, RequiredAccessLevel: AccessLevel.Root));                //Flush ttv chat
            RegisterCommand(PingCommandName, new IllCommand(IllCommands.Ping, RequiredAccessLevel: AccessLevel.Root));                      //pong respond
            RegisterCommand(GetRevCommandName, new IllCommand(IllCommands.GetAllRewards, RequiredAccessLevel: AccessLevel.Root));           //display all revards in the channel
            RegisterCommand(FindCommandName, new IllCommand(IllCommands.FindUser, RequiredAccessLevel: AccessLevel.Root));                  //Find user
            RegisterCommand(TrackCommandName, new IllCommand(IllCommands.TrackUser, RequiredAccessLevel: AccessLevel.Root));                //Track user across all databases
            RegisterCommand(SudoCommandName, new IllCommand(IllCommands.TrackUser, RequiredAccessLevel: AccessLevel.Root));                 //Inject sql code via chan message
            RegisterCommand(EnRewardCommandName, new IllCommand(IllCommands.EnableReward, RequiredAccessLevel: AccessLevel.Root));          //Enable channel reward
            RegisterCommand(DisRewardCommandName, new IllCommand(IllCommands.DisableReward, RequiredAccessLevel: AccessLevel.Root));        //Disable channel reward
            RegisterCommand(UpRewardCommandName, new IllCommand(IllCommands.UpdateReward, RequiredAccessLevel: AccessLevel.Root));          //Update channel reward
            RegisterCommand(DelRewardCommandName, new IllCommand(IllCommands.DeleteReward, RequiredAccessLevel: AccessLevel.Root));         //Delete channel reward
            RegisterCommand(CreateRewardCommandName, new IllCommand(IllCommands.CreateReward, RequiredAccessLevel: AccessLevel.Root));      //Create new channel reward
            RegisterCommand(DelModCommandName, new IllCommand(IllModeratorsInteractions.IllDeleteModerator, RequiredAccessLevel: AccessLevel.Root)); //Remove channel moderator
            RegisterCommand(DevVipCommandName, new IllCommand(IllCommands.DeleteVIP, RequiredAccessLevel: AccessLevel.Root));               //Detele channel vip
            RegisterCommand(AddVipCommandName, new IllCommand(IllCommands.AddVIP, RequiredAccessLevel: AccessLevel.Root));                  //Add vip to a channel
            RegisterCommand(AddModCommandName, new IllCommand(IllModeratorsInteractions.IllAddModerator, RequiredAccessLevel: AccessLevel.Root)); //Add moterator to a chanel
            RegisterCommand(DebugToggleCommandName, new IllCommand(IllCommands.ToggleDebug, RequiredAccessLevel: AccessLevel.Root));        //toggle debug console output
            RegisterCommand(WhiteCommandName, new IllCommand(IllCommands.AddTowhiteList, RequiredAccessLevel: AccessLevel.Root));           //add word to a white list
            RegisterCommand(SubCommandName, new IllCommand(IllCommands.AddSubscription, RequiredAccessLevel: AccessLevel.Root));            //read data from subscription file
            RegisterCommand(SubCheckCommandName, new IllCommand(IllCommands.CheckSubscription, RequiredAccessLevel: AccessLevel.Root));     //check if subscription is active
            RegisterCommand(WhisperCommandName, new IllCommand(IllCommands.Sheptun, RequiredAccessLevel: AccessLevel.Root));                //send whisper as a broadcaster

            //Broadcaster commands
            RegisterCommand(BanCommandName, new IllCommand(IllCommands.BanUserForTrack, true, BanCommandName, TimeSpan.FromSeconds(BanCooldownSec), IgnoreAccessLevel: true, RequiredAccessLevel: AccessLevel.Broadcaster));

            //Mod commands
            RegisterCommand(AntiBotCommandName, new IllCommand(IllCommands.SetAntiBotLvl, true, AntiBotCommandName, TimeSpan.FromSeconds(AntiBotCooldownSec), IgnoreAccessLevel: true, RequiredAccessLevel: AccessLevel.Mod));
            RegisterCommand(PredictCommandName, new IllCommand(IllCommands.Prediction, RequiredAccessLevel: AccessLevel.Mod));
            RegisterCommand(LangCommandName, new IllCommand(IllCommands.ChangeLanguage, RequiredAccessLevel: AccessLevel.Mod));
            RegisterCommand(SilentCommandName, new IllCommand(IllCommands.ToggleSilentMode, RequiredAccessLevel: AccessLevel.Mod));
            RegisterCommand(UnbanCommandName, new IllCommand(IllCommands.RemoveUserFromBlacklist, RequiredAccessLevel: AccessLevel.Mod));
            RegisterCommand(ChatFilterCommandName, new IllCommand(IllCommands.SetChatfilterLvl, RequiredAccessLevel: AccessLevel.Mod));
            RegisterCommand(GetModsCommandName, new IllCommand(IllCommands.GetMods, RequiredAccessLevel: AccessLevel.Mod));

            //Chat commands
            RegisterCommand(ClipCommandName, new IllCommand(IllCommands.CreateClip, true, ClipCommandName, TimeSpan.FromSeconds(ClipCooldownSec), IgnoreAccessLevel: true));
            RegisterCommand(ttvggCommandName, new IllCommand(IllCommands.Ttvgg, true, ttvggCommandName, TimeSpan.FromSeconds(TtvggCooldownSec), IgnoreAccessLevel: true));
            RegisterCommand(lpCommandName, new IllCommand(IllCommands.LpCommand, true, lpCommandName, TimeSpan.FromSeconds(lpCooldownSec)));
            RegisterCommand(OpggCommandName, new IllCommand(IllCommands.OpGG, true, OpggCommandName, TimeSpan.FromSeconds(OpggCooldownSec)));
            RegisterCommand(RtopCommandName, new IllCommand(IllCommands.RouletteTop, true, RtopCommandName, TimeSpan.FromSeconds(RtopCooldownSec), IgnoreAccessLevel: true));
            RegisterCommand(TopCommandName, new IllCommand(IllCommands.GetTopChat, true, TopCommandName, TimeSpan.FromSeconds(TopCooldownSec), IgnoreAccessLevel: true));
            RegisterCommand(QuizzCommandName, new IllCommand(IllCommands.StartQuizz, true, QuizzCommandName, TimeSpan.FromSeconds(QuizzCooldownSec), IgnoreAccessLevel: true, IsGlobal: true));
            RegisterCommand(SongCommandName, new IllCommand(IllCommands.GetTreck, true, SongCommandName, TimeSpan.FromSeconds(SongCooldownSec)));
            RegisterCommand(SrCommandName, new IllCommand(IllCommands.QuizzMediaReward, true, SrCommandName, TimeSpan.FromSeconds(SrCooldownSec)));
            RegisterCommand(HelpCommandName, new IllCommand(IllCommands.Help, true, HelpCommandName, TimeSpan.FromSeconds(HelpCooldownSec), IgnoreAccessLevel: true));
            RegisterCommand(PointsCommandName, new IllCommand(IllCommands.Points, true, PointsCommandName, TimeSpan.FromSeconds(PointsCooldownSec)));
            RegisterCommand(MmrCommandName, new IllCommand(IllCommands.GetMMR, true, MmrCommandName, TimeSpan.FromSeconds(MmrCooldownSec), IgnoreAccessLevel: true));
            RegisterCommand(HistoryCommandName, new IllCommand(IllCommands.GetMatchHistory, true, HistoryCommandName, TimeSpan.FromSeconds(HistoryCooldownSec)));
            RegisterCommand(QueueCommandName, new IllCommand(IllCommands.GetTrackQueue, true, QueueCommandName, TimeSpan.FromSeconds(QueueCooldownSec)));


            // Commands without cooldowns
            RegisterCommand(RuletteCommandName, new IllCommand(IllGames.Rulette)); //Cooldown handles internally

            // Aliases
            CommandRegistry["!лп"] = CommandRegistry[lpCommandName];
            CommandRegistry["!дз"] = CommandRegistry[lpCommandName];
            CommandRegistry["!kg"] = CommandRegistry[lpCommandName];
            CommandRegistry["!rank"] = CommandRegistry[lpCommandName];

            CommandRegistry["!hektnrf"] = CommandRegistry[RuletteCommandName];

            CommandRegistry["!опгг"] = CommandRegistry[OpggCommandName];

            CommandRegistry["!трек"] = CommandRegistry[SongCommandName];
            CommandRegistry["!песня"] = CommandRegistry[SongCommandName];

            CommandRegistry["!ммр"] = CommandRegistry[MmrCommandName];
        }

        private static void RegisterCommand(string name, IllCommand command)
        {
            CommandRegistry[name] = command;
            if (command.RequiresCooldown && command.CooldownKey != null && command.Cooldown.HasValue)
            {
                CooldownManager.RegisterCooldown(command.CooldownKey, command.Cooldown.Value, command.IgnoreAccessLevel, command.IsGlobal);
            }
        }
        private static async Task CallWithCooldownAsync(UserObject user, string CommandName, Delegate methodLogic, string[]? command = null)
        {
            var result = await CooldownManager.TryInvokeAsync(user, CommandName, methodLogic, methodLogic is Func<UserObject, string[], Task> ? command : null).ConfigureAwait(false);
            if (result != null)
            {
                if (IllAccess.Vip(user))
                {
                    TtvIRCClient.SendMessage($"@{user.Name}, команда {CommandName} будет доступна через {result.Value.TotalSeconds:F1} сек.");
                }
            }
        }
        public static async Task<UserObject> CommandHandler(UserObject user, string message)
        {
            if (!singleton.isActiveSub && !IllAccess.Root(user)) return user;

            var commandParts = StringUtil.SplitAllWords(message);
            var commandName = commandParts[0].ToLower();

            if (CommandRegistry.TryGetValue(commandName, out var command))
            {
                if (!IllAccess.MeetsLevel(user, command.RequiredAccessLevel)) return user;
                if (command.RequiresCooldown && command.CooldownKey != null)
                    await CallWithCooldownAsync(user, command.CooldownKey, command.Method, commandParts).ConfigureAwait(false);
                else
                    await CooldownManager.InvokeDelegate(command.Method, user, commandParts).ConfigureAwait(false);
            }
            return user;
        }
    }
}
