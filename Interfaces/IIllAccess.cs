using SkillzBot.MODELS;
using static SkillzBot.IllSkillzBot.IllEnums;

namespace SkillzBot.Interfaces
{
    public interface IIllAccess
    {
        bool Root(UserObject user);
        bool Broadcaster(UserObject user);
        bool Mod(UserObject user);
        bool Vip(UserObject user);
        bool MeetsLevel(UserObject user, AccessLevel level);
    }
}