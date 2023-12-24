using IllSkillzBot;
using SkillzBot.Singleton;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TwitchLib.Api.Helix;

namespace SkillzBot.SubUtils
{
    internal class SubCheck
    {
        private static readonly string _FilePath;
        private static readonly IllSingleton singleton = IllSingleton.GetInstance();
        static SubCheck()
        {
            string dataPath = IllSkillzBotMain.GetDataPath();
            _FilePath = Path.Combine(dataPath, "Subscription.txt");
        }
        public static bool RunChecker()
        {
            if (TryReadDateTimeFromFile(_FilePath, out DateTime savedDateTime))
            {
                DateTime currentDateTime = DateTime.Now;
                singleton.isActiveSub = currentDateTime < savedDateTime;
                return singleton.isActiveSub;
            }
            else
            {
                singleton.isActiveSub = true;
                Console.WriteLine("Failed to read DateTime from file.");
                return singleton.isActiveSub;
            }
        }
        private static bool TryReadDateTimeFromFile(string filePath, out DateTime result)
        {
            result = default;
            try
            {
                if (File.Exists(filePath))
                {
                    string dateTimeString = File.ReadAllText(filePath);
                    if (DateTime.TryParse(dateTimeString, out result))
                    {
                        return true; // Successfully read DateTime from file
                    }
                    else
                    {
                        Console.WriteLine("Failed to parse DateTime from file content.");
                        return false;
                    }
                }
                else
                {
                    Console.WriteLine("File does not exist.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                return false;
            }
        }
    }
}
