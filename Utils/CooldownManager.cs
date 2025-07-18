using SkillzBot.IllSkillzBot;
using SkillzBot.MODELS;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SkillzBot.Utils
{
    public class CooldownManager
    {
        private readonly Dictionary<string, DateTime> _cooldowns = new();
        private readonly Dictionary<string, TimeSpan> _cooldownDurations = new();

        public void RegisterCooldown(string methodName, TimeSpan cooldown)
        {
            _cooldownDurations[methodName] = cooldown;
            _cooldowns[methodName] = DateTime.MinValue;
        }

        public async Task<TimeSpan?> TryInvokeAsync(UserObject user, string methodName, Func<UserObject, Task> methodLogic)
        {
            if (!_cooldownDurations.ContainsKey(methodName))
                throw new InvalidOperationException($"Method {methodName} is not registered.");

            if (IllAccess.Low(user))
            {
                await methodLogic(user);
                return null;
            }

            string key = $"{user.TwitchID}:{methodName}";
            var now = DateTime.UtcNow;
            var cooldown = _cooldownDurations[methodName];
            _cooldowns.TryGetValue(key, out var lastUsed);

            if (now - lastUsed < cooldown)
            {
                var remaining = cooldown - (now - lastUsed);
                return remaining;
            }
            _cooldowns[key] = now;
            await methodLogic(user).ConfigureAwait(false);            
            return null;
        }

        public async Task<TimeSpan?> TryInvokeAsync(UserObject user, string[] command, string methodName, Func<UserObject, string[], Task> methodLogic)
        {
            if (!_cooldownDurations.ContainsKey(methodName))
                throw new InvalidOperationException($"Method {methodName} is not registered.");

            if (IllAccess.Low(user))
            {
                await methodLogic(user, command);
                return null;
            }

            string key = $"{user.TwitchID}:{methodName}";
            var now = DateTime.UtcNow;
            var cooldown = _cooldownDurations[methodName];
            _cooldowns.TryGetValue(key, out var lastUsed);

            if (now - lastUsed < cooldown)
            {
                var remaining = cooldown - (now - lastUsed);
                return remaining;
            }
            _cooldowns[key] = now;
            await methodLogic(user, command).ConfigureAwait(false);
            return null;
        }
    }
}
