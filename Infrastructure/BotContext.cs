using SkillzBot.Singleton;
using System.Threading.Tasks;

namespace SkillzBot.Infrastructure
{
    public class BotContext
    {
        public BotConfigModel Config { get; }
        public BotState State { get; }
        public GameState Game { get; }

        // Path helpers previously in IllSkillzBotMain
        public string DataPath { get; }
        public string SharedPath { get; }

        public BotContext(BotConfigModel config, string dataPath, string sharedPath)
        {
            Config = config;
            DataPath = dataPath;
            SharedPath = sharedPath;
            State = new BotState();
            Game = new GameState();
            Game.SummonerName = config.SummonerName;
        }

        public async Task LoadStateAsync()
        {
            // We pass the path explicitly now to avoid static dependency
            //await State.LoadAsync(DataPath);
            //await Game.LoadAsync(DataPath, Config.FilePaths.GameStateFileName);
            await State.LoadAsync();
            await Game.LoadAsync();
        }

        public async Task SaveStateAsync()
        {
            await State.SaveAsync();
            await Game.SaveAsync();
            //await State.SaveAsync(DataPath);
            //await Game.SaveAsync(DataPath, Config.FilePaths.GameStateFileName);
        }
    }
}