using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkillzBot.Interfaces;
using System;

namespace SkillzBot.Hosts
{
    public static class IllServiceProvider
    {
        private static IServiceProvider _serviceProvider;

        public static void Initialize(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public static IDatabaseService Database =>
            _serviceProvider?.GetRequiredService<IDatabaseService>() ??
            throw new InvalidOperationException("ServiceProvider not initialized");

        public static ILogger<T> GetLogger<T>() =>
            _serviceProvider?.GetRequiredService<ILogger<T>>() ??
            throw new InvalidOperationException("ServiceProvider not initialized");

        public static T GetService<T>() => _serviceProvider.GetRequiredService<T>();
    }
}
