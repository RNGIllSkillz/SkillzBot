using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using SkillzBot.Hosts;
using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Serilog.Core;

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