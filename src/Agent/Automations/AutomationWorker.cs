using Agent.SubAgents;

namespace Agent.Automations;

public sealed class AutomationWorker(
    IAutomationStore automationStore,
    IAutomationScheduler scheduler,
    ISubAgentCoordinator subAgentCoordinator,
    ILogger<AutomationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDue(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Automation worker failed.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    private async Task RunDue(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var due = (await automationStore.List(cancellationToken))
            .Where(x => x.Status == AutomationStatus.Enabled)
            .Where(x => x.NextRunAt is not null && x.NextRunAt <= now)
            .ToArray();

        foreach (var automation in due)
        {
            var result = await subAgentCoordinator.CreateAndReport(
                new SubAgentRunRequest(
                    automation.ConversationId,
                    automation.LastRunId ?? automation.Id,
                    automation.Task,
                    automation.Channel,
                    automation.Capabilities,
                    true,
                    automation.NotificationTarget),
                cancellationToken);
            var nextRunAt = scheduler.GetNextRun(automation.Schedule, now);
            await automationStore.UpdateRunResult(
                automation.Id,
                nextRunAt,
                result.RunId,
                result.Summary,
                cancellationToken);
        }
    }
}
