using SkillzBot.MODELS;
using SkillzBot.Singleton;
using static SkillzBot.IllSkillzBot.IllEnums;

namespace SkillzBot.IllSkillzBot
{
    internal static class IllAccess
    {
        private static readonly IllSingleton singleton = IllSingleton.GetInstance();
        public static bool Root(UserObject user) => user.Name == singleton.rootUser;
        public static bool Broadcaster(UserObject user) => user.IsBroadcaster == 1 || Root(user);
        public static bool Mod(UserObject user) => user.isMod == 1 || Broadcaster(user);
        public static bool Vip(UserObject user) => user.isVip == 1 || Mod(user);

        public static bool MeetsLevel(UserObject user, AccessLevel level) => level switch
        {
            AccessLevel.Any => true,
            AccessLevel.Vip => Vip(user),
            AccessLevel.Mod => Mod(user),
            AccessLevel.Broadcaster => Broadcaster(user),
            AccessLevel.Root => Root(user),
            _ => false
        };
    }
}
