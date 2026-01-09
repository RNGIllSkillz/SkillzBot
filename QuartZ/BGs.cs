using Quartz;
using SkillzBot.IllSkillzBot;
using System;
using System.Threading.Tasks;

namespace SkillzBot.QuartZ
{
    [DisallowConcurrentExecution]
    internal class BGTasks : IJob
    {
        private readonly BackGroundTasks _taskService;
        private readonly IllPredictions _illPredictions;
        public BGTasks(BackGroundTasks taskService, IllPredictions illPredictions)
        {
            _taskService = taskService;
            _illPredictions = illPredictions;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            string key = context.JobDetail.Key.Name;
            switch (key)
            {
                case "GetCurrentMatchTask":
                    await _illPredictions.GetCurrentMatchTask().ConfigureAwait(false);
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