using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using SkillzBot.Hosts;
using System;
using System.Threading.Tasks;
using SkillzBot.Singleton;
using SkillzBot.Interfaces;
using SkillzBot.IllSkillzBot.IllCommandsNest;
using Microsoft.Extensions.DependencyInjection;

namespace SkillzBot.Discord
{
    internal class DiscordClient
    {
        private static DiscordSocketClient _client;
        private static CommandService _commands;
        private static IServiceProvider _services;
        private static readonly ILogger<DiscordClient> _logger = IllServiceProvider.GetLogger<DiscordClient>();
        private static bool _IsTokenValid = true;
        private readonly ITtvIRCClient _ircClient;
        public DiscordClient(ITtvIRCClient ircClient, IServiceProvider services)
        {
            _ircClient = ircClient;
            _services = services;
        }
        public async Task InitializeAsync()
        {
            if (IllSingleton.Config.DiscordNoteID == 0 || string.IsNullOrEmpty(IllSingleton.Config.DiscordBotToken))
            {
                _logger.LogWarning("Discord config is invalid! Discord bot is disabled");
                _IsTokenValid = false;
                return;
            }

            if (_IsTokenValid)
            {
                await StartUp(IllSingleton.Config.DiscordBotToken).ConfigureAwait(false);
            }
        }
        private static async Task StartUp(string token)
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

                await RegisterCommandsAsync().ConfigureAwait(false);
                _client.Log += DisLog;
                _client.Ready += OnReady;
                _client.Disconnected += OnDisconnected;

                await _client.LoginAsync(TokenType.Bot, token).ConfigureAwait(false);
                await _client.StartAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start Discord Client");
            }
        }
        private static async Task OnDisconnected(Exception exception)
        {
            _logger.LogWarning(exception, "Discord Bot has been disconnected! Attempting to restart...");
            await Task.Delay(5000).ConfigureAwait(false);
            _client.Dispose();
            await StartUp(IllSingleton.Config.DiscordBotToken).ConfigureAwait(false);
        }

        private static Task DisLog(LogMessage arg)
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

        private static async Task OnReady()
        {
            _logger.LogInformation("Discord Bot is connected and ready.");
            await Task.CompletedTask;
        }
        public static async Task SendMessage(string message, ulong? DiscordNoteID = null)
        {
            if (!_IsTokenValid) return;
            DiscordNoteID ??= IllSingleton.Config.DiscordNoteID;
            if (_client.GetChannel((ulong)DiscordNoteID) is SocketTextChannel channel)
                await channel.SendMessageAsync(message).ConfigureAwait(false);
            else
                _logger.LogWarning("Discord channel with ID {ChannelId} not found.", DiscordNoteID);
        }
        public static async Task SendEmbedMsg(string Description, string ImageUrl = "", string summoner = "", string rank = "", string lp = "", ulong? DiscordNoteID = null, bool isUp = true, string stats = null)
        {
            if (!_IsTokenValid) return;
            EmbedBuilder embedBuilder = new EmbedBuilder();
            if (isUp)
            {
                embedBuilder.Title = $"На канале {IllSingleton.Config.ChannelName} начался стрим!";
                embedBuilder.Description = Description;
                embedBuilder.ImageUrl = ImageUrl;
                embedBuilder.Url = $"https://www.twitch.tv/{IllSingleton.Config.ChannelName}";
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
            embedBuilder.WithUrl($"https://www.twitch.tv/{IllSingleton.Config.ChannelName}");

            var builtEmbed = embedBuilder.Build();
            DiscordNoteID ??= IllSingleton.Config.DiscordNoteID;
            if (_client.GetChannel((ulong)DiscordNoteID) is SocketTextChannel channel)
                await channel.SendMessageAsync(embed: builtEmbed).ConfigureAwait(false);
            else
                _logger.LogWarning("Discord channel with ID {ChannelId} not found.", DiscordNoteID);
        }

        public static async Task RegisterCommandsAsync()
        {
            _client.MessageReceived += HandleCommandAsync;
            await _commands.AddModulesAsync(typeof(DiscordCommands).Assembly, _services);
            //await Task.CompletedTask;
        }
        private static async Task HandleCommandAsync(SocketMessage arg)
        {
            var message = arg as SocketUserMessage;
            if (message == null || message.Author.IsBot) return;
            if (message.Channel.Id != IllSingleton.Config.DiscordSpamID) return;

            if (message.Content.StartsWith("!"))
            {
                var ircClient = _services.GetRequiredService<ITtvIRCClient>();
                var illCommands = _services.GetRequiredService<IllCommands>();

                var discordCommands = new DiscordCommands(ircClient, illCommands);
                await discordCommands.CommandHandler(message.Content).ConfigureAwait(false);
            }
        }
    }
}