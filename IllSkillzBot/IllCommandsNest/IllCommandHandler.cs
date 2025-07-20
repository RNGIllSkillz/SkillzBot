using SkillzBot.IRC;
using SkillzBot.MODELS;
using SkillzBot.Singleton;
using SkillzBot.Utils;
using SkillzBot.WRITERS;
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
        private record CommandDefinition
            (
                string Name,
                Delegate Method,
                bool RequiresCooldown = false,
                TimeSpan? Cooldown = null,
                bool IgnoreAccessLevel = false,
                AccessLevel RequiredAccessLevel = AccessLevel.Any,
                bool IsGlobal = false,
                string[]? Aliases = null
            );
        private static readonly Dictionary<string, IllCommand> CommandRegistry = new();        
        static readonly CooldownManager CooldownManager = new();
        private static readonly IllSingleton singleton = IllSingleton.GetInstance();

        private static readonly List<CommandDefinition> Commands = new()
        {
            // Root Commands
            new("!test", IllCommands.TestingMethod, RequiredAccessLevel: AccessLevel.Root),
            new("!jobs", IllCommands.getJobs, RequiredAccessLevel: AccessLevel.Root),
            new("!deleteall", IllCommands.FlushChat, RequiredAccessLevel: AccessLevel.Root),
            new("!ping", IllCommands.Ping, RequiredAccessLevel: AccessLevel.Root),
            new("!getallrewards", IllCommands.GetAllRewards, RequiredAccessLevel: AccessLevel.Root),
            new("!find", IllCommands.FindUser, RequiredAccessLevel: AccessLevel.Root),
            new("!trackuser", IllCommands.TrackUser, RequiredAccessLevel: AccessLevel.Root),
            new("!sudo", IllCommands.TrackUser, RequiredAccessLevel: AccessLevel.Root),
            new("!enablereward", IllCommands.EnableReward, RequiredAccessLevel: AccessLevel.Root),
            new("!disablereward", IllCommands.DisableReward, RequiredAccessLevel: AccessLevel.Root),
            new("!updatereward", IllCommands.UpdateReward, RequiredAccessLevel: AccessLevel.Root),
            new("!deletereward", IllCommands.DeleteReward, RequiredAccessLevel: AccessLevel.Root),
            new("!createreward", IllCommands.CreateReward, RequiredAccessLevel: AccessLevel.Root),
            new("!deletemod", IllModeratorsInteractions.IllDeleteModerator, RequiredAccessLevel: AccessLevel.Root),
            new("!deletevip", IllCommands.DeleteVIP, RequiredAccessLevel: AccessLevel.Root),
            new("!addvip", IllCommands.AddVIP, RequiredAccessLevel: AccessLevel.Root),
            new("!addmod", IllModeratorsInteractions.IllAddModerator, RequiredAccessLevel: AccessLevel.Root),
            new("!debug", IllCommands.ToggleDebug, RequiredAccessLevel: AccessLevel.Root),
            new("!addwhite", IllCommands.AddTowhiteList, RequiredAccessLevel: AccessLevel.Root),
            new("!sub", IllCommands.AddSubscription, RequiredAccessLevel: AccessLevel.Root),
            new("!subcheck", IllCommands.CheckSubscription, RequiredAccessLevel: AccessLevel.Root),
            new("!шептун", IllCommands.Sheptun, RequiredAccessLevel: AccessLevel.Root),

            // Broadcaster
            new("!ban", IllCommands.BanUserForTrack, true, TimeSpan.FromSeconds(60), true, AccessLevel.Broadcaster),

            // Mod
            new("!antibot", IllCommands.SetAntiBotLvl, true, TimeSpan.FromSeconds(10), true, AccessLevel.Mod),
            new("!prediction", IllCommands.Prediction, RequiredAccessLevel: AccessLevel.Mod),
            new("!lang", IllCommands.ChangeLanguage, RequiredAccessLevel: AccessLevel.Mod),
            new("!silent", IllCommands.ToggleSilentMode, RequiredAccessLevel: AccessLevel.Mod),
            new("!unban", IllCommands.RemoveUserFromBlacklist, RequiredAccessLevel: AccessLevel.Mod),
            new("!chatfilter", IllCommands.SetChatfilterLvl, RequiredAccessLevel: AccessLevel.Mod),
            new("!getmods", IllCommands.GetMods, RequiredAccessLevel: AccessLevel.Mod),

            // Chat commands
            new("!clip", IllCommands.CreateClip, true, TimeSpan.FromSeconds(600), true, IsGlobal: true),
            new("!ttvgg", IllCommands.Ttvgg, true, TimeSpan.FromSeconds(600), true),
            new("!lp", IllCommands.LpCommand, true, TimeSpan.FromSeconds(60), Aliases: new[] { "!лп", "!дз", "!kg", "!rank" }),
            new("!opgg", IllCommands.OpGG, true, TimeSpan.FromSeconds(600), Aliases: new[] { "!опгг" }),
            new("!ртоп", IllCommands.RouletteTop, true, TimeSpan.FromSeconds(4800), true),
            new("!топ", IllCommands.GetTopChat, true, TimeSpan.FromSeconds(4800), true),
            new("!quizz", IllCommands.StartQuizz, true, TimeSpan.FromSeconds(4800), true, IsGlobal: true),
            new("!song", IllCommands.GetTreck, true, TimeSpan.FromSeconds(60), Aliases: new[] { "!трек", "!песня" }),
            new("!sr", IllCommands.QuizzMediaReward, true, TimeSpan.FromSeconds(60)),
            new("!help", IllCommands.Help, true, TimeSpan.FromSeconds(120), true),
            new("!points", IllCommands.Points, true, TimeSpan.FromSeconds(600)),
            new("!mmr", IllCommands.GetMMR, true, TimeSpan.FromSeconds(4800), true, Aliases: new[] { "!ммр" }),
            new("!история", IllCommands.GetMatchHistory, true, TimeSpan.FromSeconds(1800)),
            new("!очередь", IllCommands.GetTrackQueue, true, TimeSpan.FromSeconds(240)),

            // Special internal cooldown logic
            new("!рулетка", IllGames.Rulette, Aliases: new[] { "!hektnrf" }),            
        };

        static IllCommandHandler()
        {
            foreach (var def in Commands)
            {
                var cmd = new IllCommand(
                    def.Method,
                    def.RequiresCooldown,
                    def.Name,
                    def.Cooldown,
                    def.IgnoreAccessLevel,
                    def.RequiredAccessLevel,
                    def.IsGlobal
                );

                RegisterCommand(def.Name, cmd);

                if (def.Aliases != null)                
                    foreach (var alias in def.Aliases)
                        CommandRegistry[alias] = cmd;                
            }
        }

        private static void RegisterCommand(string commandName, IllCommand command)
        {
            CommandRegistry[commandName] = command;
            if (command.RequiresCooldown && command.CooldownKey != null && command.Cooldown.HasValue)            
                CooldownManager.RegisterCooldown(command.CooldownKey, command.Cooldown.Value, command.IgnoreAccessLevel, command.IsGlobal);            
        }
        private static async Task CallWithCooldownAsync(UserObject user, string CommandName, Delegate methodLogic, string[]? command = null)
        {
            var result = await CooldownManager.TryInvokeAsync(user, CommandName, methodLogic, methodLogic is Func<UserObject, string[], Task> ? command : null).ConfigureAwait(false);
            if (result != null)
            {
                if (IllAccess.Vip(user))                
                    TtvIRCClient.SendMessage($"@{user.Name}, команда {CommandName} будет доступна через {result.Value.TotalSeconds:F1} сек.");                
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
                try
                {
                    if (command.RequiresCooldown && command.CooldownKey != null)
                        await CallWithCooldownAsync(user, command.CooldownKey, command.Method, commandParts).ConfigureAwait(false);
                    else
                        await CooldownManager.InvokeDelegate(command.Method, user, commandParts).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Log.WriteLog(e, "InvokeDelegate");
                }
            }
            return user;
        }
    }
}
