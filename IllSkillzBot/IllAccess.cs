using SkillzBot.Interfaces;
using SkillzBot.MODELS;
using SkillzBot.IllConfiguration; 
using static SkillzBot.IllSkillzBot.IllEnums;

namespace SkillzBot.IllSkillzBot
{
    internal class IllAccess : IIllAccess
    {
        private readonly BotConfigModel _config;

        public IllAccess(BotConfigModel config)
        {
            _config = config;
        }

        public bool Root(UserObject user) => user.Name == _config.RootUser;

        public bool Broadcaster(UserObject user) => user.IsBroadcaster == 1 || Root(user);

        public bool Mod(UserObject user) => user.isMod == 1 || Broadcaster(user);

        public bool Vip(UserObject user) => user.isVip == 1 || Mod(user);

        public bool MeetsLevel(UserObject user, AccessLevel level) => level switch
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