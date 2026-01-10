using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Serilog;
using Serilog.Events;
using SkillzBot.API.RiotGames;
using SkillzBot.API.Twitch;
using SkillzBot.Discord;
using SkillzBot.Hosts;
using SkillzBot.JSON.Settings;
using SkillzBot.MODELS;
using SkillzBot.IllConfiguration; 
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Core;
using SkillzBot.Interfaces;

namespace IllSkillzBot
{
    class IllSkillzBotMain
    {
        static async Task Main(string[] args)
        {
            // Global settings
            Console.OutputEncoding = Encoding.UTF8;
            var culture = new CultureInfo("ru-RU");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            // Initial Bootstrap Logger (just for startup errors)
            var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(levelSwitch)
                .WriteTo.Console()
                .CreateLogger();

            try
            {
                Log.Information("Building Host...");

                var hostBuilders = new IHostBuilders(levelSwitch);
                using var host = hostBuilders.BuildMainApplicationHost(args);

                Log.Information("Starting Host...");
                await InitializeApplicationAsync();                
                await host.RunAsync();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Critical application failure");
                Environment.Exit(1);
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }        
        private static async Task InitializeApplicationAsync()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => 
                Log.Fatal((Exception)e.ExceptionObject, "Unhandled Domain Exception");   
            Console.OutputEncoding = Encoding.UTF8;
            var culture = new CultureInfo("ru-RU");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;            
            await Task.CompletedTask;
        }        
    }
}