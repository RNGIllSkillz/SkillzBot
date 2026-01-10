using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using SkillzBot.API.MMR;
using SkillzBot.API.RiotGames;
using SkillzBot.API.StreamElements;
using SkillzBot.API.Twitch;
using SkillzBot.API.YouTube;
using SkillzBot.Configuration;
using SkillzBot.Discord;
using SkillzBot.EventSub;
using SkillzBot.IllConfiguration;
using SkillzBot.IllSkillzBot;
using SkillzBot.IllSkillzBot.IllCommandsNest;
using SkillzBot.Interfaces;
using SkillzBot.IRC;
using SkillzBot.MySQL;
using SkillzBot.QuartZ;
using SkillzBot.Services.Infrastructure;
using SkillzBot.Services.State;
using SkillzBot.Services.Writers;
using SkillzBot.SubUtils;
using SkillzBot.TtvClient.TTVRewards;
using SkillzBot.Utils;
using System;
using System.Net.Http;
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
                .UseSerilog((context, services, configuration) =>
                {
                    // Basic Serilog setup
                    configuration
                        .MinimumLevel.ControlledBy(_levelSwitch)
                        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                        .MinimumLevel.Override("System", LogEventLevel.Warning)
                        .MinimumLevel.Override("Quartz", LogEventLevel.Warning)
                        .Enrich.FromLogContext()
                        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");

                    // Note: File logging requires path, which is inside IPathProvider.
                    // If you need file logging here, you can resolve IPathProvider
                    try
                    {
                        var paths = services.GetService<IPathProvider>();
                        if (paths != null)
                        {
                            string logFile = System.IO.Path.Combine(paths.DataPath, "logs", $"{System.DateTime.Now:yyyy-MM-dd}.log");
                            configuration.WriteTo.File(logFile, rollingInterval: RollingInterval.Infinite);
                        }
                    }
                    catch { /* Fallback if paths not ready */ }
                })
                .ConfigureServices((context, services) =>
                {
                    // 1. Core Infrastructure
                    services.AddSingleton<IPathProvider, PathProvider>();
                    services.AddSingleton(_levelSwitch);

                    // 2. Configuration
                    services.AddSingleton<BotConfigModel>(sp =>
                    {
                        var pathProvider = sp.GetRequiredService<IPathProvider>();
                        // Ensure config exists before reading
                        if (!System.IO.File.Exists(pathProvider.ConfigPath))
                        {
                            throw new System.IO.FileNotFoundException($"Config not found at {pathProvider.ConfigPath}");
                        }

                        return BotConfigurationFactory.Create(pathProvider.ConfigPath);
                    });

                    // 3. State Management
                    services.AddSingleton<IBotStateService, BotStateService>();
                    services.AddSingleton<IGameStateService, GameStateService>();

                    // 4. Database
                    services.AddDatabaseServices(context.Configuration);

                    // 5. External APIs
                    services.AddTwitchLibEventSubWebsockets();
                    services.AddSingleton<ITtvIRCClient, TtvIRCClientService>();
                    services.AddSingleton<ITwitchService, TwitchApiService>();
                    services.AddSingleton<IRiotApiService, RiotApiService>();

                    // HTTP Clients
                    services.AddHttpClient<IStreamElementsService, StreamElementsService>()
                        .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                        {
                            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                        });

                    services.AddHttpClient<RiotHttpHandler>()
                        .SetHandlerLifetime(TimeSpan.FromMinutes(5)) 
                        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                        {
                            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                        });

                    services.AddHttpClient<IMmrService, MmrApiService>()
                        .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                        {
                            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                        });

                    // 6. Bot Logic / Features
                    services.AddSingleton<IllChatFilters>();
                    services.AddSingleton<IllGames>();
                    services.AddSingleton<IllModeratorsInteractions>();
                    services.AddSingleton<RewardsRedemption>();
                    services.AddSingleton<IllCommands>();
                    services.AddSingleton<IllCommandHandler>();
                    services.AddSingleton<IllChatMessageHandler>();
                    services.AddSingleton<IllPredictions>();

                    // 7. Background Tasks
                    services.AddSingleton<BackGroundTasks>();
                    services.AddSingleton<QuartzBackgroundTaskManager>();
                    services.AddTransient<BGTasks>();

                    services.AddSingleton<IIllAccess, IllAccess>();
                    services.AddSingleton<CooldownManager>();
                    services.AddSingleton<DiscordClient>();
                    services.AddSingleton<SubCheck>();

                    services.AddSingleton<ConfigWriterService>();
                    services.AddSingleton<MediaQueueService>();
                    services.AddSingleton<BlacklistService>();
                    services.AddSingleton<SubscriptionService>();
                    services.AddSingleton<FlagWriterService>();
                    services.AddSingleton<ExtractMessageService>();
                    services.AddSingleton<IYouTubeService, YouTubeApiService>();                    

                    // 8. Hosted Services (Running in background)
                    services.AddHostedService<StartupInitializer>(); // Runs Once
                    services.AddHostedService<TTVEventSub>();        // Runs Forever
                    services.AddHostedService<TwitchIrcHostedService>(); // Runs Forever
                })
                .Build();
        }
    }
}