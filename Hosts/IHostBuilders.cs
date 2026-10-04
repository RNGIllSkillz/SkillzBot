using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
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
using SkillzBot.Logging;
using SkillzBot.MySQL;
using SkillzBot.QuartZ;
using SkillzBot.Services;
using SkillzBot.Services.Infrastructure;
using SkillzBot.Services.Proxy;
using SkillzBot.Services.State;
using SkillzBot.Services.Twitch;
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

        // Pooled sockets are recycled before the remote side drops them, which avoids
        // "connection reset by peer" on reuse. ProxyService routes a client through the
        // configured proxy only when its purpose is listed in ProxyApplyTo.
        private static Func<IServiceProvider, HttpMessageHandler> PrimaryHandler(string purpose) =>
            sp => sp.GetRequiredService<ProxyService>().CreateHandler(purpose);

        public IHost BuildMainApplicationHost(string[] args)
        {
            var builder = Host.CreateDefaultBuilder(args)
                .UseSerilog((context, services, configuration) =>
                {
                    // Compact, grep-friendly lines: one event per line, short component name,
                    // one-line exception summary. Full stack traces go to the errors file only.
                    const string compactTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{Component:l}] {Message:lj}{ExceptionShort:l}{NewLine}";
                    const string consoleTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] [{Component:l}] {Message:lj}{ExceptionShort:l}{NewLine}";
                    const string detailedTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{Component:l}] {Message:lj}{NewLine}{Exception}";

                    configuration
                        .MinimumLevel.ControlledBy(_levelSwitch)
                        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                        .MinimumLevel.Override("System", LogEventLevel.Warning)
                        .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                        .MinimumLevel.Override("Quartz", LogEventLevel.Warning)
                        .Enrich.FromLogContext()
                        .Enrich.With<CompactLogEnricher>()
                        .WriteTo.Console(outputTemplate: consoleTemplate);
                    try
                    {
                        var paths = services.GetService<IPathProvider>();
                        if (paths != null)
                        {
                            string logDir = System.IO.Path.Combine(paths.DataPath, "logs");
                            // bot-yyyyMMdd.log: everything at the current level, compact.
                            configuration.WriteTo.Async(sink => sink.File(
                                System.IO.Path.Combine(logDir, "bot-.log"),
                                rollingInterval: RollingInterval.Day,
                                retainedFileCountLimit: 30,
                                shared: false,
                                outputTemplate: compactTemplate));
                            // errors-yyyyMMdd.log: warnings and errors only, with full stack traces.
                            configuration.WriteTo.Async(sink => sink.File(
                                System.IO.Path.Combine(logDir, "errors-.log"),
                                restrictedToMinimumLevel: LogEventLevel.Warning,
                                rollingInterval: RollingInterval.Day,
                                retainedFileCountLimit: 60,
                                shared: false,
                                outputTemplate: detailedTemplate));
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
                            if (System.IO.File.Exists(pathProvider.LegacyConfigPath))
                            {
                                // Same JSON content, new extension. The .ini stays behind as a backup.
                                System.IO.File.Copy(pathProvider.LegacyConfigPath, pathProvider.ConfigPath);
                                Log.Warning("Config migrated from {Old} to {New}; the old file is kept as a backup and is no longer read.", pathProvider.LegacyConfigPath, pathProvider.ConfigPath);
                            }
                            else
                            {
                                throw new System.IO.FileNotFoundException($"Config not found at {pathProvider.ConfigPath} (see config.example.json)");
                            }
                        }

                        return BotConfigurationFactory.Create(pathProvider.ConfigPath);
                    });

                    services.AddSingleton<HealthState>();
                    services.AddSingleton<ProxyService>();

                    // 3. State Management
                    services.AddSingleton<IBotStateService, BotStateService>();
                    services.AddSingleton<IGameStateService, GameStateService>();

                    // 4. Database
                    services.AddDatabaseServices(context.Configuration);

                    // 5. External APIs
                    services.AddTwitchLibEventSubWebsockets();
                    services.AddSingleton<ITtvIRCClient, TtvIRCClientService>();
                    services.AddSingleton<TwitchTokenService>();          // Twitch tokens: panel grants with refresh, config tokens as fallback
                    services.AddSingleton<ITwitchService, TwitchApiService>();
                    services.AddSingleton<IRiotApiService, RiotApiService>();

                    // HTTP Clients
                    services.AddHttpClient(TwitchTokenService.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15));
                    services.AddHttpClient("StreamElementsClient", client => client.Timeout = StreamElementsTimeout)
                        .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                        .ConfigurePrimaryHttpMessageHandler(PrimaryHandler("streamelements"));
                    services.AddSingleton<IStreamElementsService, StreamElementsService>();

                    services.AddHttpClient<RiotHttpHandler>(client => client.Timeout = RiotTimeout)
                        .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                        .ConfigurePrimaryHttpMessageHandler(PrimaryHandler("riot"));

                    services.AddHttpClient(ChampionNames.HttpClientName, client => client.Timeout = RiotTimeout)
                        .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                        .ConfigurePrimaryHttpMessageHandler(PrimaryHandler("riot"));
                    services.AddSingleton<ChampionNames>();

                    services.AddHttpClient<IMmrService, MmrApiService>(client => client.Timeout = MmrTimeout)
                        .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                        .ConfigurePrimaryHttpMessageHandler(PrimaryHandler("mmr"));

                    // 6. Bot Logic / Features
                    services.AddSingleton<LinkDetector>();
                    services.AddSingleton<IllChatFilters>();
                    services.AddSingleton<IllGames>();
                    services.AddSingleton<IllModeratorsInteractions>();
                    services.AddSingleton<Services.Vip.VipRegistryService>();
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
                    services.AddHostedService(sp => sp.GetRequiredService<ProxyService>()); // Starts the proxy sidecar if configured
                    services.AddHostedService<StartupInitializer>();      // Runs once
                    services.AddHostedService<TwitchTokenRefresher>();    // Renews Twitch tokens before they expire
                    services.AddHostedService<TTVEventSub>();             // EventSub websocket + watchdog
                    services.AddHostedService<TwitchIrcHostedService>();  // IRC + chat loop
                    services.AddHostedService<MatchMonitoringService>();  // Riot polling
                    services.AddSingleton<HealthReporter>();              // Periodic one-line health log + API snapshot
                    services.AddHostedService(sp => sp.GetRequiredService<HealthReporter>());
                    services.AddSingleton<Api.ChatFeed>();
                    services.AddSingleton<Api.BotSettingsService>();
                });

            // Web panel API (see Api/ApiHost.cs); ApiPort 0 in the channel config turns it off.
            int apiPort = Api.ApiHost.ReadConfiguredPort();
            if (apiPort > 0)
            {
                builder.ConfigureWebHostDefaults(web =>
                {
                    web.ConfigureKestrel(k => k.ListenAnyIP(apiPort));
                    web.ConfigureServices(Api.ApiHost.ConfigureWebServices);
                    web.Configure(Api.ApiHost.Configure);
                });
            }
            return builder.Build();
        }
    }
}
