using Microsoft.Extensions.Logging;
using SkillzBot.Interfaces;
using SkillzBot.MODELS;
using SkillzBot.Singleton;
using SkillzBot.TtvClient.TTVRewards;
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
        private readonly ILogger<IllCommandHandler> _logger;
        private readonly ITtvIRCClient _ircClient;
        private readonly Dictionary<string, IllCommand> _commandRegistry = new();
        private readonly CooldownManager _cooldownManager = new();
        private readonly List<IllCommand> _commands;

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

        public IllCommandHandler(ILogger<IllCommandHandler> logger, ITtvIRCClient ircClient, IllGames illGames, IllModeratorsInteractions modInteractions, IllCommands illCommands)
        {
            _logger = logger;
            _ircClient = ircClient;
            _commands = new()
            {
                new("!test", illCommands.TestingMethod, RequiredAccessLevel: AccessLevel.Root),
                new("!jobs", illCommands.getJobs, RequiredAccessLevel: AccessLevel.Root),
                new("!deleteall", illCommands.FlushChat, RequiredAccessLevel: AccessLevel.Root),
                new("!ping", illCommands.Ping, RequiredAccessLevel: AccessLevel.Root),
                new("!getallrewards", illCommands.GetAllRewards, RequiredAccessLevel: AccessLevel.Root),
                new("!find", illCommands.FindUser, RequiredAccessLevel: AccessLevel.Root),
                new("!trackuser", illCommands.TrackUser, RequiredAccessLevel: AccessLevel.Root),
                new("!sudo", illCommands.TrackUser, RequiredAccessLevel: AccessLevel.Root),
                new("!enablereward", illCommands.EnableReward, RequiredAccessLevel: AccessLevel.Root),
                new("!disablereward", illCommands.DisableReward, RequiredAccessLevel: AccessLevel.Root),
                new("!updatereward", illCommands.UpdateReward, RequiredAccessLevel: AccessLevel.Root),
                new("!deletereward", illCommands.DeleteReward, RequiredAccessLevel: AccessLevel.Root),
                new("!createreward", illCommands.CreateReward, RequiredAccessLevel: AccessLevel.Root),
                new("!deletemod", modInteractions.IllDeleteModerator, RequiredAccessLevel: AccessLevel.Root),
                new("!deletevip", illCommands.DeleteVIP, RequiredAccessLevel: AccessLevel.Root),
                new("!addvip", illCommands.AddVIP, RequiredAccessLevel: AccessLevel.Root),
                new("!addmod", modInteractions.IllAddModerator, RequiredAccessLevel: AccessLevel.Root),
                new("!debug", illCommands.ToggleDebug, RequiredAccessLevel: AccessLevel.Root),
                new("!addwhite", illCommands.AddTowhiteList, RequiredAccessLevel: AccessLevel.Root),
                new("!sub", illCommands.AddSubscription, RequiredAccessLevel: AccessLevel.Root),
                new("!subcheck", illCommands.CheckSubscription, RequiredAccessLevel: AccessLevel.Root),
                new("!шептун", illCommands.Sheptun, RequiredAccessLevel: AccessLevel.Root),
                new("!reloadfilters", illCommands.ReloadFilters, RequiredAccessLevel: AccessLevel.Root),

                new("!ban", illCommands.BanUserForTrack, RequiresCooldown: true, 60, BypassCooldown: false, AccessLevel.Broadcaster),

                new("!antibot", illCommands.SetAntiBotLvl, RequiresCooldown: true, 10, BypassCooldown: false, AccessLevel.Mod),
                new("!prediction", illCommands.Prediction, RequiredAccessLevel: AccessLevel.Mod),
                new("!lang", illCommands.ChangeLanguage, RequiredAccessLevel: AccessLevel.Mod),
                new("!silent", illCommands.ToggleSilentMode, RequiredAccessLevel: AccessLevel.Mod),
                new("!unban", illCommands.RemoveUserFromBlacklist, RequiredAccessLevel: AccessLevel.Mod),
                new("!chatfilter", illCommands.SetChatfilterLvl, RequiredAccessLevel: AccessLevel.Mod),
                new("!getmods", illCommands.GetMods, RequiredAccessLevel: AccessLevel.Mod),

                new("!clip", illCommands.CreateClip, RequiresCooldown: true, 600, BypassCooldown: false, IsGlobal: true),
                new("!ttvgg", illCommands.Ttvgg, RequiresCooldown: true, 600, BypassCooldown: false),
                new("!lp", illCommands.LpCommand, RequiresCooldown: true, 60, Aliases: new[] { "!лп", "!дз", "!kg", "!rank" }),
                new("!opgg", illCommands.OpGG, RequiresCooldown: true, 600, Aliases: new[] { "!опгг" }),
                new("!ртоп", illCommands.RouletteTop, RequiresCooldown: true, 4800, BypassCooldown: false),
                new("!топ", illCommands.GetTopChat, RequiresCooldown: true, 4800, BypassCooldown: false),
                new("!quizz", illCommands.StartQuizz, RequiresCooldown: true, 4800, BypassCooldown: false, IsGlobal: true),

                new("!sr", illCommands.QuizzMediaReward, RequiresCooldown: true, 60),
                new("!help", illCommands.Help, RequiresCooldown: true, 120, BypassCooldown: false),
                new("!points", illCommands.Points, RequiresCooldown: true, 600),
                new("!mmr", illCommands.GetMMR, RequiresCooldown: true, 4800, BypassCooldown: false, Aliases: new[] { "!ммр" }),
                new("!история", illCommands.GetMatchHistory, RequiresCooldown: true, 1800),

                new("!ставки", illCommands.Ludka, RequiresCooldown: true, 120, Aliases: new[] { "!лудка", "!kelrf", "!cnfdrb" }),

                new("!рулетка", illGames.Rulette, Aliases: new[] { "!hektnrf" }),
            };

            foreach (var cmd in _commands)
            {
                RegisterCommand(cmd.Name, cmd);
                if (cmd.Aliases != null)
                    foreach (var alias in cmd.Aliases)
                        _commandRegistry[alias] = cmd;
            }
        }

        private void RegisterCommand(string commandName, IllCommand command)
        {
            _commandRegistry[commandName] = command;
            if (command.RequiresCooldown && command.CooldownKey != null && command.Cooldown.HasValue)
                _cooldownManager.RegisterCooldown(command.CooldownKey, command.Cooldown.Value, command.BypassCooldown, command.IsGlobal);
        }
        private async Task CallWithCooldownAsync(UserObject user, string CommandName, Delegate methodLogic, string[]? command = null)
        {
            var result = await _cooldownManager.TryInvokeAsync(user, CommandName, methodLogic, methodLogic is Func<UserObject, string[], Task> ? command : null).ConfigureAwait(false);
            if (result != null)
            {
                if (IllAccess.Vip(user))
                    await _ircClient.SendMessage($"@{user.Name}, команда {CommandName} будет доступна через {result.Value.TotalSeconds:F1} сек.");
            }
        }
        public async Task<UserObject> CommandHandler(UserObject user, string message)
        {
            if (!IllSingleton.State.isSubActive && !IllAccess.Root(user)) return user;

            var commandParts = StringUtil.SplitAllWords(message);
            var commandName = commandParts[0].ToLower();

            if (_commandRegistry.TryGetValue(commandName, out var command))
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
                    _logger.LogCritical(e, "Error executing command: {CommandName}", commandName);
                }
            }
            return user;
        }
    }
}