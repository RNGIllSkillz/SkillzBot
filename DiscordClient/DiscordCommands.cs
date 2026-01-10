using SkillzBot.IllSkillzBot;
using SkillzBot.IllSkillzBot.IllCommandsNest;
using SkillzBot.Utils;
using SkillzBot.IllConfiguration; 
using System.Linq;
using System.Threading.Tasks;
using SkillzBot.Interfaces;

namespace SkillzBot.Discord
{
    internal class DiscordCommands
    {
        private readonly ITtvIRCClient _ircClient;
        private readonly IllCommands _illCommands;
        private readonly BotConfigModel _config;
        private readonly IGameStateService _gameState;

        private readonly DiscordClient _discordClient;
        private readonly IllGames _illGames;

        public DiscordCommands(
            ITtvIRCClient ircClient,
            IllCommands illCommands,
            BotConfigModel config,
            IGameStateService gameState,
            DiscordClient discordClient, 
            IllGames illGames)           
        {
            _ircClient = ircClient;
            _illCommands = illCommands;
            _config = config;
            _gameState = gameState;
            _discordClient = discordClient;
            _illGames = illGames;
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
                                // 3. Use instance _discordClient
                                await _discordClient.SendMessage("Ошибка ввода (не указан регион). Поддерживаемые регионы - euw, ru, na");
                                return;
                        }
                        var sNameTemp = StringUtil.RemoveWhitespace(StringUtil.GetCommandFromUserInput(command.Take(command.Count() - 1).ToArray()));
                        lp = await _illCommands.GetLpAsync(sNameTemp, command.Last());

                        // Use instance _discordClient
                        await _discordClient.SendMessage($"Призыватель {sNameTemp} - {lp.RANK} {lp.LPoints} LP", _config.DiscordSpamID);
                    }
                    else
                    {
                        lp = await _illCommands.GetLpAsync();
                        // Use instance _discordClient and _gameState.Current
                        await _discordClient.SendMessage($"Призыватель {_gameState.Current.SummonerName} - {lp.RANK} {lp.LPoints} LP", _config.DiscordSpamID);
                    }
                    break;

                case "!8ball":
                    if (command.Length < 2)
                        await _discordClient.SendMessage("Нет вопроса - нет ответа.", _config.DiscordSpamID);
                    else
                        // 4. Use instance _illGames
                        await _discordClient.SendMessage(_illGames.GetMagic8BallAnswer(), _config.DiscordSpamID);
                    break;
                case "!gpt":
                    await _discordClient.SendMessage("в разработке...");
                    break;

                case "!say":
                    if (command.Length > 1)
                    {
                        string message = StringUtil.GetCommandFromUserInput(command);
                        await _ircClient.SendMessage(message);
                    }
                    break;

                default:
                    await _discordClient.SendMessage("Unknown command.", _config.DiscordSpamID);
                    break;
            }
        }
    }
}