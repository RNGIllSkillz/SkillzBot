using SkillzBot.MODELS;
using static SkillzBot.IllSkillzBot.IllEnums;
using SkillzBot.Singleton;

namespace SkillzBot.IllSkillzBot
{
    internal static class IllAccess
    {
        public static bool Root(UserObject user) => user.Name == IllSingleton.Config.RootUser;
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