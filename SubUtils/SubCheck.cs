using IllSkillzBot;
using System;
using System.IO;
using SkillzBot.Singleton;

namespace SkillzBot.SubUtils
{
    internal class SubCheck
    {
        private static readonly string _FilePath;
        static SubCheck()
        {
            string dataPath = IllSkillzBotMain.GetDataPath().uniquePath;
            _FilePath = Path.Combine(dataPath, "Subscription.txt");
        }
        public static bool RunChecker()
        {
            if (TryReadDateTimeFromFile(_FilePath, out DateTime savedDateTime))
            {
                DateTime currentDateTime = DateTime.Now;
                IllSingleton.State.isSubActive = currentDateTime < savedDateTime;
                return IllSingleton.State.isSubActive;
            }
            else
            {
                IllSingleton.State.isSubActive = true;
                Console.WriteLine("Failed to read DateTime from file.");
                return IllSingleton.State.isSubActive;
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
