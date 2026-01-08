using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkillzBot.EventSub;
using SkillzBot.IllSkillzBot;
using SkillzBot.IllSkillzBot.IllCommandsNest;
using SkillzBot.Interfaces;
using SkillzBot.IRC;
using SkillzBot.MySQL;
using SkillzBot.TtvClient.TTVRewards;
using SkillzBot.Writers;
using System;
using System.IO;
using TwitchLib.EventSub.Websockets.Extensions;

namespace SkillzBot.Hosts
{
    internal class IHostBuilders
    {
        private readonly string _dataPath;
        private readonly string _channelName;
        private readonly IConfiguration _configuration;

        public IHostBuilders(string dataPath, string channelName, IConfiguration configuration = null)
        {
            _dataPath = dataPath;
            _channelName = channelName;
            _configuration = configuration;
        }

        public IHostBuilder CreateMainApplicationHostBuilder() =>
            Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddSkillzLogger(options =>
                    {
                        options.LogFilePath = Path.Combine(_dataPath, "logs", $"{_channelName}_{DateTime.Now:yyyy-MM-dd}.log");
                        options.WriteToFile = true;
                        options.WriteToConsole = true;
                        options.IncludeTimestamp = true;
                        options.AddEmptyLines = true;

                        options.TraceSeparator = "···············································································";
                        options.DebugSeparator = "───────────────────────────────────────────────────────────────────────────────────";
                        options.InfoSeparator = "═══════════════════════════════════════════════════════════════════════════════════";
                        options.WarningSeparator = "▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲";
                        options.ErrorSeparator = "████████████████████████████████████████████████████████████████████████████████████";
                        options.CriticalSeparator = "🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥";
                    });
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddDatabaseServices(_configuration ?? context.Configuration);
                    services.AddTwitchLibEventSubWebsockets();

                    // Core Services
                    services.AddSingleton<ITtvIRCClient, TtvIRCClientService>();
                    services.AddSingleton<API.RiotGames.IRiotApiService, API.RiotGames.RiotApiService>();

                    // Logic Services (Added specific registrations)
                    services.AddSingleton<IllChatFilters>();
                    services.AddSingleton<IllGames>();
                    services.AddSingleton<RewardsRedemption>();
                    services.AddSingleton<IllModeratorsInteractions>();

                    // Commands & Handlers
                    services.AddSingleton<IllCommands>(); // FIX: Registered IllCommands
                    services.AddSingleton<IllCommandHandler>();
                    services.AddSingleton<IllChatMessageHandler>();

                    // --- HOSTED SERVICES (Background Tasks) ---

                    // 1. The EventSub (Websockets)
                    services.AddHostedService<TTVEventSub>();

                    // 2. THIS WAS MISSING: The IRC Client (Chat)
                    services.AddHostedService<TwitchIrcHostedService>();
                });

        public IHost BuildMainApplicationHost()
        {
            return CreateMainApplicationHostBuilder().Build();
        }
    }

    internal class IWebHostBuilders
    {
        private static IWebHostBuilder ILLApiHostBuilder() =>
        WebHost.CreateDefaultBuilder()
            .UseStartup<Startup>();

        public IWebHost ILLAPIHost()
        {
            return ILLApiHostBuilder().Build();
        }
    }
}