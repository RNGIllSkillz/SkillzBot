using System;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Client.Events;
using TwitchLib.EventSub.Core.EventArgs.Channel;

namespace SkillzBot.Interfaces
{
    public interface ITtvIRCClient : IDisposable
    {
        bool IsConnected { get; }
        bool IsInitialized { get; }
        event Func<OnMessageReceivedArgs, Task> OnMessageReceived;
        Task<bool> InitializeAsync();
        Task<bool> ReconnectAsync();
        Task SendMessage(string messageToSend, CancellationToken cancellationToken = default);
        Task OnStreamDown();
        Task OnStreamUp();
        Task OnUnban(ChannelUnbanArgs e);
        /// <summary>Any IRC traffic, PING/PONG included: says the socket is alive, not that the channel still delivers.</summary>
        DateTimeOffset LastActivity { get; }
        /// <summary>The last chat message delivered by the channel (reset to the connect time on every connect).</summary>
        DateTimeOffset LastChatMessage { get; }
        /// <summary>True while the library believes the bot is joined to the channel.</summary>
        bool InChannel { get; }
        /// <summary>Re-joins the channel when the library has dropped it; true when a JOIN was sent.</summary>
        Task<bool> EnsureJoinedAsync();
    }
}