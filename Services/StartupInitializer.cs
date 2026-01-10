using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

public class StartupInitializer : IHostedService
{
    private readonly IBotStateService _botState;
    private readonly IGameStateService _gameState;
    private readonly QuartzBackgroundTaskManager _quartz;

    public StartupInitializer(IBotStateService botState, IGameStateService gameState, QuartzBackgroundTaskManager quartz)
    {
        _botState = botState;
        _gameState = gameState;
        _quartz = quartz;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _botState.LoadAsync();
        await _gameState.LoadAsync();
        await _quartz.ScheduleTasks();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}