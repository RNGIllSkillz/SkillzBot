using SkillzBot.JSON.Settings;
using SkillzBot.WRITERS;
using Newtonsoft.Json;
using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SkillzBot.Hosts;
using SkillzBot.QuartZ;

namespace SkillzBot.Readers
{
    public class Config
    {
        private static readonly ILogger<Config> _logger = IllServiceProvider.GetLogger<Config>();
        readonly private string _ConfigPath;

        public Config(string ConfigPath)
        {
            _ConfigPath = ConfigPath;
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
            catch (Exception e)
            {
                _logger.LogCritical(e, "Error reading bot configuration file");
                throw;
            }
        }
    }
}

