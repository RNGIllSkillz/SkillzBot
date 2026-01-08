using SkillzBot.IllSkillzBot;
using SkillzBot.MODELS;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SkillzBot.Singleton;

namespace SkillzBot.Utils
{
#nullable enable
    public class CooldownManager
    {
        private record CooldownKey(string CommandName, int? TwitchID);

        private readonly Dictionary<CooldownKey, DateTime> _cooldowns = new();
        private readonly Dictionary<string, TimeSpan> _cooldownDurations = new();
        private readonly Dictionary<string, bool> _allowBypassCooldown = new();
        private readonly Dictionary<string, bool> _isGlobal = new();

        public void RegisterCooldown(string methodName, TimeSpan cooldown, bool allowBypassCooldown = true, bool isGlobal = false)
        {
            _cooldownDurations[methodName] = cooldown;
            _allowBypassCooldown[methodName] = allowBypassCooldown;
            _isGlobal[methodName] = isGlobal;

            if (isGlobal)
                _cooldowns[new CooldownKey(methodName, null)] = DateTime.MinValue;
        }

        public async Task<TimeSpan?> TryInvokeAsync(UserObject user, string methodName, Delegate methodLogic, string[]? command = null)
        {
            if (!_cooldownDurations.ContainsKey(methodName))
                throw new InvalidOperationException($"Method {methodName} is not registered.");

            bool canBypass = _allowBypassCooldown.TryGetValue(methodName, out var bypass) && bypass;
            bool isGlobal = _isGlobal.TryGetValue(methodName, out var global) && global;

            
            if (IllSingleton.State.GodMode)
                if (IllAccess.Root(user))
                {
                    await InvokeDelegate(methodLogic, user, command).ConfigureAwait(false);
                    return null;
                }

            // If elevated users can bypass cooldowns and user is VIP, execute immediately
            if (canBypass && IllAccess.Vip(user))
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
            if (user == null)
                throw new ArgumentNullException(nameof(user));
            return del switch
            {
                Func<UserObject, Task> func1 => func1(user),
                Func<UserObject, string[], Task> func2 when command != null => func2(user, command),
                Func<Task> func3 => func3(),
                _ => throw new ArgumentException("Unsupported delegate signature or missing command.")
            };
        }
        public void PruneExpiredCooldowns()
        {
            var now = DateTime.UtcNow;
            var keysToRemove = new List<CooldownKey>();

            foreach (var kvp in _cooldowns)
            {
                if (_cooldownDurations.TryGetValue(kvp.Key.CommandName, out var duration))
                {
                    if ((now - kvp.Value) > duration)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }
            }

            foreach (var key in keysToRemove)
            {
                _cooldowns.Remove(key);
            }
        }
    }
}