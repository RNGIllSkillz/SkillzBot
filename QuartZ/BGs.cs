using Quartz;
using System;
using System.Threading.Tasks;

namespace SkillzBot.QuartZ
{
    [DisallowConcurrentExecution]
    internal class BGTasks : IJob
    {
        private readonly BackGroundTasks _taskService;

        // Injected via DI now!
        public BGTasks(BackGroundTasks taskService)
        {
            _taskService = taskService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            string key = context.JobDetail.Key.Name;
            switch (key)
            {
                case "GetCurrentMatchTask":
                    // Assuming IllPredictions is still static for now, or you can refactor it too.
                    await IllSkillzBot.IllPredictions.GetCurrentMatchTask().ConfigureAwait(false);
                    break;
                case "RunEvery5Min":
                    await _taskService.RunEvery5Min().ConfigureAwait(false);
                    break;
                case "RunDaily":
                    await _taskService.RunDaily().ConfigureAwait(false);
                    break;
                case "TopRuleteTask":
                    await _taskService.TopRuleteTask().ConfigureAwait(false);
                    break;
                case "MediaQueueFlush":
                    await _taskService.MediaQueueFlush().ConfigureAwait(false);
                    break;
                case "CronTest":
                    await _taskService.CronTest().ConfigureAwait(false);
                    break;
                default:
                    // Log unknown job
                    break;
            }
        }
    }
}