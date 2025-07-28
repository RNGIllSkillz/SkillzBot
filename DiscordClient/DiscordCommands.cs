using SkillzBot.IllSkillzBot;
using SkillzBot.IllSkillzBot.IllCommandsNest;
using SkillzBot.Utils;
using SkillzBot.Singleton;
using System.Linq;
using System.Threading.Tasks;

namespace SkillzBot.Discord
{
    internal class DiscordCommands
    {
        public static async Task CommandHandler(string UserInput)
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
                        lp = await IllCommands.GetLpAsync(sNameTemp, command.Last()).ConfigureAwait(false);
                        await DiscordClient.SendMessage($"Призыватель {sNameTemp} - {lp.RANK} {lp.LPoints} LP", IllSingleton.Config.DiscordSpamID).ConfigureAwait(false);
                    }
                    else
                    {
                        lp = await IllCommands.GetLpAsync().ConfigureAwait(false);  
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
                    //var responce = await ChatGPT.GetGptResponceBasic(StringUtil.GetCommandFromUserInput(command));
                    await DiscordClient.SendMessage("в разработке...").ConfigureAwait(false);
                    break;
                    
                default:
                    await DiscordClient.SendMessage("Unknown command.", IllSingleton.Config.DiscordSpamID).ConfigureAwait(false);
                    break;

            }
        }
        
        /*
        [Command("hello")]
        public async Task HelloCommand()
        {
            await DiscordClient.SendMessage ("Hello, this is the response to the hello command!").ConfigureAwait(false);
        }

        [Command("!test")]
        public async Task TestCommand()
        {
            await DiscordClient.SendMessage("Hello, this is the response to the test command!").ConfigureAwait(false);
        }
        */
    }
}
