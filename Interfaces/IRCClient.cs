using System;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.EventSub.Core.EventArgs.Channel;

namespace SkillzBot.Interfaces
{
    public interface ITtvIRCClient : IDisposable
    {

        bool IsConnected { get; }
        bool IsInitialized { get; }

        Task<bool> InitializeAsync();
        Task<bool> ReconnectAsync();
        Task SendMessage(string messageToSend, CancellationToken cancellationToken = default);

        Task OnStreamDown();
        Task OnStreamUp();
        Task OnUnban(ChannelUnbanArgs e);
    }
}