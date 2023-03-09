using SkillzBot.JSON.Settings;
using SkillzBot.WRITERS;
using Newtonsoft.Json;
using System;
using System.IO;

namespace SkillzBot.Readers
{
    public class Config
    {
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
                Log.WriteLog(e, "Error reading bot configuration file");
                throw;
            }
        }
    }
}

