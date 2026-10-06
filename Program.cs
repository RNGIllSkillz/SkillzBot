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
            Console.OutputEncoding = Encoding.UTF8;
            var culture = new CultureInfo("ru-RU");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            // Bootstrap logger for startup errors; replaced by the host's Serilog configuration.
            var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(levelSwitch)
                .WriteTo.Console()
                .CreateLogger();

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                Log.Fatal(e.ExceptionObject as Exception, "Unhandled Domain Exception");
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Log.Error(e.Exception, "Unobserved task exception");
                e.SetObserved();
            };

            try
            {
                Log.Information("Building Host...");

                var hostBuilders = new IHostBuilders(levelSwitch);
                bool hub = SkillzBot.Services.Twitch.HubSignature.IsHubProcess;
                if (hub && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ENV_CHANNEL_NAME")))
                    Environment.SetEnvironmentVariable("ENV_CHANNEL_NAME", "hub");
                using var host = hub ? hostBuilders.BuildHubHost(args) : hostBuilders.BuildMainApplicationHost(args);

                Log.Information("Starting Host...");
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
    }
}
