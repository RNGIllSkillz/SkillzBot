using Quartz;
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
            return string.Format("{0:N3}%", proBab);
        }
        public static async Task<int> CalculateCancelUvalCost(string subscriptionType, double remainingDuration)
        {
            const int SecondsInTenMinutes = 600;
            var rewardsMap = new Dictionary<string, string>
            {
                ["IsSub"] = Singleton.IllSingleton.GetInstance().UvalSabId,
                ["IsVip"] = Singleton.IllSingleton.GetInstance().UvalVipId,
                ["IsUnsub"] = Singleton.IllSingleton.GetInstance().UvalId,
                ["IsMod"] = Singleton.IllSingleton.GetInstance().uvalMod
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
    }
}
