using SkillzBot.JSON.Settings;
using Newtonsoft.Json;
using System;
using System.IO;

namespace SkillzBot.Readers
{
    public class Config
    {
        private readonly string _ConfigPath;

        public Config(string configPath)
        {
            _ConfigPath = configPath;
        }

        public SettingsJson GetBotConfigs()
        {
            try
            {
                using (StreamReader reader = new StreamReader(_ConfigPath))
                {
                    string json = reader.ReadToEnd();
                    return JsonConvert.DeserializeObject<SettingsJson>(json);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error reading config file at {_ConfigPath}", ex);
            }
        }
    }
}