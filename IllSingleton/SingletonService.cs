using System.Threading.Tasks;
using System;

namespace SkillzBot.Singleton
{
    public class SingletonService
    {
        public BotConfigModel Configuration { get; }
        public BotState State { get; }
        public GameState GameState { get; }

        public SingletonService(BotConfigModel configuration)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            State = new BotState();
            GameState = new GameState();
            GameState.SummonerName = configuration.SummonerName;
        }
        public async Task LoadStateAsync()
        {
            await State.LoadAsync().ConfigureAwait(false);
            await GameState.LoadAsync().ConfigureAwait(false);
        }
        public async Task SaveStateAsync()
        {
            await State.SaveAsync().ConfigureAwait(false);
            await GameState.SaveAsync().ConfigureAwait(false);
        }
    }
}