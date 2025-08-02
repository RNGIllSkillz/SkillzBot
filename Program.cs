using System.Text;
using System;
using System.IO;
using SkillzBot.IRC;
using Newtonsoft.Json;
using System.Threading.Tasks;
using SkillzBot.API.Twitch;
using System.Globalization;
using System.Threading;
using SkillzBot.JSON.Settings;
using SkillzBot.MODELS;
using SkillzBot.Discord;
using SkillzBot.Hosts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using SkillzBot.Interfaces;
using SkillzBot.Singleton;


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
                await InitializeHostAsync().ConfigureAwait(false);
                await InitializeApplicationAsync().ConfigureAwait(false);
                await RunApplicationAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (_logger != null)
                {
                    _logger.LogCritical(ex, "Critical error in main application");
                }
                else
                {
                    Console.WriteLine($"Critical error before logger initialization: {ex}");
                }
                Environment.Exit(1);
            }
            finally
            {
                _resetEvent?.Dispose();
                _host?.Dispose();
            }
        }
        
        private static async Task InitializeHostAsync()
        {
            // Initialize paths first (needed for log file path)
            InitializePaths();
            await IllSingleton.InitializeAsync(_configPath).ConfigureAwait(false);

            var hostBuilders = new IHostBuilders(_dataPath, _channelName);
            _host = hostBuilders.BuildMainApplicationHost();

            _logger = _host.Services.GetRequiredService<ILogger<IllSkillzBotMain>>();
            IllServiceProvider.Initialize(_host.Services);
            await _host.StartAsync().ConfigureAwait(false);
        }

        private static async Task InitializeApplicationAsync()
        {
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
            Console.CancelKeyPress += OnCancelKeyPress;
            Console.OutputEncoding = Encoding.UTF8;
            var culture = new CultureInfo("ru-RU");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            await EnsureDefaultFilesExistAsync().ConfigureAwait(false);
            await EnsureConfigurationExistsAsync().ConfigureAwait(false);            

            _logger.LogInformation("Application initialized successfully for channel: {ChannelName}", _channelName);
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
            var services = await InitializeServicesAsync().ConfigureAwait(false);
            // Configure startup settings
            await ConfigureStartupAsync().ConfigureAwait(false);

            // Start background tasks
            var quartzManager = new QuartzBackgroundTaskManager();
            await quartzManager.ScheduleTasks().ConfigureAwait(false);

            //IRC (LEGACY)
            var IRCClient = _host.Services.GetRequiredService<ITtvIRCClient>();
            TtvIRCClient.Initialize(IRCClient);


            // Wait for shutdown signal
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
                var discordClient = new DiscordClient();
                services.Add(discordClient);

                //initialise IRC static method (LEGACY)
                var ircClient = _host.Services.GetRequiredService<ITtvIRCClient>();
                bool ircInitialized = await ircClient.InitializeAsync().ConfigureAwait(false);
                if (!ircInitialized)
                {
                    _logger.LogWarning("Failed to initialize Twitch IRC. Continuing without it...");
                }

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
                throw;
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
                Path.Combine(_dataPath, "channelList.txt"),
                Path.Combine(_dataPath, "dailyStats.txt")
            };

            foreach (string filePath in filesToCreate)
            {
                await EnsureFileExistsAsync(filePath);
            }

            _logger.LogInformation("Default files verified/created successfully");
        }

        private static async Task EnsureFileExistsAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    await File.WriteAllTextAsync(filePath, string.Empty);
                    _logger.LogDebug("Created file: {FilePath}", filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create file: {FilePath}", filePath);
                throw;
            }
        }

        private static async Task EnsureConfigurationExistsAsync()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    var defaultSettings = new SettingsJson();
                    string jsonContent = JsonConvert.SerializeObject(defaultSettings, Formatting.Indented);
                    await File.WriteAllTextAsync(_configPath, jsonContent);
                    _logger.LogInformation("Created default configuration file: {ConfigPath}", _configPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create configuration file: {ConfigPath}", _configPath);
                throw;
            }
        }

        private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs args)
        {
            var exception = (Exception)args.ExceptionObject;
            _logger.LogCritical(exception, "Unhandled exception occurred. IsTerminating: {IsTerminating}",
                args.IsTerminating);

            _logger.LogCritical(exception, "MainHandler caught : ");

            if (args.IsTerminating)
            {
                _resetEvent.Set();
            }
        }

        private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            _logger.LogInformation("Shutdown signal received");
            e.Cancel = true; // Prevent immediate termination
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