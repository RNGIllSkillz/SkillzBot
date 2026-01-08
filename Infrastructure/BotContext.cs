using SkillzBot.Singleton;
using System.Threading.Tasks;

namespace SkillzBot.Infrastructure
{
    public class BotContext
    {
        public BotConfigModel Config { get; }
        public BotState State { get; }
        public GameState Game { get; }

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

            await State.LoadAsync().ConfigureAwait(false);
            await Game.LoadAsync().ConfigureAwait(false);
        }

        public async Task SaveStateAsync()
        {
            await State.SaveAsync().ConfigureAwait(false);
            await Game.SaveAsync().ConfigureAwait(false);

        }
    }
}