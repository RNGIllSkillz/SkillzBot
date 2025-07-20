using SkillzBot.IllSkillzBot;
using SkillzBot.MODELS;
using SkillzBot.WRITERS;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SkillzBot.Utils
{
#nullable enable
    public class CooldownManager
    {
        private record CooldownKey(string CommandName, int? TwitchID);
        private readonly Dictionary<CooldownKey, DateTime> _cooldowns = new();
        private readonly Dictionary<string, TimeSpan> _cooldownDurations = new();
        private readonly Dictionary<string, bool> _ignoreAccessLevel = new();
        private readonly Dictionary<string, bool> _isGlobal = new();
        static LogWriter _logWriter = new LogWriter();
        

        public void RegisterCooldown(string methodName, TimeSpan cooldown, bool IgnoreAccessLevel = false, bool IsGlobal = false)
        {
            _cooldownDurations[methodName] = cooldown;
            _ignoreAccessLevel[methodName] = IgnoreAccessLevel;
            _isGlobal[methodName] = IsGlobal;
            if (IsGlobal)
                _cooldowns[new CooldownKey(methodName, null)] = DateTime.MinValue;
        }

        public async Task<TimeSpan?> TryInvokeAsync(UserObject user, string methodName, Delegate methodLogic, string[]? command = null)
        {
            if (!_cooldownDurations.ContainsKey(methodName))
                throw new InvalidOperationException($"Method {methodName} is not registered.");

            bool forceCooldownForAll = _ignoreAccessLevel.TryGetValue(methodName, out var force) && force;
            bool isGlobal = _isGlobal.TryGetValue(methodName, out var global) && global;
            if (!forceCooldownForAll && IllAccess.Vip(user))
            {
                await InvokeDelegate(methodLogic, user, command).ConfigureAwait(false);
                return null;
            }
            var key = new CooldownKey(methodName, isGlobal ? null : user.TwitchID);
            var now = DateTime.UtcNow;

            if (_cooldowns.TryGetValue(key, out var lastUsed))
            {
                var cooldownDuration = _cooldownDurations.GetValueOrDefault(methodName);
                var elapsed = now - lastUsed;
                if (elapsed < cooldownDuration)
                {
                    return cooldownDuration - elapsed;
                }
            }
            _cooldowns[key] = now;

            await InvokeDelegate(methodLogic, user, command).ConfigureAwait(false);
            return null;
        }
        public static Task InvokeDelegate(Delegate del, UserObject? user, string[]? command)
        {
            return del switch
            {
                Func<UserObject, Task> func1 => func1(user),
                Func<UserObject, string[], Task> func2 when command != null => func2(user, command),
                Func<Task> func3 => func3(),
                _ => throw new ArgumentException("Unsupported delegate signature or missing command.")
            };
        }
    }
}
