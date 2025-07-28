using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore;
using Microsoft.Extensions.Hosting;
using SkillzBot.EventSub;
using Microsoft.Extensions.DependencyInjection;
using TwitchLib.EventSub.Websockets.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SkillzBot.MySQL;
using System;
using System.IO;
using SkillzBot.Writers;

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
                    services.AddHostedService<TTVEventSub>();

                    // Add other services
                    // services.AddSingleton<DiscordClient>();
                    // services.AddSingleton<TtvIRCClient>();
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