using SkillzBot.MODELS;
using System;
using System.Threading.Tasks;

public interface IBotStateService
{
    BotStateModel Current { get; }
    Task UpdateStateAsync(Action<BotStateModel> updateAction);
    Task LoadAsync();
}