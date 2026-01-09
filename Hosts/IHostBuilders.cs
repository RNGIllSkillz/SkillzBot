using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using SkillzBot.API.Twitch;
using SkillzBot.EventSub;
using SkillzBot.IllSkillzBot;
using SkillzBot.IllSkillzBot.IllCommandsNest;
using SkillzBot.Interfaces;
using SkillzBot.IRC;
using SkillzBot.MySQL;
using SkillzBot.QuartZ;
using SkillzBot.TtvClient.TTVRewards;
using TwitchLib.EventSub.Websockets.Extensions;

namespace SkillzBot.Hosts
{
    internal class IHostBuilders
    {
        private readonly LoggingLevelSwitch _levelSwitch;
        public IHostBuilders(LoggingLevelSwitch levelSwitch, IConfiguration configuration = null)
        {
            _levelSwitch = levelSwitch;
        }

        public IHost BuildMainApplicationHost(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .UseSerilog() 
                .ConfigureServices((context, services) =>
                {
                    services.AddDatabaseServices(context.Configuration);
                    services.AddTwitchLibEventSubWebsockets();

                    // Core Services
                    services.AddSingleton<ITtvIRCClient, TtvIRCClientService>();
                    services.AddSingleton<ITwitchService, TwitchApiService>();
                    services.AddSingleton<API.RiotGames.IRiotApiService, API.RiotGames.RiotApiService>();

                    // Logic Services
                    services.AddSingleton<IllChatFilters>();
                    services.AddSingleton<IllGames>();
                    services.AddSingleton<IllModeratorsInteractions>();
                    services.AddSingleton<RewardsRedemption>();

                    // Internal Logic Services
                    services.AddSingleton<IllCommands>();
                    services.AddSingleton<IllCommandHandler>();
                    services.AddSingleton<IllChatMessageHandler>();

                    // Background & Quartz
                    services.AddSingleton<BackGroundTasks>();
                    services.AddSingleton<QuartzBackgroundTaskManager>();
                    services.AddTransient<BGTasks>();

                    // Hosted Services
                    services.AddHostedService<TTVEventSub>();
                    services.AddHostedService<TwitchIrcHostedService>();

                    services.AddSingleton<IllPredictions>();

                    services.AddSingleton(_levelSwitch);
                })
                .Build();
        }
    }
}