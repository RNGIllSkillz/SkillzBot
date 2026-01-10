using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using SkillzBot.Hosts;
using System;
using System.Threading.Tasks;
using SkillzBot.IllConfiguration; 
using SkillzBot.IllSkillzBot.IllCommandsNest;
using Microsoft.Extensions.DependencyInjection;
using SkillzBot.Interfaces;
using SkillzBot.IllSkillzBot;

namespace SkillzBot.Discord
{
    internal class DiscordClient
    {
        private DiscordSocketClient _client;
        private CommandService _commands;
        private readonly IServiceProvider _services; 
        private readonly ILogger<DiscordClient> _logger;
        private bool _IsTokenValid = true;
        private readonly BotConfigModel _config;

        public DiscordClient(IServiceProvider services, BotConfigModel config, ILogger<DiscordClient> logger)
        {
            _services = services;
            _config = config;
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            if (_config.DiscordNoteID == 0 || string.IsNullOrEmpty(_config.DiscordBotToken))
            {
                _logger.LogWarning("Discord config is invalid! Discord bot is disabled");
                _IsTokenValid = false;
                return;
            }

            if (_IsTokenValid)
            {
                await StartUp(_config.DiscordBotToken);
            }
        }

        private async Task StartUp(string token)
        {
            try
            {
                DiscordSocketConfig config = new DiscordSocketConfig
                {
                    GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent,
                    AlwaysDownloadUsers = false
                };
                _client = new DiscordSocketClient(config);
                _commands = new CommandService();

                await RegisterCommandsAsync();
                _client.Log += DisLog;
                _client.Ready += OnReady;
                _client.Disconnected += OnDisconnected;

                await _client.LoginAsync(TokenType.Bot, token);
                await _client.StartAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start Discord Client");
            }
        }

        private async Task OnDisconnected(Exception exception)
        {
            _logger.LogWarning(exception, "Discord Bot has been disconnected! Attempting to restart...");
            await Task.Delay(5000);
            _client.Dispose();
            await StartUp(_config.DiscordBotToken);
        }

        private Task DisLog(LogMessage arg)
        {
            var severity = arg.Severity switch
            {
                LogSeverity.Critical => LogLevel.Critical,
                LogSeverity.Error => LogLevel.Error,
                LogSeverity.Warning => LogLevel.Warning,
                LogSeverity.Info => LogLevel.Information,
                LogSeverity.Verbose => LogLevel.Trace,
                LogSeverity.Debug => LogLevel.Debug,
                _ => LogLevel.Information
            };
            _logger.Log(severity, arg.Exception, "[Discord] {Source}: {Message}", arg.Source, arg.Message);
            return Task.CompletedTask;
        }

        private async Task OnReady()
        {
            _logger.LogInformation("Discord Bot is connected and ready.");
            await Task.CompletedTask;
        }

        public async Task SendMessage(string message, ulong? DiscordNoteID = null)
        {
            if (!_IsTokenValid) return;
            DiscordNoteID ??= _config.DiscordNoteID;
            if (_client.GetChannel((ulong)DiscordNoteID) is SocketTextChannel channel)
                await channel.SendMessageAsync(message);
            else
                _logger.LogWarning("Discord channel with ID {ChannelId} not found.", DiscordNoteID);
        }

        public async Task SendEmbedMsg(string Description, string ImageUrl = "", string summoner = "", string rank = "", string lp = "", ulong? DiscordNoteID = null, bool isUp = true, string stats = null)
        {
            if (!_IsTokenValid) return;
            EmbedBuilder embedBuilder = new EmbedBuilder();
            if (isUp)
            {
                embedBuilder.Title = $"На канале {_config.ChannelName} начался стрим!";
                embedBuilder.Description = Description;
                embedBuilder.ImageUrl = ImageUrl;
                embedBuilder.Url = $"https://www.twitch.tv/{_config.ChannelName}";
                embedBuilder.Color = Color.Blue;
            }
            else
            {
                embedBuilder.Title = $"Стример офнул!";
                embedBuilder.Description = Description;
                embedBuilder.Color = Color.Red;
            }

            embedBuilder.AddField("Призыватель", summoner);
            embedBuilder.AddField("Ранк", rank);
            embedBuilder.AddField("ЛП", lp);
            if (!isUp)
                if (stats != null)
                    embedBuilder.AddField("За сегодня", stats);
            embedBuilder.WithUrl($"https://www.twitch.tv/{_config.ChannelName}");

            var builtEmbed = embedBuilder.Build();
            DiscordNoteID ??= _config.DiscordNoteID;
            if (_client.GetChannel((ulong)DiscordNoteID) is SocketTextChannel channel)
                await channel.SendMessageAsync(embed: builtEmbed);
            else
                _logger.LogWarning("Discord channel with ID {ChannelId} not found.", DiscordNoteID);
        }

        public async Task RegisterCommandsAsync()
        {
            _client.MessageReceived += HandleCommandAsync;
            await _commands.AddModulesAsync(typeof(DiscordCommands).Assembly, _services);
        }

        private async Task HandleCommandAsync(SocketMessage arg)
        {
            var message = arg as SocketUserMessage;
            if (message == null || message.Author.IsBot) return;
            if (message.Channel.Id != _config.DiscordSpamID) return;

            if (message.Content.StartsWith("!"))
            {
                // Resolve dependencies from the container
                var ircClient = _services.GetRequiredService<ITtvIRCClient>();
                var illCommands = _services.GetRequiredService<IllCommands>();
                var gameState = _services.GetRequiredService<IGameStateService>();
                var illGames = _services.GetRequiredService<IllGames>();

                // Instantiate DiscordCommands with all required dependencies
                var discordCommands = new DiscordCommands(
                    ircClient,
                    illCommands,
                    _config,
                    gameState,
                    this, // Passing 'this' instance of DiscordClient
                    illGames
                );

                await discordCommands.CommandHandler(message.Content);
            }
        }
    }
}