using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SkillzBot.MYSQL;
using SkillzBot.Interfaces;
using SkillzBot.IllConfiguration;  

namespace SkillzBot.MySQL
{
    public static class DatabaseServiceCollectionExtensions
    {
        public static IServiceCollection AddDatabaseServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<DatabaseConfiguration>()
                .Configure<BotConfigModel>((options, botConfig) =>
                {
                    options.Host = botConfig.Database.Host;
                    options.Port = botConfig.Database.Port;
                    options.Username = botConfig.Database.Username;
                    options.Password = botConfig.Database.Password;
                    options.DatabaseName = botConfig.ChannelName;
                    
                    options.ConnectionTimeout = 5;   // fail fast when the server is unreachable
                    options.CommandTimeout = 15;
                    options.MaxPoolSize = 100;
                    options.MinPoolSize = 0;
                    options.Pooling = true;
                    options.CharacterSet = "utf8mb4";
                });

            services.AddSingleton<IDatabaseService, MySqlDatabaseService>();
            services.AddSingleton<IVipRepository>(sp => (MySqlDatabaseService)sp.GetRequiredService<IDatabaseService>());
            services.AddSingleton<IEngagementRepository>(sp => (MySqlDatabaseService)sp.GetRequiredService<IDatabaseService>());
            services.AddSingleton<IAdminRepository>(sp => (MySqlDatabaseService)sp.GetRequiredService<IDatabaseService>());

            return services;
        }
    }
}