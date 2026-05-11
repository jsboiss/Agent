using Agent.SubAgents;
using Agent.Tools;

namespace Agent.Automations;

public sealed class AutomationWorker(
    IAutomationStore automationStore,
    IAutomationRunStore automationRunStore,
    IAutomationScheduler scheduler,
    ISubAgentCoordinator subAgentCoordinator,
    IAgentToolExecutor toolExecutor,
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
            var automationRun = await automationRunStore.TryStart(
                automation,
                AutomationRunTrigger.Scheduled,
                cancellationToken);

            if (automationRun is null)
            {
                continue;
            }

            if (automation.Mode == AutomationExecutionMode.Deterministic)
            {
                var deterministicRequest = GetDeterministicToolRequest(automation);

                if (deterministicRequest is null)
                {
                    await automationRunStore.Complete(
                        automationRun.Id,
                        AutomationRunStatus.Failed,
                        null,
                        null,
                        "Unsupported deterministic automation task.",
                        cancellationToken);
                    continue;
                }

                var deterministicResult = await toolExecutor.Execute(
                    deterministicRequest,
                    cancellationToken);
                var nextDeterministicRunAt = scheduler.GetNextRun(automation.Schedule, now);
                await automationRunStore.Complete(
                    automationRun.Id,
                    deterministicResult.Succeeded ? AutomationRunStatus.Completed : AutomationRunStatus.Failed,
                    null,
                    deterministicResult.Content,
                    deterministicResult.Succeeded ? null : deterministicResult.Content,
                    cancellationToken);
                await automationStore.UpdateRunResult(
                    automation.Id,
                    nextDeterministicRunAt,
                    null,
                    deterministicResult.Content,
                    cancellationToken);

                continue;
            }

            var result = await subAgentCoordinator.CreateAndReport(
                new SubAgentRunRequest(
                    automation.ConversationId,
                    automation.LastRunId ?? automation.Id,
                    GetAutomationTask(automation),
                    "automation",
                    automation.Capabilities,
                    true,
                    automation.NotificationTarget,
                    automationRun.Id),
                cancellationToken);
            var nextRunAt = scheduler.GetNextRun(automation.Schedule, now);
            if (string.IsNullOrWhiteSpace(result.RunId))
            {
                await automationRunStore.Complete(
                    automationRun.Id,
                    AutomationRunStatus.Failed,
                    null,
                    result.Summary,
                    result.Summary,
                    cancellationToken);
            }
            await automationStore.UpdateRunResult(
                automation.Id,
                nextRunAt,
                result.RunId,
                result.Summary,
                cancellationToken);
        }
    }

    private static string GetAutomationTask(AgentAutomation automation)
    {
        List<string> lines = [];

        if (!string.IsNullOrWhiteSpace(automation.WorkspaceRootPath))
        {
            lines.Add($"Workspace root: {automation.WorkspaceRootPath}");
        }

        if (!string.IsNullOrWhiteSpace(automation.SkillIds))
        {
            lines.Add($"Required skills: {automation.SkillIds}");
        }

        lines.Add(automation.Task);

        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    private static AgentToolRequest? GetDeterministicToolRequest(AgentAutomation automation)
    {
        var parts = automation.Task.Split(':', 2, StringSplitOptions.TrimEntries);
        var action = parts[0].Trim();
        var body = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        var arguments = ParseKeyValues(body);

        if (!arguments.ContainsKey("query") && !string.IsNullOrWhiteSpace(body))
        {
            arguments["query"] = body;
        }

        if (action.Equals("notify_summary", StringComparison.OrdinalIgnoreCase))
        {
            action = "send_ack";
            arguments["message"] = string.IsNullOrWhiteSpace(body) ? automation.Name : body;
            arguments["target"] = automation.NotificationTarget ?? string.Empty;
        }

        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "search_memory",
            "search_conversations",
            "gmail_search_messages",
            "calendar_search_events",
            "calendar_list_events",
            "send_ack"
        };

        return supported.Contains(action)
            ? new AgentToolRequest(action, arguments, automation.ConversationId, "automation", automation.LastRunId ?? automation.Id)
            : null;
    }

    private static Dictionary<string, string> ParseKeyValues(string value)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);

        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);

            if (pair.Length == 2)
            {
                result[pair[0]] = pair[1];
            }
        }

        return result;
    }
}
