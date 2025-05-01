using Quartz;
using Quartz.Impl;
using Quartz.Impl.Matchers;
using SkillzBot.WRITERS;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkillzBot.QuartZ;

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
        await StackBackGroundTask("GetCurrentMatchTask", "TaskGroup", "GetCurrentMatchTrigger", "TriggerGroup", "0/4 * * * * ?").ConfigureAwait(false);
        await StackBackGroundTask("RunEvery5Min", "TaskGroup", "CalculatePointsTrigger", "TriggerGroup", "0 */5 * * * ?").ConfigureAwait(false);
        await StackBackGroundTask("RunDaily", "TaskGroup", "RunDailyTrigger", "TriggerGroup", "0 0 0 * * ?").ConfigureAwait(false);
        await StackBackGroundTask("TopRuleteTask", "TaskGroup", "TopRuleteTaskTrigger", "TriggerGroup", "0 0 */3 * * ?").ConfigureAwait(false);
        await StackBackGroundTask("MediaQueueFlush", "TaskGroup", "MediaQueueFlushTrigger", "TriggerGroup", "0 */30 * * * ?").ConfigureAwait(false);
        await StackBackGroundTask("Quizz", "TaskGroup", "QuizzTrigger", "TriggerGroup", "0 */30 * * * ?").ConfigureAwait(false);
       //await StackBackGroundTask("CronTest", "TaskGroup", "CronTestTrigger", "TriggerGroup", "0/2 * * * * ?").ConfigureAwait(false);
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

        await _scheduler.ScheduleJob(job, trigger).ConfigureAwait(false);
        if (!_scheduler.IsStarted)
        {
            await _scheduler.Start().ConfigureAwait(false);
        }
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
            await _scheduler.RescheduleJob(triggerKey, updatedTrigger).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteLog(ex, "UpdateJobSchedule");
        }
    }
    public async Task<string> GetRunningJobs()
    {
        var executingJobs = await _scheduler.GetCurrentlyExecutingJobs().ConfigureAwait(false);
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
            await _scheduler.DeleteJob(jobKey).ConfigureAwait(false);
            return true;
        }
        else
        {
            return false; // Job with the specified name does not exist
        }
    }
    public async Task<string> GetAllJobsNames()
    {
        Log.WriteLog(null, "GetAllJobsNames start");
        var jobKeys = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup()).ConfigureAwait(false);
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
