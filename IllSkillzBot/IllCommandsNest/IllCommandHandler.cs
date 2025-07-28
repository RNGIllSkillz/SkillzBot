using Microsoft.Extensions.Logging;
using SkillzBot.Hosts;
using SkillzBot.IRC;
using SkillzBot.MODELS;
using SkillzBot.Utils;
using SkillzBot.Singleton;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static SkillzBot.IllSkillzBot.IllEnums;

namespace SkillzBot.IllSkillzBot.IllCommandsNest
{
#nullable enable
    internal class IllCommandHandler
    {
        private static readonly ILogger<IllChatMessageHandler> _logger = IllServiceProvider.GetLogger<IllChatMessageHandler>();

        //BypassCooldown is only for elevated users, by default elevated users are bypassing cooldowns. Regular users cant bypass cooldowns.
        internal record IllCommand
            (
                string Name,
                Delegate Method,
                bool RequiresCooldown = false,
                int CooldownSeconds = 0,
                bool BypassCooldown = true,
                AccessLevel RequiredAccessLevel = AccessLevel.Any,
                bool IsGlobal = false,
                string[]? Aliases = null
            )
        {
            public string? CooldownKey => RequiresCooldown ? Name : null;
            public TimeSpan? Cooldown => CooldownSeconds > 0 ? TimeSpan.FromSeconds(CooldownSeconds) : null;
        };

        private static readonly Dictionary<string, IllCommand> CommandRegistry = new();        
        static readonly CooldownManager CooldownManager = new();
        private static readonly List<IllCommand> Commands = new()
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
        new("!ban", IllCommands.BanUserForTrack, RequiresCooldown: true, 60, BypassCooldown: false, AccessLevel.Broadcaster),

        // Mod
        new("!antibot", IllCommands.SetAntiBotLvl, RequiresCooldown: true, 10, BypassCooldown: false, AccessLevel.Mod),
        new("!prediction", IllCommands.Prediction, RequiredAccessLevel: AccessLevel.Mod),
        new("!lang", IllCommands.ChangeLanguage, RequiredAccessLevel: AccessLevel.Mod),
        new("!silent", IllCommands.ToggleSilentMode, RequiredAccessLevel: AccessLevel.Mod),
        new("!unban", IllCommands.RemoveUserFromBlacklist, RequiredAccessLevel: AccessLevel.Mod),
        new("!chatfilter", IllCommands.SetChatfilterLvl, RequiredAccessLevel: AccessLevel.Mod),
        new("!getmods", IllCommands.GetMods, RequiredAccessLevel: AccessLevel.Mod),

        // Chat commands
        new("!clip", IllCommands.CreateClip, RequiresCooldown: true, 600, BypassCooldown: false, IsGlobal: true),
        new("!ttvgg", IllCommands.Ttvgg, RequiresCooldown: true, 600, BypassCooldown: false),
        new("!lp", IllCommands.LpCommand, RequiresCooldown: true, 60, Aliases: new[] { "!лп", "!дз", "!kg", "!rank" }),
        new("!opgg", IllCommands.OpGG, RequiresCooldown: true, 600, Aliases: new[] { "!опгг" }),
        new("!ртоп", IllCommands.RouletteTop, RequiresCooldown: true, 4800, BypassCooldown: false),
        new("!топ", IllCommands.GetTopChat, RequiresCooldown: true, 4800, BypassCooldown: false),
        new("!quizz", IllCommands.StartQuizz, RequiresCooldown: true, 4800, BypassCooldown: false, IsGlobal: true),
        new("!song", IllCommands.GetTreck, RequiresCooldown: true, 60, Aliases: new[] { "!трек", "!песня" }),
        new("!sr", IllCommands.QuizzMediaReward, RequiresCooldown: true, 60),
        new("!help", IllCommands.Help, RequiresCooldown: true, 120, BypassCooldown: false),
        new("!points", IllCommands.Points, RequiresCooldown: true, 600),
        new("!mmr", IllCommands.GetMMR, RequiresCooldown: true, 4800, BypassCooldown: false, Aliases: new[] { "!ммр" }),
        new("!история", IllCommands.GetMatchHistory, RequiresCooldown: true, 1800),
        new("!очередь", IllCommands.GetTrackQueue, RequiresCooldown: true, 240),

        // Special internal cooldown logic
        new("!рулетка", IllGames.Rulette, Aliases: new[] { "!hektnrf" }),
    };

        static IllCommandHandler()
        {
            foreach (var cmd in Commands)
            {
                RegisterCommand(cmd.Name, cmd);
                if (cmd.Aliases != null)
                    foreach (var alias in cmd.Aliases)
                        CommandRegistry[alias] = cmd;
            }
        }

        private static void RegisterCommand(string commandName, IllCommand command)
        {
            CommandRegistry[commandName] = command;
            if (command.RequiresCooldown && command.CooldownKey != null && command.Cooldown.HasValue)            
                CooldownManager.RegisterCooldown(command.CooldownKey, command.Cooldown.Value, command.BypassCooldown, command.IsGlobal);            
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
            if (!IllSingleton.State.isSubActive && !IllAccess.Root(user)) return user;

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
                    _logger.LogCritical(e, "InvokeDelegate");
                }
            }
            return user;
        }
    }
}
