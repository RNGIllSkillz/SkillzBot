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
using SkillzBot.Services;
using SkillzBot.Services.Infrastructure;
using SkillzBot.Services.State;
using SkillzBot.Services.Writers;
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

        // Every outbound HTTP call is bounded so a slow upstream cannot stall the chat loop.
        private static readonly TimeSpan StreamElementsTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan RiotTimeout = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan MmrTimeout = TimeSpan.FromSeconds(8);

        public IHostBuilders(LoggingLevelSwitch levelSwitch, IConfiguration configuration = null)
        {
            _levelSwitch = levelSwitch;
        }

        private static SocketsHttpHandler CreatePrimaryHandler() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            // Drop idle sockets before the remote side does, which avoids "connection reset by peer" on reuse.
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30)
        };

        public IHost BuildMainApplicationHost(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .UseSerilog((context, services, configuration) =>
                {
                    configuration
                        .MinimumLevel.ControlledBy(_levelSwitch)
                        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                        .MinimumLevel.Override("System", LogEventLevel.Warning)
                        .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                        .MinimumLevel.Override("Quartz", LogEventLevel.Warning)
                        .Enrich.FromLogContext()
                        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
                    try
                    {
                        var paths = services.GetService<IPathProvider>();
                        if (paths != null)
                        {
                            // Serilog appends the date itself: logs/bot-20261003.log, rolling at midnight.
                            string logFile = System.IO.Path.Combine(paths.DataPath, "logs", "bot-.log");
                            configuration.WriteTo.Async(sink => sink.File(
                                logFile,
                                rollingInterval: RollingInterval.Day,
                                retainedFileCountLimit: 60,
                                shared: false,
                                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"));
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
                    services.AddHttpClient("StreamElementsClient", client => client.Timeout = StreamElementsTimeout)
                        .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                        .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler);
                    services.AddSingleton<IStreamElementsService, StreamElementsService>();

                    services.AddHttpClient<RiotHttpHandler>(client => client.Timeout = RiotTimeout)
                        .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                        .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler);

                    services.AddHttpClient<IMmrService, MmrApiService>(client => client.Timeout = MmrTimeout)
                        .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                        .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler);

                    // 6. Bot Logic / Features
                    services.AddSingleton<LinkDetector>();
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

                    services.AddSingleton<ConfigWriterService>();
                    services.AddSingleton<MediaQueueService>();
                    services.AddSingleton<BlacklistService>();
                    services.AddSingleton<SubscriptionService>();
                    services.AddSingleton<FlagWriterService>();
                    services.AddSingleton<ExtractMessageService>();
                    services.AddSingleton<IYouTubeService, YouTubeApiService>();

                    // 8. Hosted Services (Running in background)
                    services.AddHostedService<StartupInitializer>();      // Runs once
                    services.AddHostedService<TTVEventSub>();             // EventSub websocket + watchdog
                    services.AddHostedService<TwitchIrcHostedService>();  // IRC + chat loop
                    services.AddHostedService<MatchMonitoringService>();  // Riot polling
                })
                .Build();
        }
    }
}
