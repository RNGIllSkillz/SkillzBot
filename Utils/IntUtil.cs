using SkillzBot.Singleton;
using SkillzBot.API.Twitch;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SkillzBot.Utils
{
    internal sealed class IntUtil
    {
        private static readonly Random random;
        static IntUtil()
        {
            random ??= new Random();
        }
        public static int Random(int min, int max)
        {
            return random.Next(min, max);
        }
        public static bool GetChance(int winChanse)
        {
            int rnumber = Random(1, 100);
            if (rnumber >= winChanse)
                return false;
            else
                return true;
        }
        public static string RulProbability(double winstreak, double chanse)
        {
            double t = chanse / 100;
            double proBab = t;
            for (int i = 2; i <= winstreak; i++)
            {
                proBab = t * proBab;
            }
            proBab *= 100;
            int precision = ((decimal)proBab == decimal.Truncate((decimal)proBab)) ? 0 : 4;
            return proBab.ToString($"F{precision}").TrimEnd('0').TrimEnd('.') + "%";
        }
        public static async Task<int> CalculateCancelUvalCost(string subscriptionType, double remainingDuration)
        {
            const int SecondsInTenMinutes = 600;
            var rewardsMap = new Dictionary<string, string>
            {
                ["IsSub"] = IllSingleton.Config.ChannelIds.UvalSabId,
                ["IsVip"] = IllSingleton.Config.ChannelIds.UvalVipId,
                ["IsUnsub"] = IllSingleton.Config.ChannelIds.UvalId,
                ["IsMod"] = IllSingleton.Config.ChannelIds.UvalMod
            };

            if (rewardsMap.TryGetValue(subscriptionType, out string rewardId))
            {
                var reward = await TtvAPI.GetReward(rewardId).ConfigureAwait(false);
                if (reward != null)
                    return CalculateCost(reward.Cost, remainingDuration, SecondsInTenMinutes);
            }
            return 10000000;
        }
        private static int CalculateCost(double rewardCost, double remainingDuration, int secondsInTenMinutes)
        {
            var costPerSecond = rewardCost / secondsInTenMinutes;
            return (int)Math.Ceiling(costPerSecond * remainingDuration);
        }
        public static string CalculateTopPercentage(int[] data)
        {
            var rank = data[0];
            var totalPeople = data[1];
            if (totalPeople == 0) return null;
            var topPercent = (decimal)rank / totalPeople * 100;
            if (topPercent > 80) return null;
            int precision = (topPercent == decimal.Truncate(topPercent)) ? 0 : 4;
            return "(top " + topPercent.ToString($"F{precision}").TrimEnd('0').TrimEnd('.') + "%)";
        }

        public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dateTime = dateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dateTime;
        }
    }
}
