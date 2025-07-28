using IllSkillzBot;
using SkillzBot.MODELS;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System;

namespace SkillzBot.Singleton
{
    public class GameState
    {
        private readonly object _lock = new object();

        private string _summonerName;
        private string _summonerRegion;
        private int _startLP;
        private string _elo;
        private int _earnedLP;
        private int _numLosses;
        private int _numWins;
        private int _numGames;
        private string _tier;

        public string SummonerName { get => Get(_summonerName); set => Set(ref _summonerName, value); }
        public string SummonerRegion { get => Get(_summonerRegion); set => Set(ref _summonerRegion, value); }
        public int StartLP { get => Get(_startLP); set => Set(ref _startLP, value); }
        public string Elo { get => Get(_elo); set => Set(ref _elo, value); }
        public int EarnedLP { get => Get(_earnedLP); set => Set(ref _earnedLP, value); }
        public int NumLosses { get => Get(_numLosses); set => Set(ref _numLosses, value); }
        public int NumWins { get => Get(_numWins); set => Set(ref _numWins, value); }
        public int NumGames { get => Get(_numGames); set => Set(ref _numGames, value); }
        public string Tier { get => Get(_tier); set => Set(ref _tier, value); }

        private T Get<T>(T field) { lock (_lock) return field; }
        private void Set<T>(ref T field, T value)
        {
            lock (_lock)
            {
                if (!EqualityComparer<T>.Default.Equals(field, value))
                {
                    field = value;
                    _ = Task.Run(() => SaveAsync());
                }
            }
        }
        public async Task LoadAsync()
        {
            ConfPathes dataPath = IllSkillzBotMain.GetDataPath();
            var GameStateFilePath = Path.Combine(dataPath.uniquePath, "BotGameState.txt");

            try
            {
                if (File.Exists(GameStateFilePath))
                {
                    var json = await File.ReadAllTextAsync(GameStateFilePath);

                    // Check if file is empty or contains only whitespace
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        await CreateDefaultGameStateFileAsync(GameStateFilePath);
                        return;
                    }

                    var data = JsonSerializer.Deserialize<GameState>(json);
                    if (data != null)
                    {
                        lock (_lock)
                        {
                            _summonerName = data.SummonerName;
                            _summonerRegion = data.SummonerRegion;
                            _startLP = data.StartLP;
                            _elo = data.Elo;
                            _earnedLP = data.EarnedLP;
                            _numLosses = data.NumLosses;
                            _numWins = data.NumWins;
                            _tier = data.Tier;
                            _numGames = data.NumGames;
                        }
                    }
                    else
                    {
                        await CreateDefaultGameStateFileAsync(GameStateFilePath);
                    }
                }
                else
                {
                    await CreateDefaultGameStateFileAsync(GameStateFilePath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load game state: {ex.Message}");
                try
                {
                    await CreateDefaultGameStateFileAsync(GameStateFilePath);
                }
                catch (Exception createEx)
                {
                    Console.WriteLine($"Failed to create default game state file: {createEx.Message}");
                }
            }
        }

        private async Task CreateDefaultGameStateFileAsync(string filePath)
        {
            var defaultGameState = new GameState
            {
                SummonerName = string.Empty,
                SummonerRegion = string.Empty,
                StartLP = 0,
                Elo = string.Empty,
                EarnedLP = 0,
                NumLosses = 0,
                NumWins = 0,
                Tier = string.Empty,
                NumGames = 0
            };

            var json = JsonSerializer.Serialize(defaultGameState, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(filePath, json);

            lock (_lock)
            {
                _summonerName = defaultGameState.SummonerName;
                _summonerRegion = defaultGameState.SummonerRegion;
                _startLP = defaultGameState.StartLP;
                _elo = defaultGameState.Elo;
                _earnedLP = defaultGameState.EarnedLP;
                _numLosses = defaultGameState.NumLosses;
                _numWins = defaultGameState.NumWins;
                _tier = defaultGameState.Tier;
                _numGames = defaultGameState.NumGames;
            }
        }

        public async Task SaveAsync()
        {
            ConfPathes dataPath = IllSkillzBotMain.GetDataPath();
            var GameStateFilePath = Path.Combine(dataPath.uniquePath, IllSingleton.Config.FilePaths.GameStateFileName);
            try
            {
                BotGameStateModel data;
                lock (_lock)
                {
                    data = new BotGameStateModel
                    {
                        SummonerName = _summonerName,
                        SummonerRegion = _summonerRegion,
                        StartLP = _startLP,
                        Elo = _elo,
                        EarnedLP = _earnedLP,
                        NumLosses = _numLosses,
                        NumWins = _numWins,
                        Tier = _tier,
                        NumGames = _numGames
                    };
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var json = JsonSerializer.Serialize(data, options);
                if (!File.Exists(GameStateFilePath))
                    File.Create(GameStateFilePath);
                await File.WriteAllTextAsync(GameStateFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save game state: {ex.Message}");
            }
        }
    }
}
