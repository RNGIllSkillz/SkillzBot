using IllSkillzBot;
using System;
using System.IO;

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
                File.WriteAllText(_FilePath, newTimestamp.ToString());
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
                double dailyRate = (double)rate / daysInMonth;
                int daysPayed = (int)(amount / dailyRate);
                newTimestamp = originalTimestamp.AddDays(daysPayed);
            }
            else
                newTimestamp = originalTimestamp.AddMonths(1);

            try
            {
                File.WriteAllText(_FilePath, newTimestamp.ToString());
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
            return currentDateTime;
        }
        private static bool TryReadDateTimeFromFile(string filePath, out DateTime result)
        {
            result = default;
            try
            {
                if (File.Exists(filePath))
                {
                    string dateTimeString = File.ReadAllText(filePath);
                    return DateTime.TryParse(dateTimeString, out result);
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                return false;
            }
        }
    }
}