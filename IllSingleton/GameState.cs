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
        private bool _isLoaded = false;

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
            bool changed = false;
            lock (_lock)
            {
                if (!EqualityComparer<T>.Default.Equals(field, value))
                {
                    field = value;
                    changed = true;
                }
            }

            if (changed && _isLoaded)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SaveAsync();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Unobserved exception in GameState.SaveAsync: {ex}");
                    }
                });
            }
        }

        public async Task LoadAsync()
        {
            try
            {
                _isLoaded = false;
                ConfPathes dataPath = IllSkillzBotMain.GetDataPath();
                var fileName = IllSingleton.Config.FilePaths.GameStateFileName;
                var filePath = Path.Combine(dataPath.uniquePath, fileName);
                Console.WriteLine($"Loading GameState from: {filePath}");

                if (!File.Exists(filePath))
                {
                    Console.WriteLine("GameState file not found, creating default.");
                    await CreateDefaultGameStateFileAsync(filePath);
                    _isLoaded = true;
                    return;
                }

                var json = await File.ReadAllTextAsync(filePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    Console.WriteLine("GameState file was empty, creating default.");
                    await CreateDefaultGameStateFileAsync(filePath);
                    _isLoaded = true;
                    return;
                }

                var data = JsonSerializer.Deserialize<BotGameStateModel>(json);
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
                    Console.WriteLine("GameState loaded successfully!");
                }
                else
                {
                    await CreateDefaultGameStateFileAsync(filePath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load game state: {ex.Message}");
            }
            finally
            {
                _isLoaded = true;
            }
        }

        private async Task CreateDefaultGameStateFileAsync(string filePath)
        {
            lock (_lock)
            {
                _summonerName = IllSingleton.Config.SummonerName ?? "";
                _summonerRegion = "euw";
                _startLP = 0;
                _elo = "";
                _earnedLP = 0;
                _numLosses = 0;
                _numWins = 0;
                _tier = "";
                _numGames = 0;
            }
            await SaveInternalAsync(filePath);
        }

        public async Task SaveAsync()
        {
            ConfPathes dataPath = IllSkillzBotMain.GetDataPath();
            var fileName = IllSingleton.Config.FilePaths.GameStateFileName;
            var filePath = Path.Combine(dataPath.uniquePath, fileName);
            await SaveInternalAsync(filePath);
        }

        private async Task SaveInternalAsync(string filePath)
        {
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

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(data, options);

                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save game state: {ex.Message}");
            }
        }
    }
}