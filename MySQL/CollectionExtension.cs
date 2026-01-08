using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SkillzBot.MYSQL;
using SkillzBot.Interfaces;
using SkillzBot.Singleton;

namespace SkillzBot.MySQL
{
    public static class DatabaseServiceCollectionExtensions
    {
        public static IServiceCollection AddDatabaseServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<DatabaseConfiguration>(options =>
            {
                options.Host = IllSingleton.Config.Database.Host;
                options.Port = IllSingleton.Config.Database.Port;
                options.Username = IllSingleton.Config.Database.Username;
                options.Password = IllSingleton.Config.Database.Password;
                options.DatabaseName = IllSingleton.Config.ChannelName;
                options.ConnectionTimeout = 30;
                options.CommandTimeout = 30;
                options.MaxPoolSize = 100;
                options.MinPoolSize = 0;
                options.Pooling = true;
                options.CharacterSet = "utf8mb4";
            });

            services.AddSingleton<IDatabaseService, MySqlDatabaseService>();

            return services;
        }
    }
}