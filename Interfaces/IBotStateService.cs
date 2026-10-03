using SkillzBot.MODELS;
using System;
using System.Threading.Tasks;

public interface IBotStateService
{
    BotStateModel Current { get; }
    Task UpdateStateAsync(Action<BotStateModel> updateAction);
    Task LoadAsync();
    /// <summary>Records a stream online/offline transition and keeps the live-time bank consistent.</summary>
    Task SetBroadcasterOnlineAsync(bool online);
}