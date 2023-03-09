using Quartz;
using Quartz.Impl;
using Quartz.Impl.Matchers;
using SkillzBot.IllSkillzBot;
using SkillzBot.IRC;
using SkillzBot.Tasks;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class QuartzBackgroundTaskManager
{
    private readonly IScheduler _scheduler;

    public QuartzBackgroundTaskManager()
    {
        ISchedulerFactory schedulerFactory = new StdSchedulerFactory();
        _scheduler = schedulerFactory.GetScheduler().GetAwaiter().GetResult();
    }

    public async Task ScheduleTasks()
    {        
        await StackBackGroundTask("GetCurrentMatchTask", "TaskGroup", "GetCurrentMatchTrigger", "TriggerGroup", "0/2 * * * * ?");
        await StackBackGroundTask("CalculatePoints", "TaskGroup", "CalculatePointsTrigger", "TriggerGroup", "0 */5 * * * ?");
        await StackBackGroundTask("RunDaily", "TaskGroup", "RunDailyTrigger", "TriggerGroup", "0 0 0 * * ?");
        await StackBackGroundTask("TopRuleteTask", "TaskGroup", "TopRuleteTaskTrigger", "TriggerGroup", "0 0 */3 * * ?");
        await StackBackGroundTask("MediaQueueFlush", "TaskGroup", "MediaQueueFlushTrigger", "TriggerGroup", "0 */30 * * * ?");
        await StackBackGroundTask("Quizz", "TaskGroup", "QuizzTrigger", "TriggerGroup", "0 */30 * * * ?");
        //await StackBackGroundTask("CronTest", "TaskGroup", "CronTestTrigger", "TriggerGroup", "0/30 * * * * ?");
    }

    private async Task StackBackGroundTask(string taskName, string taskGroupName, string triggerName, string triggerGroupName, string cronExpression)
    {
        IJobDetail job = JobBuilder.Create<BGTasks>()
            .WithIdentity(taskName, taskGroupName)
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(triggerName, triggerGroupName)
            .WithCronSchedule(cronExpression, x => x.InTimeZone(TimeZoneInfo.Local))
            .Build();

        await _scheduler.ScheduleJob(job, trigger);
        if (!_scheduler.IsStarted)
        {
            await _scheduler.Start();
        }
    }
    public async Task UpdateJobSchedule(string taskName, string triggerName, string cronExpression)
    {
        var jobKey = new JobKey(taskName, "TaskGroup");
        var triggerKey = new TriggerKey(triggerName, "TriggerGroup");

        var job = await _scheduler.GetJobDetail(jobKey);
        if (job == null)
        {
            throw new InvalidOperationException($"Job {taskName} does not exist.");
        }

        var trigger = await _scheduler.GetTrigger(triggerKey);
        if (trigger == null)
        {
            throw new InvalidOperationException($"Trigger {triggerName} does not exist.");
        }

        var updatedTrigger = trigger.GetTriggerBuilder()
            .WithCronSchedule(cronExpression, x => x.InTimeZone(TimeZoneInfo.Local))
            .Build();

        await _scheduler.RescheduleJob(triggerKey, updatedTrigger);
    }
    public async Task<string> GetRunningJobs()
    {
        var executingJobs = await _scheduler.GetCurrentlyExecutingJobs();
        if (executingJobs == null || executingJobs.Count == 0)
        {
            return "No running jobs found.";
        }

        var jobList = new StringBuilder();
        jobList.AppendLine("Running Jobs:");
        foreach (var job in executingJobs)
        {
            var jobDetail = job.JobDetail;
            var jobName = jobDetail.Key.Name;
            var jobGroup = jobDetail.Key.Group;
            jobList.AppendLine($"{jobName} ({jobGroup})");
        }

        return jobList.ToString();
    }
    public async Task<string> GetAllJobsNames()
    {
        var jobKeys = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());
        if (jobKeys == null || jobKeys.Count == 0)
        {
            return "No jobs found.";
        }

        var jobList = new StringBuilder();
        jobList.AppendLine("Jobs:");
        var jobNames = jobKeys.Select(key => key.Name).ToList();
        foreach (var job in jobNames)
        {
            jobList.AppendLine(job);
        }
        return jobList.ToString();
    }
    public bool IsCronExpressionValid(string cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            return false;
        }

        try
        {
            new CronExpression(cronExpression);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public class BGTasks : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        switch (context.JobDetail.Key.Name)
        {
            case "GetCurrentMatchTask":
                await IllPredictions.GetCurrentMatchTask().ConfigureAwait(false);
                break;
            case "CalculatePoints":
                await BackGroundTasks.CalculatePoints().ConfigureAwait(false);
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
                await IllGames.Quizz(false).ConfigureAwait(false);
                break;

            case "CronTest":
                BackGroundTasks.CronTest();
                break;
            default:
                throw new InvalidOperationException($"Unknown job name {context.JobDetail.Key.Name}");
        }
    }
}
