using IllSkillzBot;
using SkillzBot.MODELS;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Singleton
{
    public class BotState
    {
        private readonly object _lock = new object();
        private bool _godMode;
        private bool _wisEnabled = true;
        private bool _inMatch;
        private bool _debug = true;
        private bool _autoPred = true;
        private bool _quizIsRunning;
        private bool _broadcasterIsOnline;
        private bool _firstQuizOfTheDay = true;
        private bool _isSilent;
        private bool _isSubActive = true;
        private int _chatFilterLvl;
        private int _antiBotProtectionLvl;
        private readonly SemaphoreSlim _fileSemaphore = new SemaphoreSlim(1, 1);
        

        public bool GodMode { get => Get(_godMode); set => Set(ref _godMode, value); }
        public bool WisEnabled { get => Get(_wisEnabled); set => Set(ref _wisEnabled, value); }
        public bool InMatch { get => Get(_inMatch); set => Set(ref _inMatch, value); }
        public bool Debug { get => Get(_debug); set => Set(ref _debug, value); }
        public bool AutoPred { get => Get(_autoPred); set => Set(ref _autoPred, value); }
        public bool QuizIsRunning { get => Get(_quizIsRunning); set => Set(ref _quizIsRunning, value); }
        public bool BroadcasterIsOnline { get => Get(_broadcasterIsOnline); set => Set(ref _broadcasterIsOnline, value); }
        public bool FirstQuizOfTheDay { get => Get(_firstQuizOfTheDay); set => Set(ref _firstQuizOfTheDay, value); }
        public bool IsSilent { get => Get(_isSilent); set => Set(ref _isSilent, value); }
        public bool isSubActive { get => Get(_isSubActive); set => Set(ref _isSubActive, value); }
        public int ChatFilterLvl { get => Get(_chatFilterLvl); set => Set(ref _chatFilterLvl, value); }
        public int AntiBotProtectionLvl { get => Get(_antiBotProtectionLvl); set => Set(ref _antiBotProtectionLvl, value); }

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
            // Fire and forget, but safely queued
            if (changed)
            {
                _ = Task.Run(() => SaveAsync());
            }
        }
        public async Task LoadAsync()
        {
            ConfPathes dataPath = IllSkillzBotMain.GetDataPath();
            var BotStateFilePath = Path.Combine(dataPath.uniquePath, "BotState.txt");
            await _fileSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (File.Exists(BotStateFilePath))
                {
                    var json = await File.ReadAllTextAsync(BotStateFilePath);

                    if (string.IsNullOrWhiteSpace(json))
                    {
                        await CreateDefaultBotStateFileAsync(BotStateFilePath);
                        return;
                    }

                    var bState = JsonSerializer.Deserialize<BotState>(json);
                    if (bState != null)
                    {
                        lock (_lock)
                        {
                            _godMode = bState.GodMode;
                            _wisEnabled = bState.WisEnabled;
                            _inMatch = bState.InMatch;
                            _debug = bState.Debug;
                            _autoPred = bState.AutoPred;
                            _quizIsRunning = bState.QuizIsRunning;
                            _broadcasterIsOnline = bState.BroadcasterIsOnline;
                            _firstQuizOfTheDay = bState.FirstQuizOfTheDay;
                            _antiBotProtectionLvl = bState.AntiBotProtectionLvl;
                            _chatFilterLvl = bState.ChatFilterLvl;
                            _isSubActive = bState.isSubActive;
                            _isSilent = bState.IsSilent;
                        }
                    }
                    else
                    {
                        await CreateDefaultBotStateFileAsync(BotStateFilePath);
                    }
                }
                else
                {
                    await CreateDefaultBotStateFileAsync(BotStateFilePath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load bot state: {ex.Message}");
                try
                {
                    await CreateDefaultBotStateFileAsync(BotStateFilePath);
                }
                catch (Exception createEx)
                {
                    Console.WriteLine($"Failed to create default bot state file: {createEx.Message}");
                }
            }
            finally
            {
                _fileSemaphore.Release();
            }
        }
        private async Task CreateDefaultBotStateFileAsync(string filePath)
        {
            var defaultBotState = new BotState
            {
                GodMode = false,
                WisEnabled = false,
                InMatch = false,
                Debug = false,
                AutoPred = true,
                QuizIsRunning = false,
                BroadcasterIsOnline = false,
                FirstQuizOfTheDay = false,
                AntiBotProtectionLvl = 1,
                ChatFilterLvl = 3,
                isSubActive = true,
                IsSilent = false
            };

            var json = JsonSerializer.Serialize(defaultBotState, new JsonSerializerOptions
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
                _godMode = defaultBotState.GodMode;
                _wisEnabled = defaultBotState.WisEnabled;
                _inMatch = defaultBotState.InMatch;
                _debug = defaultBotState.Debug;
                _autoPred = defaultBotState.AutoPred;
                _quizIsRunning = defaultBotState.QuizIsRunning;
                _broadcasterIsOnline = defaultBotState.BroadcasterIsOnline;
                _firstQuizOfTheDay = defaultBotState.FirstQuizOfTheDay;
                _antiBotProtectionLvl = defaultBotState.AntiBotProtectionLvl;
                _chatFilterLvl = defaultBotState.ChatFilterLvl;
                _isSubActive = defaultBotState.isSubActive;
                _isSilent = defaultBotState.IsSilent;
            }
        }
        public async Task SaveAsync()
        {
            try
            {
                BotStateModel State;
                lock (_lock)
                {
                    // Create snapshot of state inside lock
                    State = new BotStateModel
                    {
                        GodMode = _godMode,
                        WisEnabled = _wisEnabled,
                        InMatch = _inMatch,
                        Debug = _debug,
                        AutoPred = _autoPred,
                        QuizIsRunning = _quizIsRunning,
                        BroadcasterIsOnline = _broadcasterIsOnline,
                        FirstQuizOfTheDay = _firstQuizOfTheDay,
                        AntiBotProtectionLvl = _antiBotProtectionLvl,
                        ChatFilterLvl = _chatFilterLvl,
                        IsSubActive = _isSubActive,
                        IsSilent = _isSilent
                    };
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                ConfPathes dataPath = IllSkillzBotMain.GetDataPath();
                var BotStateFilePath = Path.Combine(dataPath.uniquePath, "BotState.txt");
                var json = JsonSerializer.Serialize(State, options);

                await _fileSemaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    // Ensure directory exists
                    var dir = Path.GetDirectoryName(BotStateFilePath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    await File.WriteAllTextAsync(BotStateFilePath, json).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to save bot state: {ex.Message}");
                }
                finally
                {
                    _fileSemaphore.Release();
                }            
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save bot state: {ex.Message}");
            }
        }
    }
}
