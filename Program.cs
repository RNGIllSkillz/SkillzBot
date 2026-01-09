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
using SkillzBot.Interfaces;
using SkillzBot.JSON.Settings;
using SkillzBot.MODELS;
using SkillzBot.Singleton;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Core;

namespace IllSkillzBot
{
    class IllSkillzBotMain
    {
        private static IHost _host;
        private static readonly ManualResetEventSlim _resetEvent = new ManualResetEventSlim(false);
        private static string _dataPath;
        private static string _sharedPath;
        private static string _configPath;
        private static string _channelName;

        static async Task Main(string[] args)
        {
            try
            {
                InitializePaths();

                // 1. Initialize Singleton State
                await IllSingleton.InitializeAsync(_configPath).ConfigureAwait(false);

                // 2. Configure Serilog
                string logFileName = $"{DateTime.Now:yyyy-MM-dd}.log";
                string fullLogPath = Path.Combine(_dataPath, "logs", logFileName);
                var levelSwitch = new LoggingLevelSwitch(IllSingleton.State.Debug ? LogEventLevel.Debug : LogEventLevel.Warning);
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.ControlledBy(levelSwitch)
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                    .MinimumLevel.Override("System", LogEventLevel.Warning)
                    .MinimumLevel.Override("Quartz", LogEventLevel.Warning)
                    .MinimumLevel.Override("TwitchLib", LogEventLevel.Information)
                    .Enrich.FromLogContext()
                    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                    .WriteTo.File(
                        path: fullLogPath,
                        rollingInterval: RollingInterval.Infinite, 
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                    )
                    .CreateLogger();

                Log.Information("Paths initialized. Logging system started.");

                // 3. Ensure Files Exist
                await EnsureDefaultFilesExistAsync().ConfigureAwait(false);
                await EnsureConfigurationExistsAsync().ConfigureAwait(false);

                // 4. Build Host
                Log.Information("Building Host...");
                var hostBuilders = new IHostBuilders(levelSwitch);
                _host = hostBuilders.BuildMainApplicationHost(args);

                // 5. Initialize Service Locator (Legacy support)
                IllServiceProvider.Initialize(_host.Services);

                // 6. Initialize Application Settings
                await InitializeApplicationAsync().ConfigureAwait(false);

                // 7. Start Host
                Log.Information("Starting Host (Background Services)...");
                await _host.StartAsync().ConfigureAwait(false);
                Log.Information("Host Started.");

                // 8. Run Application Logic
                await RunApplicationAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Critical application failure");
                Environment.Exit(1);
            }
            finally
            {
                Log.CloseAndFlush();
                _resetEvent?.Dispose();
                _host?.Dispose();
            }
        }

        private static async Task InitializeApplicationAsync()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => 
                Log.Fatal((Exception)e.ExceptionObject, "Unhandled Domain Exception");
                
            Console.CancelKeyPress += (s, e) =>
            {
                Log.Information("Shutdown signal received");
                e.Cancel = true;
                _resetEvent.Set();
            };

            Console.OutputEncoding = Encoding.UTF8;
            var culture = new CultureInfo("ru-RU");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            // Legacy Static Init
            TtvAPI.Initialize(_host.Services.GetRequiredService<ILogger<TtvAPI>>());
            
            await Task.CompletedTask;
        }


        private static void InitializePaths()
        {
            _channelName = Environment.GetEnvironmentVariable("ENV_CHANNEL_NAME");
            if (string.IsNullOrWhiteSpace(_channelName)) throw new InvalidOperationException("ENV_CHANNEL_NAME required");
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _dataPath = Path.Combine(baseDir, $"Channels_Data/{_channelName}/DATA/");
            _sharedPath = Path.Combine(baseDir, "Channels_Data/_shared/");
            _configPath = Path.Combine(_dataPath, $"{_channelName}.ini");
            Directory.CreateDirectory(_dataPath); Directory.CreateDirectory(_sharedPath); Directory.CreateDirectory(Path.Combine(_dataPath, "logs"));
        }

        private static async Task RunApplicationAsync()
        {
            var services = await InitializeServicesAsync().ConfigureAwait(false);
            await ConfigureStartupAsync().ConfigureAwait(false);

            Log.Information("Scheduling Quartz Tasks...");
            var quartzManager = _host.Services.GetRequiredService<QuartzBackgroundTaskManager>();
            await quartzManager.ScheduleTasks().ConfigureAwait(false);

            Log.Information("Bot is fully running. Waiting for exit signal.");
            _resetEvent.Wait();

            await _host.StopAsync();
            foreach (var service in services.OfType<IDisposable>())
            {
                service.Dispose();
            }
        }

        private static async Task<IList<object>> InitializeServicesAsync()
        {
             var discordClient = new DiscordClient(_host.Services.GetRequiredService<ITtvIRCClient>(), _host.Services);
             await discordClient.InitializeAsync();
             
             var riotService = _host.Services.GetRequiredService<IRiotApiService>();
             await riotService.InitializeAsync();
             
             return new List<object> { discordClient };
        }

        private static async Task ConfigureStartupAsync()
        {
            try
            {
                bool isStreamLive = await TtvAPI.GetStreamStatus().ConfigureAwait(false);
                IllSingleton.State.BroadcasterIsOnline = isStreamLive;
                string status = isStreamLive ? "LIVE" : "Offline";
                Log.Information("{ChannelName} is {Status}!", IllSingleton.Config.ChannelName, status);
            }
            catch (Exception ex)
            {
                Log.Information(ex, "Failed to configure startup settings");
            }
        }

        private static async Task EnsureDefaultFilesExistAsync()
        {
            var filesToCreate = new[]
            {
                Path.Combine(_sharedPath, "dic.txt"),
                Path.Combine(_sharedPath, "dicWhiteList.txt"),
                Path.Combine(_sharedPath, "pichkaList.txt"),
                Path.Combine(_dataPath, "mediaqueue.txt"),
                Path.Combine(_dataPath, "userblacklist.txt"),
                Path.Combine(_dataPath, "mediaList.txt"),
                Path.Combine(_dataPath, "channelList.txt")
            };

            foreach (string filePath in filesToCreate)
            {
                await EnsureFileExistsAsync(filePath);
            }
        }

        private static async Task EnsureFileExistsAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                await File.WriteAllTextAsync(filePath, string.Empty);
            }
        }

        private static async Task EnsureConfigurationExistsAsync()
        {
            if (!File.Exists(_configPath))
            {
                var defaultSettings = new SettingsJson();
                string jsonContent = JsonConvert.SerializeObject(defaultSettings, Formatting.Indented);
                await File.WriteAllTextAsync(_configPath, jsonContent);
            }
        }
        /*
        private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs args)
        {
            var exception = (Exception)args.ExceptionObject;
            _logger?.LogCritical(exception, "Unhandled exception. IsTerminating: {IsTerminating}", args.IsTerminating);
            Console.WriteLine($"UNHANDLED EXCEPTION: {exception}");
            if (args.IsTerminating) _resetEvent.Set();
        }

        private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            _logger?.LogInformation("Shutdown signal received");
            Console.WriteLine("Shutdown signal received.");
            e.Cancel = true;
            _resetEvent.Set();
        }*/

        public static ConfPathes GetDataPath() => new ConfPathes { sharedPath = _sharedPath, uniquePath = _dataPath };

        public static string GetConfigPath() => _configPath;
    }
}