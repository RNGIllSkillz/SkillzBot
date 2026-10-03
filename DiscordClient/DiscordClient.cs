using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkillzBot.IllConfiguration;
using SkillzBot.IllSkillzBot;
using SkillzBot.IllSkillzBot.IllCommandsNest;
using SkillzBot.Interfaces;
using System;
using System.Threading.Tasks;

namespace SkillzBot.Discord
{
    public class DiscordClient
    {
        private DiscordSocketClient _client;
        private readonly IServiceProvider _services;
        private readonly ILogger<DiscordClient> _logger;
        private bool _isEnabled;
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
                _isEnabled = false;
                return;
            }

            try
            {
                var config = new DiscordSocketConfig
                {
                    GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent,
                    AlwaysDownloadUsers = false
                };
                _client = new DiscordSocketClient(config);
                _client.Log += DisLog;
                _client.Ready += OnReady;
                _client.Disconnected += OnDisconnected;
                _client.MessageReceived += HandleCommandAsync;

                await _client.LoginAsync(TokenType.Bot, _config.DiscordBotToken);
                await _client.StartAsync();
                _isEnabled = true;
            }
            catch (Exception ex)
            {
                _isEnabled = false;
                _logger.LogError(ex, "Failed to start Discord Client");
            }
        }

        private Task OnDisconnected(Exception exception)
        {
            // DiscordSocketClient reconnects on its own; recreating it here would race that logic.
            _logger.LogWarning(exception, "Discord gateway disconnected; the client will reconnect automatically.");
            return Task.CompletedTask;
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

        private Task OnReady()
        {
            _logger.LogInformation("Discord Bot is connected and ready.");
            return Task.CompletedTask;
        }

        public async Task SendMessage(string message, ulong? DiscordNoteID = null)
        {
            if (!_isEnabled || _client == null) return;
            DiscordNoteID ??= _config.DiscordNoteID;
            if (_client.GetChannel((ulong)DiscordNoteID) is SocketTextChannel channel)
                await channel.SendMessageAsync(message);
            else
                _logger.LogWarning("Discord channel with ID {ChannelId} not found.", DiscordNoteID);
        }

        public async Task SendEmbedMsg(string Description, string ImageUrl = "", string summoner = "", string rank = "", string lp = "", ulong? DiscordNoteID = null, bool isUp = true, string stats = null)
        {
            if (!_isEnabled || _client == null) return;
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
            if (!isUp && stats != null)
                embedBuilder.AddField("За сегодня", stats);
            embedBuilder.WithUrl($"https://www.twitch.tv/{_config.ChannelName}");

            var builtEmbed = embedBuilder.Build();
            DiscordNoteID ??= _config.DiscordNoteID;
            if (_client.GetChannel((ulong)DiscordNoteID) is SocketTextChannel channel)
                await channel.SendMessageAsync(embed: builtEmbed);
            else
                _logger.LogWarning("Discord channel with ID {ChannelId} not found.", DiscordNoteID);
        }

        private async Task HandleCommandAsync(SocketMessage arg)
        {
            try
            {
                var message = arg as SocketUserMessage;
                if (message == null || message.Author.IsBot) return;
                if (message.Channel.Id != _config.DiscordSpamID) return;
                if (!message.Content.StartsWith("!")) return;

                // Resolved lazily to avoid a constructor cycle between the Discord client and the command set.
                var discordCommands = new DiscordCommands(
                    _services.GetRequiredService<ITtvIRCClient>(),
                    _services.GetRequiredService<IllCommands>(),
                    _config,
                    _services.GetRequiredService<IGameStateService>(),
                    this,
                    _services.GetRequiredService<IllGames>());

                await discordCommands.CommandHandler(message.Content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Discord command handling failed for message: {Content}", arg?.Content);
            }
        }
    }
}
