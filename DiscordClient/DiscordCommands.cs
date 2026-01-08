using SkillzBot.IllSkillzBot;
using SkillzBot.IllSkillzBot.IllCommandsNest;
using SkillzBot.Utils;
using SkillzBot.Singleton;
using System.Linq;
using System.Threading.Tasks;
using SkillzBot.Interfaces;

namespace SkillzBot.Discord
{
    internal class DiscordCommands
    {
        private readonly ITtvIRCClient _ircClient;
        private readonly IllCommands _illCommands;

        public DiscordCommands(ITtvIRCClient ircClient, IllCommands illCommands)
        {
            _ircClient = ircClient;
            _illCommands = illCommands;
        }

        public async Task CommandHandler(string UserInput)
        {
            var command = StringUtil.SplitAllWords(UserInput.ToLower());
            switch (command[0])
            {
                case "!lp":
                case "!лп":
                    MODELS.LP lp;
                    if (command.Length > 1)
                    {
                        switch (command.Last())
                        {
                            case "ru":
                            case "euw":
                            case "na":
                                break;
                            default:
                                await DiscordClient.SendMessage("Ошибка ввода (не указан регион). Поддерживаемые регионы - euw, ru, na").ConfigureAwait(false);
                                return;
                        }
                        var sNameTemp = StringUtil.RemoveWhitespace(StringUtil.GetCommandFromUserInput(command.Take(command.Count() - 1).ToArray()));
                        lp = await _illCommands.GetLpAsync(sNameTemp, command.Last()).ConfigureAwait(false);
                        await DiscordClient.SendMessage($"Призыватель {sNameTemp} - {lp.RANK} {lp.LPoints} LP", IllSingleton.Config.DiscordSpamID).ConfigureAwait(false);
                    }
                    else
                    {
                        lp = await _illCommands.GetLpAsync().ConfigureAwait(false);
                        await DiscordClient.SendMessage($"Призыватель {IllSingleton.Game.SummonerName} - {lp.RANK} {lp.LPoints} LP", IllSingleton.Config.DiscordSpamID).ConfigureAwait(false);
                    }
                    break;

                case "!8ball":
                    if (command.Length < 2)
                        await DiscordClient.SendMessage("Нет вопроса - нет ответа.", IllSingleton.Config.DiscordSpamID).ConfigureAwait(false);
                    else
                        await DiscordClient.SendMessage(IllGames.GetMagic8BallAnswer(), IllSingleton.Config.DiscordSpamID).ConfigureAwait(false);
                    break;
                case "!gpt":

                    await DiscordClient.SendMessage("в разработке...").ConfigureAwait(false);
                    break;

                case "!say":
                    if (command.Length > 1)
                    {
                        string message = StringUtil.GetCommandFromUserInput(command);
                        await _ircClient.SendMessage(message);
                    }
                    break;

                default:
                    await DiscordClient.SendMessage("Unknown command.", IllSingleton.Config.DiscordSpamID).ConfigureAwait(false);
                    break;
            }
        }
    }
}