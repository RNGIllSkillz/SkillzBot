using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SkillzBot.API.RiotGames;
using SkillzBot.API.Twitch;
using SkillzBot.Discord;
using SkillzBot.Hosts;
using SkillzBot.Interfaces;
using SkillzBot.IRC;
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
using SkillzBot.IllSkillzBot;

namespace IllSkillzBot
{
    class IllSkillzBotMain
    {
        private static ILogger<IllSkillzBotMain> _logger;
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
                Console.WriteLine("Paths initialized.");

                // 1. Initialize Singleton State
                await IllSingleton.InitializeAsync(_configPath).ConfigureAwait(false);

                // 2. Ensure Files Exist
                await EnsureDefaultFilesExistAsync().ConfigureAwait(false);
                await EnsureConfigurationExistsAsync().ConfigureAwait(false);

                // 3. Build Host
                Console.WriteLine("Building Host...");
                var hostBuilders = new IHostBuilders(_dataPath, _channelName);
                _host = hostBuilders.BuildMainApplicationHost();

                // 4. Initialize Service Locator
                IllServiceProvider.Initialize(_host.Services);
                _logger = _host.Services.GetRequiredService<ILogger<IllSkillzBotMain>>();

                // 5. Initialize Application Settings (TtvAPI etc)
                // We run this BEFORE starting the host so basic APIs are ready
                await InitializeApplicationAsync().ConfigureAwait(false);

                // 6. Start Host (Starts Background Services: EventSub, IRC, Logger)
                Console.WriteLine("Starting Host (Background Services)...");
                await _host.StartAsync().ConfigureAwait(false);
                Console.WriteLine("Host Started.");

                // 7. Run Application Logic (Services init)
                await RunApplicationAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CRITICAL ERROR: {ex}");
                if (_logger != null) _logger.LogCritical(ex, "Critical error in main application");
                Environment.Exit(1);
            }
            finally
            {
                _resetEvent?.Dispose();
                _host?.Dispose();
            }
        }

        private static async Task InitializeApplicationAsync()
        {
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
            Console.CancelKeyPress += OnCancelKeyPress;
            Console.OutputEncoding = Encoding.UTF8;
            var culture = new CultureInfo("ru-RU");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            // Initialize Static API Wrappers
            TtvAPI.Initialize(_host.Services.GetRequiredService<ILogger<TtvAPI>>());

            _logger.LogInformation("Application initialized successfully for channel: {ChannelName}", _channelName);
            await Task.CompletedTask;
        }

        private static void InitializePaths()
        {
            _channelName = Environment.GetEnvironmentVariable("ENV_CHANNEL_NAME");

            if (string.IsNullOrWhiteSpace(_channelName))
            {
                throw new InvalidOperationException("ENV_CHANNEL_NAME environment variable is required");
            }

            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _dataPath = Path.Combine(baseDirectory, $"Channels_Data/{_channelName}/DATA/");
            _sharedPath = Path.Combine(baseDirectory, "Channels_Data/_shared/");
            _configPath = Path.Combine(_dataPath, $"{_channelName}.ini");

            Directory.CreateDirectory(_dataPath);
            Directory.CreateDirectory(_sharedPath);
            Directory.CreateDirectory(Path.Combine(_dataPath, "logs"));
        }

        private static async Task RunApplicationAsync()
        {
            Console.WriteLine("Initializing External Services (Discord/Riot)...");
            var services = await InitializeServicesAsync().ConfigureAwait(false);

            Console.WriteLine("Configuring Startup settings...");
            await ConfigureStartupAsync().ConfigureAwait(false);

            Console.WriteLine("Scheduling Cron Tasks...");
            var quartzManager = new QuartzBackgroundTaskManager();
            await quartzManager.ScheduleTasks().ConfigureAwait(false);

            Console.WriteLine("Bot is fully running. Waiting for exit signal.");
            _resetEvent.Wait();

            _logger.LogInformation("Shutting down application...");
            await _host.StopAsync().ConfigureAwait(false);
            foreach (var service in services.OfType<IDisposable>())
            {
                service.Dispose();
            }
        }

        private static async Task<IList<object>> InitializeServicesAsync()
        {
            var services = new List<object>();
            try
            {
                var discordClient = new DiscordClient(
                    _host.Services.GetRequiredService<ITtvIRCClient>(),
                    _host.Services 
                );
                
                await discordClient.InitializeAsync().ConfigureAwait(false);
                services.Add(discordClient);

                var riotService = _host.Services.GetRequiredService<IRiotApiService>();
                await riotService.InitializeAsync().ConfigureAwait(false);

                return services;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize services");
                throw;
            }
        }

        private static async Task ConfigureStartupAsync()
        {
            try
            {
                bool isStreamLive = await TtvAPI.GetStreamStatus().ConfigureAwait(false);
                IllSingleton.State.BroadcasterIsOnline = isStreamLive;
                string status = isStreamLive ? "LIVE" : "Offline";
                _logger.LogInformation("{ChannelName} is {Status}!", IllSingleton.Config.ChannelName, status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to configure startup settings");
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
        }

        public static ConfPathes GetDataPath()
        {
            return new ConfPathes
            {
                sharedPath = _sharedPath,
                uniquePath = _dataPath
            };
        }

        public static string GetConfigPath()
        {
            return _configPath;
        }
    }
}