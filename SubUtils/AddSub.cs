using IllSkillzBot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace SkillzBot.SubUtils
{
    internal class AddSub
    {
        private static readonly string _FilePath;
        static AddSub()
        {
            string dataPath = IllSkillzBotMain.GetDataPath().uniquePath;
            _FilePath = Path.Combine(dataPath, "Subscription.txt");
        }
        public static DateTime NewPurchase()
        {
             DateTime originalTimestamp = DateTime.Now;
             DateTime newTimestamp = originalTimestamp.AddMonths(1);
            try
            {
                using var fileStream = File.Open(_FilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(fileStream);
                writer.Write(newTimestamp);
                return newTimestamp;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return originalTimestamp;
            }  
        }
        public static DateTime NewPurchase(int amount, int rate)
        {            
            DateTime originalTimestamp = calcRemaining();
            DateTime newTimestamp;
            int daysInMonth = 31;
            if (amount != rate)
            {
                double dailyRate = rate / daysInMonth;
                int daysPayed = (int)(amount / dailyRate);
                newTimestamp = originalTimestamp.AddDays(daysPayed);
            }
            else
                newTimestamp = originalTimestamp.AddMonths(1);

            try
            {
                using var fileStream = File.Open(_FilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(fileStream);
                writer.Write(newTimestamp);
                return newTimestamp;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return originalTimestamp;
            }
        }

        private static DateTime calcRemaining()
        {
            DateTime currentDateTime = DateTime.Now;
            if (TryReadDateTimeFromFile(_FilePath, out DateTime savedDateTime))
            {
                if (currentDateTime < savedDateTime)                
                    return savedDateTime;
                return currentDateTime;
            }
            else
            {
                Console.WriteLine("Failed to read DateTime from file.");
                return currentDateTime;
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
