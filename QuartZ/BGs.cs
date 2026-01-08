using Quartz;
using SkillzBot.IllSkillzBot;
using SkillzBot.WRITERS;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SkillzBot.QuartZ
{
    [DisallowConcurrentExecution]
    public class BGTasks : IJob
    {        
        public async Task Execute(IJobExecutionContext context)
        {
            switch (context.JobDetail.Key.Name)
            {
                case "GetCurrentMatchTask":
                    await IllPredictions.GetCurrentMatchTask().ConfigureAwait(false);
                    break;
                case "RunEvery5Min":
                    await BackGroundTasks.StaticRunEvery5Min().ConfigureAwait(false);
                    break;
                case "RunDaily":
                    await BackGroundTasks.RunDaily().ConfigureAwait(false);
                    break;
                case "TopRuleteTask":
                    await BackGroundTasks.TopRuleteTask().ConfigureAwait(false);
                    break;
                case "MediaQueueFlush":
                    await BackGroundTasks.MediaQueueFlush().ConfigureAwait(false);
                    break;
                case "Quizz":
                    //await IllGames.Quizz(false).ConfigureAwait(false);                
                    break;
                case "CronTest":
                    await BackGroundTasks.CronTest().ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown job name {context.JobDetail.Key.Name}");
            }
        }
    }
}
