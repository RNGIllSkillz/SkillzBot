using System;

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
    }
}