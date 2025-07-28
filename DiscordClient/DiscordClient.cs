using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using SkillzBot.Hosts;
using System;
using System.Threading.Tasks;
using SkillzBot.Singleton;

namespace SkillzBot.Discord
{
    internal class DiscordClient
    {
        private static DiscordSocketClient _client;
        private static CommandService _commands;
        private static IServiceProvider _services;
        private static readonly ILogger<DiscordClient> _logger = IllServiceProvider.GetLogger<DiscordClient>();

        private static bool _IsTokenValid = true;
        public DiscordClient()
        {
            Init().GetAwaiter().GetResult();
        }
        private static async Task Init()
        {
            if (IllSingleton.Config.DiscordNoteID == 0 || IllSingleton.Config.DiscordBotToken == null)
            {
                Console.WriteLine("Discord config is invalid! Discord bot is disabled");
                _IsTokenValid = false;
            }
            if (_IsTokenValid)
                await StartUp(IllSingleton.Config.DiscordBotToken).ConfigureAwait(false);
        }
        private static async Task StartUp(string token)
        {
            DiscordSocketConfig config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
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
        private static async Task OnDisconnected(Exception exception)
        {
            Console.WriteLine("Discord Bot has been disconnected!.");
            await Task.Delay(1000).ConfigureAwait(false);
            _client.Dispose();
            await StartUp(IllSingleton.Config.DiscordBotToken).ConfigureAwait(false);
        }

        private static Task DisLog(LogMessage arg)
        {
            Console.WriteLine(arg);
            _logger.LogError(arg.Message);
            return Task.CompletedTask;
        }

        private static async Task OnReady()
        {
            Console.WriteLine("Discord Bot is connected and ready.");
            await Task.Delay(5).ConfigureAwait(false);
        }
        public static async Task SendMessage(string message, ulong? DiscordNoteID = null)
        {
            if (!_IsTokenValid) return;
            DiscordNoteID ??= IllSingleton.Config.DiscordNoteID;
            if (_client.GetChannel((ulong)DiscordNoteID) is SocketTextChannel channel)
                await channel.SendMessageAsync(message).ConfigureAwait(false);
            else
                Console.WriteLine($"Channel with ID {DiscordNoteID} not found.");
        }   
        public static async Task SendEmbedMsg(string Description,string ImageUrl = "", string summoner = "", string rank = "", string lp = "", ulong? DiscordNoteID = null, bool isUp = true, string stats = null)
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
                    embedBuilder.AddField("За сегродня", stats);
            embedBuilder.WithUrl($"https://www.twitch.tv/{IllSingleton.Config.ChannelName}");

            var builtEmbed = embedBuilder.Build();
            DiscordNoteID ??= IllSingleton.Config.DiscordNoteID;
            if (_client.GetChannel((ulong)DiscordNoteID) is SocketTextChannel channel)
                await channel.SendMessageAsync(embed: builtEmbed).ConfigureAwait(false);
            else
                Console.WriteLine($"Channel with ID {DiscordNoteID} not found.");            
        }

        public static async Task RegisterCommandsAsync()
        {
            _client.MessageReceived += HandleCommandAsync;
            await _commands.AddModulesAsync(typeof(DiscordCommands).Assembly, _services);
        }
        private static async Task HandleCommandAsync(SocketMessage arg)
        {
            var message = arg as SocketUserMessage;
            if (message.Channel.Id != IllSingleton.Config.DiscordSpamID) return;
            if (message.Author.IsBot) return;
            if (message.Content.StartsWith("!"))
                await DiscordCommands.CommandHandler(message.Content).ConfigureAwait(false);
                      
        }
        private static async Task HandleCommandAsync2(SocketMessage arg)
        {
            if (!(arg is SocketUserMessage message)) return;
            if (message.Author.IsBot) return;
            if (message.Channel.Id != IllSingleton.Config.DiscordSpamID) return;
            int argPos = 0;
            if (!(message.HasStringPrefix("!", ref argPos))) return;
            var context = new SocketCommandContext(_client, message);
            var result = await _commands.ExecuteAsync(context, argPos, _services).ConfigureAwait(false);
            if (!result.IsSuccess) Console.WriteLine(result.ErrorReason);
        }        
    }
}
