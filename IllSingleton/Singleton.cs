using System;
using System.Threading.Tasks;

namespace SkillzBot.Singleton
{
    public static class IllSingleton
    {
        private static SingletonService _service;
        private static readonly object _lock = new object();
        public static async Task InitializeAsync(string configPath)
        {
            if (_service == null)
            {
                var config = await BotConfigurationFactory.CreateAsync(configPath).ConfigureAwait(false);
                var service = new SingletonService(config);                
                lock (_lock)
                    _service ??= service;
                await _service.LoadStateAsync().ConfigureAwait(false);
            }
        }
        public static BotConfigModel Config
        {
            get
            {
                EnsureInitialized();
                return _service.Configuration;
            }
        }
        public static BotState State
        {
            get
            {
                EnsureInitialized();
                return _service.State;
            }
        }
        public static GameState Game
        {
            get
            {
                EnsureInitialized();
                return _service.GameState;
            }
        }
        public static async Task SaveAsync()
        {
            EnsureInitialized();
            await _service.SaveStateAsync();
        }
        /*
        public static BotService Service
        {
            get
            {
                EnsureInitialized();
                return _service;
            }
        }
        */
        private static void EnsureInitialized()
        {
            if (_service == null)
            {
                throw new InvalidOperationException(
                    "IllSingleton services must be initialized first by calling IllSingleton.InitializeAsync() at application startup");
            }
        }
        public static bool IsInitialized => _service != null;
    }  
}
