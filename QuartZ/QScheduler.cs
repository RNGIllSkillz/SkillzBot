using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl;
using Quartz.Impl.Matchers;
using Quartz.Spi;
using SkillzBot.Hosts;
using SkillzBot.QuartZ;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class QuartzBackgroundTaskManager
{
    private readonly IScheduler _scheduler;
    private readonly ILogger<QuartzBackgroundTaskManager> _logger;

    public QuartzBackgroundTaskManager(IServiceProvider serviceProvider, ILogger<QuartzBackgroundTaskManager> logger)
    {
        _logger = logger;
        ISchedulerFactory schedulerFactory = new StdSchedulerFactory();
        _scheduler = schedulerFactory.GetScheduler().GetAwaiter().GetResult();
        _scheduler.JobFactory = new DiJobFactory(serviceProvider);
    }

    public async Task ScheduleTasks()
    {
        //await StackBackGroundTask("GetCurrentMatchTask", "TaskGroup", "GetCurrentMatchTrigger", "TriggerGroup", "0/4 * * * * ?"); migrated to a service 
        await StackBackGroundTask("RunEvery5Min", "TaskGroup", "CalculatePointsTrigger", "TriggerGroup", "0 */5 * * * ?");
        await StackBackGroundTask("RunDaily", "TaskGroup", "RunDailyTrigger", "TriggerGroup", "0 0 0 * * ?");
        await StackBackGroundTask("TopRuleteTask", "TaskGroup", "TopRuleteTaskTrigger", "TriggerGroup", "0 0 */3 * * ?");
        await StackBackGroundTask("MediaQueueFlush", "TaskGroup", "MediaQueueFlushTrigger", "TriggerGroup", "0 */30 * * * ?");

        if (!_scheduler.IsStarted)
        {
            await _scheduler.Start();
        }
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
    }
    public async Task UpdateJobSchedule(string taskName, string triggerName, string cronExpression)
    {
        var jobKey = new JobKey(taskName, "TaskGroup");
        var triggerKey = new TriggerKey(triggerName, "TriggerGroup");

        var job = await _scheduler.GetJobDetail(jobKey).ConfigureAwait(false) ?? throw new InvalidOperationException($"Job {taskName} does not exist.");
        var trigger = await _scheduler.GetTrigger(triggerKey).ConfigureAwait(false) ?? throw new InvalidOperationException($"Trigger {triggerName} does not exist.");
        var updatedTrigger = trigger.GetTriggerBuilder()
            .WithCronSchedule(cronExpression, x => x.InTimeZone(TimeZoneInfo.Local))
            .Build();

        try
        {
            await _scheduler.RescheduleJob(triggerKey, updatedTrigger);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "UpdateJobSchedule");
        }
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
    public async Task<bool> KillJobByName(string jobName)
    {
        var jobKey = new JobKey(jobName, "TaskGroup");

        if (await _scheduler.CheckExists(jobKey).ConfigureAwait(false))
        {
            await _scheduler.DeleteJob(jobKey);
            return true;
        }
        else
        {
            return false;
        }
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
            CronExpression.ValidateExpression(cronExpression);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private class DiJobFactory : IJobFactory
    {
        private readonly IServiceProvider _serviceProvider;
        public DiJobFactory(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

        public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
        {
            return _serviceProvider.GetRequiredService(bundle.JobDetail.JobType) as IJob;
        }

        public void ReturnJob(IJob job)
        {
            // Dispose if needed, container handles transient usually
            (job as IDisposable)?.Dispose();
        }
    }
}