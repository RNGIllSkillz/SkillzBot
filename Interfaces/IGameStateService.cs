using SkillzBot.MODELS;
using System;
using System.Threading.Tasks;

public interface IGameStateService
{
    BotGameStateModel Current { get; }
    Task UpdateStateAsync(Action<BotGameStateModel> updateAction);
    Task LoadAsync();
    Task SaveAsync();
}