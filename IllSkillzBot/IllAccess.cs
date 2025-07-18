using SkillzBot.MODELS;
using SkillzBot.Singleton;

namespace SkillzBot.IllSkillzBot
{
    internal static class IllAccess
    {
        private static readonly IllSingleton singleton = IllSingleton.GetInstance();
        public static bool Root(UserObject user) => user.Name == singleton.rootUser;
        public static bool High(UserObject user) => user.IsBroadcaster == 1 || Root(user);
        public static bool Mid(UserObject user) => user.isMod == 1 || High(user);
        public static bool Low(UserObject user) => user.isVip == 1 || Mid(user);
    }
}
