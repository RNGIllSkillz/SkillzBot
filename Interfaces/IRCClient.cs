using System.Threading.Tasks;
using System.Threading;
using System;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Channel;

namespace SkillzBot.Interfaces
{
    public interface ITtvIRCClient : IDisposable
    {
        // Properties
        bool IsConnected { get; }
        bool IsInitialized { get; }

        // Methods
        Task<bool> InitializeAsync();
        Task<bool> ReconnectAsync();
        Task SendMessage(string messageToSend, CancellationToken cancellationToken = default);

        // Stream Events
        Task OnStreamDown();
        Task OnStreamUp();
        Task OnUnban(ChannelUnbanArgs e);
    }
}
